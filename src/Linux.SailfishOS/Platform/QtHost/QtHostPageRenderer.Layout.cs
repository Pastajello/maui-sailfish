using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

// Hit-testing, window geometry, the MAUI layout pass and the batched SetGeometry push; page scroll and refresh state.
internal sealed partial class QtHostPageRenderer
{
	/// <summary>
	/// Hit-test: the topmost effectively-visible host whose absolute root-space rect (dp,
	/// <see cref="NativeElementHost.MauiLogicalBounds"/>) contains the point. Hidden and input-transparent
	/// subtrees are skipped. Called on the Qt thread by <see cref="QtHostInputRouter"/>.
	/// </summary>
	internal bool TryHitTest(double dpX, double dpY, out NativeElementHost? host)
	{
		// Rects are root space and already scroll-shifted/clipped (HitClipDp), so the point needs no translation.
		// Higher ZIndex wins; ties go to the later (visually upper) child, mirroring the native `z` push.
		NativeElementHost? top = null;
		var topZ = double.MinValue;
		for (var i = 0; i < _current.Count; i++)
		{
			var candidate = _current[i];
			if (!candidate.AppliedGeometrySet || !candidate.AppliedVisible || !candidate.IsAttached)
				continue;
			if (candidate.Element is not VisualElement ve || !IsEffectivelyVisible(ve))
				continue;
			var bounds = candidate.MauiLogicalBounds;
			if (bounds.Width <= 0 || bounds.Height <= 0)
				continue;
			if (!HitsFootprint(candidate, bounds, dpX, dpY))
				continue;
			// InputTransparent (and a CascadeInputTransparent layout above it) lets the touch through to the view below,
			// as on the platforms: it is skipped here instead of ending the sequence (tracker S16).
			if (IsInputTransparent(ve))
				continue;
			// Content scrolled out of a nested scroll viewport is not hit.
			if (candidate.HitClipDp is { } clip && !clip.Contains(dpX, dpY))
				continue;
			// A host inside another paints above it whatever the list order (a nested ScrollView's host can come after its
			// content's), so a descendant wins over its ancestor and an ancestor never displaces its descendant.
			if (top is not null && IsAncestor(ve, top.Element))
				continue;
			if (top is null || ve.ZIndex >= topZ || IsAncestor(top.Element, ve))
			{
				top = candidate;
				topZ = ve.ZIndex;
			}
		}
		host = top;
		return top is not null;
	}

	/// <summary>Whether the point lies on the host's transformed footprint: the root rect when the transform is a
	/// translation, else the point mapped back into the element's own 0..w × 0..h (Scale, Rotation, anchors).</summary>
	/// <summary><c>GetPosition(relativeTo)</c> of a gesture at <paramref name="root"/> (root-space dp): null asks for the
	/// window, a view gets the point in its own coordinates (through its Scale/Rotation when it has one), a page or the
	/// window gets the root point; an element with no attached host has no position (null), as on the platforms.</summary>
	internal Point? RelativePosition(Point root, IElement? relativeTo)
	{
		if (relativeTo is null or Page or IWindow)
			return root;
		if (relativeTo is not Element element || !Cache.TryGet(element, out var host) || host is not { IsAttached: true })
			return null;
		if (host.HitTransform is { } toRoot)
		{
			if (!toRoot.TryInvert(out var toLocal))
				return null;
			var (lx, ly) = toLocal.Transform(root.X, root.Y);
			return new Point(lx, ly);
		}
		var bounds = host.MauiLogicalBounds;
		return new Point(root.X - bounds.X, root.Y - bounds.Y);
	}

	private static bool HitsFootprint(NativeElementHost host, Rect bounds, double dpX, double dpY)
	{
		if (host.HitTransform is not { } toRoot)
			return bounds.Contains(dpX, dpY);
		if (!toRoot.TryInvert(out var toLocal))
			return false;
		var (lx, ly) = toLocal.Transform(dpX, dpY);
		return lx >= 0 && ly >= 0 && lx < bounds.Width && ly < bounds.Height;
	}

	/// <summary>The element lets touches through: its own InputTransparent, or a layout above it that is
	/// InputTransparent with CascadeInputTransparent (the default), which makes its whole subtree transparent.</summary>
	internal static bool IsInputTransparent(VisualElement element)
	{
		if (element.InputTransparent)
			return true;
		for (var e = element.Parent; e is not null; e = e.Parent)
			if (e is Microsoft.Maui.Controls.Layout { InputTransparent: true, CascadeInputTransparent: true })
				return true;
		return false;
	}

