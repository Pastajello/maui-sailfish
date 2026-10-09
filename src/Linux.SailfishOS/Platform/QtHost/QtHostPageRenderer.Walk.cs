using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>The walk from MAUI elements to desired hosts: which element gets a host and with which adapter (rebinding when the handler chooses another), what a collection row maps, which row layouts stay flat.</summary>
internal sealed partial class QtHostPageRenderer
{
	/// <summary>Maps one item template instantiation (root included) like the page Walk.</summary>
	internal void MapItemSubtree(VisualElement root, List<NativeElementHost> desired,
	                             Dictionary<NativeElementHost, Dictionary<string, object?>> props)
	{
		var first = desired.Count;
		_mappingRowsDepth++;
		try
		{
			MapElement(root, desired, props);
			// The row root has no host parent: it lands in the delegate/slot placeholder.
			var rootHost = desired.Count > first && ReferenceEquals(desired[first].Element, root) ? desired[first] : null;
			if (rootHost is not null)
				rootHost.Parent = null;
			Walk(root, desired, props, parentHost: rootHost, nest: true);
		}
		finally
		{
			_mappingRowsDepth--;
		}
		// Visual state and corner clips join row snapshots here, since rows never pass through the page reconcile.
		// Row content isn't attached to the page tree, so the rendered page is the surround-colour fallback.
		for (var i = first; i < desired.Count; i++)
			MergeGenericState(desired[i], props, _rendered);
	}

	/// <summary>The matrix of a root that sits directly on its canvas (the page canvas, a row placeholder): its own
	/// transform, then its arranged position plus the canvas offset (W3.5: built three times).</summary>
	private static Affine2 RootMatrix(VisualElement root, double offsetX = 0, double offsetY = 0) =>
		QtHostVisualState.LocalTransform(root, root.Bounds.Width, root.Bounds.Height)
			.Then(Affine2.Translation(offsetX + root.Bounds.X, offsetY + root.Bounds.Y));

	/// <summary>
	/// What joins every host's snapshot besides its handler's (W3.5: three copies): the generic visual state
	/// (opacity/enabled/z cascades computed managed-side), and on an image the corner clip of a rounded Border it sits
	/// in. Children of a rounded Border are sibling hosts, so the clip crosses as a spec and the adapter masks itself;
	/// only adapters that declare mauiClipRadius/mauiClipCorners may get it (an unknown name fails the whole batch).
	/// </summary>
	private static void MergeGenericState(NativeElementHost host, Dictionary<NativeElementHost, Dictionary<string, object?>> props,
	                                      Page? surround)
	{
		if (!props.TryGetValue(host, out var hostProps))
			return;
		if (host.Element is VisualElement visual)
			QtHostVisualState.Merge(hostProps, visual);
		if (host.QmlUri == "image")
			QtHostClip.Merge(hostProps, host.Element as VisualElement, surround);
	}

	/// <summary>Lays out one row/slot subtree inside its placeholder, rooted at the cell offset. Nothing depends
	/// on the delegate's scene position, so scrolling never re-pushes row geometry; row
	/// <see cref="NativeElementHost.MauiLogicalBounds"/> are delegate-relative.</summary>
	internal void PushItemGeometry(VisualElement root, double cellX, IReadOnlyCollection<NativeElementHost> hosts,
	                               bool crossAlongY = false)
	{
		if (hosts.Count == 0)
			return;
		// The root's arranged position in its cell is its Margin (the cell is arranged at 0,0), as for the page root.
		// cellX is the cell's offset across the scroll axis: x in a vertical grid, y in a horizontal one.
		var rootMatrix = RootMatrix(root, crossAlongY ? 0 : cellX, crossAlongY ? cellX : 0);
		CollectGeometry(root, rootMatrix, rootMatrix, hosts as HashSet<NativeElementHost> ?? new HashSet<NativeElementHost>(hosts),
			parentVisible: true, hitClip: null);
		FlushGeometry();
	}

