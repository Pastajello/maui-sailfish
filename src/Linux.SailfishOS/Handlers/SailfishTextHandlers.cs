using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.SailfishOS.Platform.QtHost;

namespace Microsoft.Maui.SailfishOS.Handlers;

/// <summary>Silica Label handler.</summary>
public class SailfishLabelHandler : SailfishSnapshotHandler<ILabel>
{
	private static readonly string[] Keys =
	[
		nameof(Label.Text), nameof(Label.FormattedText),
		nameof(Label.TextColor), nameof(Label.BackgroundColor), nameof(IView.Background),
		.. FontKeys,
		nameof(Label.CharacterSpacing), nameof(Label.LineHeight), nameof(Label.TextDecorations),
		nameof(Label.HorizontalTextAlignment), nameof(Label.VerticalTextAlignment),
		nameof(Label.LineBreakMode), nameof(Label.MaxLines),
		nameof(Label.TextType), nameof(Label.TextTransform),
		nameof(IView.FlowDirection),              // RTL Start/End alignment
		nameof(IPadding.Padding),
	];

	public static readonly PropertyMapper<ILabel, SailfishLabelHandler> Mapper = SnapshotMapper<SailfishLabelHandler>(Keys);

	public static readonly CommandMapper<ILabel, SailfishLabelHandler> CommandMapper = new(SailfishViewMapper.CommandMapper);

	public SailfishLabelHandler() : this(null)
	{
	}

	public SailfishLabelHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
		: base(mapper ?? Mapper, commandMapper ?? CommandMapper, SailfishMeasure.Label)
	{
	}

	protected override string? AdapterUri => SailfishKeys.Adapter.Label;

	protected override Dictionary<string, object?>? Snapshot(ILabel view) =>
		view is Label label ? AdapterSnapshots.LabelProps(label) : null;
}

/// <summary>Silica Button handler.</summary>
public class SailfishButtonHandler : SailfishSnapshotHandler<IButton>
{
	private static readonly string[] Keys =
	[
		nameof(Button.Text), nameof(Button.TextColor),
		nameof(IView.Background), nameof(Button.BackgroundColor),
		.. FontKeys,
		nameof(IButtonStroke.CornerRadius), nameof(IButtonStroke.StrokeColor), nameof(Button.BorderColor),
		nameof(IButtonStroke.StrokeThickness), nameof(Button.BorderWidth),
		nameof(IImageSourcePart.Source), nameof(Button.ImageSource),
		nameof(IPadding.Padding),
		nameof(ITextStyle.CharacterSpacing),
	];

	public static readonly PropertyMapper<IButton, SailfishButtonHandler> Mapper = SnapshotMapper<SailfishButtonHandler>(Keys);

	public static readonly CommandMapper<IButton, SailfishButtonHandler> CommandMapper = new(SailfishViewMapper.CommandMapper);

	public SailfishButtonHandler() : this(null)
	{
	}

	public SailfishButtonHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
		: base(mapper ?? Mapper, commandMapper ?? CommandMapper, SailfishMeasure.Button)
	{
	}

	protected override string? AdapterUri => SailfishKeys.Adapter.Button;

	protected override Dictionary<string, object?>? Snapshot(IButton view) =>
		view is Button button ? AdapterSnapshots.ButtonProps(button) : null;
}

