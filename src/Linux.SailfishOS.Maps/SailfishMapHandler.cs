using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Maps;
using Microsoft.Maui.Maps.Handlers;
using Microsoft.Maui.SailfishOS.Handlers;
using IMap = Microsoft.Maui.Maps.IMap;

namespace Microsoft.Maui.SailfishOS.Maps;

/// <summary>
/// <see cref="IMap"/> on QtLocation (qml/SailfishMaps/Map.qml): OpenStreetMap tiles, pins as <c>MapQuickItem</c>s,
/// polylines, polygons and circles as QtLocation map items, the user's position from QtPositioning. The whole map is
/// one snapshot; pins and elements are watched for changes and re-sent (coalesced to one push per loop turn).
/// Clustering (<see cref="IMap.IsClusteringEnabled"/>) groups pins on a screen grid for the visible region.
/// </summary>
public class SailfishMapHandler : SailfishSnapshotHandler<IMap>, IMapHandler
{
	public const string AdapterName = "maui-map";

	/// <summary>Pins closer than this on screen (dp) form one cluster.</summary>
	public const double ClusterCellDp = 64;

	private static readonly string[] Keys =
	{
		nameof(IMap.MapType), nameof(IMap.IsShowingUser), nameof(IMap.IsScrollEnabled), nameof(IMap.IsZoomEnabled),
		nameof(IMap.IsTrafficEnabled), nameof(IMap.IsClusteringEnabled), nameof(IMap.MapStyle), nameof(IMap.Pins),
		nameof(IMap.Elements),
	};

	public static readonly PropertyMapper<IMap, SailfishMapHandler> Mapper = SnapshotMapper<SailfishMapHandler>(Keys);

	public static readonly CommandMapper<IMap, SailfishMapHandler> CommandMapper = new(SailfishViewMapper.CommandMapper)
	{
		[nameof(IMap.MoveToRegion)] = MapMoveToRegion,
		[nameof(IMapHandler.UpdateMapElement)] = (h, _, _) => h.Invalidate(),
		[nameof(IMap.ShowInfoWindow)] = (h, _, arg) => h.SendCommand("show-info", new() { ["pin"] = h.PinIndex(arg as IMapPin) }),
		[nameof(IMap.HideInfoWindow)] = (h, _, _) => h.SendCommand("hide-info"),
	};

	private readonly HashSet<INotifyPropertyChanged> _watched = new();
	private INotifyCollectionChanged? _pinsCollection, _elementsCollection;
	private bool _pushQueued;
	private MapSpan? _pendingRegion;
	private (double Lat, double Lon, double LatDeg, double LonDeg)? _visible;
	private List<List<IMapPin>> _clusters = new();
	private bool _styleWarned;

	public SailfishMapHandler() : base(Mapper, CommandMapper, (_, w, h) => new Size(
		double.IsInfinity(w) ? 320 : w, double.IsInfinity(h) ? 320 : h))
	{
	}

	protected override string? AdapterUri => AdapterName;

	object IMapHandler.PlatformView => PlatformView;

	public void UpdateMapElement(IMapElement element) => Invalidate();

	/// <summary>A region requested before the adapter existed goes out once it is laid out (or says it is ready).</summary>
	public override void PlatformArrange(Rect frame)
	{
		base.PlatformArrange(frame);
		if (_pendingRegion is { } pending && Host is { IsAttached: true } && frame.Width > 0)
		{
			_pendingRegion = null;
			MoveTo(pending, false);
		}
	}

	protected override Dictionary<string, object?>? Snapshot(IMap map)
	{
		Watch(map);
		if (!string.IsNullOrEmpty(map.MapStyle) && !_styleWarned)
		{
			_styleWarned = true;
			Console.Error.WriteLine("[Sailfish][WARN] Map.MapStyle is not supported on Sailfish: QtLocation's OSM plugin has " +
				"fixed tile servers (MapType picks street or topographic)");
		}
		var pins = map.Pins.ToList();
		_clusters = map.IsClusteringEnabled && _visible is { } v ? Cluster(pins, v) : new();
		var clustered = _clusters.SelectMany(c => c).ToHashSet();
		return new()
		{
			["mapType"] = map.MapType.ToString(),
			["showUser"] = map.IsShowingUser,
			["scrollEnabled"] = map.IsScrollEnabled,
			["zoomEnabled"] = map.IsZoomEnabled,
			["pins"] = JsonSerializer.Serialize(pins.Select((p, i) => new object[]
			{
				i, p.Location.Latitude, p.Location.Longitude, p.Label ?? "", p.Address ?? "", clustered.Contains(p) ? 0 : 1,
			})),
			["clusters"] = JsonSerializer.Serialize(_clusters.Select((c, i) => new object[]
			{
				i, c.Average(p => p.Location.Latitude), c.Average(p => p.Location.Longitude), c.Count,
			})),
			["elements"] = JsonSerializer.Serialize(map.Elements.Select(Element).Where(e => e is not null)),
		};
	}

