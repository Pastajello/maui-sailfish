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

// Snapshot builders of the containers and composite views: Border/Frame, layouts, ScrollView, SwipeView, WebView.
// MAUI control state → adapter properties (sizes in device px, Qt enums as ints); handlers call these from Snapshot().
internal static partial class AdapterSnapshots
{
	/// <summary>
	/// The Border snapshot: stroke color/width and corner radius in device px (what the QML Rectangle draws in)
	/// plus the background fill. Non-solid strokes cross transparent.
	/// </summary>
	internal static Dictionary<string, object?> BorderProps(Border border)
	{
		var density = QtHostUnits.ScenePerDp;
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
		var density = QtHostUnits.ScenePerDp;
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
		props["mauiSpacing"] = stack.Spacing * QtHostUnits.ScenePerDp;
		props["mauiOrientation"] = OrientationOf(stack);
		return props;
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

	/// <summary>The WebView snapshot: a URL or an HTML string (+ base URL). The tick re-loads when Source changes
	/// to an equal value.</summary>
	internal static Dictionary<string, object?> WebViewProps(WebView web)
	{
		var props = new Dictionary<string, object?>
		{
			["mauiUrl"] = string.Empty,
			["mauiHtml"] = string.Empty,
			["mauiBaseUrl"] = string.Empty,
			["mauiSourceId"] = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(web.Source ?? (object)web),
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
		if ((swipe.TopItems?.Count > 0 || swipe.BottomItems?.Count > 0) && _swipeWarned.TryAdd(swipe, Warned))
			QtHostDiag.Warn(QtHostDiagChannel.QtHost, "SwipeView Top/BottomItems are not rendered yet (horizontal swipes only)");
		var props = ContainerProps(swipe);
		props["mauiLeftItems"] = SwipeItemsJson(swipe.LeftItems);
		props["mauiRightItems"] = SwipeItemsJson(swipe.RightItems);
		props["mauiLeftMode"] = swipe.LeftItems?.Mode == SwipeMode.Execute ? "execute" : "reveal";
		props["mauiRightMode"] = swipe.RightItems?.Mode == SwipeMode.Execute ? "execute" : "reveal";
		props["mauiThreshold"] = swipe.Threshold > 0 ? swipe.Threshold * QtHostUnits.ScenePerDp : 0.0;
		// Reveal (MAUI default: content slides over the items) or Drag (items travel with the content).
		props["mauiTransition"] = ((ISwipeView)swipe).SwipeTransitionMode == SwipeTransitionMode.Drag ? "drag" : "reveal";
		return props;
	}

	// Warned once per SwipeView, without keeping the view alive (a HashSet rooted every warned view for the process).
	private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SwipeView, object> _swipeWarned = new();
	private static readonly object Warned = new();

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
}
