using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using SailfishView = Microsoft.Maui.Controls.PlatformConfiguration.SailfishOSSpecific.VisualElement;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// Input leg, drag hold (tracker S60, D17 a): on a pushed page with a pulley, a real back swipe from the left edge over
/// a view with a PanGestureRecognizer pans it and leaves the page where it is; a pull down over it pans it and leaves the
/// pulley shut; the same swipe over a KeepsDrag=false view goes back as Silica does.
/// </summary>
internal sealed partial class QtHostDiagnosticsRunner
{
	private void RunQtDragHoldChecks(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, Action done)
	{
		if (renderer.CurrentPage?.Navigation is not { } navigation)
		{
			_qtInputChecks.Check("drag hold: a page to push from", false);
			done();
			return;
		}
		var held = 0;
		var free = 0;
		var heldBox = new BoxView { HeightRequest = 320, Color = Colors.SteelBlue };
		var heldPan = new PanGestureRecognizer();
		heldPan.PanUpdated += (_, e) => { if (e.StatusType == GestureStatus.Running) held++; };
		heldBox.GestureRecognizers.Add(heldPan);
		var freeBox = new BoxView { HeightRequest = 320, Color = Colors.IndianRed };
		var freePan = new PanGestureRecognizer();
		freePan.PanUpdated += (_, e) => { if (e.StatusType == GestureStatus.Running) free++; };
		freeBox.GestureRecognizers.Add(freePan);
		SailfishView.SetKeepsDrag(freeBox, false);
		var page = new ContentPage
		{
			Title = "Drag hold",
			Content = new VerticalStackLayout { Spacing = 24, Children = { heldBox, freeBox } },
		};
		page.ToolbarItems.Add(new ToolbarItem { Text = "DH pulley item" });
		Console.Error.WriteLine("[Sailfish] Qt input diag: drag hold — pushing the 'Drag hold' page");
		_ = navigation.PushAsync(page, false);

		double CenterY(VisualElement element) =>
			renderer.CurrentHosts.FirstOrDefault(h => h.IsAttached && ReferenceEquals(h.Element, element)) is { } host &&
			QtHost.QtHostRuntime.TryItemGeometry(host.NativeHandle, out var scene) ? scene.Y + scene.Height / 2 : double.NaN;
		int Depth() => (int)DiagQml.EvalNum("pageStack.depth", -1);

		// Finger steps every 16 ms along (x0,y0)→(x1,y1); `mid` runs with the finger still down near the end.
		void Drag(double x0, double y0, double x1, double y1, Action mid, Action next)
		{
			const int steps = 14;
			var i = 0;
			QtHost.QtHostRuntime.InjectPointer(0, x0, y0);
			void Step()
			{
				i++;
				var x = x0 + (x1 - x0) * i / steps;
				var y = y0 + (y1 - y0) * i / steps;
				if (i < steps)
				{
					QtHost.QtHostRuntime.InjectPointer(2, x, y);
					dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(16), Step);
					return;
				}
				QtHost.QtHostRuntime.InjectPointer(2, x, y);
				mid();
				QtHost.QtHostRuntime.InjectPointer(1, x, y);
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(900), next);
			}
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(16), Step);
		}

		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1800), () =>
		{
			var depth = Depth();
			var y = CenterY(heldBox);
			double pageX = double.NaN;
			// A back swipe from the left edge over the pan view.
			Drag(6, y, 560, y, () => pageX = DiagQml.EvalNum("pageStack.currentPage.x"), () =>
			{
				_qtInputChecks.Check($"drag hold S60: a back swipe over a pan view pans it ({held}>0) and the page stays " +
					$"(depth {Depth()}=={depth}, page x mid-swipe {pageX:F0}==0, '{renderer.CurrentPage?.Title}')",
					held > 0 && Depth() == depth && Math.Abs(pageX) < 1 && ReferenceEquals(renderer.CurrentPage, page));
				var heldBefore = held;
				double flickY = double.NaN;
				// A pull down over it at the top of the page.
				Shot(dispatcher, "input-drag-held", () => Drag(500, y - 100, 500, y + 500, () => flickY = DiagQml.EvalNum("pageStack.currentPage.flickY"), () =>
				{
					_qtInputChecks.Check($"drag hold S60: a pull down over a pan view pans it ({held - heldBefore}>0) and the pulley stays shut " +
						$"(flickable y mid-pull {flickY:F0}==0)",
						held > heldBefore && Math.Abs(flickY) < 1);
					var freeY = CenterY(freeBox);
					// The same back swipe over the KeepsDrag=false view: Silica takes it.
					Drag(6, freeY, 700, freeY, () => { }, () =>
					{
						dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(600), () =>
						{
							_qtInputChecks.Check($"drag hold S60: KeepsDrag=false hands the back swipe to Silica (pan {free}>0, " +
								$"depth {Depth()}<{depth}, '{renderer.CurrentPage?.Title}')",
								free > 0 && Depth() < depth && !ReferenceEquals(renderer.CurrentPage, page));
							if (ReferenceEquals(renderer.CurrentPage, page))
								_ = navigation.PopAsync(false);
							dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1200), done);
						});
					});
				}));
			});
		});
	}
}