/// <summary>
/// Base of the text inputs (Entry, Editor, SearchBar): Qt decides native focus, and focus, caret and selection push
/// on their own, outside the snapshot, so a re-applied snapshot never resets them.
/// </summary>
public abstract class SailfishTextInputHandlerBase<TVirtualView> : SailfishSnapshotHandler<TVirtualView>
	where TVirtualView : class, IView
{
	protected SailfishTextInputHandlerBase(IPropertyMapper mapper, CommandMapper? commandMapper,
		Func<IView, double, double, Size>? measure)
		: base(mapper, commandMapper, measure)
	{
	}

	/// <summary>A snapshot mapper plus the transient keys (focus, caret, selection).</summary>
	protected static PropertyMapper<TVirtualView, THandler> TextInputMapper<THandler>(IEnumerable<string> keys)
		where THandler : SailfishTextInputHandlerBase<TVirtualView>
	{
		var mapper = SnapshotMapper<THandler>(keys);
		mapper[nameof(VisualElement.IsFocused)] = static (handler, _) => handler.PushFocus();
		mapper[nameof(ITextInput.CursorPosition)] = static (handler, _) => handler.PushCaret();
		mapper[nameof(ITextInput.SelectionLength)] = static (handler, _) => handler.PushCaret();
		return mapper;
	}

	protected override bool? FocusNatively(bool focus) => FocusTextInput(focus);

	private void PushFocus()
	{
		if (!IsConnecting && ConnectedView is VisualElement visual)
			PushTransient((SailfishKeys.Transient.Focus, visual.IsFocused));
	}

	// Caret and selection are one atomic adapter state.
	private void PushCaret()
	{
		if (!IsConnecting && ConnectedView is InputView input)
			PushTransient((SailfishKeys.Transient.Cursor, input.CursorPosition),
				(SailfishKeys.Transient.SelectionLength, input.SelectionLength));
	}
}

/// <summary>Entry handler.</summary>
public class SailfishEntryHandler : SailfishTextInputHandlerBase<IEntry>
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

	public static readonly PropertyMapper<IEntry, SailfishEntryHandler> Mapper = TextInputMapper<SailfishEntryHandler>(Keys);

	public static readonly CommandMapper<IEntry, SailfishEntryHandler> CommandMapper = new(SailfishViewMapper.CommandMapper);

	public SailfishEntryHandler() : this(null)
	{
	}

	public SailfishEntryHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
		: base(mapper ?? Mapper, commandMapper ?? CommandMapper, SailfishMeasure.TextInput)
	{
	}

	protected override string? AdapterUri => SailfishKeys.Adapter.Entry;

	protected override Dictionary<string, object?>? Snapshot(IEntry view) =>
		view is Entry entry ? AdapterSnapshots.TextInputProps(entry) : null;
}

/// <summary>Editor handler (the Silica TextArea has neither echoMode nor maximumLength).</summary>
public class SailfishEditorHandler : SailfishTextInputHandlerBase<IEditor>
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

	public static readonly PropertyMapper<IEditor, SailfishEditorHandler> Mapper = TextInputMapper<SailfishEditorHandler>(Keys);

	public static readonly CommandMapper<IEditor, SailfishEditorHandler> CommandMapper = new(SailfishViewMapper.CommandMapper);

	public SailfishEditorHandler() : this(null)
	{
	}

	public SailfishEditorHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
		: base(mapper ?? Mapper, commandMapper ?? CommandMapper, SailfishMeasure.Editor)
	{
	}

	protected override string? AdapterUri => SailfishKeys.Adapter.Editor;

	protected override Dictionary<string, object?>? Snapshot(IEditor view) =>
		view is Editor editor ? AdapterSnapshots.TextInputProps(editor) : null;
}

/// <summary>SearchBar handler.</summary>
public class SailfishSearchBarHandler : SailfishTextInputHandlerBase<ISearchBar>
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

	public static readonly PropertyMapper<ISearchBar, SailfishSearchBarHandler> Mapper = TextInputMapper<SailfishSearchBarHandler>(Keys);

	public static readonly CommandMapper<ISearchBar, SailfishSearchBarHandler> CommandMapper = new(SailfishViewMapper.CommandMapper);

	public SailfishSearchBarHandler() : this(null)
	{
	}

	public SailfishSearchBarHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
		: base(mapper ?? Mapper, commandMapper ?? CommandMapper, SailfishMeasure.TextInput)
	{
	}

	protected override string? AdapterUri => SailfishKeys.Adapter.SearchBar;

	protected override Dictionary<string, object?>? Snapshot(ISearchBar view) =>
		view is SearchBar bar ? AdapterSnapshots.SearchBarProps(bar) : null;
}