	/// <summary>
	/// Pre-order walk of the MAUI visual tree into the desired host list, keyed by element identity so hosts
	/// survive MAUI rebuilds. With <paramref name="nest"/> each host records its nearest hosted ancestor
	/// (<see cref="NativeElementHost.Parent"/>, null = page canvas), so the native tree mirrors MAUI's.
	/// </summary>
	private void Walk(IVisualTreeElement element, List<NativeElementHost> desired,
	                  Dictionary<NativeElementHost, Dictionary<string, object?>> props,
	                  NativeElementHost? parentHost = null, bool nest = true)
	{
		foreach (var child in QtHostVisualChildren.Of(element))
		{
			// MAUI parents a TitleView to its page: the page walk reaches it last, in the header (WalkPage).
			if (element is Page owner && child is View titleView && IsTitleViewOf(owner, titleView))
				continue;
			WalkChild(child, desired, props, parentHost, nest);
		}
	}

#pragma warning disable CS0618
	private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TableView, object> TableViewWarned = new();
#pragma warning restore CS0618

	private static bool IsTitleViewOf(Page page, View view) =>
		ReferenceEquals(NavigationPage.GetTitleView(page), view) || ReferenceEquals(Shell.GetTitleView(page), view);

	/// <summary>One element of the walk and its subtree: its host (under <paramref name="parentHost"/>, null = page
	/// canvas), then its children.</summary>
	private void WalkChild(IVisualTreeElement child, List<NativeElementHost> desired,
	                       Dictionary<NativeElementHost, Dictionary<string, object?>> props,
	                       NativeElementHost? parentHost, bool nest = true)
	{
		// ContextFlyout registry (an attached property on FlyoutBase in .NET 11).
		if (child is View flyoutView &&
		    FlyoutBase.GetContextFlyout(flyoutView) is MenuFlyout flyout && flyout.Count > 0)
			_contextFlyouts[flyoutView] = flyout;

		// TableView is not rendered (D2 b, tracker S35): said once instead of an empty area without a word.
#pragma warning disable CS0618   // TableView is obsolete in MAUI 11; apps still ship it
		if (child is TableView table && TableViewWarned.TryAdd(table, TableViewWarned))
			QtHostDiag.Warn(QtHostDiagChannel.QtHost,
				"TableView is not supported on Sailfish and renders nothing; use a CollectionView or a ListView with TextCell/ViewCell rows");
#pragma warning restore CS0618

		var before = desired.Count;
		var walkChildren = child is not Element childElement || MapElement(childElement, desired, props);
		// The child's own host is the first one added for it; list/row bridges may append more.
		var childHost = desired.Count > before && ReferenceEquals(desired[before].Element, child)
			? desired[before]
			: null;
		if (nest && childHost is not null)
			childHost.Parent = parentHost;
		if (!walkChildren)
			return;   // the collection bridge owns the subtree

		Walk(child, desired, props, childHost ?? parentHost, nest);
	}

