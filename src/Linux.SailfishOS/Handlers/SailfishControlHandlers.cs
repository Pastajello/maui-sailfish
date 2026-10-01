using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.SailfishOS.Platform.QtHost;

namespace Microsoft.Maui.SailfishOS.Handlers;

/// <summary>
/// Base for per-control handlers: every owned property re-pushes the family's full snapshot in one batch,
/// so paired state (hour+minute, cursor+selection) cannot tear. Each handler publishes its mapper as a public
/// static <c>Mapper</c> chained from <see cref="SailfishViewMapper.Mapper"/> (<see cref="SnapshotMapper{THandler}"/>).
/// </summary>
public abstract class SailfishSnapshotHandler<TVirtualView> : SailfishViewHandler<TVirtualView>
	where TVirtualView : class, IView
{
	private readonly HashSet<string> _keys;

	/// <summary>The font keys of every text control; ITextStyle.Font rides as "Font".</summary>
	protected static readonly string[] FontKeys =
		{ "Font", nameof(Label.FontSize), nameof(Label.FontFamily), nameof(Label.FontAttributes) };

	/// <param name="mapper">The handler's public mapper, usually built by <see cref="SnapshotMapper{THandler}"/>.</param>
	/// <param name="commandMapper">The handler's command mapper; null answers the view commands only.</param>
	/// <param name="ownedKeys">The mapper keys that push the snapshot.</param>
	/// <param name="measure">Control-specific measure run inside <see cref="SailfishMeasure.Frame"/>; null keeps
	/// the generic measure.</param>
	protected SailfishSnapshotHandler(IPropertyMapper mapper, CommandMapper? commandMapper,
		IEnumerable<string> ownedKeys, Func<IView, double, double, Size>? measure = null)
		: base(mapper, commandMapper, measure)
	{
		_keys = new HashSet<string>(ownedKeys, StringComparer.Ordinal);
	}

	/// <summary>A mapper chained from <see cref="SailfishViewMapper.Mapper"/> whose <paramref name="keys"/> all push
	/// the snapshot (<see cref="MapSnapshot"/>); they win over the generic keys of the same name.</summary>
	protected static PropertyMapper<TVirtualView, THandler> SnapshotMapper<THandler>(IEnumerable<string> keys)
		where THandler : SailfishSnapshotHandler<TVirtualView>
	{
		var mapper = new PropertyMapper<TVirtualView, THandler>(SailfishViewMapper.Mapper);
		foreach (var key in keys)
			mapper[key] = MapSnapshot;
		return mapper;
	}

	/// <summary>The action of every owned key: pushes the family's snapshot to the host.</summary>
	public static void MapSnapshot(SailfishSnapshotHandler<TVirtualView> handler, TVirtualView view) =>
		handler.PushSnapshot();

	/// <summary>Whether a change of <paramref name="propertyName"/> changes the snapshot. The mapper keys by default;
	/// a family whose snapshot depends on properties it cannot list (a shape's geometry) widens it, and those
	/// properties push the snapshot too.</summary>
	public virtual bool OwnsProperty(string propertyName) => _keys.Contains(propertyName);

	public override void UpdateValue(string property)
	{
		base.UpdateValue(property);
		if (!_keys.Contains(property) && OwnsProperty(property))
			PushSnapshot();
		// A new content view or template changes which hosts exist: its subtree follows on the next loop turn.
		if (property is nameof(IContentView.Content) or nameof(TemplatedView.ControlTemplate) && ConnectedView is { } view)
			QtHostPageRenderer.RequestSubtree(view);
	}

	/// <summary>Pushes the snapshot now; during the connect pass it joins the connect batch instead.</summary>
	protected void PushSnapshot()
	{
		if (!IsConnecting && ConnectedView is { } view && Snapshot(view) is { } props)
			PushProps(props, SnapshotYieldsToNative);
	}

	/// <summary>True when the snapshot mirrors state native writes back (a scroll position): it is then not pushed
	/// while that write-back runs.</summary>
	protected virtual bool SnapshotYieldsToNative => false;

	/// <summary>True when the snapshot depends on the arranged size (an image's decode size): it is pushed again when
	/// an arrange changes the frame size, as a native view learns its size from its layout pass.</summary>
	protected virtual bool SnapshotDependsOnSize => false;

	private Size _arrangedSize;

	public override void PlatformArrange(Rect frame)
	{
		base.PlatformArrange(frame);
		// Only a live host needs it (collection rows are measured before their hosts exist, and their create op carries
		// the arranged state); an arrange before the host exists leaves the size unconsumed, so the first arrange after
		// it does push (a page's create op is built by the walk, before the layout pass).
		if (SnapshotDependsOnSize && frame.Size != _arrangedSize &&
		    ((IElementHandler)this).PlatformView is NativeElementHost { IsAttached: true })
		{
			_arrangedSize = frame.Size;
			PushSnapshot();
		}
	}

	/// <summary>The family's full adapter snapshot, or null when not reducible yet (the reconcile poll retries).</summary>
	protected abstract Dictionary<string, object?>? Snapshot(TVirtualView view);

	protected override Dictionary<string, object?>? AdapterState() => ConnectedView is { } view ? Snapshot(view) : null;

	internal override bool Covers(string propertyName) => OwnsProperty(propertyName) || base.Covers(propertyName);

	/// <summary>Adds the transient text-input keys: focus, caret and selection push on their own, outside the
	/// snapshot, so a re-applied snapshot never resets native focus or caret.</summary>
	protected static PropertyMapper<TVirtualView, THandler> WithTransientInput<THandler>(PropertyMapper<TVirtualView, THandler> mapper)
		where THandler : SailfishSnapshotHandler<TVirtualView>
	{
		mapper[nameof(VisualElement.IsFocused)] = static (handler, _) => handler.PushFocus();
		mapper[nameof(ITextInput.CursorPosition)] = static (handler, _) => handler.PushCaret();
		mapper[nameof(ITextInput.SelectionLength)] = static (handler, _) => handler.PushCaret();
		return mapper;
	}

	private void PushFocus()
	{
		if (!IsConnecting && ConnectedView is VisualElement visual)
			PushTransient(("mauiFocus", visual.IsFocused));
	}

	// Caret and selection are one atomic adapter state.
	private void PushCaret()
	{
		if (!IsConnecting && ConnectedView is InputView input)
			PushTransient(("mauiCursor", input.CursorPosition), ("mauiSelLen", input.SelectionLength));
	}

	/// <summary>The transient text-input keys count as covered (the handler-parity measure).</summary>
	protected static bool IsTransientInputKey(string propertyName) =>
		Array.IndexOf(QtHostPageRenderer.TransientInputProperties, propertyName) >= 0;
}

