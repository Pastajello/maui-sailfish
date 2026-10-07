using System.Globalization;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>
/// Routes Qt pointer events (observed by the shim after Qt/Silica got them) to MAUI gesture recognizers.
/// Over a native adapter QML consumes the input and the router never synthesizes a second activation;
/// over an element with Tap/Pan/Swipe recognizers it captures the whole sequence; anything else stays
/// with Silica's page gestures. Touch wins over Qt's synthesized mouse events; the first touch point drives
/// taps, pans and swipes, the second one a pinch; wheel is not routed (Silica's flickables take it natively), and
/// gestures are dispatched to the MAUI main thread.
/// </summary>
internal sealed class QtHostInputRouter
{
	/* Pointer kinds exactly as the shim emits them (sailfish_host.h). */
	private const int MousePress = 0;
	private const int MouseRelease = 1;
	private const int MouseMove = 2;
	private const int Wheel = 3;
	private const int TouchBegin = 4;
	private const int TouchUpdate = 5;
	private const int TouchEnd = 6;
	private const int SecondPoint = 7;   // the second finger, right after its touch event (extra = fingers down)

	/// <summary>Adapters that consume pointer input natively; their semantic events arrive over the bridge.</summary>
	private static readonly HashSet<string> QmlConsumedUris = new(StringComparer.Ordinal)
	{
		"button", "entry", "editor", "switch", "slider",
		"check-box", "stepper", "indicator-view", "swipe-view", "web-view",
		"alert-dialog", "prompt-dialog", "action-sheet", "scroll-view",
		"list-view", "carousel-view",   // the ListView owns flick/tap; row taps arrive as "list-item-tapped"
	};

	/// <summary>QML-consumed controls whose own Tap/LongPress recognizers still fire (MAUI attaches them to the control's
	/// view on the platforms). Lists, carousels and swipe views have their own item paths; dialogs and the web view none.</summary>
	private static readonly HashSet<string> OwnGestureUris = new(StringComparer.Ordinal)
	{
		"button", "entry", "editor", "switch", "slider", "check-box", "stepper", "scroll-view",
	};

	/// <summary>Press→release stays a tap while the travel is under this (dp).</summary>
	private const double TapSlopDp = 10.0;

	/// <summary>Dominant-axis travel (dp) required to fire a swipe on release.</summary>
	private const double SwipeMinDp = 30.0;

	/// <summary>Horizontal travel (dp) that turns a page swipe into a tab switch, and the screen-edge band
	/// (dp) left to Sailfish system gestures.</summary>
	private const double TabSwipeMinDp = 80.0;
	private const double TabSwipeEdgeDp = 40.0;

	/// <summary>Mouse events this soon after touch are Qt-synthesized companions and are suppressed.</summary>
	private const long MouseSuppressionAfterTouchMs = 250;

	/// <summary>Press-and-hold time after which the pressed element's context menu opens.</summary>
	private const long HoldFireMs = 600;
	private const long HoldClockSlackMs = 16;

	/// <summary>The attached router; the renderer's tick drives <see cref="PollHold"/>.</summary>
	public static QtHostInputRouter? Active { get; private set; }

	private readonly QtHostPageRenderer _renderer;
	private readonly IDispatcher _dispatcher;
	private readonly bool _trace;

	/* --- Qt-thread state --- */
	private bool _touchActive;        // TouchBegin..TouchEnd in flight
	private long _lastTouchMs;        // Environment.TickCount64 of the last touch event

	/* Pointer capture (MAUI-consumed sequences only) */
	private View? _captured;                   // recognizer owner
	private List<TapGestureRecognizer>? _taps;
	// Multi-tap (NumberOfTapsRequired > 1): taps on the same owner within MultiTapMs and MultiTapSlopDp count up, as
	// Android's GestureDetector and iOS's tap recognizers do.
	private const long MultiTapMs = 300;
	private const double MultiTapSlopDp = 40.0;
	private View? _tapCountOwner;
	private int _tapCount;
	private long _lastTapMs;
	private double _lastTapDpX, _lastTapDpY;
	private int _tapSeq;                       // a later tap cancels a pending lower-count dispatch
	private List<PanGestureRecognizer>? _pans;
	private List<SwipeGestureRecognizer>? _swipes;
	private List<LongPressGestureRecognizer>? _longPresses;
	private List<PointerGestureRecognizer>? _pointers;
	private List<PinchGestureRecognizer>? _pinches;
	private Rect _ownerDp;                     // the owner's root-space rect at the press (pinch scale origins)
	private bool _pinching;                    // two fingers down on a pinch owner
	private bool _pinched;                     // this sequence pinched: its release is no tap or pan
	private double _pinchDistance;             // finger distance at the last pinch update (dp)
	private bool _longPressFired;              // release after a fired long press = no tap
	private int _longPressSeq;                 // invalidates a pending long-press timer
	private bool _longPressPending;            // armed, neither fired nor canceled yet
	private double _longPressSlopDp;           // the recognizers' AllowableMovement: travel beyond it cancels
	private double _lastPressDpX, _lastPressDpY;   // every press, captured or not (a list row's capture starts there)
	private Action<bool>? _rowHold;            // a list row captured for a pan: keeps the ListView from stealing it
	private bool _rowGestureTook;              // a row long press fired or a row pan started: the row's tap is no tap
	private bool _fingerDown;                  // between a press and its release (a late row capture is dropped)
	private bool _rowCapture;                  // the captured sequence started on a list row (CaptureRow)
	private double _pressDpX, _pressDpY;
	private double _totalDpX, _totalDpY;
	private bool _dragging;
	private int _gestureId;                    // pan sequence id (IPanGestureController)

