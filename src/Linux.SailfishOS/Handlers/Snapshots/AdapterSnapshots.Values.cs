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

// Snapshot builders of the value controls: Switch, CheckBox, Slider, Stepper, progress, pickers, RadioButton, IndicatorView.
// MAUI control state → adapter properties (sizes in device px, Qt enums as ints); handlers call these from Snapshot().
internal static partial class AdapterSnapshots
{
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
		["mauiThumbScale"] = QtHostUnits.ScenePerDp,
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
		var density = QtHostUnits.ScenePerDp;
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

	internal static double LocalMidnightMs(DateTime date)
	{
		var midnight = new DateTime(date.Year, date.Month, date.Day);
		var offset = TimeZoneInfo.Local.GetUtcOffset(midnight);
		return new DateTimeOffset(midnight, offset).ToUnixTimeMilliseconds();
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
		var density = QtHostUnits.ScenePerDp;
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
}
