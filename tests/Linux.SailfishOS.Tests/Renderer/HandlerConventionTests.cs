using System.Text.Json;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.SailfishOS.Handlers;
using Microsoft.Maui.SailfishOS.Platform.QtHost;
using Xunit;

namespace Linux.SailfishOS.Tests.Renderer;

/// <summary>Tracker S13 (plan M15): SwipeItem colours (MAUI 11), the window as the RTL root, and handler conventions
/// a MAUI developer relies on.</summary>
[Collection("renderer")]
public sealed class HandlerConventionTests
{
	[Fact]
	public void SwipeItem_text_and_icon_colours_reach_the_adapter()
	{
		var swipe = new SwipeView
		{
			Content = new Label { Text = "row" },
			RightItems = new SwipeItems { new SwipeItem { Text = "Delete", TextColor = Colors.Red, IconColor = Colors.Yellow } },
			HeightRequest = 60,
		};
		using var h = new RendererHarness(new ContentPage { Title = "T", Content = new VerticalStackLayout { Children = { swipe } } });
		var host = h.Shim.ByUri("swipe-view").Single();
		using (var doc = JsonDocument.Parse(host.Text("mauiRightItems")!))
		{
			var item = doc.RootElement[0];
			Assert.Equal(BridgeValue.ColorString(Colors.Red), item.GetProperty("fg").GetString());
			Assert.Equal(BridgeValue.ColorString(Colors.Yellow), item.GetProperty("iconColor").GetString());
		}

		((SwipeItem)swipe.RightItems[0]).TextColor = Colors.Green;   // an item's own change is pushed again
		h.Poll();
		using var after = JsonDocument.Parse(host.Text("mauiRightItems")!);
		Assert.Equal(BridgeValue.ColorString(Colors.Green), after.RootElement[0].GetProperty("fg").GetString());
	}

	[Fact]
	public void RightToLeft_on_the_window_mirrors_its_pages()
	{
		var label = new Label { Text = "x" };
		var page = new ContentPage { Content = label };
		_ = new Window(page) { FlowDirection = FlowDirection.RightToLeft };
		Assert.True(QtHostVisualState.IsRightToLeft(label));
		label.FlowDirection = FlowDirection.LeftToRight;   // the nearest explicit value still wins
		Assert.False(QtHostVisualState.IsRightToLeft(label));
	}

	[Fact]
	public void The_navigation_handlers_CommandMapper_is_the_one_it_uses()
	{
		Assert.Same(SailfishNavigationViewHandler.NavigationCommandMapper, SailfishNavigationViewHandler.CommandMapper);
		Assert.NotNull(((ICommandMapper)SailfishNavigationViewHandler.CommandMapper).GetCommand(nameof(IStackNavigation.RequestNavigation)));
	}

	[Fact]
	public void A_clipped_or_shadowed_view_reports_no_container()
	{
		var box = new Border { Shadow = new Shadow { Radius = 4 }, Clip = new Microsoft.Maui.Controls.Shapes.EllipseGeometry() };
		using var h = new RendererHarness(new ContentPage { Title = "T", Content = box });
		var handler = (Microsoft.Maui.Handlers.ViewHandler)box.Handler!;
		Assert.False(handler.NeedsContainer);
		Assert.False(handler.HasContainer);
	}

	[Fact]
	public void Application_and_window_handlers_take_mappers_like_MAUIs()
	{
		Assert.NotNull(typeof(SailfishApplicationHandler).GetConstructor(new[] { typeof(IPropertyMapper), typeof(CommandMapper) }));
		Assert.NotNull(typeof(SailfishWindowHandler).GetConstructor(new[] { typeof(IPropertyMapper), typeof(CommandMapper) }));
		Assert.Contains(nameof(IWindow.Title), SailfishWindowHandler.Mapper.GetKeys());
		Assert.NotNull(((ICommandMapper)SailfishApplicationHandler.CommandMapper).GetCommand("ActivateWindow"));
	}
}
