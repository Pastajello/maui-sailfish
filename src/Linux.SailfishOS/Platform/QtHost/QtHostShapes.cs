using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using ShapesPath = Microsoft.Maui.Controls.Shapes.Path;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>
/// Serializes MAUI Shapes and BoxView into PathF op lists plus paint specs for the Canvas adapter
/// <c>qml/shapes/Shape.qml</c>, since Qt 5.6 has no QtQuick.Shapes. The encoding is defined in
/// qml/shapes/pathops.js; values are in device px, except the GraphicsView stream, which stays in dp.
/// </summary>
internal static class QtHostShapes
{
	/// <summary>Adapter URI (qml/adapters.json) of the Canvas shape painter.</summary>
	public const string AdapterUri = "shape";

	// Serializes as [], so "no paint"/"no ops" still crosses the bridge.
	private static readonly List<object?> None = new();

	/// <summary>Converts a PathF into the op list replayed by pathops.js:buildPath.</summary>
	public static List<object?> PathOps(PathF path, double density)
	{
		var ops = new List<object?>();
		var count = path.OperationCount;
		for (var i = 0; i < count; i++)
		{
			var type = path.GetSegmentInfo(i, out _, out var arcAngle, out var arcClockwise);
			var points = path.GetPointsForSegment(i) ?? Array.Empty<PointF>();
			switch (type)
			{
				case PathOperation.Move when points.Length > 0:
					ops.Add(Op("m", density, points[0]));
					break;
				case PathOperation.Line when points.Length > 0:
					ops.Add(Op("l", density, points[0]));
					break;
				case PathOperation.Quad when points.Length > 1:
					ops.Add(Op("q", density, points[0], points[1]));
					break;
				case PathOperation.Cubic when points.Length > 2:
					ops.Add(Op("c", density, points[0], points[1], points[2]));
					break;
				case PathOperation.Arc when points.Length > 1:
				{
					// The arc table holds two angles per segment (start, end) and one clockwise flag.
					var arc = Op("a", density, points[0], points[1]);
					arc.Add(Num(path.GetArcAngle(arcAngle)));
					arc.Add(Num(path.GetArcAngle(arcAngle + 1)));
					arc.Add(path.GetArcClockwise(arcClockwise) ? 1 : 0);
					// connect=0: MAUI's SKPath.AddArc starts a new contour, so QML moves to the arc start.
					arc.Add(0);
					ops.Add(arc);
					break;
				}
				case PathOperation.Close:
					ops.Add(new List<object?> { "z" });
					break;
			}
		}
		return ops;
	}

	/// <summary>
	/// Path bounds used as the Stretch source rect, grown by half the stroke so the outline is not clipped.
	/// Flatness 1 is used because MAUI's 0.001 default reports wrong bounds for curves.
	/// </summary>
	public static RectF NaturalBounds(PathF path, double strokeThickness)
	{
		var b = path.GetBoundsByFlattening(1f);
		var half = (float)(strokeThickness / 2);
		return new RectF(b.X - half, b.Y - half, b.Width + half * 2, b.Height + half * 2);
	}

	/// <summary>Converts a Brush to a paint spec in device px (null = paint nothing).</summary>
	public static List<object?>? PaintSpec(Brush? brush, RectF bounds, double density)
	{
		switch (brush)
		{
			case null:
				return null;
			case SolidColorBrush solid:
				return Solid(solid.Color);
			case LinearGradientBrush linear:
			{
				var stops = Stops(linear.GradientStops);
				if (stops.Count == 0)
					return null;
				return new List<object?>
				{
					"linear",
					Num((bounds.X + linear.StartPoint.X * bounds.Width) * density),
					Num((bounds.Y + linear.StartPoint.Y * bounds.Height) * density),
					Num((bounds.X + linear.EndPoint.X * bounds.Width) * density),
					Num((bounds.Y + linear.EndPoint.Y * bounds.Height) * density),
					stops,
				};
			}
			case RadialGradientBrush radial:
			{
				var stops = Stops(radial.GradientStops);
				if (stops.Count == 0)
					return null;
				// Like MAUI's Skia backend, the normalized radius scales by the larger side.
				var radius = radial.Radius * Math.Max(bounds.Width, bounds.Height);
				return new List<object?>
				{
					"radial",
					Num((bounds.X + radial.Center.X * bounds.Width) * density),
					Num((bounds.Y + radial.Center.Y * bounds.Height) * density),
					Num(radius * density),
					stops,
				};
			}
			default:
				return null;
		}
	}

