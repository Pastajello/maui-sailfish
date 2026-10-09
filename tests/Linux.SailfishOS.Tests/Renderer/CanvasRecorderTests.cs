using Microsoft.Maui.Graphics;
using Microsoft.Maui.SailfishOS.Platform.QtHost;
using Xunit;

namespace Linux.SailfishOS.Tests.Renderer;

/// <summary>Tracker S30 (plan M9 step 5): the ICanvas recorder writes every member as the GraphicsView adapter
/// (qml/shapes/GraphicsView.qml) replays it.</summary>
public sealed class CanvasRecorderTests
{
	private static object?[] Op(QtHostCanvasRecorder canvas, int index = -1)
	{
		var list = canvas.Commands;
		return ((System.Collections.IEnumerable)list[index < 0 ? list.Count + index : index]!).Cast<object?>().ToArray();
	}

	private static string Name(object?[] op) => (string)op[0]!;

	[Fact]
	public void State_members_record_their_ops()
	{
		var c = new QtHostCanvasRecorder();
		c.Antialias = false;
		Assert.Equal(new object?[] { "aa", 0 }, Op(c));
		c.FontSize = 18;
		Assert.Equal("fos", Name(Op(c)));
		c.Font = new Microsoft.Maui.Graphics.Font("OpenSansRegular", 700, FontStyleType.Italic);
		var font = Op(c);
		// Off the Qt thread (a host test) the alias passes through unchanged.
		Assert.Equal(new object?[] { "fon", "OpenSansRegular", 700, 1 }, font);
		c.SaveState();
		c.RestoreState();
		Assert.Equal(new[] { "sv", "rs" }, c.Commands.TakeLast(2).Select(o => Name(((System.Collections.IEnumerable)o!).Cast<object?>().ToArray())));
	}

	[Fact]
	public void Fill_and_clip_paths_carry_their_winding_mode()
	{
		var path = new PathF();
		path.MoveTo(0, 0);
		path.LineTo(10, 0);
		path.LineTo(10, 10);
		path.Close();
		var c = new QtHostCanvasRecorder();

		c.FillPath(path, WindingMode.EvenOdd);
		var fill = Op(c);
		Assert.Equal("fpath", Name(fill));
		Assert.Equal((int)WindingMode.EvenOdd, fill[2]);

		c.ClipPath(path, WindingMode.NonZero);
		var clip = Op(c);
		Assert.Equal("clipp", Name(clip));
		Assert.Equal((int)WindingMode.NonZero, clip[2]);
	}

	[Fact]
	public void Text_members_record_point_rect_and_attributed_text()
	{
		var c = new QtHostCanvasRecorder();
		c.DrawString("Ag", 10, 60, HorizontalAlignment.Center);
		Assert.Equal("strp", Name(Op(c)));
		Assert.Equal("Ag", Op(c)[1]);
		Assert.Equal((int)HorizontalAlignment.Center, Op(c)[4]);

		c.DrawString("box", 0, 0, 100, 40, HorizontalAlignment.Right, VerticalAlignment.Bottom, TextFlow.OverflowBounds, 2);
		var rect = Op(c);
		Assert.Equal("str", Name(rect));
		Assert.Equal(new object?[] { (int)HorizontalAlignment.Right, (int)VerticalAlignment.Bottom, (int)TextFlow.OverflowBounds },
			rect.Skip(6).Take(3).ToArray());
	}

	[Fact]
	public void Geometry_members_record_their_ops()
	{
		var c = new QtHostCanvasRecorder();
		c.DrawLine(0, 0, 1, 1);
		c.DrawRectangle(0, 0, 1, 1);
		c.DrawRoundedRectangle(0, 0, 1, 1, 2);
		c.DrawEllipse(0, 0, 1, 1);
		c.DrawArc(0, 0, 1, 1, 0, 90, true, false);
		c.FillRectangle(0, 0, 1, 1);
		c.FillRoundedRectangle(0, 0, 1, 1, 2);
		c.FillEllipse(0, 0, 1, 1);
		c.FillArc(0, 0, 1, 1, 0, 90, false);
		c.Translate(1, 2);
		c.Scale(2, 2);
		c.Rotate(45);
		c.Rotate(45, 1, 1);
		c.ClipRectangle(0, 0, 5, 5);
		c.SubtractFromClip(0, 0, 1, 1);

		Assert.Equal(new[] { "line", "rect", "rrect", "ell", "arc", "frect", "frrect", "fell", "farc", "tr", "sc2", "ro", "ro2", "clipr", "subclipr" },
			c.Commands.Select(o => Name(((System.Collections.IEnumerable)o!).Cast<object?>().ToArray())));
	}
}