	/// <summary>
	/// Maps one element to its adapter host. The element's handler chooses the adapter and supplies its state, as a
	/// MAUI handler creates its native view (<see cref="HostingOf"/>); the cases here only add what needs the page
	/// around the element (collections, the refresh surface, the page's main scroll, image placeholders). Returns false
	/// when the page reconcile must not walk the subtree (a collection's items are materialized per delegate by
	/// <see cref="QtHostCollectionBridge"/>, a WebView or a drawn IndicatorView has no MAUI children to host).
	/// </summary>
	private bool MapElement(Element child, List<NativeElementHost> desired,
	                        Dictionary<NativeElementHost, Dictionary<string, object?>> props)
	{
		switch (child)
		{
			case CollectionView collection:
				// Native virtualized ListView: the bridge registers the host and materializes item trees per delegate.
				_collection.RegisterList(collection, desired, props);
				return false;
			case CarouselView carousel:
				// The same bridge in carousel mode: one snapped page per item (Position/CurrentItem sync).
				_collection.RegisterList(carousel, desired, props);
				return false;
			case ScrollView rowScroll when _mappingRows:
			{
				// In collection rows the ListView delegate is the scroller, so a template ScrollView is a plain container.
				var host = _cache.GetOrAdd(rowScroll, "content-view");
				props[host] = Handlers.AdapterSnapshots.ContainerProps(rowScroll);
				desired.Add(host);
				return true;
			}
			case ScrollView scrollView:
			{
				// Every ScrollView is its own SilicaFlickable host whose content scrolls natively (ScrollX/ScrollY sync both
				// ways); Silica pulleys clone onto it like onto a hosted list.
				var host = _cache.GetOrAdd(scrollView, QtHostAdapters.ScrollView);
				props[host] = Handlers.AdapterSnapshots.ScrollProps(scrollView);
				if (_primaryScrollWalk is null && scrollView.Orientation == ScrollOrientation.Vertical)
					_primaryScrollWalk = scrollView;
				// The first ScrollView inside a RefreshView carries the pull gesture and spinner itself.
				if (_scrollRefreshWalk is null && RefreshAncestorOf(scrollView) is { } refresh)
				{
					_scrollRefreshWalk = refresh;
					_scrollRefreshHostWalk = host;
					foreach (var kv in RefreshSurfaceProps(refresh))
						props[host][kv.Key] = kv.Value;
				}
				desired.Add(host);
				return true;
			}
			case IImage image when HostingOf((View)child) is { State: null }:
			{
				// QtHostImages resolves no URL: an unresolvable file keeps an empty placeholder (Android and iOS show
				// nothing for a missing file; a visible "[Image]" read as app text) and a warning, a stream still being
				// read hosts nothing yet, and a null source hosts nothing (a placeholder would lock the element to a label
				// host in the first-URI-wins cache, so a later Source could never create the image host).
				if (image.Source is not null && !QtHostImages.IsPending(image.Source as ImageSource))
				{
					if (_missingImageWarned.Add(image.Source.ToString() ?? string.Empty))
						QtHostDiag.Warn(QtHostDiagChannel.QmlObject, $"image source not found, nothing shown: {image.Source}");
					AddPlaceholder(child, string.Empty, desired, props);
				}
				else if (child.Parent is IView container)
					// Read, the stream has a size: the image re-measures from 0 × 0 and the container gains its host.
					QtHostImages.WhenReady(image.Source as ImageSource, child, () =>
					{
						QtHostImages.InvalidateIntrinsicSize(image);
						RequestSubtree(container);
					});
				return true;
			}
			case Microsoft.Maui.Controls.Shapes.Shape or BoxView when HostingOf((View)child) is { State: null }:
				// A shape reduces to a path of its arranged size: no host before the first arrange, the poll retries.
				_skippedUnarranged = true;
				_awaitingArrange.Add(child);
				return true;
			case RefreshView refreshView:
				// RefreshView modifies a scroll surface (the wrapped list or flickable arms the gesture); it still owns a
				// plain container host for its content.
				_refreshWalk ??= refreshView;
				break;
		}
		if (child is not View view)
			return true;
		// Flat rows: in collection rows a layout that paints nothing gets no QML host of its own; its children go to the nearest
		// hosted ancestor (CollectGeometry folds its offset into their rects). A push that makes it paint remaps the row.
		if (_mappingRows && FlatRows && view is Grid or StackBase)
		{
			if (view.Handler is null)
				QtHostLayout.AttachHandlers(view, _mauiContext);
			if (CanFlatten(view))
			{
				_flatRowContainers.AddOrUpdate(view, s_flatMarker);
				return true;
			}
		}
		var hosting = HostingOf(view) ?? (Uri: "content-view", State: null, WalksChildren: true);
		if (hosting.Uri == "content-view" && view is not (Layout or TemplatedView or IContentView) &&
		    view.Handler is not Handlers.ISailfishAdapterHandler { AdapterUri: not null } && _unsupportedWarned.Add(view.GetType()))
			QtHostDiag.Warn(QtHostDiagChannel.QtHost,
				$"no Sailfish adapter for {view.GetType().FullName} — rendered as an empty container (its children still paint)");
		var viewHost = _cache.GetOrAdd(view, hosting.Uri);
		props[viewHost] = hosting.State ?? Handlers.AdapterSnapshots.ContainerProps(view);
		desired.Add(viewHost);
		return hosting.WalksChildren;
	}

	/// <summary>Row layouts mapped without a host (flat rows).</summary>
	internal bool IsFlattened(VisualElement element) => _flatRowContainers.TryGetValue(element, out _);