	/// <summary>Converts a Graphics Paint to a GraphicsView paint spec in dp, anchored to the SetFillPaint rect.</summary>
	public static List<object?>? PaintSpec(Paint? paint, RectF bounds)
	{
		switch (paint)
		{
			case null:
				return null;
			case SolidPaint solid:
				return Solid(solid.Color);
			case LinearGradientPaint linear:
			{
				var stops = Stops(linear.GradientStops);
				if (stops.Count == 0)
					stops = FallbackStops(linear);
				if (stops.Count == 0)
					return Solid(paint.ForegroundColor);
				return new List<object?>
				{
					"linear",
					Num(bounds.X + linear.StartPoint.X * bounds.Width),
					Num(bounds.Y + linear.StartPoint.Y * bounds.Height),
					Num(bounds.X + linear.EndPoint.X * bounds.Width),
					Num(bounds.Y + linear.EndPoint.Y * bounds.Height),
					stops,
				};
			}
			case RadialGradientPaint radial:
			{
				var stops = Stops(radial.GradientStops);
				if (stops.Count == 0)
					stops = FallbackStops(radial);
				if (stops.Count == 0)
					return Solid(paint.ForegroundColor);
				return new List<object?>
				{
					"radial",
					Num(bounds.X + radial.Center.X * bounds.Width),
					Num(bounds.Y + radial.Center.Y * bounds.Height),
					Num(radial.Radius * Math.Max(bounds.Width, bounds.Height)),
					stops,
				};
			}
			default:
				return Solid(paint.ForegroundColor);
		}
	}

	/// <summary>
	/// Full shape snapshot for the adapter. Returns null while the shape has no path yet; the adapter
	/// then keeps its previous ops until a later pass.
	/// </summary>
	public static Dictionary<string, object?>? ShapeProps(Shape shape)
	{
		// Size-derived shapes return a garbage path before the first arrange (Width/Height are -1).
		var sizeDerived = shape is not (Line or Polygon or Polyline or ShapesPath);
		if (sizeDerived && (shape.Width <= 0 || shape.Height <= 0))
			return null;

		PathF path;
		try
		{
			path = shape.GetPath();
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[Sailfish] Qt shapes: GetPath() failed for {shape.GetType().Name}: {ex.Message}");
			return null;
		}
		if (path.OperationCount == 0)
			return null;

		var density = SailfishDisplay.Density;
		var thickness = shape.StrokeThickness;
		var bounds = NaturalBounds(path, thickness);
		var fill = PaintSpec(shape.Fill, bounds, density);
		var stroke = PaintSpec(shape.Stroke, bounds, density);
		double? radius = shape switch
		{
			Rectangle r when r.RadiusX == r.RadiusY => r.RadiusX,
			RoundRectangle rr when rr.CornerRadius.TopLeft == rr.CornerRadius.TopRight &&
			                       rr.CornerRadius.TopLeft == rr.CornerRadius.BottomLeft &&
			                       rr.CornerRadius.TopLeft == rr.CornerRadius.BottomRight => rr.CornerRadius.TopLeft,
			_ => null,
		};
		var plain = radius is not null && shape.Clip is null && (shape.StrokeDashArray?.Count ?? 0) == 0 &&
		            (shape.BackgroundColor?.Alpha ?? 0) <= 0;
		return new Dictionary<string, object?>
		{
			["mauiKind"] = shape.GetType().Name,
			["mauiPathOps"] = PathOps(path, density),
			["mauiNatural"] = Natural(bounds, density),
			["mauiAspect"] = (int)shape.Aspect,
			["mauiFillSpec"] = fill ?? None,
			["mauiStrokeSpec"] = stroke ?? None,
			["mauiRect"] = plain
				? PlainRect(fill, Math.Min(radius!.Value, Math.Min(shape.Width, shape.Height) / 2) * density, stroke, thickness * density)
				: None,
			["mauiStrokeWidth"] = Num(thickness * density),
			["mauiCap"] = (int)shape.StrokeLineCap,
			["mauiJoin"] = (int)shape.StrokeLineJoin,
			["mauiMiter"] = Num(shape.StrokeMiterLimit),
			["mauiDash"] = Dash(thickness, shape.StrokeDashArray, shape.StrokeDashOffset, density),
			// MAUI shapes always fill non-zero; the fill rule is not exposed on Shape.
			["mauiWinding"] = 1,
			["mauiClipOps"] = GeometryOps(shape.Clip, density),
			["mauiBackground"] = shape.BackgroundColor ?? Colors.Transparent,
		};
	}