/// <summary>
/// Snapshot handler for a control without a Core interface of its own (a library control): the owned keys become
/// an instance mapper chained from <see cref="ViewHandler.ViewMapper"/>.
/// </summary>
public abstract class SailfishSnapshotHandler : SailfishSnapshotHandler<IView>
{
	protected SailfishSnapshotHandler(IEnumerable<string> keys, Func<IView, double, double, Size>? measure = null)
		: this(keys, null, measure)
	{
	}

	/// <summary>Snapshot handler that also answers MAUI commands.</summary>
	protected SailfishSnapshotHandler(IEnumerable<string> keys, CommandMapper? commands,
		Func<IView, double, double, Size>? measure = null)
		: this(keys as string[] ?? keys.ToArray(), commands, measure)
	{
	}

	private SailfishSnapshotHandler(string[] keys, CommandMapper? commands, Func<IView, double, double, Size>? measure)
		: base(SnapshotMapper<SailfishSnapshotHandler>(keys), commands, keys, measure)
	{
	}
}

/// <summary>Silica Label handler.</summary>
public class SailfishLabelHandler : SailfishSnapshotHandler<ILabel>
{
	private static readonly string[] Keys =
	[
		nameof(ILabel.Text), nameof(Label.Text), nameof(Label.FormattedText),
		nameof(ILabel.TextColor), nameof(Label.TextColor), nameof(Label.BackgroundColor), nameof(IView.Background),
		.. FontKeys,
		nameof(ILabel.CharacterSpacing), nameof(Label.CharacterSpacing),
		nameof(ILabel.LineHeight), nameof(Label.LineHeight), nameof(Label.TextDecorations),
		nameof(ILabel.HorizontalTextAlignment), nameof(Label.HorizontalTextAlignment),
		nameof(ILabel.VerticalTextAlignment), nameof(Label.VerticalTextAlignment),
		nameof(Label.LineBreakMode), nameof(Label.MaxLines),
		nameof(Label.TextType), nameof(Label.TextTransform),
		nameof(IView.FlowDirection),              // RTL Start/End alignment
		nameof(IPadding.Padding),
	];

	public static readonly PropertyMapper<ILabel, SailfishLabelHandler> Mapper = SnapshotMapper<SailfishLabelHandler>(Keys);

	public static readonly CommandMapper<ILabel, SailfishLabelHandler> CommandMapper = new(ViewCommandMapper);

	public SailfishLabelHandler() : base(Mapper, CommandMapper, Keys, SailfishMeasure.Label)
	{
	}

	protected override string? AdapterUri => "label";

	protected override Dictionary<string, object?>? Snapshot(ILabel view) =>
		view is Label label ? QtHostPageRenderer.LabelProps(label) : null;
}

/// <summary>Silica Button handler.</summary>
public class SailfishButtonHandler : SailfishSnapshotHandler<IButton>
{
	private static readonly string[] Keys =
	[
		nameof(ITextButton.Text), nameof(Button.Text),
		nameof(ITextButton.TextColor), nameof(Button.TextColor),
		nameof(IView.Background), nameof(Button.BackgroundColor),
		.. FontKeys,
		nameof(IButtonStroke.CornerRadius), nameof(IButtonStroke.StrokeColor), nameof(Button.BorderColor),
		nameof(IButtonStroke.StrokeThickness), nameof(Button.BorderWidth),
		nameof(IImageSourcePart.Source), nameof(Button.ImageSource),
		nameof(IPadding.Padding),
		nameof(ITextStyle.CharacterSpacing),
	];

	public static readonly PropertyMapper<IButton, SailfishButtonHandler> Mapper = SnapshotMapper<SailfishButtonHandler>(Keys);

	public static readonly CommandMapper<IButton, SailfishButtonHandler> CommandMapper = new(ViewCommandMapper);

	public SailfishButtonHandler() : base(Mapper, CommandMapper, Keys, SailfishMeasure.Button)
	{
	}

	protected override string? AdapterUri => "button";

	protected override Dictionary<string, object?>? Snapshot(IButton view) =>
		view is Button button ? QtHostPageRenderer.ButtonProps(button) : null;
}

/// <summary>Entry handler; focus, cursor and selection stay transient pushes from the renderer.</summary>
public class SailfishEntryHandler : SailfishSnapshotHandler<IEntry>
{
	private static readonly string[] Keys =
	[
		nameof(Entry.Text), nameof(Entry.Placeholder), nameof(Entry.IsPassword),
		nameof(Entry.IsReadOnly), nameof(Entry.Keyboard), nameof(Entry.IsTextPredictionEnabled),
		nameof(Entry.MaxLength),
		nameof(Entry.ReturnType), nameof(Entry.ClearButtonVisibility), nameof(Entry.IsSpellCheckEnabled),
		nameof(ITextStyle.TextColor), nameof(IPlaceholder.PlaceholderColor), .. FontKeys,
		nameof(ITextAlignment.HorizontalTextAlignment), nameof(ITextAlignment.VerticalTextAlignment),
		nameof(ITextStyle.CharacterSpacing),
		nameof(IView.FlowDirection),
		nameof(VisualElement.BackgroundColor), nameof(VisualElement.Background),
	];

	public static readonly PropertyMapper<IEntry, SailfishEntryHandler> Mapper = WithTransientInput(SnapshotMapper<SailfishEntryHandler>(Keys));

	public static readonly CommandMapper<IEntry, SailfishEntryHandler> CommandMapper = new(ViewCommandMapper);

	public SailfishEntryHandler() : base(Mapper, CommandMapper, Keys, SailfishMeasure.TextInput)
	{
	}

	internal override bool Covers(string propertyName) => IsTransientInputKey(propertyName) || base.Covers(propertyName);

	protected override bool? FocusNatively(bool focus) => FocusTextInput(focus);

	protected override string? AdapterUri => "entry";

	protected override Dictionary<string, object?>? Snapshot(IEntry view) =>
		view is Entry entry
			? QtHostPageRenderer.TextInputProps(entry, entry.IsPassword ? 2 : 0,
				QtHostPageRenderer.ClampMaxLength(entry.MaxLength))
			: null;
}

