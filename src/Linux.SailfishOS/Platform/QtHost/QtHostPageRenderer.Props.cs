using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

// Snapshot builders: MAUI control state → adapter properties (sizes in device px, Qt enums as ints).
public sealed partial class QtHostPageRenderer
{
	/// <summary>
	/// The Border snapshot: stroke color/width and corner radius in device px (what the QML Rectangle draws in)
	/// plus the background fill. Non-solid strokes cross transparent.
	/// </summary>
	internal static Dictionary<string, object?> BorderProps(Border border)
	{
		var density = SailfishDisplay.Density;
		var radius = border.StrokeShape is Microsoft.Maui.Controls.Shapes.RoundRectangle round
			? round.CornerRadius.TopLeft
			: 0.0;
		var props = new Dictionary<string, object?>
		{
			["mauiBorderColor"] = QtHostPaint.Solid(border.Stroke) ?? Colors.Transparent,
			["mauiBorderWidth"] = border.StrokeThickness * density,
			["mauiCornerRadius"] = radius * density,
			["mauiBackground"] = EffectiveBackground(border),
			// A Border clips its content; Qt 5.6 clips only rectangularly. The clip lives on the adapter's inner box,
			// so a shadow can paint outside it.
			["mauiClip"] = true,
		};
		AddBorderCanvas(props, border, density);
		AddShadow(props, border.Shadow, density);
		return props;
	}

	/// <summary>MAUI Shadow → the Border adapter's shadow canvas (device px; transparent = none).</summary>
	private static void AddShadow(Dictionary<string, object?> props, Shadow? shadow, double density)
	{
		var color = shadow?.Brush is SolidColorBrush solid ? solid.Color : null;
		if (shadow is null || color is null || shadow.Opacity <= 0)
		{
			props["mauiShadowColor"] = Colors.Transparent;
			return;
		}
		props["mauiShadowColor"] = color.WithAlpha((float)Math.Clamp(color.Alpha * shadow.Opacity, 0, 1));
		props["mauiShadowBlur"] = Math.Max(0, shadow.Radius) * density;
		props["mauiShadowX"] = shadow.Offset.X * density;
		props["mauiShadowY"] = shadow.Offset.Y * density;
	}

	/// <summary>
	/// Strokes the plain Rectangle cannot draw (dashes, per-corner radii, other StrokeShapes, gradient stroke)
	/// are painted by the adapter's Canvas: the shape path inset by half the stroke, like MauiDrawable.
	/// Empty ops keep the Rectangle.
	/// </summary>
	private static void AddBorderCanvas(Dictionary<string, object?> props, Border border, double density)
	{
		var none = new List<object?>();
		props["mauiShapeOps"] = none;
		var thickness = Math.Max(0, border.StrokeThickness);
		var dashed = border.StrokeDashArray is { Count: > 0 } && thickness > 0 && border.Stroke is not null;
		var shaped = border.StrokeShape switch
		{
			null => false,
			Microsoft.Maui.Controls.Shapes.Rectangle => false,
			Microsoft.Maui.Controls.Shapes.RoundRectangle r => !(r.CornerRadius.TopLeft == r.CornerRadius.TopRight &&
			                                                     r.CornerRadius.TopLeft == r.CornerRadius.BottomLeft &&
			                                                     r.CornerRadius.TopLeft == r.CornerRadius.BottomRight),
			_ => true,
		};
		var gradient = border.Stroke is GradientBrush && thickness > 0;
		if (!(dashed || shaped || gradient) || border.Width <= 0 || border.Height <= 0)
			return;
		// A dashed/gradient stroke without a StrokeShape outlines the box.
		var shape = border.StrokeShape as IShape ?? new Microsoft.Maui.Controls.Shapes.Rectangle();
		PathF path;
		try
		{
			var inset = thickness / 2;
			path = shape.PathForBounds(new Rect(inset, inset, Math.Max(0, border.Width - thickness), Math.Max(0, border.Height - thickness)));
		}
		catch (Exception ex)
		{
			QtHostDiag.Warn(QtHostDiagChannel.Geometry, $"border stroke shape {shape.GetType().Name}: PathForBounds failed ({ex.Message}) — plain rectangle");
			return;
		}
		if (path is null || path.OperationCount == 0)
			return;
		var bounds = new RectF(0, 0, (float)border.Width, (float)border.Height);
		props["mauiShapeOps"] = QtHostShapes.PathOps(path, density);
		props["mauiFillSpec"] = QtHostShapes.PaintSpec(border.Background, bounds, density)
			?? QtHostShapes.SolidSpec(border.BackgroundColor) ?? none;
		props["mauiStrokeSpec"] = thickness > 0 ? QtHostShapes.PaintSpec(border.Stroke, bounds, density) ?? none : none;
		props["mauiCap"] = (int)border.StrokeLineCap;
		props["mauiJoin"] = (int)border.StrokeLineJoin;
		props["mauiMiter"] = border.StrokeMiterLimit;
		props["mauiDash"] = QtHostShapes.Dash(thickness, border.StrokeDashArray, border.StrokeDashOffset, density);
	}

	/// <summary>Properties that re-diff the full Border snapshot as one coherent adapter state.</summary>
	internal static bool IsBorderVisualProperty(string propertyName) =>
		propertyName is nameof(Border.Stroke) or nameof(Border.StrokeThickness)
			or nameof(Border.StrokeShape) or nameof(VisualElement.BackgroundColor)
			or nameof(VisualElement.Background)
			// canvas stroke
			or nameof(Border.StrokeDashArray) or nameof(Border.StrokeDashOffset)
			or nameof(Border.StrokeLineCap) or nameof(Border.StrokeLineJoin)
			or nameof(Border.StrokeMiterLimit) or nameof(VisualElement.Shadow);