	/// <summary>One element as the adapter draws it: kind, z, visibility, stroke, fill and its geometry.</summary>
	private static Dictionary<string, object?>? Element(IMapElement element, int index)
	{
		var e = element as MapElement;
		var common = new Dictionary<string, object?>
		{
			["i"] = index,
			["z"] = element.ZIndex,
			["visible"] = element.IsVisible,
			["stroke"] = Argb(e?.StrokeColor),
			["width"] = e?.StrokeWidth ?? 1,
		};
		switch (element)
		{
			case Polyline line:
				common["kind"] = "polyline";
				common["path"] = line.Geopath.Select(p => new[] { p.Latitude, p.Longitude });
				return common;
			case Polygon polygon:
				common["kind"] = "polygon";
				common["fill"] = Argb(polygon.FillColor);
				common["path"] = polygon.Geopath.Select(p => new[] { p.Latitude, p.Longitude });
				return common;
			case Circle circle:
				common["kind"] = "circle";
				common["fill"] = Argb(circle.FillColor);
				common["center"] = new[] { circle.Center.Latitude, circle.Center.Longitude };
				common["radius"] = circle.Radius.Meters;
				return common;
			default:
				return null;
		}
	}

	protected override void OnAdapterEvent(string name, JsonElement payload)
	{
		if (ConnectedView is not { } map)
			return;
		// A map not laid out yet reports NaN coordinates, which JSON carries as null: such an event says nothing.
		foreach (var key in new[] { "lat", "lon", "latDeg", "lonDeg" })
			if (payload.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.Number)
				return;
		switch (name)
		{
			case "map-clicked":
				map.Clicked(LocationOf(payload));
				break;
			case "map-long-clicked":
				map.LongClicked(LocationOf(payload));
				break;
			case "pin-clicked" when Pin(map, payload) is { } pin:
				// MarkerClicked handled: no info window (as on Android/iOS).
				if (!pin.SendMarkerClick())
					SendCommand("show-info", new() { ["pin"] = payload.GetProperty("pin").GetInt32() });
				break;
			case "info-clicked" when Pin(map, payload) is { } pin:
				if (pin.SendInfoWindowClick())
					SendCommand("hide-info");
				break;
			case "cluster-clicked":
				var index = payload.GetProperty("cluster").GetInt32();
				if (index >= 0 && index < _clusters.Count)
				{
					var members = _clusters[index];
					var at = LocationOf(payload);
					// Not handled by the app: zoom in so the cluster's pins come apart.
					if (!map.ClusterClicked(members, at))
						map.MoveToRegion(Pad(MapSpan.FromLocations(members.Select(p => p.Location))), true);
				}
				break;
			case "element-clicked":
				var i = payload.GetProperty("element").GetInt32();
				if (i >= 0 && i < map.Elements.Count)
					map.Elements[i].Clicked();
				break;
			case "region-changed":
				var region = (payload.GetProperty("lat").GetDouble(), payload.GetProperty("lon").GetDouble(),
					payload.GetProperty("latDeg").GetDouble(), payload.GetProperty("lonDeg").GetDouble());
				var regrid = map.IsClusteringEnabled && (_visible is null || Math.Abs(_visible.Value.LonDeg - region.Item4) > 1e-9);
				_visible = region;
				map.VisibleRegion = new MapSpan(new Location(region.Item1, region.Item2), region.Item3, region.Item4);
				if (regrid)
					Invalidate();
				break;
			case "user-location":
				map.UserLocationUpdated(LocationOf(payload));
				break;
			case "adapter-ready" when _pendingRegion is { } pending:
				_pendingRegion = null;
				MoveTo(pending, false);
				break;
		}
	}

