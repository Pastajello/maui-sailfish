using System.Globalization;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>
/// A 2D affine transform in screen space (y down, row vectors: p' = p·M + T), so rotation is clockwise
/// like MAUI and QML. Doubles rather than Matrix3x2 floats keep untransformed geometry bit-stable.
/// </summary>
internal readonly record struct Affine2(double M11, double M12, double M21, double M22, double Tx, double Ty)
{
	public static readonly Affine2 Identity = new(1, 0, 0, 1, 0, 0);

	public static Affine2 Translation(double x, double y) => new(1, 0, 0, 1, x, y);

	/// <summary>Clockwise rotation in screen space.</summary>
	public static Affine2 RotationDeg(double degrees)
	{
		var a = degrees * Math.PI / 180.0;
		var c = Math.Cos(a);
		var s = Math.Sin(a);
		return new Affine2(c, s, -s, c, 0, 0);
	}

	public static Affine2 Scale(double sx, double sy) => new(sx, 0, 0, sy, 0, 0);

	/// <summary>Applies <c>this</c> first, then <paramref name="then"/>.</summary>
	public Affine2 Then(in Affine2 then) => new(
		M11 * then.M11 + M12 * then.M21,
		M11 * then.M12 + M12 * then.M22,
		M21 * then.M11 + M22 * then.M21,
		M21 * then.M12 + M22 * then.M22,
		Tx * then.M11 + Ty * then.M21 + then.Tx,
		Tx * then.M12 + Ty * then.M22 + then.Ty);

	public (double X, double Y) Transform(double x, double y) =>
		(x * M11 + y * M21 + Tx, x * M12 + y * M22 + Ty);

	/// <summary>True for a pure translation (the fast path; nothing is pushed natively).</summary>
	public bool IsTranslationOnly => M11 == 1 && M12 == 0 && M21 == 0 && M22 == 1;

	/// <summary>Rotation angle of the linear part (atan2 of the first row).</summary>
	public double RotationDegrees => Math.Atan2(M12, M11) * 180.0 / Math.PI;

	/// <summary>Best-fit uniform scale (area-preserving for shear, exact for uniform scale ∘ rotation).</summary>
	public double UniformScale
	{
		get
		{
			var det = Math.Abs(M11 * M22 - M12 * M21);
			return det > 0 ? Math.Sqrt(det) : Math.Sqrt(M11 * M11 + M12 * M12);
		}
	}

	public bool TryInvert(out Affine2 inverse)
	{
		var det = M11 * M22 - M12 * M21;
		if (Math.Abs(det) < 1e-12)
		{
			inverse = Identity;
			return false;
		}
		var i11 = M22 / det;
		var i12 = -M12 / det;
		var i21 = -M21 / det;
		var i22 = M11 / det;
		inverse = new Affine2(i11, i12, i21, i22, -(Tx * i11 + Ty * i21), -(Tx * i12 + Ty * i22));
		return true;
	}

	/// <summary>True for uniform scale ∘ rotation, which QQuickItem rotation+scale reproduce exactly.</summary>
	public bool IsUniformScaleRotation
	{
		get
		{
			var eps = 1e-9 * Math.Max(1.0, Math.Abs(M11) + Math.Abs(M22));
			return Math.Abs(M11 - M22) <= eps && Math.Abs(M12 + M21) <= eps;
		}
	}
}