	/* Press-and-hold (context menu) */
	private string? _holdHostId;               // target host of the armed hold
	private MenuFlyout? _holdFlyout;           // flyout to open when it fires
	private long _holdStartMs;
	private double _holdPressDpX, _holdPressDpY;
	private bool _holdFired;                   // release after a fire = no tap
	private int _holdSeq;                      // the armed hold's sequence (its own fire timer checks it)

	// Page swipe between tabs, tracked independently of MAUI capture.
	private bool _tabSwipeArmed;
	private bool _tabDragging;                 // the page follows the finger
	private double _tabSwipeX, _tabSwipeY;

	/* --- Counters (diagnostics) --- */
	public long EventsSeen { get; private set; }
	public long TouchEvents { get; private set; }
	public long SyntheticMouseSuppressed { get; private set; }
	public long NativeConsumed { get; private set; }
	public long MauiCaptured { get; private set; }
	public long TapsFired { get; private set; }
	public long PanUpdatesFired { get; private set; }
	public long SwipesFired { get; private set; }
	public long Ignored { get; private set; }

	/// <summary>Press-and-hold sequences that opened a context menu.</summary>
	public long LongPressFired { get; private set; }

	/// <summary>LongPressGestureRecognizer presses that reached MinimumPressDuration.</summary>
	public long LongPressGesturesFired { get; private set; }

	/// <summary>PinchGestureRecognizer updates sent (started/running/completed).</summary>
	public long PinchUpdatesFired { get; private set; }

	/// <summary>PointerGestureRecognizer events sent (entered/pressed/moved/released/exited).</summary>
	public long PointerEventsFired { get; private set; }

	/// <param name="renderer">Renderer whose absolutized rects are hit-tested.</param>
	/// <param name="dispatcher">MAUI main-thread dispatcher (gesture delivery).</param>
	/// <param name="trace">MAUI_SAILFISH_QT_HOST_INPUT_TRACE=1 logs every routing decision.</param>
	public QtHostInputRouter(QtHostPageRenderer renderer, IDispatcher dispatcher, bool trace = false)
	{
		_renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
		_dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
		_trace = trace;
	}

	/// <summary>Starts observing real Qt pointer events (Qt thread callbacks).</summary>
	public void Attach()
	{
		QtHostRuntime.PointerInput += OnPointer;
		Active = this;
	}

	/// <summary>Stops observing; call before host shutdown.</summary>
	public void Detach()
	{
		QtHostRuntime.PointerInput -= OnPointer;
		if (ReferenceEquals(Active, this))
			Active = null;
	}

	/// <summary>Fires the armed press-and-hold at the threshold. A still finger produces no pointer events, so the
	/// hold arms its own timer (as a native long-press does); the renderer's tick only backs it up.</summary>
	public void PollHold()
	{
		if (_holdFlyout is null || _holdFired)
			return;
		// A timer may run a tick early on a coarse clock.
		if (Environment.TickCount64 - _holdStartMs < HoldFireMs - HoldClockSlackMs)
			return;
		_holdFired = true;
		LongPressFired++;
		var flyout = _holdFlyout;
		var hostId = _holdHostId!;
		_holdFlyout = null;
		_renderer.FireContextMenu(hostId, flyout);
	}

	/// <summary>One-line counter summary for diagnostics/acceptance logs.</summary>
	public string CounterSummary() =>
		$"events={EventsSeen} touch={TouchEvents} synthetic-mouse-suppressed={SyntheticMouseSuppressed} " +
		$"qml-consumed={NativeConsumed} maui-captured={MauiCaptured} taps={TapsFired} pans={PanUpdatesFired} " +
		$"swipes={SwipesFired} ignored={Ignored}";

	/* ------------------------------------------------------------------ */

	internal void OnPointer(int kind, double x, double y, double delta, int extra)
	{
		EventsSeen++;

		// Touch is authoritative during a sequence and a short cooldown (the synthesized release can trail TouchEnd).
		var now = Environment.TickCount64;
		if (kind is TouchBegin or TouchUpdate or TouchEnd)
		{
			TouchEvents++;
			_touchActive = kind != TouchEnd;
			_lastTouchMs = now;
		}
		else if (kind is MousePress or MouseRelease or MouseMove &&
		         (_touchActive || now - _lastTouchMs < MouseSuppressionAfterTouchMs))
		{
			SyntheticMouseSuppressed++;
			if (_trace)
				Trace(kind, x, y, "suppressed (synthetic mouse companion of touch)");
			return;
		}

		switch (kind)
		{
			case MousePress:
			case TouchBegin:
				OnPress(kind, x, y);
				break;
			case MouseMove:
			case TouchUpdate:
				OnMove(kind, x, y);
				break;
			case MouseRelease:
			case TouchEnd:
				OnRelease(kind, x, y);
				break;
			case SecondPoint:
				OnSecondPoint(x, y, extra);
				break;
			case Wheel:
				// Observed only; wheel is not routed yet.
				if (_trace)
					Trace(kind, x, y, $"observed (wheel delta={delta.ToString(CultureInfo.InvariantCulture)}) — not routed yet");
				break;
		}
	}

