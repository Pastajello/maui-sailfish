using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.SailfishOS.Platform.QtHost;

namespace Microsoft.Maui.SailfishOS.Handlers;

/// <summary>WebView handler on the Gecko adapter; navigation and JS commands go out as transient props,
/// and JS results come back as events.</summary>
public class SailfishWebViewHandler : SailfishSnapshotHandler<IWebView>
{
	private static readonly string[] Keys = { nameof(WebView.Source), nameof(IWebView.UserAgent) };

	public static readonly PropertyMapper<IWebView, SailfishWebViewHandler> Mapper = SnapshotMapper<SailfishWebViewHandler>(Keys);

	private int _jsSeq;
	private readonly Dictionary<string, EvaluateJavaScriptAsyncRequest> _pendingJs = new(StringComparer.Ordinal);

	public static readonly CommandMapper<IWebView, SailfishWebViewHandler> CommandMapper = new(SailfishViewMapper.CommandMapper)
	{
		[nameof(IWebView.GoBack)] = (h, _, _) => h.Nav(SailfishKeys.Command.Back),
		[nameof(IWebView.GoForward)] = (h, _, _) => h.Nav(SailfishKeys.Command.Forward),
		[nameof(IWebView.Reload)] = (h, _, _) => h.Nav(SailfishKeys.Command.Reload),
		[nameof(IWebView.Eval)] = (h, _, args) => h.RunJs(args as string, null),
		[nameof(IWebView.EvaluateJavaScriptAsync)] = (h, _, args) =>
		{
			if (args is EvaluateJavaScriptAsyncRequest request)
				h.RunJs(request.Script, request);
		},
	};

	private static int _sandboxChecked;

	public SailfishWebViewHandler() : this(null)
	{
	}

	public SailfishWebViewHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
		: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
	{
	}

	protected override string? AdapterUri => SailfishKeys.Adapter.WebView;

	protected override void ConnectHandler(NativeElementHost platformView)
	{
		base.ConnectHandler(platformView);
		// Gecko cannot start in Sailjail without the WebView permission; MAUI has no permission type for it to deny.
		if (Interlocked.Exchange(ref _sandboxChecked, 1) == 0 && !Platform.SailfishPermissions.Declares("WebView"))
			QtHostDiag.Warn(QtHostDiagChannel.QmlLoad,
				"WebView: " + Platform.SailfishPermissions.MissingMessage("WebView", new[] { "WebView" }));
	}

	protected override bool WalksChildren => false;

	protected override Dictionary<string, object?>? Snapshot(IWebView view) =>
		view is WebView web ? AdapterSnapshots.WebViewProps(web) : null;

	private void Nav(string action) =>
		SendCommand(SailfishKeys.Command.Nav, new() { [SailfishKeys.Command.NavAction] = action });

	private void RunJs(string? script, EvaluateJavaScriptAsyncRequest? request)
	{
		if (string.IsNullOrEmpty(script))
		{
			request?.SetResult(null!);
			return;
		}
		var id = "js" + (++_jsSeq).ToString(System.Globalization.CultureInfo.InvariantCulture);
		if (request is not null)
			lock (_pendingJs)
				_pendingJs[id] = request;
		if (!SendCommand(SailfishKeys.Command.Js,
			    new() { [SailfishKeys.Command.JsRequest] = id, [SailfishKeys.Command.JsScript] = script }))
			CompleteJs(id, ok: false, null);   // no page to run it in: the caller gets null, as for a script error
	}

	/// <summary>Completes a pending EvaluateJavaScriptAsync (null on a script error, as MAUI does).</summary>
	internal void CompleteJs(string requestId, bool ok, string? result)
	{
		EvaluateJavaScriptAsyncRequest? request;
		lock (_pendingJs)
			_pendingJs.Remove(requestId, out request);
		request?.SetResult(ok ? result! : null!);
	}

	/// <summary>The page went away with scripts in flight: their callers get null (as for a script error) instead of
	/// waiting forever for a result the destroyed Gecko view will never send.</summary>
	protected override void DisconnectHandler(NativeElementHost platformView)
	{
		EvaluateJavaScriptAsyncRequest[] pending;
		lock (_pendingJs)
		{
			pending = _pendingJs.Values.ToArray();
			_pendingJs.Clear();
		}
		foreach (var request in pending)
			request.SetResult(null!);
		base.DisconnectHandler(platformView);
	}
}

/// <summary>SwipeView handler; Open/Close commands move the native row.</summary>
public class SailfishSwipeViewHandler : SailfishSnapshotHandler<ISwipeView>
{
	private static readonly string[] Keys =
	{
		nameof(ISwipeView.LeftItems), nameof(ISwipeView.RightItems), nameof(ISwipeView.TopItems),
		nameof(ISwipeView.BottomItems), nameof(ISwipeView.Threshold),
		nameof(VisualElement.BackgroundColor), nameof(VisualElement.Background),
		nameof(ISwipeView.SwipeTransitionMode),
	};

	public static readonly PropertyMapper<ISwipeView, SailfishSwipeViewHandler> Mapper = SnapshotMapper<SailfishSwipeViewHandler>(Keys);

	public static readonly CommandMapper<ISwipeView, SailfishSwipeViewHandler> CommandMapper = new(SailfishViewMapper.CommandMapper)
	{
		[nameof(ISwipeView.RequestOpen)] = (h, _, args) => h.Open(args is SwipeViewOpenRequest open ? SideOf(open.OpenSwipeItem) : "right"),
		[nameof(ISwipeView.RequestClose)] = (h, _, _) => h.Open(string.Empty),
	};