	/// <summary>BoxView snapshot; BoxView has no PathF, so its (rounded) rectangle path is built here.</summary>
	public static Dictionary<string, object?>? BoxViewProps(BoxView box)
	{
		if (box.Width <= 0 || box.Height <= 0)
			return null;

		var density = SailfishDisplay.Density;
		var w = (float)box.Width;
		var h = (float)box.Height;
		// The -1 "platform default" corner radius paints square here.
		var corner = box.CornerRadius;
		var max = Math.Min((double)w, h) / 2;
		var topLeft = (float)Math.Clamp(corner.TopLeft, 0.0, max);
		var topRight = (float)Math.Clamp(corner.TopRight, 0.0, max);
		var bottomLeft = (float)Math.Clamp(corner.BottomLeft, 0.0, max);
		var bottomRight = (float)Math.Clamp(corner.BottomRight, 0.0, max);
		var path = new PathF();
		if (topLeft > 0 || topRight > 0 || bottomLeft > 0 || bottomRight > 0)
			path.AppendRoundedRectangle(0, 0, w, h, topLeft, topRight, bottomLeft, bottomRight, true);
		else
			path.AppendRectangle(0, 0, w, h, true);
		var bounds = new RectF(0, 0, w, h);
		// Same precedence as MAUI's BoxView handlers: Fill, Background, then Color/BackgroundColor.
		var fill = PaintSpec(box.Fill, bounds, density)
			?? PaintSpec(box.Background, bounds, density)
			?? Solid(box.Color ?? box.BackgroundColor);
		// A BackgroundColor painted UNDER a different fill needs the Canvas.
		var background = box.BackgroundColor;
		// An unset Background is Brush.Default, not null; missing that put every BackgroundColor box on the Canvas,
		// whose per-item GL context exhausts EGL with a few hundred boxes.
		var backgroundIsFill = Brush.IsNullOrEmpty(box.Fill) && Brush.IsNullOrEmpty(box.Background) && box.Color is null;
		// A clear Color over a BackgroundColor shows just the background rectangle (EmployeeDirectory's group footers:
		// a style's BackgroundColor under Color="Transparent"); the Canvas painted it slowly, and only partly.
		var backgroundOnly = !backgroundIsFill && Brush.IsNullOrEmpty(box.Fill) && Brush.IsNullOrEmpty(box.Background) &&
		                     box.Color is { Alpha: <= 0 } && background is { Alpha: > 0 };
		var plain = topLeft == topRight && topLeft == bottomLeft && topLeft == bottomRight && box.Clip is null &&
		            (background is null || background.Alpha <= 0 || backgroundIsFill || backgroundOnly);
		var rect = backgroundOnly ? PlainRect(Solid(background), 0, null, 0)
			: plain ? PlainRect(fill, topLeft * density, null, 0, gradientOk: topLeft <= 0) : None;

		return new Dictionary<string, object?>
		{
			["mauiKind"] = "BoxView",
			["mauiPathOps"] = PathOps(path, density),
			["mauiNatural"] = Natural(bounds, density),
			["mauiAspect"] = (int)Stretch.Fill,
			["mauiFillSpec"] = fill ?? None,
			["mauiRect"] = rect,
			// Square gradient boxes use a QtGraphicalEffects gradient instead of the Canvas.
			["mauiRectGradient"] = plain && topLeft <= 0 && fill is not null && !Equals(fill[0], "solid") ? fill : None,
			["mauiStrokeSpec"] = None,
			["mauiStrokeWidth"] = 0.0,
			["mauiCap"] = 0,
			["mauiJoin"] = 0,
			["mauiMiter"] = 10.0,
			["mauiDash"] = None,
			["mauiWinding"] = 1,
			["mauiClipOps"] = GeometryOps(box.Clip, density),
			["mauiBackground"] = box.BackgroundColor ?? Colors.Transparent,
		};
	}