/// <summary>Editor handler (the Silica TextArea has neither echoMode nor maximumLength).</summary>
public class SailfishEditorHandler : SailfishSnapshotHandler<IEditor>
{
	private static readonly string[] Keys =
	[
		nameof(Editor.Text), nameof(Editor.Placeholder), nameof(Editor.IsReadOnly),
		nameof(Editor.Keyboard), nameof(Editor.IsTextPredictionEnabled),
		nameof(Editor.MaxLength), nameof(Editor.IsSpellCheckEnabled),
		nameof(ITextStyle.TextColor), nameof(IPlaceholder.PlaceholderColor), .. FontKeys,
		nameof(ITextAlignment.HorizontalTextAlignment), nameof(ITextAlignment.VerticalTextAlignment),
		nameof(ITextStyle.CharacterSpacing),
		nameof(IView.FlowDirection),
		nameof(VisualElement.BackgroundColor), nameof(VisualElement.Background),
	];

	public static readonly PropertyMapper<IEditor, SailfishEditorHandler> Mapper = WithTransientInput(SnapshotMapper<SailfishEditorHandler>(Keys));

	public static readonly CommandMapper<IEditor, SailfishEditorHandler> CommandMapper = new(ViewCommandMapper);

	public SailfishEditorHandler() : base(Mapper, CommandMapper, Keys, SailfishMeasure.Editor)
	{
	}

	internal override bool Covers(string propertyName) => IsTransientInputKey(propertyName) || base.Covers(propertyName);

	protected override bool? FocusNatively(bool focus) => FocusTextInput(focus);

	protected override string? AdapterUri => "editor";

	protected override Dictionary<string, object?>? Snapshot(IEditor view) =>
		view is Editor editor ? QtHostPageRenderer.TextInputProps(editor, null, null) : null;
}

/// <summary>SearchBar handler; focus stays transient.</summary>
public class SailfishSearchBarHandler : SailfishSnapshotHandler<ISearchBar>
{
	private static readonly string[] Keys =
	[
		nameof(SearchBar.Text), nameof(SearchBar.Placeholder), nameof(SearchBar.CancelButtonColor),
		nameof(SearchBar.IsReadOnly), nameof(SearchBar.Keyboard), nameof(SearchBar.IsTextPredictionEnabled),
		nameof(SearchBar.IsSpellCheckEnabled), nameof(SearchBar.MaxLength), nameof(SearchBar.ReturnType),
		nameof(SearchBar.SearchIconColor),
		nameof(ITextStyle.TextColor), nameof(IPlaceholder.PlaceholderColor), .. FontKeys,
		nameof(ITextAlignment.HorizontalTextAlignment), nameof(ITextAlignment.VerticalTextAlignment),
		nameof(ITextStyle.CharacterSpacing),
		nameof(IView.FlowDirection),
	];

	public static readonly PropertyMapper<ISearchBar, SailfishSearchBarHandler> Mapper = WithTransientInput(SnapshotMapper<SailfishSearchBarHandler>(Keys));

	public static readonly CommandMapper<ISearchBar, SailfishSearchBarHandler> CommandMapper = new(ViewCommandMapper);

	public SailfishSearchBarHandler() : base(Mapper, CommandMapper, Keys, SailfishMeasure.TextInput)
	{
	}

	internal override bool Covers(string propertyName) => IsTransientInputKey(propertyName) || base.Covers(propertyName);

	protected override bool? FocusNatively(bool focus) => FocusTextInput(focus);

	protected override string? AdapterUri => "search-bar";

	protected override Dictionary<string, object?>? Snapshot(ISearchBar view) =>
		view is SearchBar bar ? QtHostPageRenderer.SearchBarProps(bar) : null;
}

/// <summary>Switch handler (Silica Switch adapter).</summary>
public class SailfishSwitchHandler : SailfishSnapshotHandler<ISwitch>
{
	private static readonly string[] Keys =
	{
		nameof(Switch.IsToggled), nameof(ISwitch.IsOn),
		nameof(ISwitch.ThumbColor), nameof(ISwitch.TrackColor), nameof(Switch.OnColor),
	};

	public static readonly PropertyMapper<ISwitch, SailfishSwitchHandler> Mapper = SnapshotMapper<SailfishSwitchHandler>(Keys);

	public static readonly CommandMapper<ISwitch, SailfishSwitchHandler> CommandMapper = new(ViewCommandMapper);

	public SailfishSwitchHandler() : base(Mapper, CommandMapper, Keys, SailfishMeasure.Switch)
	{
	}

	protected override string? AdapterUri => "switch";

	protected override Dictionary<string, object?>? Snapshot(ISwitch view) =>
		view is Switch sw ? QtHostPageRenderer.SwitchProps(sw) : null;
}

/// <summary>CheckBox handler: a framed check box, not the Silica Switch.</summary>
public class SailfishCheckBoxHandler : SailfishSnapshotHandler<ICheckBox>
{
	private static readonly string[] Keys =
	{
		nameof(CheckBox.IsChecked), nameof(ICheckBox.Foreground), nameof(CheckBox.Color),
	};

	public static readonly PropertyMapper<ICheckBox, SailfishCheckBoxHandler> Mapper = SnapshotMapper<SailfishCheckBoxHandler>(Keys);

	public static readonly CommandMapper<ICheckBox, SailfishCheckBoxHandler> CommandMapper = new(ViewCommandMapper);

	public SailfishCheckBoxHandler() : base(Mapper, CommandMapper, Keys, SailfishMeasure.Check)
	{
	}

	protected override string? AdapterUri => "check-box";

	protected override Dictionary<string, object?>? Snapshot(ICheckBox view) =>
		view is CheckBox cb ? QtHostPageRenderer.CheckBoxProps(cb) : null;
}

/// <summary>Slider handler; bounds must precede the value because QML clamps on assignment.</summary>
public class SailfishSliderHandler : SailfishSnapshotHandler<ISlider>
{
	private static readonly string[] Keys =
	{
		nameof(Slider.Value), nameof(Slider.Minimum), nameof(Slider.Maximum),
		nameof(ISlider.MinimumTrackColor), nameof(ISlider.MaximumTrackColor), nameof(ISlider.ThumbColor),
		nameof(ISlider.ThumbImageSource),
	};

	public static readonly PropertyMapper<ISlider, SailfishSliderHandler> Mapper = SnapshotMapper<SailfishSliderHandler>(Keys);

	public static readonly CommandMapper<ISlider, SailfishSliderHandler> CommandMapper = new(ViewCommandMapper);

	public SailfishSliderHandler() : base(Mapper, CommandMapper, Keys, SailfishMeasure.Slider)
	{
	}

	protected override string? AdapterUri => "slider";