	/// <summary>Controls' Map sends a <see cref="MoveToRegionRequest"/> (its last region again when the handler
	/// connects, with a null region before any was set); a bare span is accepted too.</summary>
	private static void MapMoveToRegion(SailfishMapHandler handler, IMap map, object? arg)
	{
		if (arg is MoveToRegionRequest { Region: { } region } request)
			handler.MoveTo(region, request.Animated);
		else if (arg is MapSpan span)
			handler.MoveTo(span, animated: true);
	}

	private void MoveTo(MapSpan span, bool animated)
	{
		if (!SendCommand("move-to", new()
		{
			["lat"] = span.Center.Latitude,
			["lon"] = span.Center.Longitude,
			["latDeg"] = span.LatitudeDegrees,
			["lonDeg"] = span.LongitudeDegrees,
			["animated"] = animated,
		}))
			_pendingRegion = span;   // the adapter is not there yet: it asks once it is
	}

	/// <summary>Re-sends the snapshot once on the next loop turn, however many changes arrive in this one.</summary>
	private void Invalidate()
	{
		if (_pushQueued)
			return;
		_pushQueued = true;
		Dispatching.Dispatcher.GetForCurrentThread()?.Dispatch(() =>
		{
			_pushQueued = false;
			PushSnapshot();
		});
	}

	/// <summary>Follows the pins and elements: their own property changes and the collections' membership.</summary>
	private void Watch(IMap map)
	{
		foreach (var item in map.Pins.Cast<object>().Concat(map.Elements))
			if (item is INotifyPropertyChanged inpc && _watched.Add(inpc))
				inpc.PropertyChanged += OnItemChanged;
		Rewatch(ref _pinsCollection, map.Pins as INotifyCollectionChanged);
		Rewatch(ref _elementsCollection, map.Elements as INotifyCollectionChanged);
	}

	private void Rewatch(ref INotifyCollectionChanged? current, INotifyCollectionChanged? next)
	{
		if (ReferenceEquals(current, next))
			return;
		if (current is not null)
			current.CollectionChanged -= OnCollectionChanged;
		current = next;
		if (current is not null)
			current.CollectionChanged += OnCollectionChanged;
	}

	private void OnItemChanged(object? sender, PropertyChangedEventArgs e) => Invalidate();

	private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Invalidate();

	protected override void DisconnectHandler(Platform.QtHost.NativeElementHost platformView)
	{
		foreach (var inpc in _watched)
			inpc.PropertyChanged -= OnItemChanged;
		_watched.Clear();
		Rewatch(ref _pinsCollection, null);
		Rewatch(ref _elementsCollection, null);
		base.DisconnectHandler(platformView);
	}

	/// <summary>Grid clustering: pins in the same screen cell of the visible region form a cluster (two or more).</summary>
	internal static List<List<IMapPin>> Cluster(IReadOnlyList<IMapPin> pins, (double Lat, double Lon, double LatDeg, double LonDeg) visible,
		double viewWidthDp = 360)
	{
		var cellDeg = visible.LonDeg / viewWidthDp * ClusterCellDp;
		if (cellDeg <= 0)
			return new();
		return pins
			.GroupBy(p => (Math.Floor(p.Location.Longitude / cellDeg), Math.Floor(p.Location.Latitude / cellDeg)))
			.Where(g => g.Count() > 1)
			.Select(g => g.ToList())
			.ToList();
	}

	private static MapSpan Pad(MapSpan span) =>
		new(span.Center, Math.Max(span.LatitudeDegrees * 1.6, 0.002), Math.Max(span.LongitudeDegrees * 1.6, 0.002));

	private static IMapPin? Pin(IMap map, JsonElement payload)
	{
		var i = payload.GetProperty("pin").GetInt32();
		return i >= 0 && i < map.Pins.Count ? map.Pins[i] : null;
	}

	private int PinIndex(IMapPin? pin) => pin is null || ConnectedView is not { } map ? -1 : map.Pins.IndexOf(pin);

	private static Location LocationOf(JsonElement payload) =>
		new(payload.GetProperty("lat").GetDouble(), payload.GetProperty("lon").GetDouble());

	private static string Argb(Color? color) => color is null ? "" : color.ToArgbHex(includeAlpha: true);
}