	/// <summary>
	/// Diagnostics: the first host whose element owns a <see cref="TapGestureRecognizer"/>, as an injected-tap
	/// target. Bounds are absolute root-space dp.
	/// </summary>
	internal bool TryFindTapTarget(out NativeElementHost? host, out Rect dpBounds)
	{
		foreach (var candidate in _current)
		{
			if (!candidate.AppliedGeometrySet || !candidate.AppliedVisible || !candidate.IsAttached)
				continue;
			if (candidate.Element is View { IsEnabled: true } v &&
			    IsEffectivelyVisible(v) &&
			    v.GestureRecognizers.Any(r => r is TapGestureRecognizer))
			{
				host = candidate;
				dpBounds = candidate.MauiLogicalBounds;
				return true;
			}
		}
		host = null;
		dpBounds = default;
		return false;
	}

	/// <summary>Diagnostics: the first attached, visible native text input ("entry"/"editor"), as a focus target.</summary>
	internal bool TryFindFocusTarget(out NativeElementHost? host, out Rect dpBounds)
	{
		foreach (var candidate in _current)
		{
			if (!candidate.AppliedGeometrySet || !candidate.AppliedVisible || !candidate.IsAttached)
				continue;
			if ((candidate.QmlUri == "entry" || candidate.QmlUri == "editor") &&
			    candidate.Element is VisualElement v && v.IsEnabled && IsEffectivelyVisible(v))
			{
				host = candidate;
				dpBounds = candidate.MauiLogicalBounds;
				return true;
			}
		}
		host = null;
		dpBounds = default;
		return false;
	}

	/// <summary>Self-and-ancestors visibility; guards against stale applied state after late collapses.</summary>
	private static bool IsEffectivelyVisible(VisualElement element)
	{
		for (VisualElement? v = element; v is not null; v = v.Parent as VisualElement)
		{
			if (!v.IsVisible || ((IView)v).Visibility != Visibility.Visible)
				return false;
		}
		return true;
	}

	// --- Window geometry, MAUI layout pass and SetGeometry ---