	protected override Dictionary<string, object?>? Snapshot(ISlider view) =>
		view is Slider slider ? QtHostPageRenderer.SliderProps(slider) : null;
}

/// <summary>ProgressBar handler.</summary>
public class SailfishProgressBarHandler : SailfishSnapshotHandler<IProgress>
{
	private static readonly string[] Keys = { nameof(ProgressBar.Progress), nameof(IProgress.ProgressColor) };

	public static readonly PropertyMapper<IProgress, SailfishProgressBarHandler> Mapper = SnapshotMapper<SailfishProgressBarHandler>(Keys);

	public static readonly CommandMapper<IProgress, SailfishProgressBarHandler> CommandMapper = new(ViewCommandMapper);

	public SailfishProgressBarHandler() : base(Mapper, CommandMapper, Keys, SailfishMeasure.Progress)
	{
	}

	protected override string? AdapterUri => "progress-bar";

	protected override Dictionary<string, object?>? Snapshot(IProgress view) =>
		view is ProgressBar progress ? QtHostPageRenderer.ProgressBarProps(progress) : null;
}

/// <summary>ActivityIndicator handler (Silica BusyIndicator).</summary>
public class SailfishActivityIndicatorHandler : SailfishSnapshotHandler<IActivityIndicator>
{
	private static readonly string[] Keys = { nameof(ActivityIndicator.IsRunning), nameof(IActivityIndicator.Color) };

	public static readonly PropertyMapper<IActivityIndicator, SailfishActivityIndicatorHandler> Mapper = SnapshotMapper<SailfishActivityIndicatorHandler>(Keys);

	public static readonly CommandMapper<IActivityIndicator, SailfishActivityIndicatorHandler> CommandMapper = new(ViewCommandMapper);

	public SailfishActivityIndicatorHandler() : base(Mapper, CommandMapper, Keys, SailfishMeasure.Activity)
	{
	}

	protected override string? AdapterUri => "activity-indicator";

	protected override Dictionary<string, object?>? Snapshot(IActivityIndicator view) =>
		view is ActivityIndicator indicator ? QtHostPageRenderer.ActivityIndicatorProps(indicator) : null;
}

/// <summary>Picker handler; items must precede the index so the QML index sync sees the full list.</summary>
public class SailfishPickerHandler : SailfishSnapshotHandler<IPicker>
{
	private static readonly string[] Keys =
	[
		nameof(Picker.SelectedIndex), nameof(Picker.ItemsSource), nameof(IPicker.Items), nameof(Picker.Title),
		nameof(Picker.TextColor), nameof(Picker.TitleColor), nameof(ITextStyle.CharacterSpacing),
		.. FontKeys,
		nameof(Picker.IsOpen),
		nameof(ITextAlignment.HorizontalTextAlignment), nameof(ITextAlignment.VerticalTextAlignment),
	];

	public static readonly PropertyMapper<IPicker, SailfishPickerHandler> Mapper = SnapshotMapper<SailfishPickerHandler>(Keys);

	public static readonly CommandMapper<IPicker, SailfishPickerHandler> CommandMapper = new(ViewCommandMapper);

	public SailfishPickerHandler() : base(Mapper, CommandMapper, Keys, SailfishMeasure.ValueBox)
	{
	}

	protected override string? AdapterUri => "picker";

	protected override Dictionary<string, object?>? Snapshot(IPicker view) =>
		view is Picker picker ? QtHostPageRenderer.PickerProps(picker) : null;
}

/// <summary>DatePicker handler (dates cross as local-midnight epoch ms).</summary>
public class SailfishDatePickerHandler : SailfishSnapshotHandler<IDatePicker>
{
	private static readonly string[] Keys =
	[
		nameof(DatePicker.Date), nameof(DatePicker.MinimumDate), nameof(DatePicker.MaximumDate),
		nameof(DatePicker.Format), nameof(DatePicker.TextColor), nameof(ITextStyle.CharacterSpacing),
		.. FontKeys,
		nameof(DatePicker.IsOpen),
	];

	public static readonly PropertyMapper<IDatePicker, SailfishDatePickerHandler> Mapper = SnapshotMapper<SailfishDatePickerHandler>(Keys);

	public static readonly CommandMapper<IDatePicker, SailfishDatePickerHandler> CommandMapper = new(ViewCommandMapper);

	public SailfishDatePickerHandler() : base(Mapper, CommandMapper, Keys, SailfishMeasure.ValueBox)
	{
	}

	protected override string? AdapterUri => "date-picker";

	protected override Dictionary<string, object?>? Snapshot(IDatePicker view) =>
		view is DatePicker picker ? QtHostPageRenderer.DatePickerProps(picker) : null;
}

/// <summary>TimePicker handler; hour and minute travel in one snapshot so no half-updated time is applied.</summary>
public class SailfishTimePickerHandler : SailfishSnapshotHandler<ITimePicker>
{
	private static readonly string[] Keys =
	[
		nameof(TimePicker.Time),
		nameof(TimePicker.Format), nameof(TimePicker.TextColor), nameof(ITextStyle.CharacterSpacing),
		.. FontKeys,
		nameof(TimePicker.IsOpen),
	];

	public static readonly PropertyMapper<ITimePicker, SailfishTimePickerHandler> Mapper = SnapshotMapper<SailfishTimePickerHandler>(Keys);

	public static readonly CommandMapper<ITimePicker, SailfishTimePickerHandler> CommandMapper = new(ViewCommandMapper);

	public SailfishTimePickerHandler() : base(Mapper, CommandMapper, Keys, SailfishMeasure.ValueBox)
	{
	}

	protected override string? AdapterUri => "time-picker";

	protected override Dictionary<string, object?>? Snapshot(ITimePicker view) =>
		view is TimePicker picked ? QtHostPageRenderer.TimePickerProps(picked) : null;
}

/// <summary>RadioButton handler; group exclusivity stays in MAUI.</summary>
public class SailfishRadioButtonHandler : SailfishSnapshotHandler<IRadioButton>
{
	private static readonly string[] Keys =
	[
		nameof(RadioButton.IsChecked), nameof(RadioButton.Content), nameof(TemplatedView.ControlTemplate),
		nameof(RadioButton.TextColor), nameof(ITextStyle.CharacterSpacing),
		.. FontKeys,
		nameof(IButtonStroke.StrokeColor), nameof(RadioButton.BorderColor),
		nameof(IButtonStroke.StrokeThickness), nameof(RadioButton.BorderWidth),
		nameof(IButtonStroke.CornerRadius),
	];

