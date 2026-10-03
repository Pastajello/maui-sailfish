using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.SailfishOS.Platform;
using Microsoft.Maui.SailfishOS.Platform.QtHost;

namespace Microsoft.Maui.SailfishOS.Handlers;

// Snapshot builders of the text inputs: Entry/Editor (InputView), SearchBar, their text style and input hints.
// MAUI control state → adapter properties (sizes in device px, Qt enums as ints); handlers call these from Snapshot().
internal static partial class AdapterSnapshots
{
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
		var density = QtHostUnits.ScenePerDp;
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
			["mauiPixelSize"] = SailfishMeasure.AppFontSize(input, Entry.FontSizeProperty, font.Size) is { } size ? size * density : 0.0,
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
}
