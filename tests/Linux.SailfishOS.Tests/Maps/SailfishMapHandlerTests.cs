using System.Text.Json;
using Linux.SailfishOS.Tests.Renderer;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Maps;
using Microsoft.Maui.SailfishOS.Hosting;
using Microsoft.Maui.SailfishOS.Maps;
using Microsoft.Maui.SailfishOS.Platform;
using Xunit;
using Map = Microsoft.Maui.Controls.Maps.Map;

namespace Linux.SailfishOS.Tests.Maps;

/// <summary>The Sailfish Map handler (src/Linux.SailfishOS.Maps): Controls.Maps' Map as one snapshot of the QtLocation
/// adapter, kept current as pins and elements change, and the adapter's events back into MAUI.</summary>
[Collection("renderer")]
public class SailfishMapHandlerTests
{
	private sealed class TestApp : Application
	{
	}

	public SailfishMapHandlerTests()
	{
		SailfishDispatcherProvider.BindLoopThread();
		if (Microsoft.Maui.Dispatching.DispatcherProvider.Current is not SailfishDispatcherProvider)
			Microsoft.Maui.Dispatching.DispatcherProvider.SetCurrent(new SailfishDispatcherProvider());
	}

	private static IServiceProvider MapServices()
	{
		SailfishMaps.Register();
		var builder = MauiApp.CreateBuilder(useDefaults: false);
		builder.UseMauiAppSailfish<TestApp>();
		builder.UseMauiMaps();
		return builder.Build().Services;
	}

	private static (RendererHarness H, FakeShim.FakeObject Native) Render(Map map)
	{
		map.HeightRequest = 400;
		var h = new RendererHarness(new ContentPage { Content = new VerticalStackLayout { Children = { map } } }, MapServices());
		for (var i = 0; i < 3; i++)
			h.Poll();
		var native = h.Shim.ByUri(SailfishMapHandler.AdapterName).FirstOrDefault()
			?? throw new InvalidOperationException($"no map host; handler {map.Handler?.GetType().Name}, hosts: " +
				string.Join(",", h.Shim.Objects.Select(o => o.Uri).Distinct()));
		return (h, native);
	}

	private static Pin Pin(string label, double lat, double lon) => new() { Label = label, Location = new Location(lat, lon) };

	private static JsonElement Json(FakeShim.FakeObject native, string key) => JsonDocument.Parse(native.Text(key)!).RootElement;

	private static void Drain()
	{
		var loop = SailfishDispatcherProvider.BindLoopThread();
		loop.DrainQueue();
	}

	[Fact]
	public void A_map_with_pins_and_a_polyline_is_one_snapshot()
	{
		var map = new Map { MapType = MapType.Hybrid, IsShowingUser = true };
		map.Pins.Add(Pin("Start", 49.27, 19.98));
		var line = new Polyline { StrokeColor = Colors.Red, StrokeWidth = 4 };
		line.Geopath.Add(new Location(49.27, 19.98));
		line.Geopath.Add(new Location(49.23, 19.98));
		map.MapElements.Add(line);
		var (h, native) = Render(map);
		using var _ = h;

		Assert.Equal("Hybrid", native.Text("mapType"));
		Assert.Equal("true", native.Text("showUser")?.ToLowerInvariant());
		var pin = Json(native, "pins")[0];
		Assert.Equal("Start", pin[3].GetString());
		Assert.Equal(49.27, pin[1].GetDouble());
		var element = Json(native, "elements")[0];
		Assert.Equal("polyline", element.GetProperty("kind").GetString());
		Assert.Equal(2, element.GetProperty("path").GetArrayLength());
		Assert.Equal("#FFFF0000", element.GetProperty("stroke").GetString());
	}

	[Fact]
	public void Pins_added_later_and_a_moved_pin_reach_the_adapter()
	{
		var map = new Map();
		var (h, native) = Render(map);
		using var _ = h;
		var pin = Pin("A", 50, 20);

		map.Pins.Add(pin);
		Drain();
		Assert.Equal(1, Json(native, "pins").GetArrayLength());

		pin.Location = new Location(51, 21);
		Drain();
		Assert.Equal(51, Json(native, "pins")[0][1].GetDouble());
	}

	[Fact]
	public void Adapter_events_reach_the_map_and_its_pins()
	{
		var map = new Map();
		var pin = Pin("A", 50, 20);
		var markers = 0;
		pin.MarkerClicked += (_, e) => { markers++; e.HideInfoWindow = true; };
		map.Pins.Add(pin);
		Location? clicked = null;
		map.MapClicked += (_, e) => clicked = e.Location;
		var (h, native) = Render(map);
		using var _ = h;

		h.Renderer.HandleNativeEvent("map-clicked", $"{{\"id\":\"{native.Id}\",\"lat\":49.5,\"lon\":20.5}}");
		h.Renderer.HandleNativeEvent("pin-clicked", $"{{\"id\":\"{native.Id}\",\"pin\":0}}");
		h.Renderer.HandleNativeEvent("region-changed",
			$"{{\"id\":\"{native.Id}\",\"lat\":49.2,\"lon\":20.0,\"latDeg\":0.1,\"lonDeg\":0.2}}");

		Assert.Equal(49.5, clicked!.Latitude);
		Assert.Equal(1, markers);
		Assert.Equal(0.2, map.VisibleRegion!.LongitudeDegrees, 6);
	}

	// A map not laid out yet reports NaN, which JSON carries as null: the event was a crash (GetDouble on null).
	[Fact]
	public void An_event_with_null_coordinates_is_ignored()
	{
		var map = new Map();
		var (h, native) = Render(map);
		using var _ = h;

		h.Renderer.HandleNativeEvent("region-changed",
			$"{{\"id\":\"{native.Id}\",\"lat\":null,\"lon\":null,\"latDeg\":null,\"lonDeg\":null}}");
		h.Renderer.HandleNativeEvent("map-clicked", $"{{\"id\":\"{native.Id}\",\"lat\":null,\"lon\":1}}");

		Assert.Null(map.VisibleRegion);
	}

	[Fact]
	public void MoveToRegion_before_the_adapter_is_ready_is_sent_once_it_is()
	{
		var map = new Map();
		var (h, native) = Render(map);
		using var _ = h;
		map.MoveToRegion(MapSpan.FromCenterAndRadius(new Location(49.2, 20), Distance.FromKilometers(2)));
		h.Renderer.HandleNativeEvent("adapter-ready", $"{{\"id\":\"{native.Id}\"}}");

		Assert.Contains(h.Shim.Commands, c => c.Id == native.Id && c.Json.Contains("\"move-to\"") && c.Json.Contains("49.2"));
	}

	[Fact]
	public void Clustering_groups_pins_in_the_same_screen_cell()
	{
		var pins = new IMapPin[]
		{
			Pin("a", 49.000, 20.000), Pin("b", 49.001, 20.001), Pin("c", 49.0005, 20.0008),
			Pin("far", 49.5, 20.5),
		};
		// A 10 km wide view: 64 dp of a 360 dp view is ~0.023°; the first three fall together, the far one alone.
		var clusters = SailfishMapHandler.Cluster(pins, (49.2, 20.2, 0.1, 0.13));
		var only = Assert.Single(clusters);
		Assert.Equal(3, only.Count);
	}
}