	public static readonly PropertyMapper<IRadioButton, SailfishRadioButtonHandler> Mapper = SnapshotMapper<SailfishRadioButtonHandler>(Keys);

	public static readonly CommandMapper<IRadioButton, SailfishRadioButtonHandler> CommandMapper = new(ViewCommandMapper);

	public SailfishRadioButtonHandler() : base(Mapper, CommandMapper, Keys, SailfishMeasure.Radio)
	{
	}

	// A ControlTemplate (an implicit Style, or View content and MAUI's default template) makes the RadioButton a
	// templated control, as MAUI renders it everywhere: the template tree paints and MAUI's own tap gesture toggles
	// IsChecked. The Silica radio would only show the content's ToString().
	private bool Templated => VirtualView is IContentView { PresentedContent: not null };

	protected override string? AdapterUri => Templated ? "content-view" : "radio-button";

	protected override bool WalksChildren => Templated;

	protected override Dictionary<string, object?>? Snapshot(IRadioButton view) =>
		view is not RadioButton radio ? null
		: Templated ? QtHostPageRenderer.ContainerProps(radio)
		: QtHostPageRenderer.RadioButtonProps(radio);
}

/// <summary>WebView handler on the Gecko adapter; navigation and JS commands go out as transient props,
/// and JS results come back as events.</summary>
public class SailfishWebViewHandler : SailfishSnapshotHandler<IWebView>
{
	private static readonly string[] Keys = { nameof(IWebView.Source), nameof(WebView.Source), nameof(IWebView.UserAgent) };

	public static readonly PropertyMapper<IWebView, SailfishWebViewHandler> Mapper = SnapshotMapper<SailfishWebViewHandler>(Keys);

	private int _navTick;
	private int _jsTick;
	private int _jsSeq;
	private readonly Dictionary<string, EvaluateJavaScriptAsyncRequest> _pendingJs = new(StringComparer.Ordinal);

	public static readonly CommandMapper<IWebView, SailfishWebViewHandler> CommandMapper = new(ViewCommandMapper)
	{
		[nameof(IWebView.GoBack)] = (h, _, _) => h.Nav("back"),
		[nameof(IWebView.GoForward)] = (h, _, _) => h.Nav("forward"),
		[nameof(IWebView.Reload)] = (h, _, _) => h.Nav("reload"),
		[nameof(IWebView.Eval)] = (h, _, args) => h.RunJs(args as string, null),
		[nameof(IWebView.EvaluateJavaScriptAsync)] = (h, _, args) =>
		{
			if (args is EvaluateJavaScriptAsyncRequest request)
				h.RunJs(request.Script, request);
		},
	};

	public SailfishWebViewHandler() : base(Mapper, CommandMapper, Keys, SailfishMeasure.Generic)
	{
	}

	protected override string? AdapterUri => "web-view";

	protected override bool WalksChildren => false;

	protected override Dictionary<string, object?>? Snapshot(IWebView view) =>
		view is WebView web ? QtHostPageRenderer.WebViewProps(web) : null;

	private void Nav(string command) => PushProps(new Dictionary<string, object?>
	{
		["mauiNavCommand"] = command,
		["mauiNavTick"] = ++_navTick,
	});

	private void RunJs(string? script, EvaluateJavaScriptAsyncRequest? request)
	{
		if (string.IsNullOrEmpty(script))
		{
			request?.SetResult(null!);
			return;
		}
		var id = "js" + (++_jsSeq).ToString(System.Globalization.CultureInfo.InvariantCulture);
		if (request is not null)
			_pendingJs[id] = request;
		PushProps(new Dictionary<string, object?>
		{
			["mauiJs"] = script,
			["mauiJsId"] = id,
			["mauiJsTick"] = ++_jsTick,
		});
	}

	/// <summary>Completes a pending EvaluateJavaScriptAsync (null on a script error, as MAUI does).</summary>
	internal void CompleteJs(string requestId, bool ok, string? result)
	{
		if (_pendingJs.Remove(requestId, out var request))
			request.SetResult(ok ? result! : null!);
	}
}

/// <summary>SwipeView handler; Open/Close commands move the native row.</summary>
public class SailfishSwipeViewHandler : SailfishSnapshotHandler<ISwipeView>
{
	private static readonly string[] Keys =
	{
		nameof(ISwipeView.LeftItems), nameof(ISwipeView.RightItems), nameof(ISwipeView.TopItems),
		nameof(ISwipeView.BottomItems), nameof(ISwipeView.Threshold),
		nameof(VisualElement.BackgroundColor), nameof(VisualElement.Background),
		nameof(ISwipeView.SwipeTransitionMode),
	};

	public static readonly PropertyMapper<ISwipeView, SailfishSwipeViewHandler> Mapper = SnapshotMapper<SailfishSwipeViewHandler>(Keys);

	private int _openTick;

	public static readonly CommandMapper<ISwipeView, SailfishSwipeViewHandler> CommandMapper = new(ViewCommandMapper)
	{
		[nameof(ISwipeView.RequestOpen)] = (h, _, args) => h.Open(args is SwipeViewOpenRequest open ? SideOf(open.OpenSwipeItem) : "right"),
		[nameof(ISwipeView.RequestClose)] = (h, _, _) => h.Open(string.Empty),
	};

	public SailfishSwipeViewHandler() : base(Mapper, CommandMapper, Keys, SailfishMeasure.Content)
	{
	}

	protected override string? AdapterUri => "swipe-view";

	protected override Dictionary<string, object?>? Snapshot(ISwipeView view) =>
		view is SwipeView swipe ? QtHostPageRenderer.SwipeProps(swipe) : null;

	private static string SideOf(OpenSwipeItem item) => item switch
	{
		OpenSwipeItem.LeftItems => "left",
		OpenSwipeItem.RightItems => "right",
		_ => "right",   // top/bottom items are not rendered yet
	};

	private void Open(string side) => PushProps(new Dictionary<string, object?>
	{
		["mauiOpenSide"] = side,
		["mauiOpenTick"] = ++_openTick,
	});
}

/// <summary>Stepper handler (minus/plus IconButton pair).</summary>
public class SailfishStepperHandler : SailfishSnapshotHandler<IStepper>
{
	private static readonly string[] Keys =
	{
		nameof(IStepper.Value), nameof(Stepper.Value), nameof(IStepper.Minimum), nameof(Stepper.Minimum),
		nameof(IStepper.Maximum), nameof(Stepper.Maximum), nameof(IStepper.Interval), nameof(Stepper.Increment),
	};

	public static readonly PropertyMapper<IStepper, SailfishStepperHandler> Mapper = SnapshotMapper<SailfishStepperHandler>(Keys);