	private void OnPress(int kind, double x, double y)
	{
		// Window px → root-space dp, same conversion as geometry.
		var dpX = QtHostUnits.ToLogical(x);
		var dpY = QtHostUnits.ToLogical(y);

		_lastPressDpX = dpX;
		_lastPressDpY = dpY;
		_rowGestureTook = false;
		_fingerDown = true;
		var hit = _renderer.TryHitTest(dpX, dpY, out var host) && host is not null;
		ArmTabSwipe(dpX, dpY, hit ? host : null);
		HideSoftInputOnTap(hit ? host : null);

		if (!hit || host is null)
		{
			Ignored++;
			if (_trace)
				Trace(kind, x, y, $"no host at ({dpX.ToString("F1", CultureInfo.InvariantCulture)},{dpY.ToString("F1", CultureInfo.InvariantCulture)})dp — Silica page keeps the event");
			return;
		}

		// A ContextFlyout on the element or an ancestor arms the hold timer; the press still routes normally.
		_holdFlyout = null;
		_holdFired = false;
		_holdHostId = null;
		if (host.Element is VisualElement holdVe &&
		    _renderer.TryContextFlyout(holdVe, out _, out var holdFlyout))
		{
			_holdHostId = host.Id;
			_holdFlyout = holdFlyout;
			_holdStartMs = Environment.TickCount64;
			_holdPressDpX = dpX;
			_holdPressDpY = dpY;
			var seq = ++_holdSeq;
			_dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(HoldFireMs), () =>
			{
				if (seq == _holdSeq)
					PollHold();
			});
			if (_trace)
				Trace(kind, x, y, $"{host} — context-flyout hold armed (fires at {HoldFireMs}ms)");
		}

		if (QmlConsumedUris.Contains(host.QmlUri))
		{
			// QML consumes: the semantic event arrives over the bridge, so never synthesize a second activation.
			NativeConsumed++;
			// The control's own Tap/LongPress recognizers still fire beside its native action, as MAUI's Android and
			// iOS managers attach them to the control's own view. An ancestor's do not: the control took the touch
			// (Android never passes it up; iOS lets the UIControl win), so a tappable row around a CheckBox does not
			// toggle twice.
			if (OwnGestureUris.Contains(host.QmlUri) && host.Element is View control &&
			    QtHostVisualState.EffectiveEnabled(control) &&
			    TryFindRecognizers(control, out var ownOwner, out var ownTaps, out _, out _, out var ownLongPresses, out _, out _,
				    stopAt: control) &&
			    (ownTaps is not null || ownLongPresses is not null))
			{
				_taps = ownTaps;
				_pans = null;
				_swipes = null;
				_longPresses = ownLongPresses;
				_pointers = null;
				_pinches = null;
				OwnGestureCaptures++;
				BeginCapture(ownOwner, dpX, dpY, host.MauiLogicalBounds);
				if (_trace)
					Trace(kind, x, y, $"{host} — QML consumes; its own tap={ownTaps is not null} longPress={ownLongPresses is not null} recognizers follow the sequence");
				return;
			}
			if (_trace)
				Trace(kind, x, y, $"{host} — QML consumes (semantic event via the bridge)");
			return;
		}

		// A disabled ancestor disables the subtree too.
		if (host.Element is not VisualElement ve || ve.InputTransparent || !QtHostVisualState.EffectiveEnabled(ve))
		{
			Ignored++;
			if (_trace)
				Trace(kind, x, y, $"{host} — not interactive (input-transparent/disabled)");
			return;
		}

		// MAUI gestures bubble: the nearest element carrying recognizers owns the sequence.
		if (!TryFindRecognizers(ve, out var owner, out _taps, out _pans, out _swipes, out _longPresses, out _pointers, out _pinches))
		{
			Ignored++;
			if (_trace)
				Trace(kind, x, y, $"{host} — no MAUI gesture recognizers (ignored)");
			return;
		}