	/// <summary>
	/// Consumes the QML "window-geometry" report {pageWidth, pageHeight, headerHeight, statusHeight} (Qt scene
	/// units), pulls the Qt screen info, updates the unit conversion and requests a relayout.
	/// </summary>
	internal void ApplyWindowGeometry(string payload)
	{
		double pageW, pageH, header, status, title;
		try
		{
			using var doc = JsonDocument.Parse(payload);
			var root = doc.RootElement;
			pageW = BridgeJson.Num(root, "pageWidth");
			pageH = BridgeJson.Num(root, "pageHeight");
			header = BridgeJson.Num(root, "headerHeight");
			status = BridgeJson.Num(root, "statusHeight");
			title = BridgeJson.Num(root, "titleHeight");
		}
		catch (Exception ex)
		{
			QtHostDiag.Warn(QtHostDiagChannel.Geometry, $"bad window-geometry payload: {ex.Message}");
			return;
		}
		if (pageW <= 0 || pageH <= 0)
			return;

		// Qt-side report: device pixels, devicePixelRatio, orientation.
		var screenInfo = QtHostRuntime.ScreenInfo();
		var dpr = 1.0;
		double winPxW = 0, winPxH = 0;
		var orientation = string.Empty;
		if (!string.IsNullOrEmpty(screenInfo))
		{
			try
			{
				using var doc = JsonDocument.Parse(screenInfo);
				var root = doc.RootElement;
				if (root.TryGetProperty("window", out var win))
				{
					winPxW = BridgeJson.Num(win, "width");
					winPxH = BridgeJson.Num(win, "height");
					var winDpr = BridgeJson.Num(win, "dpr");
					if (winDpr > 0)
						dpr = winDpr;
				}
				if (root.TryGetProperty("screen", out var scr) &&
				    scr.TryGetProperty("orientation", out var orientEl))
					orientation = orientEl.GetString() ?? string.Empty;
			}
			catch
			{
				// malformed report — fall back to dpr=1 and the QML page size
			}
		}

		var changed = !_windowGeometryKnown
			|| pageW != _lastPageW || pageH != _lastPageH
			|| header != _lastHeader || status != _lastStatus || title != _lastTitleH
			|| dpr != _lastDpr || orientation != _lastOrientation;
		if (GeometryTrace)
			QtHostDiag.Trace(QtHostDiagChannel.Geometry, $"window-geometry {payload} screen_info={screenInfo} changed={changed}");
		if (!changed)
			return;
		_lastPageW = pageW; _lastPageH = pageH; _lastHeader = header; _lastStatus = status; _lastTitleH = title;
		_lastDpr = dpr; _lastOrientation = orientation;

		// Density follows the same dp model as the SDL2 backend, so logical bounds match across backends.
		QtHostUnits.DevicePixelRatio = dpr;
		if (winPxW > 0 && winPxH > 0)
			SailfishDisplay.Update((int)Math.Round(winPxW), (int)Math.Round(winPxH));

		_windowDp = new Size(QtHostUnits.ToLogical(pageW), QtHostUnits.ToLogical(pageH));
		// Content starts below the Silica status area and PageHeader; MAUI never sees the QML chrome.
		var topDp = QtHostUnits.ToLogical(status + header);
		_contentRectDp = new Rect(0, topDp, _windowDp.Width, Math.Max(0, _windowDp.Height - topDp));
		// The PageHeader's own band (no tab rows): a TitleView's area.
		_titleRectDp = new Rect(0, QtHostUnits.ToLogical(status), _windowDp.Width, QtHostUnits.ToLogical(Math.Max(0, title)));
		_windowGeometryKnown = true;
		_layoutDirty = true;
		// Window.Width/Height follow the platform window, as the other platforms report their frame.
		((IWindow)_window).FrameChanged(new Rect(0, 0, _windowDp.Width, _windowDp.Height));

		LastWindowGeometryReport =
			$"page={pageW.ToString("F0", CultureInfo.InvariantCulture)}x{pageH.ToString("F0", CultureInfo.InvariantCulture)}qt " +
			$"windowPx={winPxW.ToString("F0", CultureInfo.InvariantCulture)}x{winPxH.ToString("F0", CultureInfo.InvariantCulture)} " +
			$"dpr={dpr.ToString("F2", CultureInfo.InvariantCulture)} orientation='{orientation}' " +
			$"insets(top={header + status}qt→{topDp.ToString("F1", CultureInfo.InvariantCulture)}dp) " +
			$"density={SailfishDisplay.Density.ToString("F3", CultureInfo.InvariantCulture)} " +
			$"window={_windowDp.Width.ToString("F0", CultureInfo.InvariantCulture)}x{_windowDp.Height.ToString("F0", CultureInfo.InvariantCulture)}dp " +
			$"content=({_contentRectDp.X:F0},{_contentRectDp.Y:F0} {_contentRectDp.Width:F0}x{_contentRectDp.Height:F0})dp";
		// Every change of the window report is logged (rotation/configure evidence).
		QtHostDiag.Trace(QtHostDiagChannel.Geometry, $"window report — {LastWindowGeometryReport}");

		// Relayout + geometry push now, unless a transition or an unfollowed pop holds the reconcile: the poll that
		// ends it lays out with this geometry.
		if (CanReconcile)
			Reconcile();
		else
			_layoutDirty = true;
		// The first report (startup) is also when the stack and the application state become readable: the native
		// sync adopts them on the next loop turn instead of at a timer poll.
		RequestPoll();
	}

	/// <summary>
	/// Runs the MAUI layout when dirty and pushes the resulting absolute rects through SetGeometry.
	/// </summary>
	/// <summary>The last dirty layout pass in ms: MAUI measure + arrange, geometry collection, native flush.</summary>
	public (double MeasureArrangeMs, double CollectMs, double FlushMs) LastLayoutSplit { get; private set; }

	/// <summary>Geometry passes without measure/arrange (<see cref="RequestScrollGeometry"/>).</summary>
	public long GeometryPasses { get; private set; }