	/// <summary>
	/// The obsolete Frame rides the Border adapter. CornerRadius -1 means MAUI's default (5, like FrameHandler);
	/// an unset BorderColor keeps the box outline-less; HasShadow paints the iOS Frame shadow.
	/// </summary>
#pragma warning disable CS0618 // Frame is obsolete but must stay paintable.
	internal static Dictionary<string, object?> FrameProps(Frame frame)
#pragma warning restore CS0618
	{
		var density = SailfishDisplay.Density;
		var radius = frame.CornerRadius < 0 ? 5f : frame.CornerRadius;
		return new Dictionary<string, object?>
		{
			["mauiBorderColor"] = frame.BorderColor ?? Colors.Transparent,
			["mauiBorderWidth"] = frame.BorderColor is null ? 0.0 : density,
			["mauiCornerRadius"] = radius * density,
			["mauiBackground"] = EffectiveBackground(frame),
			["mauiClip"] = frame.IsClippedToBounds,
			// HasShadow: the in-box iOS Frame shadow (black, radius 5, opacity 0.8, no offset).
			["mauiShadowColor"] = frame.HasShadow ? Colors.Black.WithAlpha(0.8f) : Colors.Transparent,
			["mauiShadowBlur"] = 5 * density,
			["mauiShadowX"] = 0.0,
			["mauiShadowY"] = 0.0,
		};
	}

	/// <summary>A presenter/layout container crosses only what it paints: the background fill and clip.</summary>
	internal static Dictionary<string, object?> ContainerProps(VisualElement view) =>
		new() { ["mauiBackground"] = EffectiveBackground(view), ["clip"] = ClipsToBounds(view) };

	/// <summary>Grid container: background, clip and the column count the adapter mirrors.</summary>
	internal static Dictionary<string, object?> GridProps(Grid grid)
	{
		var props = ContainerProps(grid);
		props["mauiColumns"] = grid.ColumnDefinitions.Count;
		return props;
	}

	/// <summary>Stack container: background, clip and the positioner's spacing (device px) and orientation.</summary>
	internal static Dictionary<string, object?> StackProps(StackBase stack)
	{
		var props = ContainerProps(stack);
		props["mauiSpacing"] = stack.Spacing * SailfishDisplay.Density;
		props["mauiOrientation"] = OrientationOf(stack);
		return props;
	}

	/// <summary>The nearest RefreshView above a scroll surface, but only when the page has no pulley (Silica's
	/// pull-down menu owns the same overscroll). Mirrors the collection bridge's rule.</summary>
	internal RefreshView? RefreshAncestorOf(Element view)
	{
		RefreshView? refresh = null;
		for (var e = view.Parent; e is not null; e = e.Parent)
		{
			refresh ??= e as RefreshView;
			if (e is Page page)
				return refresh is not null && page.ToolbarItems.Count == 0 && !HasFlyoutPulley(page) ? refresh : null;
		}
		return null;
	}

	/// <summary>The nested scroll host's snapshot: direction, MAUI scroll position (managed is the authority) and
	/// background. The content extent rides the geometry pass, since it is known only after arrange.</summary>
	internal static Dictionary<string, object?> ScrollProps(ScrollView scrollView)
	{
		var p = ContainerProps(scrollView);
		p["clip"] = true;   // a scroll viewport always clips its content
		p["mauiOrientation"] = scrollView.Orientation switch
		{
			ScrollOrientation.Horizontal => "horizontal",
			ScrollOrientation.Both => "both",
			ScrollOrientation.Neither => "neither",
			_ => "vertical",
		};
		p["mauiScrollX"] = QtHostUnits.ToQtUnits(scrollView.ScrollX);
		p["mauiScrollY"] = QtHostUnits.ToQtUnits(scrollView.ScrollY);
		// ScrollBarVisibility (Default 0 / Always 1 / Never 2) → the Silica scroll decorators.
		p["mauiHBar"] = (int)scrollView.HorizontalScrollBarVisibility;
		p["mauiVBar"] = (int)scrollView.VerticalScrollBarVisibility;
		return p;
	}

	/// <summary>Layout.IsClippedToBounds → the host's native Item.clip (children nest inside it, so Qt clips
	/// them). ScrollView is excluded here; ScrollProps sets its viewport clip.</summary>
	private static bool ClipsToBounds(VisualElement view) =>
		view switch
		{
			ScrollView => false,
			ILayout layout => layout.ClipsToBounds,
			_ => false,
		};

	/// <summary>The effective background paint color (the Background brush wins over BackgroundColor).</summary>
	private static Color EffectiveBackground(VisualElement view) => QtHostPaint.Background(view) ?? Colors.Transparent;

	/// <summary>True when the app set a background, even a transparent one. Unset keeps the Silica default; an
	/// explicit transparent plate must cross, or Silica's highlight plate paints over it. An unset Background is
	/// Brush.Default (an empty brush, not null): counted as set, every plain button lost its Silica plate.</summary>
	private static bool HasExplicitBackground(VisualElement view) =>
		!Brush.IsNullOrEmpty(view.Background) || view.BackgroundColor is not null;


	/// <summary>MAUI StackOrientation → the StackLayout adapter's positioner state string.</summary>
	private static string OrientationName(StackOrientation orientation) =>
		orientation == StackOrientation.Horizontal ? "horizontal" : "vertical";

	/// <summary>The flow direction of any StackBase; in MAUI 11 the Vertical/Horizontal subclasses carry it in
	/// their type and only legacy StackLayout has Orientation.</summary>
	private static string OrientationOf(StackBase stack) =>
		stack switch
		{
			HorizontalStackLayout => "horizontal",
			StackLayout legacy => OrientationName(legacy.Orientation),
			_ => "vertical",   // VerticalStackLayout
		};