	/// <summary>True when the layout paints and does nothing a QML item would carry: no fill, clip, shadow, transform,
	/// opacity, input, gesture, semantics or z-order of its own. Such a container is pure geometry.</summary>
	internal static bool CanFlatten(View view)
	{
		if (view is not ILayout layout || layout.ClipsToBounds || view.Clip is not null || view.Shadow is not null)
			return false;
		if (QtHostPaint.Background(view) is { Alpha: > 0 })
			return false;
		if (view.Opacity < 1 || !view.IsVisible || !view.IsEnabled || view.InputTransparent || view.ZIndex != 0 ||
		    view.GestureRecognizers.Count > 0 || FlyoutBase.GetContextFlyout(view) is not null)
			return false;
		if (view.Rotation != 0 || view.RotationX != 0 || view.RotationY != 0 || view.Scale != 1 || view.ScaleX != 1 ||
		    view.ScaleY != 1 || view.TranslationX != 0 || view.TranslationY != 0)
			return false;
		if (!string.IsNullOrEmpty(view.AutomationId) || SemanticProperties.GetDescription(view) is not null ||
		    SemanticProperties.GetHint(view) is not null || SemanticProperties.GetHeadingLevel(view) != SemanticHeadingLevel.None ||
		    AutomationProperties.GetIsInAccessibleTree(view) == false || AutomationProperties.GetExcludedWithChildren(view) == true)
			return false;
		// Only the plain container adapters; anything with an adapter of its own keeps it.
		return view.Handler is not Handlers.ISailfishAdapterHandler { AdapterUri: { } uri } ||
		       uri is "grid" or "stack-layout" or "content-view";
	}

	/// <summary>A handler pushed to a flattened row layout (it has no live host): when the layout now paints, the row
	/// is mapped again and the layout gets its host.</summary>
	private void OnFlattenedPush(VisualElement element)
	{
		if (element is not View view || CanFlatten(view))
			return;
		_flatRowContainers.Remove(element);
		_collection.RemapRowContaining(element);
	}

	/// <summary>The adapter the element's handler chose and its state (handlers attach on demand); null for a handler
	/// that is not a Sailfish one (the element then gets a plain container host).</summary>
	private (string Uri, Dictionary<string, object?>? State, bool WalksChildren)? HostingOf(View view)
	{
		if (view.Handler is null)
			QtHostLayout.AttachHandlers(view, _mauiContext);
		if (view.Handler is not Handlers.ISailfishAdapterHandler handler)
			return null;
		if (NeedsRebind(view, handler))
		{
			RebindAdapter(view);
			if (view.Handler is not Handlers.ISailfishAdapterHandler rebound)
				return null;
			handler = rebound;
		}
		// A library adapter must be registered; an unknown URI would degrade to the fallback label.
		var uri = ChosenAdapter(handler) ?? "content-view";
		return (uri, handler.AdapterState(), handler.WalksChildren);
	}

	private static string? ChosenAdapter(Handlers.ISailfishAdapterHandler handler) =>
		handler.AdapterUri is { } chosen && QtHostAdapters.TryGetSrc(chosen, out _) ? chosen : null;

	/// <summary>The handler's adapter is no longer the one its host was created from: an Image whose Source now
	/// resolves (it had an empty placeholder), an IndicatorView or RadioButton whose template came or went. A host
	/// the reconcile bound to a generic container or placeholder stays while the handler chooses nothing.</summary>
	private bool NeedsRebind(View view, Handlers.ISailfishAdapterHandler handler)
	{
		if (!_cache.TryGet(view, out var bound))
			return false;
		return ChosenAdapter(handler) is { } chosen
			? bound!.QmlUri != chosen
			: bound!.QmlUri is not ("content-view" or "label");
	}

	/// <summary>A QML object cannot change its type, so the element gets a fresh host and handler, as another platform
	/// recreates a platform view; the element's old host leaves the desired tree and the diff destroys it.</summary>
	internal void RebindAdapter(View view)
	{
		QtHostDiag.Trace(QtHostDiagChannel.QmlObject, $"adapter rebind {view.GetType().Name}");
		_cache.Forget(view);
		view.Handler?.DisconnectHandler();
		QtHostLayout.AttachHandlers(view, _mauiContext);
	}

	private void AddPlaceholder(Element element, string text, List<NativeElementHost> desired,
	                            Dictionary<NativeElementHost, Dictionary<string, object?>> props)
	{
		var host = _cache.GetOrAdd(element, "label");
		props[host] = new Dictionary<string, object?> { ["text"] = text, ["mauiEmphasis"] = "secondary" };
		desired.Add(host);
	}
}