	/// <summary>Root rects and the geometry flush only, no MAUI measure/arrange: a scroll moved content, not layout.</summary>
	private void RunGeometryPass(Page page)
	{
		_geometryDirty = false;
		GeometryPasses++;
		_suppressPush++;
		_inLayoutPass = true;
		try
		{
			var root = page is ContentPage contentPage ? contentPage.Content as VisualElement : page;
			var hosted = new HashSet<NativeElementHost>(_current);
			if (root is not null)
			{
				var rootMatrix = RootMatrix(root);
				CollectGeometry(root, rootMatrix, rootMatrix, hosted, parentVisible: true);
			}
			CollectPageOverlays(hosted);
			FlushGeometry();
		}
		catch (Exception ex)
		{
			QtHostDiag.Error(QtHostDiagChannel.Geometry, $"geometry pass failed: {ex.Message}");
			_layoutDirty = true;   // the full pass retries
			KickIn(250);
		}
		finally
		{
			_inLayoutPass = false;
			_suppressPush--;
		}
		_collection.RefreshSceneBounds(force: false);
	}

	/// <summary>The rects of the views on the page canvas outside its content: the TitleView, arranged in the header
	/// (<see cref="_titleRectDp"/>), and the search results over the content area.</summary>
	private void CollectPageOverlays(HashSet<NativeElementHost> hosted)
	{
		foreach (var overlay in new[] { _titleView, _searchResultsView })
		{
			if (overlay is null)
				continue;
			var matrix = RootMatrix(overlay);
			CollectGeometry(overlay, matrix, matrix, hosted, parentVisible: true);
		}
	}

	private void RunLayoutPass(Page page)
	{
		var laidOut = false;
		if (_windowGeometryKnown && _layoutDirty)
		{
			laidOut = true;
			_layoutDirty = false;
			_geometryDirty = false;   // the pass collects the geometry too
			LayoutPasses++;
			// The pass writes Bounds/X/Y/... onto MAUI elements: those writes neither request another pass nor push
			// state that yields to native.
			_suppressPush++;
			_inLayoutPass = true;
			try
			{
				var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
				QtHostLayout.AttachHandlers(page, _mauiContext);
				QtHostLayout.MeasureAndArrange(page, _windowDp, _contentRectDp);
				if (_titleView is { } titleView)
				{
					QtHostLayout.AttachHandlers(titleView, _mauiContext);
					QtHostLayout.MeasureAndArrangeIn(titleView, _titleRectDp);
				}
				if (_searchResultsView is { } results)
				{
					QtHostLayout.AttachHandlers(results, _mauiContext);
					QtHostLayout.MeasureAndArrangeIn(results, _contentRectDp);
				}
				var t1 = System.Diagnostics.Stopwatch.GetTimestamp();

				// Absolutize parent-relative Bounds against the root space (content area origin, dp).
				var root = page is ContentPage contentPage
					? contentPage.Content as VisualElement
					: page;
				var hosted = new HashSet<NativeElementHost>(_current);
				if (root is not null)
				{
					// The root's host sits on the page canvas: its parent-relative matrix is its root matrix.
					var rootMatrix = RootMatrix(root);
					CollectGeometry(root, rootMatrix, rootMatrix, hosted, parentVisible: true);
				}
				CollectPageOverlays(hosted);
				var t2 = System.Diagnostics.Stopwatch.GetTimestamp();

				FlushGeometry();
				PushScrollState();   // page flickable state → native
				PushRefreshState();  // armed RefreshView id + spinner state
				if (_pulleyReattachPending)
				{
					// The geometry batch just showed the page's hosts (a returning tab's list): menus created before it
					// pick their surface and clone again.
					_pulleyReattachPending = false;
					CallPage(null, "mauiReattachPulleys");
				}
				var t3 = System.Diagnostics.Stopwatch.GetTimestamp();
				LastLayoutSplit = (Ms(t0, t1), Ms(t1, t2), Ms(t2, t3));
			}
			catch (Exception ex)
			{
				QtHostDiag.Error(QtHostDiagChannel.Geometry, $"layout pass failed: {ex.Message}");
				if (_layoutFailStackOnce.Add(ex.GetType().Name))
					QtHostDiag.Error(QtHostDiagChannel.Geometry, $"layout pass stack: {ex}");
				_layoutDirty = true;   // retry shortly (a failing pass must not spin the loop)
				KickIn(250);
			}
			finally
			{
				_inLayoutPass = false;
				_suppressPush--;
			}
		}
		_collection.RefreshSceneBounds(force: laidOut);   // item/slot children follow the window
		if (laidOut)
		{
			_collection.SchedulePending();   // a list whose width changed rebuilds its rows
			// Shapes the walk skipped for want of a size may have one now: the reconcile creates them. Only a shape that has a
			// size now is worth a reconcile; one in a hidden subtree is never arranged and would re-kick every pass (each
			// native scroll report of a ScrollView used to run a full-page reconcile).
			if (_awaitingArrange.Any(e => e is VisualElement { Width: > 0, Height: > 0 }))
				RequestPoll();
		}
	}

