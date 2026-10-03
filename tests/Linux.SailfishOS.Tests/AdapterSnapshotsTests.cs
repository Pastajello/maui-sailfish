using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.SailfishOS.Handlers;
using Microsoft.Maui.SailfishOS.Platform.QtHost;
using Xunit;

namespace Linux.SailfishOS.Tests;

/// <summary>Snapshot builders (Handlers/Snapshots): MAUI state → adapter props, in the units the geometry uses.</summary>
[Collection("renderer")]   // QtHostUnits.DevicePixelRatio is process-wide
public class AdapterSnapshotsTests
{
	// Props and geometry convert dp the same way (QtHostUnits): with a HiDPI ratio a font and its box still agree.
	[Fact]
	public void Sizes_use_the_geometry_conversion()
	{
		using var statics = new TestStatics();
		QtHostUnits.DevicePixelRatio = 2;
		var props = AdapterSnapshots.LabelProps(new Label { Text = "x", FontSize = 20 });
		Assert.Equal(QtHostUnits.ToQtUnits(20), Convert.ToDouble(props["mauiPixelSize"]), 3);
		var stack = AdapterSnapshots.StackProps(new VerticalStackLayout { Spacing = 10 });
		Assert.Equal(QtHostUnits.ToQtUnits(10), Convert.ToDouble(stack["mauiSpacing"]), 3);
	}

	[Fact]
	public void A_button_colour_carries_whether_the_app_set_it()
	{
		var plain = AdapterSnapshots.ButtonProps(new Button { Text = "a" });
		var styled = AdapterSnapshots.ButtonProps(new Button { Text = "a", TextColor = Colors.White, BackgroundColor = Colors.Blue });
		Assert.Equal((false, false), ((bool)plain["mauiTextColorSet"]!, (bool)plain["mauiPlateSet"]!));
		Assert.Equal((true, true), ((bool)styled["mauiTextColorSet"]!, (bool)styled["mauiPlateSet"]!));
	}
}