/// <summary>A projective 4×4 transform, row-major with column vectors (QMatrix4x4's layout), for 3D-rotated hosts.</summary>
internal readonly struct Mat4
{
	public double[] Values { get; }

	private Mat4(double[] values) => Values = values;

	private static Mat4 Of(params double[] v) => new(v);

	public static Mat4 Translation(double x, double y) => Of(1, 0, 0, x, 0, 1, 0, y, 0, 0, 1, 0, 0, 0, 0, 1);

	public static Mat4 Scale(double sx, double sy) => Of(sx, 0, 0, 0, 0, sy, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);

	/// <summary>Positive tilts the top edge away from the viewer (y grows downwards, the viewer sits at +z).</summary>
	public static Mat4 RotationX(double degrees)
	{
		var (s, c) = Math.SinCos(degrees * Math.PI / 180);
		return Of(1, 0, 0, 0, 0, c, -s, 0, 0, s, c, 0, 0, 0, 0, 1);
	}

	/// <summary>Positive turns the right edge away from the viewer.</summary>
	public static Mat4 RotationY(double degrees)
	{
		var (s, c) = Math.SinCos(degrees * Math.PI / 180);
		return Of(c, 0, s, 0, 0, 1, 0, 0, -s, 0, c, 0, 0, 0, 0, 1);
	}

	/// <summary>Clockwise on screen, as <see cref="Affine2.RotationDeg"/>.</summary>
	public static Mat4 RotationZ(double degrees)
	{
		var (s, c) = Math.SinCos(degrees * Math.PI / 180);
		return Of(c, -s, 0, 0, s, c, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
	}

	/// <summary>A camera at <paramref name="distance"/> on +z: a point at z is drawn scaled by d / (d − z).</summary>
	public static Mat4 Perspective(double distance) => Of(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, -1 / distance, 1);

	/// <summary>A 2D map (dp) as a 4×4 in Qt units: the linear part as is, the translation scaled.</summary>
	public static Mat4 Affine(in Affine2 a, double qtPerDp) =>
		Of(a.M11, a.M21, 0, a.Tx * qtPerDp, a.M12, a.M22, 0, a.Ty * qtPerDp, 0, 0, 1, 0, 0, 0, 0, 1);

	/// <summary>Applies <c>this</c> first, then <paramref name="then"/>.</summary>
	public Mat4 Then(in Mat4 then)
	{
		var r = new double[16];
		for (var i = 0; i < 4; i++)
			for (var j = 0; j < 4; j++)
			{
				double sum = 0;
				for (var k = 0; k < 4; k++)
					sum += then.Values[i * 4 + k] * Values[k * 4 + j];
				r[i * 4 + j] = sum;
			}
		return new Mat4(r);
	}

	/// <summary>The projected 2D image of (x, y, 0).</summary>
	public (double X, double Y) Project(double x, double y)
	{
		var v = Values;
		var w = v[12] * x + v[13] * y + v[15];
		return ((v[0] * x + v[1] * y + v[3]) / w, (v[4] * x + v[5] * y + v[7]) / w);
	}
}

/// <summary>
/// Generic visual state (opacity, enabled, z, transforms, background, semantics, shadow/clip) mapped onto
/// standard QQuickItem properties so adapters need not know about it. Nested hosts let Qt cascade
/// opacity/enabled; flat collection rows cascade managed-side. Non-uniform scale, shear and 3D rotation go to a
/// QML Matrix4x4 (<see cref="HostMatrix"/>).
/// </summary>
internal static class QtHostVisualState
{
	/// <summary>The element's generic property snapshot; with <paramref name="flatRoot"/> set (flat
	/// collection-row hosts) opacity/enabled cascade up to and including the row root.</summary>
	public static Dictionary<string, object?> Props(VisualElement element, VisualElement? flatRoot = null)
	{
		var props = new Dictionary<string, object?>
		{
			["opacity"] = flatRoot is null ? Math.Clamp(element.Opacity, 0.0, 1.0) : EffectiveOpacity(element, flatRoot),
			["enabled"] = flatRoot is null ? element.IsEnabled : EffectiveEnabled(element, flatRoot),
			["z"] = element.ZIndex,
		};
		// The shim lays a fill Rectangle under Silica controls that paint no background.
		if (HasGenericBackground(element))
			props["mauiBackgroundFill"] = QtHostPaint.Background(element) ?? Microsoft.Maui.Graphics.Colors.Transparent;
		// Semantics → Qt accessibility; AutomationId as a dynamic property for UI-test lookups.
		if (((IView)element).Semantics is { } semantics)
		{
			props["mauiAccessibleName"] = semantics.Description ?? string.Empty;
			props["mauiAccessibleDescription"] = semantics.Hint ?? string.Empty;
		}
		if (!string.IsNullOrEmpty(element.AutomationId))
			props["mauiAutomationId"] = element.AutomationId;
		// HeadingLevel → Accessible.role Heading (Qt has no levels); IsInAccessibleTree=false and
		// ExcludedWithChildren (self or an ancestor) → Accessible.ignored, since Qt re-parents an ignored item's
		// children instead of hiding them.
		props["mauiAccessibleRole"] = SemanticProperties.GetHeadingLevel(element) != SemanticHeadingLevel.None ? "heading" : string.Empty;
		props["mauiAccessibleIgnored"] = IsExcludedFromAccessibility(element);
		props["mauiMirrored"] = MirrorState(element);
		// Shadow/Clip via the shim's layer effect; Border/Frame paint their own shadow, Shapes clip themselves.
		if (element is not Border and not Frame)
			props["mauiLayerShadow"] = ShadowSpec(element.Shadow, SailfishDisplay.Density);
		if (element is not Microsoft.Maui.Controls.Shapes.Shape)
			props["mauiLayerClip"] = ClipSpec(element.Clip, SailfishDisplay.Density);
		return props;
	}

	/// <summary>True when the element is out of the accessibility tree: IsInAccessibleTree=false on itself, or
	/// ExcludedWithChildren=true on itself or an ancestor.</summary>
	public static bool IsExcludedFromAccessibility(VisualElement element)
	{
		if (AutomationProperties.GetIsInAccessibleTree(element) == false)
			return true;
		for (Element? e = element; e is not null; e = e.Parent)
			if (AutomationProperties.GetExcludedWithChildren(e) == true)
				return true;
		return false;
	}

	/// <summary>Leaf controls whose Silica internals follow LayoutMirroring. Containers, labels and text fields
	/// stay unmirrored: MAUI's layout already mirrors their children, and their text alignment is mapped from
	/// the effective FlowDirection.</summary>
	private static bool MirrorsNatively(VisualElement element) =>
		element is Button or ImageButton or Switch or CheckBox or Slider or Stepper or ProgressBar
			or ActivityIndicator or Picker or DatePicker or TimePicker or RadioButton or Image
			or SwipeView or IndicatorView or WebView;

	/// <summary>"on" for a leaf control under an effective RightToLeft FlowDirection, "off" for any other host
	/// there (an explicit value stops a mirrored ancestor's inheritance), "" in left-to-right content.</summary>
	public static string MirrorState(VisualElement element) =>
		!IsRightToLeft(element) ? string.Empty
		: MirrorsNatively(element) ? "on" : "off";

	/// <summary>Whether the effective flow direction is RTL: the nearest ancestor-or-self with an explicit
	/// FlowDirection decides. MAUI resolves this through the platform view tree, which this host lacks.</summary>
	public static bool IsRightToLeft(Element? element)
	{
		for (var e = element; e is not null; e = e.Parent)
		{
			if (e is VisualElement ve && ve.FlowDirection != FlowDirection.MatchParent)
				return ve.FlowDirection == FlowDirection.RightToLeft;
			// The window is the root (it is no VisualElement): RTL set on it mirrors every page (tracker S13).
			if (e is Window window && window.FlowDirection != FlowDirection.MatchParent)
				return window.FlowDirection == FlowDirection.RightToLeft;
		}
		return false;
	}

	/// <summary>MAUI Shadow → "#AARRGGBB|radius|x|y" in device px; "" means none.</summary>
	public static string ShadowSpec(Shadow? shadow, double density)
	{
		if (shadow?.Brush is not SolidColorBrush { Color: { } color } || shadow.Opacity <= 0)
			return string.Empty;
		var c = color.WithAlpha((float)Math.Clamp(color.Alpha * shadow.Opacity, 0, 1));
		if (c.Alpha <= 0)
			return string.Empty;
		return string.Create(CultureInfo.InvariantCulture,
			$"{BridgeValue.ColorString(c)}|{Math.Max(0, shadow.Radius) * density:0.###}|{shadow.Offset.X * density:0.###}|{shadow.Offset.Y * density:0.###}");
	}

	/// <summary>VisualElement.Clip → {"ops":[…],"eo":0|1} in element-space device px; "" means none.
	/// EvenOdd is MAUI's default fill rule for paths and groups.</summary>
	public static string ClipSpec(Geometry? clip, double density)
	{
		var ops = QtHostShapes.GeometryOps(clip, density);
		if (ops.Count == 0)
			return string.Empty;
		var evenOdd = clip switch
		{
			PathGeometry path => path.FillRule == FillRule.EvenOdd,
			GeometryGroup group => group.FillRule == FillRule.EvenOdd,
			_ => true,
		};
		return BridgeValue.Serialize(new Dictionary<string, object?> { ["ops"] = ops, ["eo"] = evenOdd ? 1 : 0 });
	}

	/// <summary>Controls whose adapters paint no background of their own.</summary>
	public static readonly IReadOnlyList<Type> GenericBackgroundTypes = new[]
	{
		typeof(Switch), typeof(CheckBox), typeof(Slider), typeof(Stepper), typeof(ProgressBar),
		typeof(ActivityIndicator), typeof(Picker), typeof(DatePicker), typeof(TimePicker),
		typeof(RadioButton), typeof(IndicatorView), typeof(WebView), typeof(Entry), typeof(Editor),
		typeof(SearchBar),
	};

	public static bool HasGenericBackground(VisualElement element)
	{
		// A drawing surface (QtHostSurface) paints only its pixels; the view's background goes under them, as Android
		// draws a View's background before its content.
		if (element.Handler?.PlatformView is NativeElementHost { QmlUri: QtHostSurface.AdapterUri })
			return true;
		var type = element.GetType();
		foreach (var t in GenericBackgroundTypes)
			if (t.IsAssignableFrom(type))
				return true;
		return false;
	}

	/// <summary>Merges the generic snapshot into a host's create/diff props.</summary>
	public static void Merge(Dictionary<string, object?> props, VisualElement element, VisualElement? flatRoot = null)
	{
		foreach (var kv in Props(element, flatRoot))
			props[kv.Key] = kv.Value;
	}

	/// <summary>Multiplied opacity up the MAUI chain (stopping after <paramref name="upTo"/>), for flat
	/// collection-row hosts and the input router.</summary>
	public static double EffectiveOpacity(VisualElement element, VisualElement? upTo = null)
	{
		var opacity = 1.0;
		for (VisualElement? v = element; v is not null; v = v.Parent as VisualElement)
		{
			opacity *= Math.Clamp(v.Opacity, 0.0, 1.0);
			if (ReferenceEquals(v, upTo))
				break;
		}
		return Math.Clamp(opacity, 0.0, 1.0);
	}

	/// <summary>False when the element or an ancestor (up to <paramref name="upTo"/>) is disabled, for flat
	/// collection-row hosts and the input router.</summary>
	public static bool EffectiveEnabled(VisualElement element, VisualElement? upTo = null)
	{
		for (VisualElement? v = element; v is not null; v = v.Parent as VisualElement)
		{
			if (!v.IsEnabled)
				return false;
			if (ReferenceEquals(v, upTo))
				break;
		}
		return true;
	}

	/// <summary>
	/// The element's own transform in local space: scale ∘ rotation around the anchor pivot, then the
	/// unrotated TranslationX/Y (MAUI's composition order). Anchors may fall outside 0..1.
	/// </summary>
	public static Affine2 LocalTransform(VisualElement element, double width, double height)
	{
		// Legacy Scale multiplies ScaleX/ScaleY, as on MAUI's in-box platforms.
		var sx = element.Scale * element.ScaleX;
		var sy = element.Scale * element.ScaleY;
		var rot = element.Rotation;
		var tx = element.TranslationX;
		var ty = element.TranslationY;
		if (sx == 1 && sy == 1 && rot == 0 && tx == 0 && ty == 0)
			return Affine2.Identity;
		var px = element.AnchorX * width;
		var py = element.AnchorY * height;
		// p → (p − pivot) → scale → rotate → + pivot + translation
		return Affine2.Translation(-px, -py)
			.Then(Affine2.Scale(sx, sy))
			.Then(Affine2.RotationDeg(rot))
			.Then(Affine2.Translation(px + tx, py + ty));
	}

	/// <summary>Android's default camera distance (View.setCameraDistance: 1280 at mdpi, scaling with density), in dp:
	/// the perspective RotationX/RotationY are seen with.</summary>
	public const double CameraDistanceDp = 1280;

	/// <summary>Whether the element rotates out of the screen plane (RotationX/RotationY).</summary>
	public static bool Is3D(VisualElement element) => element.RotationX != 0 || element.RotationY != 0;

	/// <summary>
	/// The host's QML transform as a row-major 4×4 matrix (column vectors, as QMatrix4x4), in Qt units relative to the
	/// pushed x/y; null when QQuickItem rotation+scale reproduce it (a translation, or uniform scale ∘ rotation).
	/// <paramref name="toHost"/> is the 2D map into the hosting parent (the element's own transform, its offset and the
	/// unhosted ancestors'). A 3D element replaces its own 2D part with scale → RotationX → RotationY → Rotation and
	/// translation → perspective around the anchor, as Android's RenderNode composes them; the result then goes through
	/// the ancestors' part.
	/// </summary>
	public static double[]? HostMatrix(VisualElement element, in Affine2 toHost, double width, double height, double qtPerDp)
	{
		if (!Is3D(element))
		{
			if (toHost.IsTranslationOnly || toHost.IsUniformScaleRotation)
				return null;
			// Non-uniform scale or shear: the linear part as is (x/y carry the translation).
			return new[]
			{
				toHost.M11, toHost.M21, 0, 0,
				toHost.M12, toHost.M22, 0, 0,
				0, 0, 1, 0,
				0, 0, 0, 1,
			};
		}
		// The ancestors' part: the element's own 2D transform undone.
		var own = LocalTransform(element, width, height);
		if (!own.TryInvert(out var ownInverse))
			return null;
		var outer = ownInverse.Then(toHost);
		var px = element.AnchorX * width * qtPerDp;
		var py = element.AnchorY * height * qtPerDp;
		var m = Mat4.Translation(-px, -py)
			.Then(Mat4.Scale(element.Scale * element.ScaleX, element.Scale * element.ScaleY))
			.Then(Mat4.RotationX(element.RotationX))
			.Then(Mat4.RotationY(element.RotationY))
			.Then(Mat4.RotationZ(element.Rotation))
			.Then(Mat4.Translation(element.TranslationX * qtPerDp, element.TranslationY * qtPerDp))
			.Then(Mat4.Perspective(CameraDistanceDp * qtPerDp))
			.Then(Mat4.Translation(px, py))
			.Then(Mat4.Affine(outer, qtPerDp))
			// Relative to the pushed x/y, where the 2D map puts the origin.
			.Then(Mat4.Translation(-toHost.Tx * qtPerDp, -toHost.Ty * qtPerDp));
		return m.Values;
	}

	/// <summary>Warning text when the transform is approximated: the input router hit-tests a 3D-rotated element by its
	/// 2D footprint. Null otherwise.</summary>
	public static string? TransformLimit(VisualElement element) =>
		Is3D(element)
			? $"RotationX={element.RotationX}/RotationY={element.RotationY}: drawn in 3D, hit-tested by the 2D footprint"
			: null;

	/// <summary>Properties pushed immediately as generic native state.</summary>
	public static bool IsStateProperty(string propertyName) =>
		propertyName is nameof(VisualElement.Opacity) or nameof(VisualElement.IsEnabled)
			or nameof(VisualElement.ZIndex);
}
