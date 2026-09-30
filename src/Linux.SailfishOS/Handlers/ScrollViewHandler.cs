using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.SailfishOS.Platform.QtHost;

namespace Microsoft.Maui.SailfishOS.Handlers;

/// <summary>
/// ScrollView handler. It pushes the viewport state (scroll position, orientation, scroll bars, background) and
/// answers <see cref="IScrollViewController.ScrollToRequested"/>, otherwise ScrollToAsync never completes: it writes
/// ScrollX/ScrollY (the mapper pushes them to the flickable) and always calls SendScrollFinished; ShouldAnimate is a
/// jump. Inside collection rows the ListView delegate scrolls, so the host is a plain container there.
/// </summary>
public class ScrollViewHandler : SailfishSnapshotHandler<IScrollView>
{
	private static readonly string[] Keys =
	{
		nameof(ScrollView.ScrollX), nameof(ScrollView.ScrollY), nameof(IScrollView.Orientation),
		nameof(IScrollView.HorizontalScrollBarVisibility), nameof(IScrollView.VerticalScrollBarVisibility),
		nameof(VisualElement.BackgroundColor), nameof(IView.Background),
	};

	public static readonly PropertyMapper<IScrollView, ScrollViewHandler> Mapper = SnapshotMapper<ScrollViewHandler>(Keys);

	public static readonly CommandMapper<IScrollView, ScrollViewHandler> CommandMapper = new(ViewCommandMapper);

	private IScrollViewController? _controller;
	private EventHandler<ScrollToRequestedEventArgs>? _onScrollToRequested;

	public ScrollViewHandler() : base(Mapper, CommandMapper, Keys)
	{
	}

	/// <summary>A native scroll writes ScrollX/ScrollY back; pushing it again would fight the flick in flight.</summary>
	protected override bool SnapshotYieldsToNative => true;

	/// <summary>The scroll position moves the content's root rects (hit-testing, the viewport clip): a geometry pass,
	/// also after a native scroll. MAUI's layout does not depend on it, so no measure/arrange.</summary>
	public override void UpdateValue(string property)
	{
		base.UpdateValue(property);
		if (property is nameof(ScrollView.ScrollX) or nameof(ScrollView.ScrollY))
			QtHostPageRenderer.Current?.RequestScrollGeometry();
	}

	protected override Dictionary<string, object?>? Snapshot(IScrollView view) =>
		view is not ScrollView scroll ? null
		: ((IElementHandler)this).PlatformView is NativeElementHost { QmlUri: "scroll-view" }
			? QtHostPageRenderer.ScrollProps(scroll)
			: QtHostPageRenderer.ContainerProps(scroll);

	protected override void ConnectHandler(NativeElementHost platformView)
	{
		base.ConnectHandler(platformView);
		if (VirtualView is IScrollViewController controller)
		{
			_controller = controller;
			_onScrollToRequested = OnScrollToRequested;
			controller.ScrollToRequested += _onScrollToRequested;
		}
	}

	protected override void DisconnectHandler(NativeElementHost platformView)
	{
		if (_controller is not null && _onScrollToRequested is not null)
			_controller.ScrollToRequested -= _onScrollToRequested;
		_controller = null;
		_onScrollToRequested = null;
		base.DisconnectHandler(platformView);
	}

	private void OnScrollToRequested(object? sender, ScrollToRequestedEventArgs e)
	{
		if (sender is not IScrollViewController controller)
			return;
		try
		{
			var x = e.ScrollX;
			var y = e.ScrollY;
			switch (e.Mode)
			{
				case ScrollToMode.Position:
					controller.SetScrolledPosition(x, y);
					break;
				case ScrollToMode.Element when e.Element is VisualElement element && sender is ScrollView scroll:
				{
					var target = scroll.GetScrollPositionForElement(element, e.Position);
					controller.SetScrolledPosition(target.X, target.Y);
					break;
				}
				default:
					// Unsupported modes still finish so the caller's task completes.
					break;
			}
		}
		finally
		{
			controller.SendScrollFinished();
		}
	}
}
