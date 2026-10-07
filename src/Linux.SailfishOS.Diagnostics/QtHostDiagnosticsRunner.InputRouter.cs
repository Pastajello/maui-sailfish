using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// Input leg, router part (tracker S16): on a pushed page, injected touches check a swipe recognizer for two
/// directions (Left|Right) past its 100 dp threshold, a tap that falls through an InputTransparent overlay to the view
/// below, and a tap on the part of a Scale=2 view outside its unscaled rect, whose GetPosition(view) is in the view's
/// own (unscaled) coordinates. Then (tracker S17) a CollectionView whose rows carry LongPress and Pan recognizers: a
/// hold on one row fires LongPressed without selecting it, a horizontal drag on another pans it.
/// </summary>
internal sealed partial class QtHostDiagnosticsRunner
{
	private readonly DiagChecks _qtInputChecks = new("Qt input diag");

	private void RunQtRouterChecks(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, Action done)
	{
		var swipes = 0;
		var underTaps = 0;
		var scaledTaps = 0;
		Point? scaledAt = null;

		var swipeBox = new BoxView { WidthRequest = 300, HeightRequest = 120, Color = Colors.SteelBlue };
		var swipe = new SwipeGestureRecognizer { Direction = SwipeDirection.Left | SwipeDirection.Right };
		swipe.Swiped += (_, _) => swipes++;
		swipeBox.GestureRecognizers.Add(swipe);

		var under = new BoxView { Color = Colors.DarkOliveGreen };
		var underTap = new TapGestureRecognizer();
		underTap.Tapped += (_, _) => underTaps++;
		under.GestureRecognizers.Add(underTap);
		var overlay = new BoxView { Color = Colors.Transparent, InputTransparent = true };
		var stacked = new Grid { WidthRequest = 300, HeightRequest = 120, Children = { under, overlay } };

		var scaled = new BoxView { WidthRequest = 100, HeightRequest = 100, Scale = 2, Color = Colors.DarkOrange, Margin = new Thickness(100, 80, 0, 0), HorizontalOptions = LayoutOptions.Start };
		var scaledTap = new TapGestureRecognizer();
		scaledTap.Tapped += (_, e) => { scaledTaps++; scaledAt = e.GetPosition(scaled); };
		scaled.GestureRecognizers.Add(scaledTap);

		var page = new ContentPage
		{
			Title = "Router",
			Content = new VerticalStackLayout { Padding = new Thickness(16), Spacing = 24, Children = { swipeBox, stacked, scaled } },
		};
		if (renderer.CurrentPage?.Navigation is not { } navigation)
		{
			_qtInputChecks.Check("router checks: a page to push from", false);
			done();
			return;
		}
		Console.Error.WriteLine("[Sailfish] Qt input diag: router checks — pushing the 'Router' page");
		_ = navigation.PushAsync(page, false);

		QtHost.NativeElementHost? HostOf(VisualElement element) =>
			renderer.CurrentHosts.FirstOrDefault(h => ReferenceEquals(h.Element, element) && h.IsAttached);
		static double Px(double dp) => QtHost.QtHostUnits.ToQtUnits(dp);

		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1800), () =>
		{
			renderer.Render();
			if (HostOf(swipeBox) is not { } swipeHost || HostOf(under) is not { } underHost || HostOf(scaled) is not { } scaledHost)
			{
				_qtInputChecks.Check($"router checks: hosts on the 'Router' page (current '{renderer.CurrentPage?.Title}')", false);
				done();
				return;
			}
			// Swipe: 150 dp to the left across the box, past the default 100 dp threshold.
			var sb = swipeHost.MauiLogicalBounds;
			var y = Px(sb.Center.Y);
			QtHost.QtHostRuntime.InjectPointer(0, Px(sb.Center.X + 75), y);
			for (var i = 1; i <= 10; i++)
				QtHost.QtHostRuntime.InjectPointer(2, Px(sb.Center.X + 75 - 15 * i), y);
			QtHost.QtHostRuntime.InjectPointer(1, Px(sb.Center.X - 75), y);

			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(500), () =>
			{
				var ub = underHost.MauiLogicalBounds;
				DiagQml.Tap(Px(ub.Center.X), Px(ub.Center.Y));
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(500), () =>
				{
					// 70 dp right of the centre: outside the unscaled 100 dp box (50 dp), inside the scaled one (100 dp). The
					// host's root rect starts at the scaled view's top-left (the transformed origin), so the centre is 100 dp in.
					var origin = scaledHost.MauiLogicalBounds.Location;
					DiagQml.Tap(Px(origin.X + 100 + 70), Px(origin.Y + 100));
					dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(800), () =>
					{
						_qtInputChecks.Check($"router S16 swipe Left|Right: 150 dp left swipe fired Swiped {swipes}==1", swipes == 1);
						_qtInputChecks.Check($"router S16 InputTransparent overlay: the tap fell through to the view below ({underTaps}==1)", underTaps == 1);
						var at = scaledAt ?? new Point(double.NaN, double.NaN);
						_qtInputChecks.Check($"router S16 Scale=2: a tap 70 dp right of the centre hit the scaled view ({scaledTaps}==1), " +
							$"GetPosition(view)=({at.X:F1},{at.Y:F1}) ≈ (85,50) in its own coordinates",
							scaledTaps == 1 && Math.Abs(at.X - 85) <= 2 && Math.Abs(at.Y - 50) <= 2);
						RunQtRowGestureChecks(renderer, dispatcher, page, () => RunQtControlEventChecks(renderer, dispatcher, page, () =>
						{
							_ = navigation.PopAsync(false);
							dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1500), done);
						}));
					});
				});
			});
		});
	}

	private void RunQtRowGestureChecks(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, ContentPage page, Action done)
	{
		var held = new List<string?>();
		var panned = new List<(GestureStatus Status, string? Row)>();
		var list = new CollectionView
		{
			HeightRequest = 300,
			SelectionMode = SelectionMode.Single,
			ItemsSource = new[] { "row one", "row two", "row three" },
			ItemTemplate = new DataTemplate(() =>
			{
				var label = new Label { HeightRequest = 90, Padding = new Thickness(16, 0), VerticalTextAlignment = TextAlignment.Center };
				label.SetBinding(Label.TextProperty, ".");
				var longPress = new LongPressGestureRecognizer();
				longPress.LongPressed += (s, _) => held.Add((string?)((BindableObject)s!).BindingContext);
				var pan = new PanGestureRecognizer();
				pan.PanUpdated += (s, e) => panned.Add((e.StatusType, (string?)((BindableObject)s!).BindingContext));
				label.GestureRecognizers.Add(longPress);
				label.GestureRecognizers.Add(pan);
				return label;
			}),
		};
		Console.Error.WriteLine("[Sailfish] Qt input diag: row gestures — the 'Router' page shows a CollectionView (LongPress + Pan rows)");
		page.Content = new VerticalStackLayout { Padding = new Thickness(16), Children = { list } };
		static double Px(double dp) => QtHost.QtHostUnits.ToQtUnits(dp);

		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1800), () =>
		{
			renderer.Render();
			var host = renderer.CurrentHosts.FirstOrDefault(h => ReferenceEquals(h.Element, list) && h.IsAttached);
			if (host is null)
			{
				_qtInputChecks.Check("row gestures: the CollectionView host is attached", false);
				done();
				return;
			}
			var b = host.MauiLogicalBounds;
			var x = Px(b.X + b.Width / 2);
			var row0 = Px(b.Y + 45);
			var row1 = Px(b.Y + 135);
			// Hold row one for 900 ms (LongPress default 500 ms).
			QtHost.QtHostRuntime.InjectPointer(0, x, row0);
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(900), () =>
			{
				QtHost.QtHostRuntime.InjectPointer(1, x, row0);
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(500), () =>
				{
					// Drag row two 150 dp sideways, a move every 40 ms as a finger does (the delegate's press report
					// reaches managed between the events, not after the release).
					QtHost.QtHostRuntime.InjectPointer(0, x, row1);
					for (var i = 1; i <= 10; i++)
					{
						var step = i;
						dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(80 + 40 * step),
							() => QtHost.QtHostRuntime.InjectPointer(2, x - Px(15 * step), row1));
					}
					dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(80 + 40 * 11), () => QtHost.QtHostRuntime.InjectPointer(1, x - Px(150), row1));
					dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(80 + 40 * 11 + 800), () =>
					{
						var router = QtHost.QtHostInputRouter.Active;
						_qtInputChecks.Check($"router S17 row long press: LongPressed [{string.Join(",", held)}] has 'row one' (row captures {router?.RowCaptures})",
							held.Contains("row one"));
						_qtInputChecks.Check($"router S17 row pan: PanUpdated on 'row two' Started and Completed ({string.Join(",", panned.Select(p => p.Status + ":" + p.Row).Distinct())}), " +
							$"no long press on it, the drag selected nothing ('{list.SelectedItem}')",
							panned.Contains((GestureStatus.Started, "row two")) && panned.Contains((GestureStatus.Completed, "row two")) &&
							!held.Contains("row two") && list.SelectedItem is null);
						done();
					});
				});
			});
		});
	}
}