	public static readonly CommandMapper<IStepper, SailfishStepperHandler> CommandMapper = new(ViewCommandMapper);

	public SailfishStepperHandler() : base(Mapper, CommandMapper, Keys, SailfishMeasure.Stepper)
	{
	}

	protected override string? AdapterUri => "stepper";

	protected override Dictionary<string, object?>? Snapshot(IStepper view) =>
		view is Stepper stepper ? QtHostPageRenderer.StepperProps(stepper) : null;
}

/// <summary>IndicatorView dot strip; with an IndicatorTemplate MAUI lays out the template and no adapter is used.</summary>
public class SailfishIndicatorViewHandler : SailfishSnapshotHandler<IIndicatorView>
{
	private static readonly string[] Keys =
	{
		nameof(IndicatorView.Count), nameof(IndicatorView.Position),
		nameof(IndicatorView.IndicatorColor), nameof(IndicatorView.SelectedIndicatorColor),
		nameof(IndicatorView.IndicatorSize), nameof(IndicatorView.IndicatorsShape),
		nameof(IndicatorView.MaximumVisible), nameof(IndicatorView.HideSingle),
	};

	public static readonly PropertyMapper<IIndicatorView, SailfishIndicatorViewHandler> Mapper = SnapshotMapper<SailfishIndicatorViewHandler>(Keys);

	public static readonly CommandMapper<IIndicatorView, SailfishIndicatorViewHandler> CommandMapper = new(ViewCommandMapper);

	public SailfishIndicatorViewHandler() : base(Mapper, CommandMapper, Keys)
	{
	}

	public override Size GetDesiredSize(double widthConstraint, double heightConstraint) =>
		VirtualView is IndicatorView { IndicatorTemplate: null }
			? SailfishMeasure.Frame(VirtualView!, widthConstraint, heightConstraint, SailfishMeasure.Indicator)
			: base.GetDesiredSize(widthConstraint, heightConstraint);

	protected override string? AdapterUri =>
		VirtualView is IndicatorView { IndicatorTemplate: null } ? "indicator-view" : null;

	// The dot strip adapter is the default template's drawing, so template children are not walked.
	protected override bool WalksChildren => ConnectedView is not IndicatorView { IndicatorTemplate: null };

	protected override Dictionary<string, object?>? Snapshot(IIndicatorView view) =>
		view is IndicatorView { IndicatorTemplate: null } indicator ? QtHostPageRenderer.IndicatorProps(indicator) : null;
}

/// <summary>Image handler: the source resolves to a URL Qt loads itself; an unresolvable source gets no host.</summary>
public class SailfishImageHandler : SailfishSnapshotHandler<IImage>
{
	private static readonly string[] Keys =
	{
		nameof(Image.Source), nameof(Image.Aspect), nameof(Image.BackgroundColor), nameof(IView.Background),
		nameof(IImage.IsAnimationPlaying),
		// ImageButton frame (the same handler serves both)
		nameof(IButtonStroke.CornerRadius), nameof(IButtonStroke.StrokeColor), nameof(ImageButton.BorderColor),
		nameof(IButtonStroke.StrokeThickness), nameof(ImageButton.BorderWidth), nameof(IPadding.Padding),
	};

	public static readonly PropertyMapper<IImage, SailfishImageHandler> Mapper = SnapshotMapper<SailfishImageHandler>(Keys);

	public static readonly CommandMapper<IImage, SailfishImageHandler> CommandMapper = new(ViewCommandMapper);

	public SailfishImageHandler() : base(Mapper, CommandMapper, Keys, SailfishMeasure.Image)
	{
	}

	// The arranged size sets the decode size (QtHostImages).
	protected override bool SnapshotDependsOnSize => true;

	// ImageButton implements IImage without deriving from Image, so key on the interface.
	protected override string? AdapterUri =>
		VirtualView is IImage resolved && QtHostImages.Resolve(resolved.Source as ImageSource) is not null ? "image" : null;

	protected override Dictionary<string, object?>? Snapshot(IImage view) =>
		view is IImage image ? QtHostImages.Props(image) : null;
}

/// <summary>Border handler; the obsolete Frame uses the same adapter.</summary>
public class SailfishBorderHandler : SailfishSnapshotHandler<IContentView>
{
	private static readonly string[] Keys =
	{
		nameof(Border.Stroke), nameof(Border.StrokeThickness), nameof(Border.StrokeShape),
		nameof(Border.BackgroundColor), nameof(Border.Background), nameof(Border.Padding),
		// The IBorderStroke names MAUI maps
		nameof(IBorderStroke.Shape), nameof(IBorderStroke.StrokeDashPattern), nameof(IBorderStroke.StrokeDashOffset),
		nameof(IBorderStroke.StrokeLineCap), nameof(IBorderStroke.StrokeLineJoin), nameof(IBorderStroke.StrokeMiterLimit),
		nameof(IView.Shadow),
#pragma warning disable CS0618 // Frame is obsolete but its props must re-diff.
		nameof(Frame.BorderColor), nameof(Frame.CornerRadius), nameof(Frame.HasShadow),
#pragma warning restore CS0618
	};

	public static readonly PropertyMapper<IContentView, SailfishBorderHandler> Mapper = SnapshotMapper<SailfishBorderHandler>(Keys);

	public static readonly CommandMapper<IContentView, SailfishBorderHandler> CommandMapper = new(ViewCommandMapper);

	public SailfishBorderHandler() : base(Mapper, CommandMapper, Keys, SailfishMeasure.Boxed)
	{
	}

	protected override string? AdapterUri => "border";

	public override bool OwnsProperty(string propertyName) =>
		base.OwnsProperty(propertyName) || QtHostPageRenderer.IsBorderVisualProperty(propertyName);

	/// <summary>The path/drawing is built for the arranged size.</summary>
	protected override bool SnapshotDependsOnSize => true;

	protected override Dictionary<string, object?>? Snapshot(IContentView view) =>
		view switch
		{
			Border border => QtHostPageRenderer.BorderProps(border),
#pragma warning disable CS0618 // Frame is obsolete but must stay paintable.
			Frame frame => QtHostPageRenderer.FrameProps(frame),
#pragma warning restore CS0618
			_ => null,
		};
}

/// <summary>Shape/BoxView handler: every non-visual-state property re-diffs the paint snapshot.</summary>
public class SailfishShapeHandler : SailfishSnapshotHandler<IShapeView>
{
	private static readonly string[] Keys = [];