	/// <summary>
	/// Geometry walk. <paramref name="toRoot"/> maps the element's local space into root space (layout offsets
	/// plus every visual transform) and feeds <see cref="NativeElementHost.MauiLogicalBounds"/>;
	/// <paramref name="toHost"/> maps into the nearest hosted ancestor's QML item, which is what gets pushed
	/// (Qt composes the ancestors). A Collapsed element is applied invisible on its last rect and its subtree skipped.
	/// </summary>
	private void CollectGeometry(VisualElement element, in Affine2 toRoot, in Affine2 toHost,
	                             HashSet<NativeElementHost> hosted, bool parentVisible,
	                             Rect? hitClip = null, bool? inheritedRtl = null)
	{
		// RTL mirrors children inside this element like platform layout managers do (MAUI's arrange is always
		// LTR); each RTL level mirrors its own children, so nesting composes.
		var rtl = element.FlowDirection == FlowDirection.MatchParent
			? inheritedRtl ?? QtHostVisualState.IsRightToLeft(element.Parent)
			: element.FlowDirection == FlowDirection.RightToLeft;
		var visibility = ((IView)element).Visibility;
		var visible = parentVisible && visibility == Visibility.Visible;
		var bounds = element.Bounds;
		NativeElementHost? host = null;
		var isHost = element is not Page && _cache.TryGet(element, out host) && host is not null
		             && host.IsAttached && hosted.Contains(host);
		var nestedScroll = isHost && host is { QmlUri: QtHostAdapters.ScrollView } && element is ScrollView
			? (ScrollView)element
			: null;
		if (isHost && host is not null)
		{
			SetGeometry(host, new Rect(toRoot.Tx, toRoot.Ty, bounds.Width, bounds.Height),
				new Rect(toHost.Tx, toHost.Ty, bounds.Width, bounds.Height), visibility == Visibility.Visible);
			PushTransform(host, element, toHost);
			host.HitClipDp = hitClip;
			host.HitTransform = toRoot.IsTranslationOnly ? null : toRoot;
		}

		// A nested scroll host scrolls its content natively: local rects stay unshifted, root rects shift by the
		// scroll position (hit-testing) and are clipped to the viewport, and the content no longer extends the page.
		var childRootOffset = Affine2.Translation(0, 0);
		var childHitClip = hitClip;
		if (nestedScroll is not null && host is not null)
		{
			PushScrollExtent(host, nestedScroll);
			childRootOffset = Affine2.Translation(-nestedScroll.ScrollX, -nestedScroll.ScrollY);
			var viewport = new Rect(toRoot.Tx, toRoot.Ty, bounds.Width, bounds.Height);
			childHitClip = hitClip is { } outer ? outer.Intersect(viewport) : viewport;
		}

		// A collection's item views are its logical children (for their bindings); the collection bridge places them.
		if (element is ItemsView)
			return;

		foreach (var child in QtHostVisualChildren.Of(element))
		{
			if (child is not VisualElement visual)
				continue;
			if (((IView)visual).Visibility == Visibility.Collapsed)
			{
				// MAUI does not arrange Collapsed subtrees; hiding the host hides its descendants natively.
				if (_cache.TryGet(visual, out var collapsed) && collapsed is not null && collapsed.IsAttached
				    && hosted.Contains(collapsed))
					SetGeometry(collapsed, collapsed.MauiLogicalBounds, LocalOf(collapsed), visible: false);
				continue;
			}
			var childBounds = visual.Bounds;
			var childX = rtl ? ContentWidth(element) - childBounds.X - childBounds.Width : childBounds.X;
			var childLocal = QtHostVisualState
				.LocalTransform(visual, childBounds.Width, childBounds.Height)
				.Then(Affine2.Translation(childX, childBounds.Y));
			CollectGeometry(visual, childLocal.Then(childRootOffset).Then(toRoot),
				isHost ? childLocal : childLocal.Then(toHost), hosted, visible, childHitClip, rtl);
		}
	}