		// MAUI consumes: capture survives the finger leaving the bounds.
		if (_trace)
			Trace(kind, x, y, $"{host} → gesture target {owner.GetType().Name} CAPTURED " +
				$"(tap={_taps is not null} pan={_pans?.Count ?? 0} swipe={_swipes?.Count ?? 0} " +
				$"longPress={_longPresses?.Count ?? 0} pointer={_pointers?.Count ?? 0} pinch={_pinches?.Count ?? 0})");
		BeginCapture(owner, dpX, dpY, OwnerRect(ve, owner, host.MauiLogicalBounds));
	}

	/// <summary>Starts a captured sequence on <paramref name="owner"/> with the recognizer lists already set.</summary>
	private void BeginCapture(View owner, double dpX, double dpY, Rect ownerDp)
	{
		_captured = owner;
		_pressDpX = dpX;
		_pressDpY = dpY;
		_totalDpX = 0;
		_totalDpY = 0;
		_dragging = false;
		_pinching = false;
		_pinched = false;
		_ownerDp = ownerDp;
		_gestureId++;
		MauiCaptured++;
		_longPressFired = false;
		DispatchPointer(PointerPhase.Entered);
		DispatchPointer(PointerPhase.Pressed);
		ArmLongPress();
	}

	/// <summary>
	/// A press on a CollectionView row whose template carries Pan/Swipe/Pointer/LongPress/Pinch recognizers (the
	/// ListView consumed the press, the delegate reported it as "list-item-pressed"): the nearest owner between
	/// <paramref name="hit"/> and <paramref name="cellRoot"/> takes the sequence as anywhere else. Taps stay with the
	/// row's own path (list-item-tapped). <paramref name="hold"/> keeps the ListView from stealing a captured pan.
	/// </summary>
	internal bool CaptureRow(View hit, View cellRoot, Action<bool> hold)
	{
		// The delegate's press report can arrive after the release (events queued behind a fast tap): nothing to capture.
		if (!_fingerDown || _captured is not null || !TryFindRecognizers(hit, out var owner, out _, out var pans, out var swipes,
			    out var longPresses, out var pointers, out var pinches, stopAt: cellRoot, skipTaps: true))
			return false;
		_taps = null;
		_pans = pans;
		_swipes = swipes;
		_longPresses = longPresses;
		_pointers = pointers;
		_pinches = pinches;
		RowCaptures++;
		_rowCapture = true;
		BeginCapture(owner, _lastPressDpX, _lastPressDpY, new Rect(_lastPressDpX, _lastPressDpY, owner.Width, owner.Height));
		if (pans is not null || swipes is not null || pinches is not null)
		{
			_rowHold = hold;
			hold(true);
		}
		if (_trace)
			QtHostDiag.Trace(QtHostDiagChannel.Input, $"list row → gesture target {owner.GetType().Name} CAPTURED " +
				$"(pan={pans?.Count ?? 0} swipe={swipes?.Count ?? 0} longPress={longPresses?.Count ?? 0} pointer={pointers?.Count ?? 0})");
		return true;
	}

	/// <summary>Whether a row long press fired or a row pan started since the last press (once): the row's tap that the
	/// ListView reports on release is then no tap and no selection, as after a long press or a drag anywhere else.</summary>
	internal bool TakeRowGesture()
	{
		var took = _rowGestureTook;
		_rowGestureTook = false;
		return took;
	}

	/// <summary>Arms a tab swipe on a tabbed page unless the press starts in the edge band, on a control
	/// that consumes input, or on an element with its own pan/swipe recognizers.</summary>
	private void ArmTabSwipe(double dpX, double dpY, NativeElementHost? host)
	{
		_tabSwipeArmed = false;
		_tabDragging = false;
		if (!_renderer.HasTabBar)
			return;
		var width = _renderer.WindowWidthDp;
		if (dpX < TabSwipeEdgeDp || dpX > width - TabSwipeEdgeDp)
			return;
		// A tab row with more tabs than fit scrolls sideways under the finger.
		if (_renderer.TabRowScrolls && dpY < _renderer.ChromeBottomDp)
			return;
		if (host is not null)
		{
			if (QmlConsumedUris.Contains(host.QmlUri) || host.QmlUri == QtHostAdapters.ScrollView)
				return;
			if (host.Element is VisualElement target && TryFindRecognizers(target, out _, out _, out var pans, out var swipes, out _, out _, out var pinches)
			    && (pans is not null || swipes is not null || pinches is not null))
				return;
			// A press inside content that scrolls sideways: the drag is that content's (WeatherTwentyOne's hourly row, a
			// horizontal ScrollView, switched the tab instead of scrolling).
			if (host.Element is Element hit && ScrollsSideways(hit))
				return;
		}
		_tabSwipeArmed = true;
		_tabSwipeX = dpX;
		_tabSwipeY = dpY;
	}

	/// <summary>Whether <paramref name="element"/> or an ancestor scrolls horizontally (a horizontal ScrollView, a horizontal
	/// list or carousel, a swipe row): its horizontal drags are its own, not a tab swipe.</summary>
	internal static bool ScrollsSideways(Element element)
	{
		for (var e = element; e is not null and not Page; e = e.Parent)
		{
			switch (e)
			{
				case ScrollView { Orientation: ScrollOrientation.Horizontal or ScrollOrientation.Both }:
				case CarouselView:
				case SwipeView:
				case ItemsView { } items when IsHorizontal(items):
					return true;
			}
		}
		return false;

		static bool IsHorizontal(ItemsView items) => items switch
		{
			StructuredItemsView { ItemsLayout: LinearItemsLayout { Orientation: ItemsLayoutOrientation.Horizontal } } => true,
			StructuredItemsView { ItemsLayout: GridItemsLayout { Orientation: ItemsLayoutOrientation.Horizontal } } => true,
			_ => false,
		};
	}

	/// <summary>Once the travel is clearly horizontal the page follows the finger; a vertical start leaves the drag to
	/// the page's scrolling.</summary>
	private void TrackTabDrag(double x, double y)
	{
		var dx = QtHostUnits.ToLogical(x) - _tabSwipeX;
		var dy = QtHostUnits.ToLogical(y) - _tabSwipeY;
		if (!_tabDragging)
		{
			if (Math.Abs(dy) > 2 * TapSlopDp && Math.Abs(dy) >= Math.Abs(dx))
			{
				_tabSwipeArmed = false;   // a scroll
				return;
			}
			if (Math.Abs(dx) <= 2 * TapSlopDp || Math.Abs(dx) < 2 * Math.Abs(dy))
				return;
			_tabDragging = true;
		}
		_renderer.SetTabDrag(dx);
	}

	/// <summary>A dominant horizontal travel past the threshold switches tab (left swipe = next); a followed drag that
	/// falls short springs back.</summary>
	private void FinishTabSwipe(double x, double y)
	{
		if (!_tabSwipeArmed)
			return;
		_tabSwipeArmed = false;
		var dragging = _tabDragging;
		_tabDragging = false;
		var dx = QtHostUnits.ToLogical(x) - _tabSwipeX;
		var dy = QtHostUnits.ToLogical(y) - _tabSwipeY;
		var commit = Math.Abs(dx) >= TabSwipeMinDp && Math.Abs(dx) >= 2 * Math.Abs(dy);
		var delta = dx < 0 ? 1 : -1;
		if (commit)
			TabSwipes++;
		if (dragging)
			_renderer.EndTabDrag(commit ? delta : 0);   // the tab switches once the page slid out
		else if (commit)
			_dispatcher.Dispatch(() => _renderer.SwipeTab(delta));
	}

	/// <summary>Page swipes that switched a tab.</summary>
	public long TabSwipes { get; private set; }

	/// <summary>Presses on a QML-consumed control whose own Tap/LongPress recognizers took the sequence too.</summary>
	public long OwnGestureCaptures { get; private set; }

	/// <summary>List-row presses whose template recognizers took the sequence (<see cref="CaptureRow"/>).</summary>
	public long RowCaptures { get; private set; }

	/// <summary>Pending long presses canceled (early release, movement, a pan or a second finger).</summary>
	public long LongPressCancels { get; private set; }

	/// <summary>Text inputs unfocused by a press elsewhere on a <c>HideSoftInputOnTapped</c> page.</summary>
	public long SoftInputHides { get; private set; }

	/// <summary>Adapters that take text: a press on one keeps the keyboard.</summary>
	private static readonly HashSet<string> TextInputUris = new(StringComparer.Ordinal) { "entry", "editor", "search-bar" };

	/// <summary><c>ContentPage.HideSoftInputOnTapped</c>: a press outside a text input unfocuses the focused one, which
	/// closes Maliit, as the keyboard hides on Android/iOS (MAUI's plain-net manager for it does nothing).</summary>
	private void HideSoftInputOnTap(NativeElementHost? host)
	{
		if (_renderer.CurrentPage is not ContentPage { HideSoftInputOnTapped: true })
			return;
		if (host is not null && TextInputUris.Contains(host.QmlUri))
			return;
		foreach (var candidate in _renderer.CurrentHosts)
		{
			if (candidate.Element is InputView { IsFocused: true } input)
			{
				input.Unfocus();
				SoftInputHides++;
				if (_trace)
					QtHostDiag.Trace(QtHostDiagChannel.Focus, $"HideSoftInputOnTapped: press outside text input → {input.GetType().Name} unfocused");
			}
		}
	}

	private void OnMove(int kind, double x, double y)
	{
		if (_tabSwipeArmed)
			TrackTabDrag(x, y);

		// Travel beyond the tap slop cancels the armed hold.
		if (_holdFlyout is not null && !_holdFired)
		{
			var hx = QtHostUnits.ToLogical(x) - _holdPressDpX;
			var hy = QtHostUnits.ToLogical(y) - _holdPressDpY;
			if (Math.Max(Math.Abs(hx), Math.Abs(hy)) > TapSlopDp)
				_holdFlyout = null;
		}

		if (_captured is null)
			return;   // hover or uncaptured move

		var dpX = QtHostUnits.ToLogical(x);
		var dpY = QtHostUnits.ToLogical(y);
		_totalDpX = dpX - _pressDpX;
		_totalDpY = dpY - _pressDpY;
		if (_pinched)
			return;   // two fingers: the pinch owns the sequence (updated from the second point)

		// LongPressGestureRecognizer.AllowableMovement: travel beyond it cancels the pending long press.
		if (_longPressPending && Math.Sqrt(_totalDpX * _totalDpX + _totalDpY * _totalDpY) > _longPressSlopDp)
			CancelLongPress();
		if (!_dragging && Math.Max(Math.Abs(_totalDpX), Math.Abs(_totalDpY)) > TapSlopDp)
		{
			_dragging = true;
			if (_pans is not null)
				CancelLongPress();   // the pan owns a moving finger
			if (_rowCapture && (_pans is not null || _swipes is not null))
				_rowGestureTook = true;
			DispatchPan(GestureStatus.Started);
		}
		if (_dragging)
			DispatchPan(GestureStatus.Running);
		DispatchPointer(PointerPhase.Moved);
	}

	private void OnRelease(int kind, double x, double y)
	{
		_fingerDown = false;
		// A release after a fired long-press is not a tap (as with Silica's ListItem).
		var holdFired = _holdFired;
		_holdFlyout = null;
		_holdFired = false;
		_holdHostId = null;
		FinishTabSwipe(x, y);

		if (_captured is null)
			return;   // the press was QML-consumed or ignored

		CancelLongPress();   // released before the duration: a pending long press is Canceled
		DispatchPointer(PointerPhase.Released);
		DispatchPointer(PointerPhase.Exited);
		if (_pinched)
		{
			if (_pinching)
				DispatchPinch(GestureStatus.Completed, 1, default);
			ClearCapture();
			if (_trace)
				Trace(kind, x, y, "release after a pinch — no tap or pan");
			return;
		}
		if (_longPressFired)
		{
			ClearCapture();
			if (_trace)
				Trace(kind, x, y, "release after a long-press gesture — tap suppressed");
			return;
		}

		if (holdFired)
		{
			ClearCapture();
			if (_trace)
				Trace(kind, x, y, "release after a fired long-press — tap suppressed");
			return;
		}

		if (_dragging)
		{
			// Completed uses the release position, not the last move, like the platform mappers.
			_totalDpX = QtHostUnits.ToLogical(x) - _pressDpX;
			_totalDpY = QtHostUnits.ToLogical(y) - _pressDpY;
			DispatchPan(GestureStatus.Completed);
			DispatchSwipe();
		}
		else
		{
			DispatchTap();
		}
		ClearCapture();
		if (_trace)
			Trace(kind, x, y, "sequence end — capture released");
	}

	/* --- Gesture dispatch (MAUI main thread) --- */

	private void DispatchTap()
	{
		var taps = _taps;
		var owner = _captured;
		if (taps is null || owner is null)
			return;
		var now = Environment.TickCount64;
		var near = Math.Abs(_pressDpX - _lastTapDpX) <= MultiTapSlopDp && Math.Abs(_pressDpY - _lastTapDpY) <= MultiTapSlopDp;
		_tapCount = ReferenceEquals(owner, _tapCountOwner) && now - _lastTapMs <= MultiTapMs && near ? _tapCount + 1 : 1;
		_tapCountOwner = owner;
		_lastTapMs = now;
		_lastTapDpX = _pressDpX;
		_lastTapDpY = _pressDpY;
		var seq = ++_tapSeq;
		var count = _tapCount;
		var most = 1;
		foreach (var t in taps)
			most = Math.Max(most, t.NumberOfTapsRequired);
		if (count >= most)
			_tapCountOwner = null;   // the longest sequence is complete: the next tap starts a new one
		var getPosition = PositionOf(new Point(_pressDpX, _pressDpY));
		void Fire()
		{
			foreach (var t in taps)
				// A touch is the primary button: a recognizer for the secondary button alone never fires on it.
				if (t.NumberOfTapsRequired == count && (t.Buttons & ButtonsMask.Primary) != 0)
				{
					TapsFired++;
					SendTapped(t, owner, getPosition);
				}
		}
		// A shorter count waits for the multi-tap window when a longer one is possible (a double-tap must not also
		// flag the tile it reveals, BugSweeper), as Android's single-tap-confirmed and iOS's require-to-fail do.
		if (count < most)
			_dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(MultiTapMs), () =>
			{
				if (seq == _tapSeq)
					Fire();
			});
		else
			_dispatcher.Dispatch(Fire);
	}

	/// <summary>Calls TapGestureRecognizer.SendTapped (public infrastructure in .NET 11 MAUI); handler
	/// failures are logged, never rethrown into the Qt loop.</summary>
	internal static void SendTapped(TapGestureRecognizer tap, View view, Point position) =>
		SendTapped(tap, view, _ => position);

	internal static void SendTapped(TapGestureRecognizer tap, View view, Func<IElement?, Point?> getPosition)
	{
		try
		{
			tap.SendTapped(view, getPosition);
		}
		catch (Exception ex)
		{
			QtHostDiag.Error(QtHostDiagChannel.Input, $"Tapped handler failed: {ex.Message}");
		}
	}

	private void DispatchPan(GestureStatus status)
	{
		var pans = _pans;
		var owner = _captured;
		if (pans is null || pans.Count == 0 || owner is null)
			return;
		var tx = _totalDpX;
		var ty = _totalDpY;
		var id = _gestureId;
		PanUpdatesFired++;
		_dispatcher.Dispatch(() =>
		{
			foreach (var pan in pans)
			{
				var controller = (IPanGestureController)pan;
				switch (status)
				{
					case GestureStatus.Started: controller.SendPanStarted(owner, id); break;
					case GestureStatus.Running: controller.SendPan(owner, tx, ty, id); break;
					case GestureStatus.Completed: controller.SendPanCompleted(owner, id); break;
					case GestureStatus.Canceled: controller.SendPanCanceled(owner, id); break;
				}
			}
		});
	}

	private void DispatchSwipe()
	{
		var swipes = _swipes;
		var owner = _captured;
		if (swipes is null || swipes.Count == 0 || owner is not View view)
			return;
		if (Math.Max(Math.Abs(_totalDpX), Math.Abs(_totalDpY)) < SwipeMinDp)
			return;
		var horizontal = Math.Abs(_totalDpX) >= Math.Abs(_totalDpY);
		var direction = horizontal
			? (_totalDpX > 0 ? SwipeDirection.Right : SwipeDirection.Left)
			: (_totalDpY > 0 ? SwipeDirection.Down : SwipeDirection.Up);
		var distance = horizontal ? Math.Abs(_totalDpX) : Math.Abs(_totalDpY);
		foreach (var swipe in swipes)
		{
			// Direction is a flags enum (Left | Right takes either) and Threshold the distance MAUI's own detection
			// requires (default 100); an exact match and a fixed 30 dp missed both (tracker S16).
			if ((swipe.Direction & direction) == 0 || distance < Math.Max(swipe.Threshold, SwipeMinDp))
				continue;
			SwipesFired++;
			_dispatcher.Dispatch(() => swipe.SendSwiped(view, direction));
		}
	}

	/// <summary>
	/// The second finger. With two down on an owner that has a PinchGestureRecognizer the pinch starts (cancelling a
	/// pan in progress, as Android's ScaleGestureDetector takes over), each update reports the change of the finger
	/// distance since the last one and the midpoint relative to the owner, and lifting a finger completes it.
	/// </summary>
	private void OnSecondPoint(double x, double y, int fingersDown)
	{
		if (_captured is null || _pinches is null)
			return;
		var p1X = _pressDpX + _totalDpX;
		var p1Y = _pressDpY + _totalDpY;
		var p2X = QtHostUnits.ToLogical(x);
		var p2Y = QtHostUnits.ToLogical(y);
		var distance = Math.Sqrt((p2X - p1X) * (p2X - p1X) + (p2Y - p1Y) * (p2Y - p1Y));
		var origin = new Point(
			_ownerDp.Width > 0 ? ((p1X + p2X) / 2 - _ownerDp.X) / _ownerDp.Width : 0.5,
			_ownerDp.Height > 0 ? ((p1Y + p2Y) / 2 - _ownerDp.Y) / _ownerDp.Height : 0.5);
		if (fingersDown >= 2 && !_pinching)
		{
			if (distance < 1)
				return;
			if (_dragging)
				DispatchPan(GestureStatus.Canceled);
			_dragging = false;
			CancelLongPress();
			_holdFlyout = null;
			_pinching = true;
			_pinched = true;
			_pinchDistance = distance;
			DispatchPinch(GestureStatus.Started, 1, origin);
		}
		else if (fingersDown >= 2 && _pinching)
		{
			if (distance < 1 || Math.Abs(distance - _pinchDistance) < 0.01)
				return;
			var scale = distance / _pinchDistance;
			_pinchDistance = distance;
			DispatchPinch(GestureStatus.Running, scale, origin);
		}
		else if (_pinching)
		{
			_pinching = false;
			DispatchPinch(GestureStatus.Completed, 1, origin);
		}
	}

	private void DispatchPinch(GestureStatus status, double scale, Point origin)
	{
		var pinches = _pinches;
		var owner = _captured;
		if (pinches is null || owner is null)
			return;
		PinchUpdatesFired++;
		_dispatcher.Dispatch(() =>
		{
			foreach (var pinch in pinches)
			{
				try
				{
					var controller = (IPinchGestureController)pinch;
					switch (status)
					{
						case GestureStatus.Started: controller.SendPinchStarted(owner, origin); break;
						case GestureStatus.Running: controller.SendPinch(owner, scale, origin); break;
						case GestureStatus.Completed: controller.SendPinchEnded(owner); break;
						case GestureStatus.Canceled: controller.SendPinchCanceled(owner); break;
					}
				}
				catch (Exception ex)
				{
					QtHostDiag.Error(QtHostDiagChannel.Input, $"pinch {status} handler failed: {ex.Message}");
				}
			}
		});
	}

	/// <summary>The recognizer owner's root-space rect: the hit host's rect moved by the hit element's offset inside the
	/// owner (layout offsets; transforms in between are ignored).</summary>
	private static Rect OwnerRect(VisualElement hit, View owner, Rect hitDp)
	{
		double dx = 0, dy = 0;
		for (var v = hit; v is not null && !ReferenceEquals(v, owner); v = v.Parent as VisualElement)
		{
			dx += v.Bounds.X;
			dy += v.Bounds.Y;
		}
		return new Rect(hitDp.X - dx, hitDp.Y - dy, owner.Bounds.Width, owner.Bounds.Height);
	}

	private void ClearCapture()
	{
		_captured = null;
		_taps = null;
		_pans = null;
		_swipes = null;
		_longPresses = null;
		_pointers = null;
		_pinches = null;
		_pinching = false;
		_pinched = false;
		_longPressFired = false;
		_longPressPending = false;
		_rowCapture = false;
		_dragging = false;
		_totalDpX = 0;
		_totalDpY = 0;
		if (_rowHold is { } hold)
		{
			_rowHold = null;
			hold(false);
		}
	}

	/// <summary>Finds the nearest View (element or ancestor) carrying Tap/Pan/Swipe/LongPress/Pointer/Pinch recognizers.</summary>
	private static bool TryFindRecognizers(VisualElement element, out View owner,
	                                       out List<TapGestureRecognizer>? taps,
	                                       out List<PanGestureRecognizer>? pans,
	                                       out List<SwipeGestureRecognizer>? swipes,
	                                       out List<LongPressGestureRecognizer>? longPresses,
	                                       out List<PointerGestureRecognizer>? pointers,
	                                       out List<PinchGestureRecognizer>? pinches,
	                                       View? stopAt = null, bool skipTaps = false)
	{
		for (var v = element as View; v is not null; v = ReferenceEquals(v, stopAt) ? null : v.Parent as View)
		{
			taps = null;
			pans = null;
			swipes = null;
			longPresses = null;
			pointers = null;
			pinches = null;
			foreach (var recognizer in v.GestureRecognizers)
			{
				switch (recognizer)
				{
					case TapGestureRecognizer t when !skipTaps: (taps ??= new List<TapGestureRecognizer>()).Add(t); break;
					case PanGestureRecognizer p: (pans ??= new List<PanGestureRecognizer>()).Add(p); break;
					case SwipeGestureRecognizer s: (swipes ??= new List<SwipeGestureRecognizer>()).Add(s); break;
					case LongPressGestureRecognizer l: (longPresses ??= new List<LongPressGestureRecognizer>()).Add(l); break;
					case PointerGestureRecognizer p: (pointers ??= new List<PointerGestureRecognizer>()).Add(p); break;
					case PinchGestureRecognizer p: (pinches ??= new List<PinchGestureRecognizer>()).Add(p); break;
				}
			}
			if (taps is not null || pans is not null || swipes is not null || longPresses is not null || pointers is not null ||
			    pinches is not null)
			{
				owner = v;
				return true;
			}
		}
		owner = null!;
		taps = null;
		pans = null;
		swipes = null;
		longPresses = null;
		pointers = null;
		pinches = null;
		return false;
	}

	private enum PointerPhase { Entered, Pressed, Moved, Released, Exited }

	/// <summary>The getPosition a gesture's event args answer <c>GetPosition(relativeTo)</c> with (tracker S16: it used to
	/// return the root point whatever was asked).</summary>
	private Func<IElement?, Point?> PositionOf(Point root) => relativeTo => _renderer.RelativePosition(root, relativeTo);

	/// <summary>PointerGestureRecognizer: a touch is entered+pressed, moved and released+exited (no hover on a
	/// touch screen).</summary>
	private void DispatchPointer(PointerPhase phase)
	{
		var pointers = _pointers;
		var owner = _captured;
		if (pointers is null || owner is null)
			return;
		var getPosition = PositionOf(new Point(_pressDpX + _totalDpX, _pressDpY + _totalDpY));
		PointerEventsFired += pointers.Count;
		_dispatcher.Dispatch(() =>
		{
			foreach (var pointer in pointers)
			{
				if ((pointer.Buttons & ButtonsMask.Primary) == 0)
					continue;   // a touch is the primary button
				try
				{
					switch (phase)
					{
						case PointerPhase.Entered: pointer.SendPointerEntered(owner, getPosition); break;
						case PointerPhase.Pressed: pointer.SendPointerPressed(owner, getPosition); break;
						case PointerPhase.Moved: pointer.SendPointerMoved(owner, getPosition); break;
						case PointerPhase.Released: pointer.SendPointerReleased(owner, getPosition); break;
						case PointerPhase.Exited: pointer.SendPointerExited(owner, getPosition); break;
					}
				}
				catch (Exception ex)
				{
					QtHostDiag.Error(QtHostDiagChannel.Input, $"pointer {phase} handler failed: {ex.Message}");
				}
			}
		});
	}

	/// <summary>A pending long press that will not fire (early release, travel beyond AllowableMovement, a pan taking
	/// the finger, a second finger): its recognizers get LongPressing Canceled, as MAUI's Android manager reports.</summary>
	private void CancelLongPress()
	{
		_longPressSeq++;
		if (!_longPressPending)
			return;
		_longPressPending = false;
		LongPressCancels++;
		var longPresses = _longPresses;
		var owner = _captured;
		if (longPresses is null || owner is null)
			return;
		var getPosition = PositionOf(new Point(_pressDpX + _totalDpX, _pressDpY + _totalDpY));
		_dispatcher.Dispatch(() =>
		{
			foreach (var longPress in longPresses)
			{
				try
				{
					longPress.SendLongPressing(owner, GestureStatus.Canceled, getPosition);
				}
				catch (Exception ex)
				{
					QtHostDiag.Error(QtHostDiagChannel.Input, $"LongPressing handler failed: {ex.Message}");
				}
			}
		});
	}

	/// <summary>Arms LongPressGestureRecognizer at its MinimumPressDuration: Started, then LongPressed and Completed
	/// (MAUI's Android order); a release or travel beyond AllowableMovement first cancels it (Canceled).</summary>
	private void ArmLongPress()
	{
		var longPresses = _longPresses;
		var owner = _captured;
		if (longPresses is null || owner is null)
			return;
		var seq = ++_longPressSeq;
		var duration = Math.Max(1, longPresses.Min(l => l.MinimumPressDuration));
		_longPressPending = true;
		_longPressSlopDp = Math.Max(0, longPresses.Min(l => l.AllowableMovement));
		_dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(duration), () =>
		{
			if (seq != _longPressSeq || !ReferenceEquals(_captured, owner) || !_longPressPending)
				return;
			_longPressPending = false;
			_longPressFired = true;
			if (_rowCapture)
				_rowGestureTook = true;
			LongPressGesturesFired++;
			var getPosition = PositionOf(new Point(_pressDpX, _pressDpY));
			foreach (var longPress in longPresses)
			{
				try
				{
					longPress.SendLongPressing(owner, GestureStatus.Started, getPosition);
					longPress.SendLongPressed(owner, getPosition);
					longPress.SendLongPressing(owner, GestureStatus.Completed, getPosition);
				}
				catch (Exception ex)
				{
					QtHostDiag.Error(QtHostDiagChannel.Input, $"LongPressed handler failed: {ex.Message}");
				}
			}
		});
	}

	private void Trace(int kind, double x, double y, string decision) =>
		QtHostDiag.Trace(QtHostDiagChannel.Input,
			$"kind={KindName(kind)} win=({x.ToString("F0", CultureInfo.InvariantCulture)},{y.ToString("F0", CultureInfo.InvariantCulture)})px " +
			$"dp=({QtHostUnits.ToLogical(x).ToString("F1", CultureInfo.InvariantCulture)},{QtHostUnits.ToLogical(y).ToString("F1", CultureInfo.InvariantCulture)}) — {decision}");

	private static string KindName(int kind) => kind switch
	{
		MousePress => "mouse-press",
		MouseRelease => "mouse-release",
		MouseMove => "mouse-move",
		Wheel => "wheel",
		TouchBegin => "touch-begin",
		TouchUpdate => "touch-update",
		TouchEnd => "touch-end",
		SecondPoint => "second-point",
		_ => kind.ToString(CultureInfo.InvariantCulture),
	};
}