	/// <summary>
	/// Reconcile-time snapshot of an InputView: static text-input state only. Focus, cursor and selection are
	/// transient native state pushed only on PropertyChanged, so a poll can never clobber the live caret.
	/// </summary>
	internal static Dictionary<string, object?> TextInputProps(InputView input, int? echoMode, int? maxLength)
	{
		var props = new Dictionary<string, object?>
		{
			["text"] = input.Text ?? string.Empty,
			["placeholderText"] = input.Placeholder ?? string.Empty,
			["readOnly"] = input.IsReadOnly,
			// Spell checking off also turns VKB suggestions off (Qt has no separate hint; MAUI Android does the same).
			["mauiHints"] = MapInputMethodHints(input.Keyboard, input.IsTextPredictionEnabled && input.IsSpellCheckEnabled),
			// A set BackgroundColor/Background replaces the native background, underline included, as on Android
			// (the "borderless entry" idiom: BackgroundColor="Transparent" inside the app's own frame).
			["mauiNoUnderline"] = input.IsSet(VisualElement.BackgroundColorProperty) || input.IsSet(VisualElement.BackgroundProperty),
		};
		if (echoMode is { } echo)
			props["echoMode"] = echo;
		if (maxLength is { } len)
			props["maximumLength"] = len;
		switch (input)
		{
			case Entry entry:
				props["mauiEnterIcon"] = EnterKeyIcon(entry.ReturnType);
				props["mauiClearButton"] = entry.ClearButtonVisibility == ClearButtonVisibility.WhileEditing;
				break;
			case Editor editor:
				// The Silica TextArea has no maximumLength, so the adapter truncates.
				props["mauiMaxLength"] = editor.MaxLength is <= 0 or int.MaxValue ? -1 : editor.MaxLength;
				break;
		}
		foreach (var kv in TextStyleProps(input))
			props[kv.Key] = kv.Value;
		return props;
	}

	// Value-control snapshots shared by the renderer walk and handler mappers. Unset colors cross transparent
	// so adapters keep the Silica palette.

	internal static Dictionary<string, object?> SwitchProps(Switch sw) => new()
	{
		["checked"] = sw.IsToggled,
		["mauiThumbColor"] = sw.ThumbColor ?? Colors.Transparent,
		["mauiTrackColor"] = sw.OnColor ?? Colors.Transparent,
	};

	internal static Dictionary<string, object?> CheckBoxProps(CheckBox checkBox) => new()
	{
		["checked"] = checkBox.IsChecked == true,
		// Foreground/Color tints the frame and the fill (transparent = Silica highlight).
		["mauiColor"] = checkBox.Color ?? Colors.Transparent,
	};

	internal static Dictionary<string, object?> SliderProps(Slider slider) => new()
	{
		// Bounds before value: QML clamps on assignment and the shim preserves batch order.
		["minimumValue"] = slider.Minimum,
		["maximumValue"] = slider.Maximum,
		["value"] = slider.Value,
		["mauiMinTrackColor"] = slider.MinimumTrackColor ?? Colors.Transparent,
		["mauiMaxTrackColor"] = slider.MaximumTrackColor ?? Colors.Transparent,
		["mauiThumbColor"] = slider.ThumbColor ?? Colors.Transparent,
		// ThumbImageSource replaces the Silica handle at the image's size in dp.
		["mauiThumbImage"] = QtHostImages.Resolve(slider.ThumbImageSource) ?? string.Empty,
		["mauiThumbScale"] = SailfishDisplay.Density,
	};

	internal static Dictionary<string, object?> ProgressBarProps(ProgressBar progress) => new()
	{
		["value"] = progress.Progress,
		["mauiProgressColor"] = progress.ProgressColor ?? Colors.Transparent,
	};

	internal static Dictionary<string, object?> ActivityIndicatorProps(ActivityIndicator indicator) => new()
	{
		["running"] = indicator.IsRunning,
		["mauiColor"] = indicator.Color ?? Colors.Transparent,
	};

	/// <summary>SearchField snapshot, including text styling.</summary>
	internal static Dictionary<string, object?> SearchBarProps(SearchBar searchBar)
	{
		var props = new Dictionary<string, object?>
		{
			["text"] = searchBar.Text ?? string.Empty,
			["placeholderText"] = searchBar.Placeholder ?? string.Empty,
			// Transparent = unset, so the Silica palette tint of the clear icon survives.
			["mauiCancelColor"] = searchBar.CancelButtonColor ?? Colors.Transparent,
			// Rest of the text-input contract (Entry parity).
			["readOnly"] = searchBar.IsReadOnly,
			["mauiHints"] = MapInputMethodHints(searchBar.Keyboard, searchBar.IsTextPredictionEnabled && searchBar.IsSpellCheckEnabled),
			["maximumLength"] = ClampMaxLength(searchBar.MaxLength),
			["mauiEnterIcon"] = EnterKeyIcon(searchBar.ReturnType),
			["mauiSearchIconColor"] = searchBar.SearchIconColor ?? Colors.Transparent,
		};
		foreach (var kv in TextStyleProps(searchBar))
			props[kv.Key] = kv.Value;
		return props;
	}

	/// <summary>
	/// Text styling of a Silica text field (TextBase). Unset values cross as transparent / 0 / "" so the Silica
	/// look stays until the app styles it; CharacterSpacing rides mauiLetterSpacing, applied by the shim on the QFont.
	/// </summary>
	internal static Dictionary<string, object?> TextStyleProps(InputView input)
	{
		var density = SailfishDisplay.Density;
		var font = ((ITextStyle)input).Font;
		var rtl = IsRightToLeft(input);
		var align = input is ITextAlignment aligned && (input.IsSet(Entry.HorizontalTextAlignmentProperty) || rtl)
			? aligned.HorizontalTextAlignment switch
			{
				TextAlignment.Center => "center",
				TextAlignment.End => rtl ? "left" : "right",
				_ => rtl ? "right" : "left",
			}
			: string.Empty;
		return new Dictionary<string, object?>
		{
			["mauiColor"] = input.TextColor ?? Colors.Transparent,
			["mauiPlaceholderColor"] = input.PlaceholderColor ?? Colors.Transparent,
			["mauiPixelSize"] = HasAppFontSize(input, Entry.FontSizeProperty, font.Size) ? font.Size * density : 0.0,
			["mauiFamily"] = QtHostFonts.Resolve(font.Family),
			["mauiBold"] = font.Weight >= FontWeight.Bold,
			["mauiItalic"] = font.Slant == FontSlant.Italic,
			["mauiHAlign"] = align,
			// VerticalTextAlignment inside a field taller than its natural height (the adapter moves the text; a field
			// of its natural height stays the Silica layout). An Entry centres by default, as MAUI's default Center does
			// on Android (DeveloperBalance's category rows, stretched by their buttons); an Editor only when set.
			["mauiVAlign"] = input is ITextAlignment vertical &&
			                 (input is Entry || input.IsSet(Editor.VerticalTextAlignmentProperty))
				? VAlignName(vertical.VerticalTextAlignment) : string.Empty,
			["mauiLetterSpacing"] = input.CharacterSpacing * density,
		};
	}