	/// <summary>Width children are mirrored within under RTL: a ScrollView's content extent, else the element's width.</summary>
	private static double ContentWidth(VisualElement element) =>
		element is ScrollView { Content: View content } scroll
			? Math.Max(scroll.Bounds.Width, content.Bounds.Right + content.Margin.Right + scroll.Padding.Right)
			: element.Bounds.Width;

	/// <summary>The nested scroll host's content extent (Qt units): content far edge plus padding, at least the viewport.</summary>
	private void PushScrollExtent(NativeElementHost host, ScrollView scrollView)
	{
		var width = scrollView.Bounds.Width;
		var height = scrollView.Bounds.Height;
		if (scrollView.Content is View content)
		{
			var margin = content.Margin;
			width = Math.Max(width, content.Bounds.Right + margin.Right + scrollView.Padding.Right);
			height = Math.Max(height, content.Bounds.Bottom + margin.Bottom + scrollView.Padding.Bottom);
		}
		ApplyUpdates(host, new Dictionary<string, object?>
		{
			["mauiContentWidth"] = QtHostUnits.ToQtUnits(width),
			["mauiContentHeight"] = QtHostUnits.ToQtUnits(height),
		});
	}

	/// <summary>The last applied parent-relative rect of a host in dp (re-applied invisible on collapse).</summary>
	private static Rect LocalOf(NativeElementHost host) =>
		host.AppliedGeometrySet
			? QtHostUnits.ToLogical(host.AppliedGeometry)
			: new Rect(0, 0, host.MauiLogicalBounds.Width, host.MauiLogicalBounds.Height);

	/// <summary>
	/// Pushes the host's transform: QQuickItem rotation/scale around TopLeft when they reproduce it (the pushed x/y carry
	/// the translation, so uniform scale ∘ rotation is exact), else a QML Matrix4x4 ("mauiMatrix": non-uniform scale,
	/// shear, RotationX/RotationY). An identity resets whatever was pushed before.
	/// </summary>
	private void PushTransform(NativeElementHost host, VisualElement element, in Affine2 toHost)
	{
		var limit = QtHostVisualState.TransformLimit(element);
		if (limit is not null && _transformLimitWarned.Add(host.Id))
			QtHostDiag.Trace(QtHostDiagChannel.QmlProperty, $"{host} transform — {limit}");

		var matrix = QtHostVisualState.HostMatrix(element, toHost, element.Bounds.Width, element.Bounds.Height,
			QtHostUnits.ToQtUnits(1));
		var hadMatrix = host.AppliedProperties.ContainsKey("mauiMatrix");
		if (matrix is not null)
		{
			ApplyUpdates(host, new Dictionary<string, object?>
			{
				["rotation"] = 0.0,
				["scale"] = 1.0,
				["mauiMatrix"] = matrix,
			});
			return;
		}
		var updates = new Dictionary<string, object?>();
		if (hadMatrix)
			updates["mauiMatrix"] = Array.Empty<double>();   // identity
		if (toHost.IsTranslationOnly)
		{
			if (host.AppliedProperties.ContainsKey("rotation") || host.AppliedProperties.ContainsKey("scale"))
			{
				updates["rotation"] = 0.0;
				updates["scale"] = 1.0;
			}
		}
		else
		{
			updates["transformOrigin"] = "TopLeft";   // the pushed x/y is the transformed origin
			updates["rotation"] = toHost.RotationDegrees;
			updates["scale"] = toHost.UniformScale;
		}
		if (updates.Count > 0)
			ApplyUpdates(host, updates);
	}

	/// <summary>
	/// Records the element's absolute root-space rect (dp), converts it once to Qt units and queues it into the
	/// per-pass batch; unchanged geometry is dropped, so steady-state passes push nothing.
	/// </summary>
	private void SetGeometry(NativeElementHost host, Rect logicalDp, bool visible) =>
		SetGeometry(host, logicalDp, null, visible);

	/// <param name="localDp">Rect relative to the host's QML parent, pushed as-is. Null for collection rows:
	/// <paramref name="logicalDp"/> is then pushed as scene coordinates the shim maps through the delegate.</param>
	private void SetGeometry(NativeElementHost host, Rect logicalDp, Rect? localDp, bool visible)
	{
		host.MauiLogicalBounds = logicalDp;
		var qt = QtHostUnits.ToQtUnits(localDp ?? logicalDp);
		var geo = new NativeGeometry(qt.X, qt.Y, qt.Width, qt.Height);
		if (host.AppliedGeometrySet && host.AppliedGeometry == geo && host.AppliedVisible == visible)
			return;
		_geometryBatch.Add((host, geo, visible, localDp is not null));
	}