	/// <summary>
	/// [fill, radius px, stroke, stroke px] for drawing a solid rectangle with a plain QML Rectangle,
	/// which is far cheaper than a Canvas paint. Returns [] when the paint needs the Canvas.
	/// </summary>
	private static List<object?> PlainRect(List<object?>? fill, double radiusPx, List<object?>? stroke, double strokePx, bool gradientOk = false)
	{
		if (stroke is not null && !Equals(stroke[0], "solid"))
			return None;
		if (fill is not null && !Equals(fill[0], "solid"))
		{
			if (!gradientOk)
				return None;
			fill = null;   // painted by mauiRectGradient; the Rectangle stays transparent
		}
		return new List<object?>
		{
			fill?[1] ?? string.Empty,
			Num(Math.Max(0, radiusPx)),
			stroke?[1] ?? string.Empty,
			Num(stroke is null ? 0 : strokePx),
		};
	}

	/// <summary>Converts a clip Geometry to ops in device px; it is in element space, so it applies before Stretch.</summary>
	public static List<object?> GeometryOps(Geometry? geometry, double density)
	{
		if (geometry is null)
			return None;
		var path = new PathF();
		geometry.AppendPath(path);
		return path.OperationCount == 0 ? None : PathOps(path, density);
	}

	/// <summary>
	/// Flat [offset, d0, d1, ...] dash list; MAUI dash values are multiples of the stroke thickness.
	/// Qt 5.6 Context2D has no setLineDash, so the adapter drops it and counts the skip.
	/// </summary>
	public static List<object?> Dash(double thickness, DoubleCollection? pattern, double dashOffset, double density)
	{
		if (pattern is null || pattern.Count == 0)
			return None;
		var dash = new List<object?>(pattern.Count + 1) { Num(dashOffset * thickness * density) };
		foreach (var entry in pattern)
			dash.Add(Num(entry * thickness * density));
		return dash;
	}

	private static List<object?> Natural(RectF bounds, double density) => new()
	{
		Num(bounds.X * density), Num(bounds.Y * density),
		Num(bounds.Width * density), Num(bounds.Height * density),
	};

	/// <summary>A plain color as a paint spec (null for none).</summary>
	public static List<object?>? SolidSpec(Color? color) => Solid(color);

	private static List<object?>? Solid(Color? color) =>
		color is null || color.Alpha <= 0
			? null
			: new List<object?> { "solid", BridgeValue.ColorString(color) };

	private static List<object?> Stops(GradientStopCollection stops) =>
		stops.Select(s => Stop(s.Offset, s.Color)).ToList();

	private static List<object?> Stops(PaintGradientStop[]? stops) =>
		stops?.Select(s => Stop(s.Offset, s.Color)).ToList() ?? new List<object?>();

	// Like MAUI's backends, a GradientPaint without stops falls back to Start/EndColor.
	private static List<object?> FallbackStops(GradientPaint paint)
	{
		var stops = new List<object?>();
		if (paint.StartColor is { } start && paint.EndColor is { } end && (start.Alpha > 0 || end.Alpha > 0))
		{
			stops.Add(Stop(0f, start));
			stops.Add(Stop(1f, end));
		}
		return stops;
	}

	private static object? Stop(float offset, Color color) =>
		new List<object?> { Num(Math.Clamp(offset, 0f, 1f)), BridgeValue.ColorString(color) };

	private static List<object?> Op(string kind, double scale, params PointF[] points)
	{
		var op = new List<object?>(points.Length * 2 + 1) { kind };
		foreach (var p in points)
		{
			op.Add(Num(p.X * scale));
			op.Add(Num(p.Y * scale));
		}
		return op;
	}

	// Three decimals is well below a pixel and keeps long path JSON small.
	private static double Num(double v) => double.IsFinite(v) ? Math.Round(v, 3) : 0;
}