	private static string VAlignName(TextAlignment alignment) => alignment switch
	{
		TextAlignment.Center => "center",
		TextAlignment.End => "bottom",
		_ => "top",
	};

	/// <summary>
	/// The Picker snapshot: display strings (the Silica ComboBox menu is text-only), title and selected index.
	/// ItemDisplayBinding with a simple property path resolves managed-side; anything else uses ToString().
	/// </summary>
	internal static Dictionary<string, object?> PickerProps(Picker picker)
	{
		var props = new Dictionary<string, object?>
		{
			["mauiTitle"] = picker.Title ?? string.Empty,
			["mauiItems"] = ItemStrings(picker),
			["mauiSelectedIndex"] = picker.SelectedIndex,
			["mauiTitleColor"] = picker.TitleColor ?? Colors.Transparent,
			["mauiOpen"] = picker.IsOpen,
			// Only what the app set moves the ValueButton row; Start/End are logical (the adapter mirrors under RTL).
			["mauiHAlign"] = !picker.IsSet(Picker.HorizontalTextAlignmentProperty) ? string.Empty
				: picker.HorizontalTextAlignment switch
				{
					TextAlignment.Center => "center",
					TextAlignment.End => "end",
					_ => "start",
				},
			["mauiVAlign"] = picker.IsSet(Picker.VerticalTextAlignmentProperty) ? VAlignName(picker.VerticalTextAlignment) : string.Empty,
		};
		AddTextStyle(props, picker, Picker.FontSizeProperty, picker.FontSize, picker.FontFamily,
			picker.FontAttributes, picker.CharacterSpacing, picker.TextColor);
		return props;
	}

	/// <summary>Text styling for an adapter with one text item: only what the app set changes the Silica look.
	/// CharacterSpacing is applied by the shim on mauiTextItem (Qt 5.6 QML has no absolute tracking).</summary>
	internal static void AddTextStyle(Dictionary<string, object?> props, BindableObject element, BindableProperty fontSizeProperty,
	                                  double fontSize, string? family, FontAttributes attributes, double characterSpacing, Color? textColor)
	{
		AddFont(props, element, fontSizeProperty, fontSize, family, attributes, characterSpacing);
		props["mauiTextColor"] = textColor ?? Colors.Transparent;
	}

	/// <summary>Font props that cross only when the app set them (0 size = keep the Silica theme size).</summary>
	internal static void AddFont(Dictionary<string, object?> props, BindableObject element, BindableProperty fontSizeProperty,
	                             double fontSize, string? family, FontAttributes attributes, double characterSpacing)
	{
		var density = SailfishDisplay.Density;
		props["mauiPixelSize"] = HasAppFontSize(element, fontSizeProperty, fontSize) ? fontSize * density : 0.0;
		props["mauiFamily"] = QtHostFonts.Resolve(family);
		props["mauiBold"] = (attributes & FontAttributes.Bold) != 0;
		props["mauiItalic"] = (attributes & FontAttributes.Italic) != 0;
		props["mauiLetterSpacing"] = characterSpacing * density;
	}

	/// <summary>Date/TimePicker value text in the MAUI Format (default "d" / "t", current culture).</summary>
	internal static string DateValueText(DatePicker picker) =>
		picker.Date is { } date ? FormatOrDefault(date, picker.Format, "d") : string.Empty;

	internal static string TimeValueText(TimePicker picker) =>
		picker.Time is { } time ? FormatOrDefault(DateTime.Today.Add(time), picker.Format, "t") : string.Empty;

	private static string FormatOrDefault(DateTime value, string? format, string fallback)
	{
		try
		{
			return value.ToString(string.IsNullOrEmpty(format) ? fallback : format, System.Globalization.CultureInfo.CurrentCulture);
		}
		catch (FormatException)
		{
			return value.ToString(fallback, System.Globalization.CultureInfo.CurrentCulture);
		}
	}

	internal static Dictionary<string, object?> TimePickerProps(TimePicker picker)
	{
		var time = picker.Time ?? TimeSpan.Zero;
		var props = new Dictionary<string, object?>
		{
			["mauiHour"] = time.Hours,
			["mauiMinute"] = time.Minutes,
			["mauiValueText"] = TimeValueText(picker),
			["mauiOpen"] = picker.IsOpen,
		};
		AddTextStyle(props, picker, TimePicker.FontSizeProperty, picker.FontSize, picker.FontFamily,
			picker.FontAttributes, picker.CharacterSpacing, picker.TextColor);
		return props;
	}

	/// <summary>RadioButton content, text styling and the optional control border.</summary>
	internal static Dictionary<string, object?> RadioButtonProps(RadioButton radio)
	{
		var density = SailfishDisplay.Density;
		var props = new Dictionary<string, object?>
		{
			["text"] = radio.Content?.ToString() ?? string.Empty,
			["checked"] = radio.IsChecked,
			["mauiStrokeColor"] = radio.BorderColor ?? Colors.Transparent,
			["mauiStrokeWidth"] = radio.BorderWidth >= 0 ? radio.BorderWidth * density : -1.0,
			["mauiCornerRadius"] = radio.CornerRadius >= 0 ? radio.CornerRadius * density : -1.0,
		};
		AddTextStyle(props, radio, RadioButton.FontSizeProperty, radio.FontSize, radio.FontFamily,
			radio.FontAttributes, radio.CharacterSpacing, radio.TextColor);
		return props;
	}