	public SailfishSwipeViewHandler() : this(null)
	{
	}

	public SailfishSwipeViewHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
		: base(mapper ?? Mapper, commandMapper ?? CommandMapper, SailfishMeasure.Content)
	{
	}

	protected override string? AdapterUri => SailfishKeys.Adapter.SwipeView;

	protected override Dictionary<string, object?>? Snapshot(ISwipeView view) =>
		view is SwipeView swipe ? AdapterSnapshots.SwipeProps(swipe) : null;

	private static string SideOf(OpenSwipeItem item) => item switch
	{
		OpenSwipeItem.LeftItems => "left",
		OpenSwipeItem.RightItems => "right",
		_ => "right",   // top/bottom items are not rendered yet
	};

	private void Open(string side) => SendCommand(SailfishKeys.Command.Open, new() { [SailfishKeys.Command.OpenSide] = side });

	// A SwipeItem's own properties (Text, colours, icon, visibility) change without the SwipeView raising anything: the
	// handler watches the items and re-pushes their side, as the platform handlers map each item (tracker S13).
	private SwipeView? _watched;
	private readonly HashSet<Element> _watchedItems = new();

	public override void SetVirtualView(IView view)
	{
		base.SetVirtualView(view);
		Watch(view as SwipeView);
	}

	protected override void DisconnectHandler(NativeElementHost platformView)
	{
		Watch(null);
		base.DisconnectHandler(platformView);
	}

	private void Watch(SwipeView? swipe)
	{
		if (_watched is { } old)
			foreach (var items in new[] { old.LeftItems, old.RightItems })
				if (items is not null)
					items.CollectionChanged -= OnItemsChanged;
		foreach (var item in _watchedItems)
			item.PropertyChanged -= OnItemChanged;
		_watchedItems.Clear();
		_watched = swipe;
		if (swipe is null)
			return;
		foreach (var items in new[] { swipe.LeftItems, swipe.RightItems })
		{
			if (items is null)
				continue;
			items.CollectionChanged += OnItemsChanged;
			foreach (var element in items)
				if (element is Element item && _watchedItems.Add(item))
					item.PropertyChanged += OnItemChanged;
		}
	}

	private void OnItemsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
	{
		Watch(_watched);   // new items are watched too
		Repush();
	}

	private void OnItemChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Repush();

	private void Repush()
	{
		if (ConnectedView is not null)
			UpdateValue(nameof(ISwipeView.LeftItems));   // one snapshot key re-pushes both sides
	}
}

/// <summary>CollectionView handler: hosting only; <see cref="QtHostCollectionBridge"/> owns rows and selection.</summary>
public class SailfishListViewHandler : SailfishSnapshotHandler<IView>
{
	private static readonly string[] Keys = [];

	/// <summary>The list follows its ItemsView properties through this mapper (QtHostCollectionBridge.ViewProperties).</summary>
	public static readonly PropertyMapper<IView, SailfishListViewHandler> Mapper = WithItemsProperties(SnapshotMapper<SailfishListViewHandler>(Keys));

	private static PropertyMapper<IView, SailfishListViewHandler> WithItemsProperties(PropertyMapper<IView, SailfishListViewHandler> mapper)
	{
		foreach (var key in QtHostCollectionBridge.ViewProperties)
			mapper[key] = MapItemsProperty;
		return mapper;
	}

	/// <summary>Hands an ItemsView change to the list's adapter; the connect pass is the registration's.</summary>
	public static void MapItemsProperty(SailfishListViewHandler handler, IView view) =>
		handler.OnItemsProperty(view);

	private string? _mapping;   // the key being mapped (PropertyMapper actions do not receive it)

	public override void UpdateValue(string property)
	{
		_mapping = property;
		try
		{
			base.UpdateValue(property);
		}
		finally
		{
			_mapping = null;
		}
	}

	private void OnItemsProperty(IView view)
	{
		if (!IsConnecting && _mapping is { } key)
			Adapter?.OnViewProperty(key);
	}

	/// <summary>The list's adapter (rows, delegates, slots, selection, scroll) while the list is on a page, as a
	/// RecyclerView's adapter is its handler's; the mapper hands it every ItemsView change, the measure reads its extent.
	/// The page reconcile creates it when it meets the list (the rows need the page around it) and retires it when the
	/// list leaves; the handler asks for it instead of being handed it.</summary>
	internal QtHostListAdapter? Adapter =>
		ConnectedView is ItemsView view ? SailfishHandlerCore.SessionOf(this)?.ListAdapterOf(view) : null;

	public static readonly CommandMapper<IView, SailfishListViewHandler> CommandMapper = new(SailfishViewMapper.CommandMapper);

	public SailfishListViewHandler() : this(null)
	{
	}

	public SailfishListViewHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
		: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
	{
		_measure = (_, widthConstraint, heightConstraint) => SailfishMeasure.Collection(Adapter, widthConstraint, heightConstraint);
	}

	private readonly Func<IView, double, double, Size> _measure;

	/// <summary>The list measures from its own adapter's extent (its rows, or the cross size of a horizontal list).</summary>
	public override Size GetDesiredSize(double widthConstraint, double heightConstraint) =>
		ConnectedView is { } view ? SailfishMeasure.Frame(view, widthConstraint, heightConstraint, _measure) : Size.Zero;

	protected override string? AdapterUri => QtHostCollectionBridge.AdapterUriFor(ConnectedView);

	protected override Dictionary<string, object?>? Snapshot(IView view) => null;
}