	public static readonly PropertyMapper<IShapeView, SailfishShapeHandler> Mapper = SnapshotMapper<SailfishShapeHandler>(Keys);

	public static readonly CommandMapper<IShapeView, SailfishShapeHandler> CommandMapper = new(ViewCommandMapper);

	// A bare Shape measures 0x0: its path derives from the arranged size.
	public SailfishShapeHandler() : base(Mapper, CommandMapper, Keys,
		(v, wc, hc) => v is BoxView ? SailfishMeasure.Box(v, wc, hc) : SailfishMeasure.Constrain(0, 0, wc, hc))
	{
	}

	protected override string? AdapterUri => QtHostShapes.AdapterUri;

	public override bool OwnsProperty(string propertyName) =>
		!QtHostVisualState.IsStateProperty(propertyName);

	/// <summary>The path/drawing is built for the arranged size.</summary>
	protected override bool SnapshotDependsOnSize => true;

	protected override Dictionary<string, object?>? Snapshot(IShapeView view) => view switch
	{
		BoxView box => QtHostShapes.BoxViewProps(box),
		Microsoft.Maui.Controls.Shapes.Shape shape => QtHostShapes.ShapeProps(shape),
		_ => null,
	};
}

/// <summary>GraphicsView handler; the adapter state is the recorded IDrawable stream.</summary>
public class SailfishGraphicsHandler : SailfishSnapshotHandler<IGraphicsView>
{
	private static readonly string[] Keys = [];

	public static readonly PropertyMapper<IGraphicsView, SailfishGraphicsHandler> Mapper = SnapshotMapper<SailfishGraphicsHandler>(Keys);

	/// <summary>GraphicsView.Invalidate re-records the drawable.</summary>
	public static readonly CommandMapper<IGraphicsView, SailfishGraphicsHandler> CommandMapper = new(ViewCommandMapper)
	{
		[nameof(IGraphicsView.Invalidate)] = static (handler, view, _) => MapSnapshot(handler, view),
	};

	public SailfishGraphicsHandler() : base(Mapper, CommandMapper, Keys)
	{
	}

	protected override string? AdapterUri => QtHostGraphics.AdapterUri;

	public override bool OwnsProperty(string propertyName) =>
		!QtHostVisualState.IsStateProperty(propertyName);

	/// <summary>The path/drawing is built for the arranged size.</summary>
	protected override bool SnapshotDependsOnSize => true;

	protected override Dictionary<string, object?>? Snapshot(IGraphicsView view) =>
		view is GraphicsView graphics ? QtHostGraphics.Props(graphics) : null;
}

/// <summary>Container handler: only the background crosses the bridge, and only painted containers get a host.</summary>
public class SailfishContentViewHandler : SailfishSnapshotHandler<IContentView>
{
	private static readonly string[] Keys = { nameof(VisualElement.BackgroundColor), nameof(VisualElement.Background) };

	public static readonly PropertyMapper<IContentView, SailfishContentViewHandler> Mapper = SnapshotMapper<SailfishContentViewHandler>(Keys);

	public static readonly CommandMapper<IContentView, SailfishContentViewHandler> CommandMapper = new(ViewCommandMapper);

	public SailfishContentViewHandler() : base(Mapper, CommandMapper, Keys, SailfishMeasure.Content)
	{
	}

	// RefreshView paints nothing itself; the wrapped scroll surface carries the spinner and gesture.
	protected override string? AdapterUri => VirtualView is RefreshView ? null : "content-view";

	protected override Dictionary<string, object?>? Snapshot(IContentView view) =>
		view is VisualElement ve ? QtHostPageRenderer.ContainerProps(ve) : null;
}

/// <summary>Grid container handler.</summary>
public class SailfishGridHandler : SailfishLayoutHandlerBase<IGridLayout>
{
	private static readonly string[] Keys =
	{
		nameof(VisualElement.BackgroundColor), nameof(VisualElement.Background),
		nameof(ILayout.ClipsToBounds), nameof(Layout.IsClippedToBounds),
		nameof(Grid.ColumnDefinitions), nameof(Grid.RowDefinitions),
	};

	public static readonly PropertyMapper<IGridLayout, SailfishGridHandler> Mapper = SnapshotMapper<SailfishGridHandler>(Keys);

	public static readonly CommandMapper<IGridLayout, SailfishGridHandler> CommandMapper = LayoutCommands<SailfishGridHandler>();

	public SailfishGridHandler() : base(Mapper, CommandMapper, Keys)
	{
	}

	protected override string? AdapterUri => "grid";

	protected override Dictionary<string, object?>? Snapshot(IGridLayout view) =>
		view is Grid grid ? QtHostPageRenderer.GridProps(grid) : null;
}

/// <summary>Stack container handler (StackBase covers the oriented stacks and StackLayout).</summary>
public class SailfishStackHandler : SailfishLayoutHandlerBase<IStackLayout>
{
	private static readonly string[] Keys =
	{
		nameof(VisualElement.BackgroundColor), nameof(VisualElement.Background),
		nameof(ILayout.ClipsToBounds), nameof(Layout.IsClippedToBounds),
		nameof(StackBase.Spacing), nameof(StackLayout.Orientation),
	};

	public static readonly PropertyMapper<IStackLayout, SailfishStackHandler> Mapper = SnapshotMapper<SailfishStackHandler>(Keys);

	public static readonly CommandMapper<IStackLayout, SailfishStackHandler> CommandMapper = LayoutCommands<SailfishStackHandler>();

	public SailfishStackHandler() : base(Mapper, CommandMapper, Keys)
	{
	}

	protected override string? AdapterUri => "stack-layout";

	protected override Dictionary<string, object?>? Snapshot(IStackLayout view) =>
		view is StackBase stack ? QtHostPageRenderer.StackProps(stack) : null;
}

/// <summary>CollectionView handler: hosting only; <see cref="QtHostCollectionBridge"/> owns rows and selection.</summary>
public class SailfishListViewHandler : SailfishSnapshotHandler<IView>
{
	private static readonly string[] Keys = [];

	/// <summary>The list follows its ItemsView properties through this mapper (QtHostCollectionBridge.ViewProperties).</summary>
	public static readonly PropertyMapper<IView, SailfishListViewHandler> Mapper = WithItemsProperties(SnapshotMapper<SailfishListViewHandler>(Keys));

	private static PropertyMapper<IView, SailfishListViewHandler> WithItemsProperties(PropertyMapper<IView, SailfishListViewHandler> mapper)
	{
		foreach (var key in QtHostCollectionBridge.ViewProperties)
			mapper[key] = MapItemsProperty;
		return mapper;
	}