	private static List<string> ItemStrings(Picker picker)
	{
		var items = new List<string>();
		if (picker.ItemsSource is not System.Collections.IEnumerable source)
			return items;
		var path = (picker.ItemDisplayBinding as Binding)?.Path;
		foreach (var item in source)
			items.Add(DisplayText(item, path));
		return items;
	}

	[System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075",
		Justification = "ItemDisplayBinding paths address application model properties; such models must be rooted by the application (docs/aot-and-trimming.md, plan item 1.2). The sample never sets ItemDisplayBinding, so acceptance never rides this path.")]
	private static string DisplayText(object? item, string? path)
	{
		if (item is null)
			return string.Empty;
		if (!string.IsNullOrEmpty(path))
		{
			try
			{
				var prop = item.GetType().GetProperty(path!);
				if (prop is not null)
					return prop.GetValue(item)?.ToString() ?? string.Empty;
			}
			catch (Exception ex)
			{
				QtHostDiag.Warn(QtHostDiagChannel.QmlObject, $"Picker ItemDisplayBinding path '{path}' failed on {item.GetType().Name}: {ex.Message}");
			}
		}
		return item.ToString() ?? string.Empty;
	}

	/// <summary>
	/// The DatePicker snapshot. Dates cross as epoch ms of local midnight, so a JS Date reads the same wall-clock
	/// day back in the device timezone (the Silica DatePickerDialog encoding); null crosses as 0 (unset).
	/// </summary>
	internal static Dictionary<string, object?> DatePickerProps(DatePicker datePicker)
	{
		var props = new Dictionary<string, object?>
		{
			["mauiDateMs"] = datePicker.Date is { } date ? LocalMidnightMs(date) : 0.0,
			["mauiMinMs"] = datePicker.MinimumDate is { } min ? LocalMidnightMs(min) : 0.0,
			["mauiMaxMs"] = datePicker.MaximumDate is { } max ? LocalMidnightMs(max) : 0.0,
			["mauiValueText"] = DateValueText(datePicker),
			["mauiOpen"] = datePicker.IsOpen,
		};
		AddTextStyle(props, datePicker, DatePicker.FontSizeProperty, datePicker.FontSize, datePicker.FontFamily,
			datePicker.FontAttributes, datePicker.CharacterSpacing, datePicker.TextColor);
		return props;
	}

	private static double LocalMidnightMs(DateTime date)
	{
		var midnight = new DateTime(date.Year, date.Month, date.Day);
		var offset = TimeZoneInfo.Local.GetUtcOffset(midnight);
		return new DateTimeOffset(midnight, offset).ToUnixTimeMilliseconds();
	}

	/// <summary>MAUI ReturnType → the Silica EnterKey icon on the VKB ("" keeps the default).</summary>
	internal static string EnterKeyIcon(ReturnType returnType) => returnType switch
	{
		ReturnType.Next => "image://theme/icon-m-enter-next",
		ReturnType.Search => "image://theme/icon-m-search",
		ReturnType.Done or ReturnType.Go or ReturnType.Send => "image://theme/icon-m-enter-accept",
		_ => string.Empty,
	};

	/// <summary>MAUI Keyboard → Qt 5.6 inputMethodHints (values from QtCore/qnamespace.h). Keyboard statics are
	/// singletons, so reference comparison identifies them; password hints are composed natively from echoMode.</summary>
	internal static int MapInputMethodHints(Keyboard? keyboard, bool textPredictionEnabled)
	{
		const int ImhNoAutoUppercase = 0x4;
		const int ImhPreferNumbers = 0x8;
		const int ImhNoPredictiveText = 0x40;
		const int ImhFormattedNumbersOnly = 0x20000;
		const int ImhDialableCharactersOnly = 0x100000;
		const int ImhEmailCharactersOnly = 0x200000;
		const int ImhUrlCharactersOnly = 0x400000;

		var hints = 0;
		if (ReferenceEquals(keyboard, Keyboard.Email))
			hints |= ImhEmailCharactersOnly | ImhNoAutoUppercase;
		else if (ReferenceEquals(keyboard, Keyboard.Url))
			hints |= ImhUrlCharactersOnly | ImhNoAutoUppercase;
		else if (ReferenceEquals(keyboard, Keyboard.Telephone))
			hints |= ImhDialableCharactersOnly;
		else if (ReferenceEquals(keyboard, Keyboard.Numeric))
			hints |= ImhFormattedNumbersOnly | ImhPreferNumbers;
		else if (ReferenceEquals(keyboard, Keyboard.Plain))
			hints |= ImhNoPredictiveText;
		else if (keyboard is CustomKeyboard custom && (custom.Flags & KeyboardFlags.Suggestions) == 0)
			hints |= ImhNoPredictiveText;   // Keyboard.Create(None) and friends
		if (!textPredictionEnabled)
			hints |= ImhNoPredictiveText;
		return hints;
	}

	/// <summary>MAUI MaxLength (int.MaxValue = unlimited) → Qt TextInput maximumLength (default cap 32767).</summary>
	internal static int ClampMaxLength(int maxLength) =>
		maxLength is <= 0 or >= 32767 ? 32767 : maxLength;

	/// <summary>Label text crossing the bridge (FormattedText spans flattened).</summary>
	private static string LabelText(Label label) =>
		label.FormattedText is { Spans.Count: > 0 }
			? string.Concat(label.FormattedText.Spans.Select(s => s.Text))
			: label.Text ?? string.Empty;

	/// <summary>
	/// The full label snapshot (see qml/controls/Label.qml). Sizes cross in device px, Qt enums as plain ints,
	/// and FormattedText spans as escaped HTML rich text. "text" must precede "mauiTextFormat": the batch
	/// preserves insertion order.
	/// </summary>
	internal static Dictionary<string, object?> LabelProps(Label label)
	{
		var density = SailfishDisplay.Density;
		var spans = label.FormattedText is { Spans.Count: > 0 } ? label.FormattedText.Spans : null;
		var html = spans is null && label.TextType == TextType.Html;
		return new Dictionary<string, object?>
		{
			// "text" is the display string: escaped HTML with spans, the app's HTML with TextType.Html (both
			// RichText), plain text with TextTransform applied otherwise. HTML stays untransformed: upper-casing it
			// would break entities.
			["text"] = spans is not null ? BuildSpanHtml(spans, density)
				: html ? label.Text ?? string.Empty
				: Microsoft.Maui.Controls.Internals.TextTransformUtilities.GetTransformedText(label.Text ?? string.Empty, label.TextTransform),
			["mauiTextFormat"] = spans is not null || html ? 1 : 0,   // Text.RichText / Text.PlainText
			["mauiEmphasis"] = label.FontSize >= 24 ? "header" : "normal",
			// Transparent keeps the emphasis-derived theme color.
			["mauiColor"] = label.TextColor ?? Colors.Transparent,
			["mauiBackground"] = label.BackgroundColor ?? Colors.Transparent,
			["mauiPixelSize"] = label.FontSize > 0 ? label.FontSize * density : 0.0,
			["mauiFamily"] = QtHostFonts.Resolve(label.FontFamily),
			["mauiBold"] = (label.FontAttributes & FontAttributes.Bold) != 0,
			["mauiItalic"] = (label.FontAttributes & FontAttributes.Italic) != 0,
			["mauiUnderline"] = (label.TextDecorations & TextDecorations.Underline) != 0,
			["mauiStrike"] = (label.TextDecorations & TextDecorations.Strikethrough) != 0,
			["mauiLetterSpacing"] = label.CharacterSpacing * density,
			["mauiLineHeight"] = label.LineHeight > 0 ? label.LineHeight : 1.0,
			["mauiMaxLines"] = label.MaxLines > 0 ? label.MaxLines : 0,
			["mauiWrap"] = MapWrapMode(label.LineBreakMode),
			["mauiElide"] = MapElideMode(label.LineBreakMode),
			["mauiHAlign"] = MapHAlign(label.HorizontalTextAlignment, IsRightToLeft(label)),
			["mauiVAlign"] = MapVAlign(label.VerticalTextAlignment),
			// Label.Padding insets the text inside the background box.
			["mauiPadL"] = label.Padding.Left * density,
			["mauiPadT"] = label.Padding.Top * density,
			["mauiPadR"] = label.Padding.Right * density,
			["mauiPadB"] = label.Padding.Bottom * density,
		};
	}

	/// <summary>The WebView snapshot: a URL or an HTML string (+ base URL). The tick re-loads when Source changes
	/// to an equal value.</summary>
	internal static Dictionary<string, object?> WebViewProps(WebView web)
	{
		var props = new Dictionary<string, object?>
		{
			["mauiUrl"] = string.Empty,
			["mauiHtml"] = string.Empty,
			["mauiBaseUrl"] = string.Empty,
			["mauiSourceTick"] = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(web.Source ?? (object)web),
			// WebView.UserAgent → Gecko httpUserAgent ("" = default).
			["mauiUserAgent"] = web.UserAgent ?? string.Empty,
		};
		switch (web.Source)
		{
			case UrlWebViewSource url:
				props["mauiUrl"] = url.Url ?? string.Empty;
				break;
			case HtmlWebViewSource html:
				props["mauiHtml"] = html.Html ?? string.Empty;
				props["mauiBaseUrl"] = html.BaseUrl ?? string.Empty;
				break;
		}
		return props;
	}


	/// <summary>The SwipeView snapshot: Left/Right items as JSON, modes and threshold. Top/Bottom items are not
	/// rendered yet (warned once).</summary>
	internal static Dictionary<string, object?> SwipeProps(SwipeView swipe)
	{
		if ((swipe.TopItems?.Count > 0 || swipe.BottomItems?.Count > 0) && _swipeWarned.Add(swipe))
			QtHostDiag.Warn(QtHostDiagChannel.QtHost, "SwipeView Top/BottomItems are not rendered yet (horizontal swipes only)");
		var props = ContainerProps(swipe);
		props["mauiLeftItems"] = SwipeItemsJson(swipe.LeftItems);
		props["mauiRightItems"] = SwipeItemsJson(swipe.RightItems);
		props["mauiLeftMode"] = swipe.LeftItems?.Mode == SwipeMode.Execute ? "execute" : "reveal";
		props["mauiRightMode"] = swipe.RightItems?.Mode == SwipeMode.Execute ? "execute" : "reveal";
		props["mauiThreshold"] = swipe.Threshold > 0 ? swipe.Threshold * SailfishDisplay.Density : 0.0;
		// Reveal (MAUI default: content slides over the items) or Drag (items travel with the content).
		props["mauiTransition"] = ((ISwipeView)swipe).SwipeTransitionMode == SwipeTransitionMode.Drag ? "drag" : "reveal";
		return props;
	}

	private static readonly HashSet<SwipeView> _swipeWarned = new();

	/// <summary>The visible items of one side, in the order the adapter shows (and reports) them.</summary>
	internal static List<ISwipeItem> VisibleSwipeItems(SwipeItems? items)
	{
		var list = new List<ISwipeItem>();
		if (items is null)
			return list;
		foreach (var element in items)
			if (element is SwipeItem { IsVisible: true } or SwipeItemView { IsVisible: true })
				list.Add((ISwipeItem)element);
		return list;
	}

	private static string SwipeItemsJson(SwipeItems? items)
	{
		var list = new List<Dictionary<string, object?>>();
		foreach (var element in VisibleSwipeItems(items))
		{
			switch (element)
			{
				case SwipeItem item:
					list.Add(new Dictionary<string, object?>
					{
						["text"] = item.Text ?? string.Empty,
						["icon"] = QtHostImages.Resolve(item.IconImageSource) ?? string.Empty,
						["bg"] = item.BackgroundColor is { } bg ? BridgeValue.ColorString(bg) : string.Empty,
					});
					break;
				case SwipeItemView view:
					list.Add(SwipeItemViewJson(view));
					break;
			}
		}
		return BridgeValue.Serialize(list);   // trimmed app: no reflection-based JsonSerializer
	}

	/// <summary>
	/// A SwipeItemView as a Silica swipe action: its custom content is not hosted, so it shows the first opaque
	/// background, Image and Label found in it (an icon in a coloured circle reads as that icon on that colour).
	/// </summary>
	private static Dictionary<string, object?> SwipeItemViewJson(SwipeItemView view)
	{
		Color? bg = null;
		string? icon = null, text = null;
		var pending = new Stack<Element>();
		pending.Push(view);
		while (pending.Count > 0 && (bg is null || icon is null || text is null))
		{
			var element = pending.Pop();
			if (element is VisualElement { IsVisible: false })
				continue;
			if (bg is null && element is VisualElement visual && QtHostPaint.Background(visual) is { Alpha: > 0 } fill)
				bg = fill;
			switch (element)
			{
				case Image image when icon is null:
					icon = QtHostImages.Resolve(image.Source);
					break;
				case Label label when text is null && !string.IsNullOrEmpty(label.Text):
					text = label.Text;
					break;
			}
			var children = ((IVisualTreeElement)element).GetVisualChildren();
			for (var i = children.Count - 1; i >= 0; i--)
				if (children[i] is Element child)
					pending.Push(child);
		}
		return new Dictionary<string, object?>
		{
			["text"] = text ?? string.Empty,
			["icon"] = icon ?? string.Empty,
			["bg"] = bg is { } color ? BridgeValue.ColorString(color) : string.Empty,
		};
	}

	/// <summary>The Stepper snapshot.</summary>
	internal static Dictionary<string, object?> StepperProps(Stepper stepper) => new()
	{
		["value"] = stepper.Value,
		["mauiMinimum"] = stepper.Minimum,
		["mauiMaximum"] = stepper.Maximum,
		["mauiIncrement"] = stepper.Increment,
	};

	/// <summary>The IndicatorView dot-strip snapshot; unset colours keep the Silica palette.</summary>
	internal static Dictionary<string, object?> IndicatorProps(IndicatorView indicator)
	{
		var density = SailfishDisplay.Density;
		return new Dictionary<string, object?>
		{
			["mauiCount"] = indicator.Count,
			["mauiPosition"] = indicator.Position,
			["mauiDotColor"] = indicator.IndicatorColor ?? Colors.Transparent,
			["mauiSelectedColor"] = indicator.SelectedIndicatorColor ?? Colors.Transparent,
			["mauiDotSize"] = (indicator.IndicatorSize > 0 ? indicator.IndicatorSize : 6) * density,
			["mauiSquare"] = indicator.IndicatorsShape == IndicatorShape.Square,
			["mauiMaxVisible"] = indicator.MaximumVisible,
			["mauiHideSingle"] = indicator.HideSingle,
		};
	}

	/// <summary>Whether the app chose this font size. Once the handler attaches, MAUI stores the platform
	/// default as a set value without PropertyChanged, so that default counts as unset.</summary>
	internal static bool HasAppFontSize(BindableObject element, BindableProperty property, double size) =>
		element.IsSet(property) && size > 0 && Math.Abs(size - SailfishFontManager.DefaultSize) > 0.01;

	/// <summary>The Button snapshot, shared by the reconcile and SailfishButtonHandler so both push one spec.</summary>
	internal static Dictionary<string, object?> ButtonProps(Button button)
	{
		var buttonProps = new Dictionary<string, object?> { ["text"] = button.Text ?? string.Empty };
		// The label colour and the plate tint, with whether the app set them: unset hands Silica's own colours back,
		// so clearing a colour (TextColor = null, a VisualState setter that ends) restores the theme. An explicit
		// transparent plate is set too: the app is asking for no plate.
		buttonProps["mauiTextColor"] = button.TextColor ?? Colors.Transparent;
		buttonProps["mauiTextColorSet"] = button.TextColor is not null;
		var plateSet = HasExplicitBackground(button);
		buttonProps["mauiPlateColor"] = plateSet ? EffectiveBackground(button) : Colors.Transparent;
		buttonProps["mauiPlateSet"] = plateSet;
		// Other styling crosses only when set, so the Silica look stays (MAUI's default FontSize would otherwise
		// override Theme.fontSizeMedium).
		AddFont(buttonProps, button, Button.FontSizeProperty, button.FontSize, button.FontFamily,
			button.FontAttributes, button.CharacterSpacing);
		var density = SailfishDisplay.Density;
		buttonProps["mauiCornerRadius"] = button.CornerRadius >= 0 ? button.CornerRadius * density : -1.0;
		buttonProps["mauiStrokeColor"] = button.BorderColor ?? Colors.Transparent;
		buttonProps["mauiStrokeWidth"] = button.BorderWidth >= 0 ? button.BorderWidth * density : -1.0;
		buttonProps["mauiIconSource"] = QtHostImages.Resolve(button.ImageSource) ?? string.Empty;
		// Button.ContentLayout: where the image sits against the text, and the gap (px).
		buttonProps["mauiIconPosition"] = button.ContentLayout.Position switch
		{
			Button.ButtonContentLayout.ImagePosition.Top => "top",
			Button.ButtonContentLayout.ImagePosition.Bottom => "bottom",
			Button.ButtonContentLayout.ImagePosition.Right => "right",
			_ => "left",
		};
		buttonProps["mauiIconSpacing"] = button.ContentLayout.Spacing * density;
		// The plate (MAUI's background) covers the whole frame when the app sized the button or stacks its image;
		// otherwise it keeps Silica's itemSizeExtraSmall height inside the frame.
		buttonProps["mauiFillPlate"] = button.HeightRequest > 0 || button.MinimumHeightRequest > 0 ||
			(button.ImageSource is not null && button.ContentLayout.Position is
				Button.ButtonContentLayout.ImagePosition.Top or Button.ButtonContentLayout.ImagePosition.Bottom);
		// An unset FontSize paints at Theme.fontSizeMedium, larger than the 14 dp MAUI layouts are made for: let the
		// label shrink to fit a narrower button (a fixed 100 dp column) down to that 14 dp instead of fading out.
		buttonProps["mauiFitPixelSize"] = HasAppFontSize(button, Button.FontSizeProperty, button.FontSize)
			? 0.0
			: Handlers.SailfishMeasure.DefaultFontSize * density;
		return buttonProps;
	}

	/// <summary>The Label properties whose change re-diffs LabelProps.</summary>
	private static bool IsLabelVisualProperty(string propertyName) => propertyName is
		nameof(Label.Text) or nameof(Label.FormattedText) or nameof(Label.TextColor) or
		nameof(Label.BackgroundColor) or nameof(Label.FontSize) or nameof(Label.FontFamily) or
		nameof(Label.FontAttributes) or nameof(Label.TextDecorations) or
		nameof(Label.HorizontalTextAlignment) or nameof(Label.VerticalTextAlignment) or
		nameof(Label.LineBreakMode) or nameof(Label.MaxLines) or nameof(Label.LineHeight) or
		nameof(Label.CharacterSpacing) or nameof(Label.Padding) or nameof(Label.TextType) or nameof(Label.TextTransform);

	// Enum values are Qt 5.6 Qt Quick Text constants. Head/Middle truncation = elided single line; Tail = wrapped
	// with the last visible line elided.

	private static int MapWrapMode(LineBreakMode mode) => mode switch
	{
		LineBreakMode.NoWrap or LineBreakMode.HeadTruncation or LineBreakMode.MiddleTruncation => 0,
		LineBreakMode.CharacterWrap => 3,
		_ => 1,
	};

	private static int MapElideMode(LineBreakMode mode) => mode switch
	{
		LineBreakMode.HeadTruncation => 1,
		LineBreakMode.MiddleTruncation => 2,
		LineBreakMode.TailTruncation => 3,
		_ => 0,
	};

	/// <summary>Start/End follow the effective FlowDirection; MAUI's layout already mirrors positions, text
	/// alignment is the adapter's part.</summary>
	private static int MapHAlign(TextAlignment alignment, bool rightToLeft = false) => alignment switch
	{
		TextAlignment.Center => 4,                        // Text.AlignHCenter
		TextAlignment.End => rightToLeft ? 1 : 2,         // Text.AlignLeft / AlignRight
		_ => rightToLeft ? 2 : 1,                         // Start/Fill
	};

	/// <summary>Whether the effective flow direction is RTL: the nearest ancestor-or-self with an explicit
	/// FlowDirection decides. MAUI resolves this through the platform view tree, which this host lacks.</summary>
	internal static bool IsRightToLeft(Element? element)
	{
		for (var e = element; e is not null; e = e.Parent)
		{
			if (e is VisualElement ve && ve.FlowDirection != FlowDirection.MatchParent)
				return ve.FlowDirection == FlowDirection.RightToLeft;
		}
		return false;
	}

	private static int MapVAlign(TextAlignment alignment) => alignment switch
	{
		TextAlignment.Center => 128, // Text.AlignVCenter
		TextAlignment.End => 64,     // Text.AlignBottom
		_ => 32,                     // Text.AlignTop (Start/Fill)
	};

	/// <summary>
	/// FormattedText spans → escaped HTML rich text (colors as "#AARRGGBB", sizes in device px). Per-span
	/// CharacterSpacing/LineHeight are not supported; the label-level values apply.
	/// </summary>
	private static string BuildSpanHtml(IList<Span> spans, double density)
	{
		var sb = new StringBuilder();
		foreach (var span in spans)
		{
			var text = span.Text ?? string.Empty;
			if (text.Length == 0)
				continue;
			sb.Append("<span style=\"");
			if (span.TextColor is { } color)
				sb.Append("color:").Append(BridgeValue.ColorString(color)).Append(';');
			if ((span.FontAttributes & FontAttributes.Bold) != 0)
				sb.Append("font-weight:bold;");
			if ((span.FontAttributes & FontAttributes.Italic) != 0)
				sb.Append("font-style:italic;");
			if (!string.IsNullOrEmpty(span.FontFamily))
				sb.Append("font-family:'").Append(QtHostFonts.Resolve(span.FontFamily).Replace("\\", "\\\\").Replace("'", "\\'")).Append("';");
			if (span.FontSize > 0)
				sb.Append("font-size:").Append((span.FontSize * density).ToString("F0", CultureInfo.InvariantCulture)).Append("px;");
			var underline = (span.TextDecorations & TextDecorations.Underline) != 0;
			var strike = (span.TextDecorations & TextDecorations.Strikethrough) != 0;
			if (underline && strike)
				sb.Append("text-decoration:underline line-through;");
			else if (underline)
				sb.Append("text-decoration:underline;");
			else if (strike)
				sb.Append("text-decoration:line-through;");
			sb.Append("\">").Append(HtmlEscape(text)).Append("</span>");
		}
		return sb.ToString();
	}

	/// <summary>HTML-escapes span text (newlines become &lt;br/&gt;).</summary>
	private static string HtmlEscape(string text)
	{
		var sb = new StringBuilder(text.Length + 8);
		foreach (var c in text)
		{
			switch (c)
			{
				case '&': sb.Append("&amp;"); break;
				case '<': sb.Append("&lt;"); break;
				case '>': sb.Append("&gt;"); break;
				case '"': sb.Append("&quot;"); break;
				case '\n': sb.Append("<br/>"); break;
				case '\r': break;
				default: sb.Append(c); break;
			}
		}
		return sb.ToString();
	}

	private static int CountItems(object? source) => source switch
	{
		null => 0,
		System.Collections.ICollection collection => collection.Count,
		System.Collections.IEnumerable enumerable => enumerable.Cast<object>().Count(),
		_ => 0,
	};
}