	/// <summary>
	/// Flushes queued geometry as one native call (sailfish_host_apply_geometry). Applied state is recorded only
	/// on full success, so partial failures retry next pass.
	/// </summary>
	private void FlushGeometry()
	{
		if (_geometryBatch.Count == 0)
			return;

		var sb = new StringBuilder(_geometryBatch.Count * 96 + 2);
		sb.Append('[');
		for (var i = 0; i < _geometryBatch.Count; i++)
		{
			var (host, geo, visible, local) = _geometryBatch[i];
			if (i > 0)
				sb.Append(',');
			sb.Append("{\"handle\":\"").Append(host.NativeHandle.ToString(CultureInfo.InvariantCulture))
			  .Append("\",\"x\":").Append(BridgeValue.Number(geo.X))
			  .Append(",\"y\":").Append(BridgeValue.Number(geo.Y))
			  .Append(",\"w\":").Append(BridgeValue.Number(geo.Width))
			  .Append(",\"h\":").Append(BridgeValue.Number(geo.Height))
			  .Append(",\"vis\":").Append(visible ? '1' : '0');
			if (local)
				sb.Append(",\"local\":1");   // parent-relative, applied as-is
			sb.Append('}');
			if (GeometryTrace)
			{
				var dp = host.MauiLogicalBounds;
				QtHostDiag.Trace(QtHostDiagChannel.Geometry, $"SET {host} dp=({dp.X:F1},{dp.Y:F1} {dp.Width:F1}x{dp.Height:F1}) " +
					$"-> qt=({geo.X:F1},{geo.Y:F1} {geo.Width:F1}x{geo.Height:F1}) vis={visible}");
			}
		}
		sb.Append(']');

		var count = _geometryBatch.Count;
		var failed = QtHostRuntime.ApplyGeometry(sb.ToString());
		if (failed < 0)
		{
			GeometryFailed += count;
			if (_bridgeFailLogged.Add($"geometry:{failed}"))
				QtHostDiag.Error(QtHostDiagChannel.Geometry, $"apply_geometry rc={failed} ({count} entries): {QtHostRuntime.LastErrorText}");
		}
		else
		{
			GeometryApplied += count - failed;
			if (failed > 0)
			{
				GeometryFailed += failed;
				if (_bridgeFailLogged.Add($"geometry-partial:{failed}"))
					QtHostDiag.Warn(QtHostDiagChannel.Geometry, $"apply_geometry partial: {failed}/{count} not applied: {QtHostRuntime.LastErrorText}");
				// Partial failures are almost always dead handles (QML object died before the first window report):
				// detach them so the next reconcile recreates them.
				foreach (var (host, _, _, _) in _geometryBatch)
					HealIfDead(host);   // each heal asks for the reconcile that recreates it
			}
			else
			{
				foreach (var (host, geo, visible, _) in _geometryBatch)
				{
					host.AppliedGeometry = geo;
					host.AppliedVisible = visible;
					host.AppliedGeometrySet = true;
				}
			}
		}
		if (GeometryTrace)
			QtHostDiag.Trace(QtHostDiagChannel.Geometry, $"flush entries={count} failed={failed}");
		_geometryBatch.Clear();
	}

	// The page flickable ("scroller", below the host canvas) scrolls no content. It turns interactive only for
	// the overscroll that drives Silica pulleys (which need an interactive flickable) or a page-armed refresh;
	// contentH stays the viewport height so the canvas never drifts.