	/// <summary>Hands an ItemsView change to the list's adapter; the connect pass is the registration's.</summary>
	public static void MapItemsProperty(SailfishListViewHandler handler, IView view) =>
		handler.OnItemsProperty(view);

	private string? _mapping;   // the key being mapped (PropertyMapper actions do not receive it)

	public override void UpdateValue(string property)
	{
		_mapping = property;
		try
		{
			base.UpdateValue(property);
		}
		finally
		{
			_mapping = null;
		}
	}

	private void OnItemsProperty(IView view)
	{
		if (!IsConnecting && _mapping is { } key)
			Adapter?.OnViewProperty(key);
	}

	/// <summary>The list's adapter (rows, delegates, slots, selection, scroll), set when the page reconcile registers
	/// the list and cleared when the list retires; the mapper hands it every ItemsView change.</summary>
	internal QtHostListAdapter? Adapter { get; set; }

	public static readonly CommandMapper<IView, SailfishListViewHandler> CommandMapper = new(ViewCommandMapper);

	public SailfishListViewHandler() : base(Mapper, CommandMapper, Keys, SailfishMeasure.Collection)
	{
	}

	protected override string? AdapterUri => QtHostCollectionBridge.AdapterUriFor(VirtualView);

	protected override Dictionary<string, object?>? Snapshot(IView view) => null;
}

/// <summary>
/// Handler of a view no Sailfish handler serves (another layout, a templated view, an unknown control): the
/// reconcile hosts it as a plain container, so only its background crosses, with the generic view state.
/// </summary>
public class SailfishContainerHandler : SailfishSnapshotHandler<IView>
{
	private static readonly string[] Keys = { nameof(VisualElement.BackgroundColor), nameof(VisualElement.Background) };

	public static readonly PropertyMapper<IView, SailfishContainerHandler> Mapper = SnapshotMapper<SailfishContainerHandler>(Keys);

	public static readonly CommandMapper<IView, SailfishContainerHandler> CommandMapper = new(ViewCommandMapper);

	// The generic measure, as the NullViewHandler these views had before.
	public SailfishContainerHandler() : base(Mapper, CommandMapper, Keys)
	{
	}

	protected override Dictionary<string, object?>? Snapshot(IView view) =>
		view is VisualElement ve ? QtHostPageRenderer.ContainerProps(ve) : null;
}

/// <summary>
/// Base of the layout handlers, as ILayoutHandler is on the other platforms: Controls' Layout reports each child
/// change through Handler.Invoke(Add/Insert/Remove/Update/UpdateZIndex/Clear), and the host tree follows at once
/// (the reconcile creates, reorders or destroys exactly those hosts) instead of waiting for a tree scan.
/// </summary>
public abstract class SailfishLayoutHandlerBase<TLayout> : SailfishSnapshotHandler<TLayout>, ILayoutHandler<NativeElementHost>
	where TLayout : class, ILayout
{
	protected SailfishLayoutHandlerBase(IPropertyMapper mapper, CommandMapper? commandMapper,
		IEnumerable<string> ownedKeys, Func<IView, double, double, Size>? measure = null)
		: base(mapper, commandMapper, ownedKeys, measure)
	{
	}

	/// <summary>A command mapper answering the layout commands Controls raises.</summary>
	protected static CommandMapper<TLayout, THandler> LayoutCommands<THandler>()
		where THandler : SailfishLayoutHandlerBase<TLayout> =>
		new(ViewCommandMapper)
		{
			[nameof(ILayoutHandler.Add)] = static (handler, _, args) => handler.Add(ChildOf(args)!),
			[nameof(ILayoutHandler.Insert)] = static (handler, _, args) =>
				handler.Insert((args as LayoutHandlerUpdate)?.Index ?? -1, ChildOf(args)!),
			[nameof(ILayoutHandler.Remove)] = static (handler, _, args) => handler.Remove(ChildOf(args)!),
			[nameof(ILayoutHandler.Update)] = static (handler, _, args) =>
				handler.Update((args as LayoutHandlerUpdate)?.Index ?? -1, ChildOf(args)!),
			[nameof(ILayoutHandler.UpdateZIndex)] = static (handler, _, args) => handler.UpdateZIndex(ChildOf(args)!),
			[nameof(ILayoutHandler.Clear)] = static (handler, _, _) => handler.Clear(),
		};

	private static IView? ChildOf(object? args) => args switch
	{
		LayoutHandlerUpdate update => update.View,
		IView view => view,
		_ => null,
	};

	// Each change is a change of this container's host subtree, applied as one batch (create/destroy/order ops) on
	// the next loop turn; several changes in one turn coalesce.
	public void Add(IView view) => ChildrenChanged();

	public void Remove(IView view) => ChildrenChanged();

	public void Clear() => ChildrenChanged();

	public void Insert(int index, IView view) => ChildrenChanged();

	public void Update(int index, IView view) => ChildrenChanged();

	public void UpdateZIndex(IView view) => ChildrenChanged();

	private void ChildrenChanged()
	{
		if (ConnectedView is { } layout)
			QtHostPageRenderer.RequestSubtree(layout);
	}

	ILayout IElementHandler<ILayout, NativeElementHost>.VirtualView => VirtualView;

	ILayout IViewHandler<ILayout, NativeElementHost>.VirtualView => VirtualView;

	NativeElementHost IElementHandler<ILayout, NativeElementHost>.PlatformView => PlatformView;
}

/// <summary>Handler of the layouts without an adapter of their own (FlexLayout, AbsoluteLayout, custom layouts):
/// a plain container host whose children MAUI's layout manager arranges.</summary>
public class SailfishLayoutHandler : SailfishLayoutHandlerBase<ILayout>
{
	private static readonly string[] Keys =
	{
		nameof(VisualElement.BackgroundColor), nameof(VisualElement.Background),
		nameof(ILayout.ClipsToBounds), nameof(Layout.IsClippedToBounds),
	};

	public static readonly PropertyMapper<ILayout, SailfishLayoutHandler> Mapper = SnapshotMapper<SailfishLayoutHandler>(Keys);

	public static readonly CommandMapper<ILayout, SailfishLayoutHandler> CommandMapper = LayoutCommands<SailfishLayoutHandler>();

	public SailfishLayoutHandler() : base(Mapper, CommandMapper, Keys)
	{
	}

	protected override Dictionary<string, object?>? Snapshot(ILayout view) =>
		view is VisualElement ve ? QtHostPageRenderer.ContainerProps(ve) : null;
}
