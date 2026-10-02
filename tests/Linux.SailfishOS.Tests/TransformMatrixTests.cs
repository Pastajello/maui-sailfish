using Microsoft.Maui.Controls;
using Microsoft.Maui.SailfishOS.Platform.QtHost;
using Xunit;

namespace Linux.SailfishOS.Tests;

/// <summary>Non-uniform scale, shear and RotationX/RotationY reach the host as a QML Matrix4x4.</summary>
public class TransformMatrixTests
{
	private const double Q = 2;   // Qt units per dp

	private static Affine2 ToHost(VisualElement element, double w, double h, double x = 10, double y = 20) =>
		QtHostVisualState.LocalTransform(element, w, h).Then(Affine2.Translation(x, y));

	private static (double X, double Y) Project(double[] m, double x, double y)
	{
		var w = m[12] * x + m[13] * y + m[15];
		return ((m[0] * x + m[1] * y + m[3]) / w, (m[4] * x + m[5] * y + m[7]) / w);
	}

	[Fact]
	public void Translation_and_uniform_scale_rotation_need_no_matrix()
	{
		var plain = new BoxView();
		Assert.Null(QtHostVisualState.HostMatrix(plain, ToHost(plain, 100, 50), 100, 50, Q));
		var turned = new BoxView { Rotation = 30, Scale = 1.5 };
		Assert.Null(QtHostVisualState.HostMatrix(turned, ToHost(turned, 100, 50), 100, 50, Q));
	}

	[Fact]
	public void Non_uniform_scale_is_the_linear_part()
	{
		var view = new BoxView { ScaleX = 2, ScaleY = 0.5 };
		var toHost = ToHost(view, 100, 50);
		var m = QtHostVisualState.HostMatrix(view, toHost, 100, 50, Q)!;
		// The corner (100, 50) dp lands where the 2D map puts it, relative to the pushed origin, in Qt units.
		var (ex, ey) = toHost.Transform(100, 50);
		var (px, py) = Project(m, 100 * Q, 50 * Q);
		Assert.Equal((ex - toHost.Tx) * Q, px, 6);
		Assert.Equal((ey - toHost.Ty) * Q, py, 6);
	}

	[Fact]
	public void RotationY_half_turn_mirrors_around_the_anchor()
	{
		var view = new BoxView { RotationY = 180 };
		var m = QtHostVisualState.HostMatrix(view, ToHost(view, 100, 50), 100, 50, Q)!;
		var (x0, y0) = Project(m, 0, 0);
		var (x1, y1) = Project(m, 100 * Q, 50 * Q);
		Assert.Equal(100 * Q, x0, 6);
		Assert.Equal(0, y0, 6);
		Assert.Equal(0, x1, 6);
		Assert.Equal(50 * Q, y1, 6);
	}

	[Fact]
	public void RotationX_tilts_the_top_edge_away_in_perspective()
	{
		var view = new BoxView { RotationX = 40 };
		var m = QtHostVisualState.HostMatrix(view, ToHost(view, 200, 200), 200, 200, Q)!;
		var topLeft = Project(m, 0, 0);
		var topRight = Project(m, 200 * Q, 0);
		var bottomLeft = Project(m, 0, 200 * Q);
		var bottomRight = Project(m, 200 * Q, 200 * Q);
		var top = topRight.X - topLeft.X;
		var bottom = bottomRight.X - bottomLeft.X;
		Assert.True(top < 200 * Q && bottom > 200 * Q, $"top {top}, bottom {bottom}");
		// Foreshortened: the face is shorter than it was.
		Assert.True(bottomLeft.Y - topLeft.Y < 200 * Q);
		// The anchor (centre) stays put.
		var (cx, cy) = Project(m, 100 * Q, 100 * Q);
		Assert.Equal(100 * Q, cx, 6);
		Assert.Equal(100 * Q, cy, 6);
	}

	[Fact]
	public void A_3D_element_keeps_its_translation_and_its_ancestors_scale()
	{
		var view = new BoxView { RotationY = 0.0001, TranslationX = 30 };
		// An unhosted ancestor scales its subtree by 2 around the origin.
		var toHost = QtHostVisualState.LocalTransform(view, 100, 50).Then(Affine2.Translation(10, 20)).Then(Affine2.Scale(2, 2));
		var m = QtHostVisualState.HostMatrix(view, toHost, 100, 50, Q)!;
		var (ex, ey) = toHost.Transform(100, 50);
		var (px, py) = Project(m, 100 * Q, 50 * Q);
		Assert.Equal((ex - toHost.Tx) * Q, px, 2);
		Assert.Equal((ey - toHost.Ty) * Q, py, 2);
	}
}