	/// <summary>Pushes the page flickable state to the QML page (diffed).</summary>
	private void PushScrollState()
	{
		_primaryScroll = _primaryScrollWalk;
		var enabled = PageHasPulley || _pageRefresh.View is not null;
		var contentHScene = QtHostUnits.ToQtUnits(_windowDp.Height);
		var json = "{\"enabled\":" + (enabled ? "true" : "false") +
		           ",\"contentH\":" + BridgeValue.Number(contentHScene) +
		           ",\"scrollY\":0}";
		// Diag: MAUI_SAILFISH_OPEN_PULLEY="<pageSeq>:<pull|push>" opens the pulley after the Nth page render for
		// screenshots. Silica's PullDownMenu has no activate(): the flickable is parked at the menu's _finalPosition
		// and mauiHoldScrollY keeps the ScrollY push from snapping it shut.
		var openSpec = SailfishEnv.Get("MAUI_SAILFISH_OPEN_PULLEY");
		if (!_pulleyOpened && !string.IsNullOrEmpty(openSpec))
		{
			var parts = openSpec.Split(':');
			if (parts.Length == 2 && int.TryParse(parts[0], out var openSeq) && _renderedPageSeq >= openSeq)
			{
				_pulleyOpened = true;
				var which = parts[1] == "push" ? "pushUpMenu" : "pullDownMenu";
				// The shim eval scope has no setTimeout; the QML helper defers with Qt.callLater.
				var openResult = QtHostRuntime.Eval(
					$"(function(){{var p={TopModelPageJs};if(!p)return 'nopage';if(!p.mauiOpenPulley)return 'nofn';p.mauiOpenPulley('{which}');return 'ok';}})()");
				QtHostDiag.Trace(QtHostDiagChannel.Input, $"OPEN_PULLEY eval seq={_renderedPageSeq} spec={openSpec} -> {openResult}");
			}
		}
		// Addressed by the top model-page id like the ops batch: window.mauiModelPage can still name the outgoing
		// page right after a root swap, and the diff would then never deliver "enabled" to the new one (its pulley
		// stayed on an unfilled list and the first pull did nothing). The id joins the diff basis for the same reason.
		var key = (NativeTopPageId ?? string.Empty) + "|" + json;
		if (key == _lastScrollPush)
			return;
		_lastScrollPush = key;
		CallPage(null, "setMauiScroll", json);
		if (GeometryTrace)
			QtHostDiag.Trace(QtHostDiagChannel.Geometry, $"page flickable push {json}");
	}

	/// <summary>
	/// Swaps the page-armed RefreshView; the gesture and spinner live on the scroll surface, so this only
	/// manages the subscription and the diffed setMauiRefresh push.
	/// </summary>
	private void ArmRefresh(RefreshView? refresh)
	{
		if (!_pageRefresh.Arm(refresh, _ => PushRefreshState(), () => _suppressPush != 0))
			return;
		_lastRefreshPush = string.Empty;   // the new page instance needs a fresh push
		PushRefreshState();   // arm/disarm must reach the page without a layout pass
	}

	/// <summary>Keeps the subscription of the RefreshView armed on a scroll-view host; the id and initial state
	/// ride the host props, later changes push straight to the adapter.</summary>
	private void ArmScrollRefresh(RefreshView? refresh, NativeElementHost? host)
	{
		_scrollRefreshHost = host;
		_scrollRefresh.Arm(refresh, r =>
		{
			if (_scrollRefreshHost is { } h)
				ApplyUpdates(h, RefreshSurfaceProps(r));
		}, () => _suppressPush != 0);
	}

	/// <summary>The page-side refresh state (armed id + spinner), diffed.</summary>
	private void PushRefreshState()
	{
		var refreshing = _pageRefresh.View?.IsRefreshing == true;
		var armed = _pageRefresh.View is { IsRefreshEnabled: true };
		var color = _pageRefresh.View?.RefreshColor is { } tint ? BridgeValue.ColorString(tint) : string.Empty;
		var json = "{\"id\":\"" + (armed ? RefreshId : string.Empty) +
		           "\",\"refreshing\":" + (refreshing ? "true" : "false") +
		           ",\"color\":\"" + color + "\"}";
		var key = (NativeTopPageId ?? string.Empty) + "|" + json;
		if (key == _lastRefreshPush)
			return;
		_lastRefreshPush = key;
		CallPage(null, "setMauiRefresh", json);   // see PushScrollState
	}

	/// <summary>The nearest RefreshView above a scroll surface, but only when the page has no pulley (Silica's
	/// pull-down menu owns the same overscroll). Mirrors the collection bridge's rule.</summary>
	internal RefreshView? RefreshAncestorOf(Element view)
	{
		RefreshView? refresh = null;
		for (var e = view.Parent; e is not null; e = e.Parent)
		{
			refresh ??= e as RefreshView;
			if (e is Page page)
				return refresh is not null && ToolbarItemsOf(page).Count == 0 && !HasFlyoutPulley(page) ? refresh : null;
		}
		return null;
	}
}
