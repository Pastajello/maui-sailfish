using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>
/// Adapter events from QML (taps, text, pickers, swipes, WebView, dialogs, tabs…) decoded and written back into MAUI
/// under the renderer's push suppression, so the write never echoes back to native. It owns the decoding and the
/// write-back counters; the renderer owns the host registry, the dialogs, tabs and menus, and is reached through the
/// small surface it exposes for this (TryResolveHost, SuppressPush, MarkLayoutDirty, SelectTab, …).
/// </summary>
internal sealed class AdapterEventRouter
{
	private readonly QtHostPageRenderer _r;

	public AdapterEventRouter(QtHostPageRenderer renderer) => _r = renderer;

	/// <summary>Adapter events written back into MAUI.</summary>
	public long NativeEventsDelivered { get; private set; }

	/// <summary>Adapter event echoes dropped by change suppression.</summary>
	public long NativeEventsSuppressed { get; private set; }

	/// <summary>Adapter "focus-changed" events that drove VisualElement.Focus/Unfocus.</summary>
	public long FocusTransitions { get; private set; }

	/// <summary>Entry/Editor completions fired from adapter "completed" events (hardware or VKB Return).</summary>
	public long CompletedFired { get; private set; }

	/// <summary>Native caret/selection reports written back into InputView.CursorPosition/SelectionLength.</summary>
	public long CursorWriteBacks { get; private set; }

	/// <summary>Native scroll reports written back into ScrollView.ScrollY.</summary>
	public long ScrollWriteBacks { get; private set; }

	/// <summary>Native DockedPanel open-state changes reported over the bridge.</summary>
	public long PanelOpenChanges { get; private set; }


	/// <summary>Layout passes armed by native write-backs (text/scroll/date/time).</summary>
	public long LayoutDirtyFromWriteback { get; private set; }


	/// <summary>Delivers a QML tap to the mapped MAUI button. The payload is the adapter's event JSON
	/// <c>{"id":…}</c> (qml/lib/adapter.js), as for every adapter event.</summary>
	public void HandleTap(string payload)
	{
		var id = HostIdOf(payload) ?? string.Empty;
		if (string.IsNullOrEmpty(id) || !_r.TryGetHost(id, out var host) || host.Element is null)
		{
			QtHostDiag.Warn(QtHostDiagChannel.Input, $"tap for unknown button id='{id}'");
			return;
		}

		// ImageButton rides the image adapter, whose MouseArea fires the same "tap".
		if (host.Element is ImageButton imageButton)
		{
			QtHostDiag.Trace(QtHostDiagChannel.Input, $"tap -> MAUI ImageButton (id={id})");
			imageButton.SendClicked();
			return;
		}
		if (host.Element is not Button button)
		{
			QtHostDiag.Warn(QtHostDiagChannel.Input, $"tap for non-button id='{id}'");
			return;
		}

		QtHostDiag.Trace(QtHostDiagChannel.Input, $"tap -> MAUI Button '{button.Text}' (id={id}, enabled={QtHostVisualState.EffectiveEnabled(button)})");
		button.SendClicked();
		// Content/navigation changes caused by the handler are picked up by Poll().
	}

	/// <summary>
	/// Routes a semantic adapter event to the mapped MAUI element (main thread). Handler failures are logged,
	/// never rethrown toward the Qt loop.
	/// </summary>
	public void Handle(string name, string payload)
	{
		try
		{
			switch (name)
			{
				case "tap":
					HandleTap(payload);
					break;
				case "text-changed":
				case "toggled":
				case "value-changed":
				case "selected":                 // ComboBox pick → Picker.SelectedIndex
					ApplyNativeState(name, payload);
					break;
				case "date-selected":
					ApplyDateSelected(payload);   // DatePickerDialog accept → DatePicker.Date
					break;
				case "time-selected":
					ApplyTimeSelected(payload);   // TimePickerDialog accept → TimePicker.Time
					break;
				case "picker-open":
					ApplyPickerOpen(payload);     // native open/close → IsOpen
					break;
				case "window-geometry":
					_r.ApplyWindowGeometry(payload);   // window report / rotation / configure
					break;
				case "focus-changed":
					ApplyFocusChanged(payload);     // Qt focus source → MAUI logical focus
					break;
				case "completed":
					ApplyCompleted(payload);        // accepted → Entry/Editor.SendCompleted
					break;
				case "cursor-changed":
					ApplyCursorChanged(payload);    // native caret/selection → MAUI InputView
					break;
				case "scroll-changed":
					// Scroll-view hosts carry their "id"; the page flickable's own report carries "page" (pulley/refresh
					// overscroll) and has nothing to write back.
					if (HostIdOf(payload) is not null)
						ApplyNestedScrollChanged(payload);
					break;
				case "list-item-attached":
				case "list-item-rebind":
				case "list-item-detached":
				case "list-item-released":
				case "list-item-tapped":
				case "list-item-pressed":
				case "list-scroll":
				case "carousel-position":
				case "carousel-motion":
					_r.Collection.HandleEvent(name, payload);   // ListView delegate/selection/scroll events, carousel pages
					break;
				case "indicator-tapped":
					ApplyIndicatorTapped(payload);  // a dot tap selects that page
					break;
				case "swipe-item-invoked":
					ApplySwipeItemInvoked(payload); // SwipeItem → Invoked + Command
					break;
				case "pressed-changed":
					ApplyPressedChanged(payload);   // Button/ImageButton Pressed/Released
					break;
				case "drag-changed":
					ApplyDragChanged(payload);      // Slider DragStarted/DragCompleted
					break;
				case "swipe-changing":
					ApplySwipeChanging(payload);    // SwipeView SwipeStarted (first) + SwipeChanging
					break;
				case "swipe-state":
					ApplySwipeState(payload);       // SwipeStarted/Ended + IsOpen
					break;
				case "webview-navigating":
				case "webview-navigated":
				case "webview-js":
					ApplyWebViewEvent(name, payload);   // Gecko → MAUI WebView
					break;
				case "refresh-requested":
					ApplyRefreshRequested(payload); // pull-to-refresh release → IsRefreshing
					break;
				case "context-activated":
					_r.ApplyContextActivated(payload); // ContextMenu pick → MenuFlyoutItem
					break;
				case "context-closed":
					_r.ApplyContextClosed(); // ContextMenu gone: navigation may follow the native stack again
					break;
				case "tab-selected":
				{
					// A tap in the model page's tab bar → the MAUI tab.
					using var tabDoc = JsonDocument.Parse(payload);
					if (tabDoc.RootElement.TryGetProperty("index", out var tabIndex) && tabIndex.TryGetInt32(out var ti))
					{
						QtHostDiag.Trace(QtHostDiagChannel.Navigation, $"tab-selected {ti}");
						// A fresh timeline: the last navigation's stays open when its page builds no list rows.
						_r.RestartTimeline("tab");
						if (tabDoc.RootElement.TryGetProperty("level", out var level) && level.TryGetInt32(out var lv) && lv == 1)
							_r.SelectTab(1, ti);
						else
							_r.SelectTab(0, ti);
					}
					break;
				}
				case "tab-swipe-commit":
				{
					// The swiped page slid out: switch the tab (the new one slides in when its tabs arrive).
					using var swipeDoc = JsonDocument.Parse(payload);
					var delta = (int)BridgeJson.Num(swipeDoc.RootElement, "delta");
					_r.RestartTimeline("tab");
					_r.SwipeTab(delta);
					break;
				}
				case "remorse-done":
					SailfishRemorse.OnDone(payload);   // RemorsePopup/RemorseItem countdown ended or was cancelled
					break;
				case "search-changed":
					_r.ApplySearchChanged(payload);    // Shell.SearchHandler field → Query
					break;
				case "search-submit":
					_r.ApplySearchSubmit(payload);     // its enter key → the query confirmed
					break;
				case "span-tapped":
					ApplySpanTapped(payload);          // a tappable FormattedText span → its TapGestureRecognizers
					break;
				case "toolbar-activated":
					_r.ApplyToolbarActivated(payload); // pulley MenuItem → ToolbarItem
					break;
				case "pulley-attached":
					// Which flickable carries the menu and whether it is interactive (the gesture fires only on the dragged scroller).
					QtHostDiag.Trace(QtHostDiagChannel.QmlSignal, $"pulley menu attached: {payload}");
					break;
				case "reparented":
				{
					// A host whose parent appeared in a later batch was re-parented out of the canvas root; its earlier
					// geometry push used the wrong parent chain, so re-push it.
					using var repDoc = JsonDocument.Parse(payload);
					if (_r.TryResolveHost(repDoc.RootElement, out _, out var repHost))
					{
						repHost.AppliedGeometrySet = false;
						_r.MarkLayoutDirty();
					}
					break;
				}
				case "panel-open-changed":
					PanelOpenChanges++;             // DockedPanel native open write-back
					QtHostDiag.Trace(QtHostDiagChannel.QmlSignal, $"docked-panel open state changed: {payload}");
					_r.RoutePanelOpenChanged(payload); // SailfishBottomSheet native write-back
					break;
				case "drawer-open-changed":
					QtHostDiag.Trace(QtHostDiagChannel.QmlSignal, $"drawer open state changed: {payload}");
					break;
				case "alert-accepted":
				case "alert-rejected":
					_r.CompleteDialog(name == "alert-accepted");  // Dialog result
					break;
				case "prompt-accepted":
					_r.CompleteDialog(QtHostPageRenderer.ParseDialogText(payload));  // DisplayPromptAsync → entered text
					break;
				case "prompt-rejected":
					_r.CompleteDialog(null);                      // prompt dismissed → null
					break;
				case "action-selected":
					_r.CompleteDialog(QtHostPageRenderer.ParseDialogText(payload));  // DisplayActionSheetAsync → entry text
					break;
				case "action-cancelled":
					_r.CompleteDialog(_r.SheetCancel);              // sheet dismissed → cancel text
					break;
				default:
					RouteAdapterEvent(name, payload);   // a library adapter's own event; "rendered" and friends end here too
					break;
			}
		}
		catch (Exception ex)
		{
			// An app handler raised from the event (Clicked, TextChanged, …) or the routing itself: logged with its stack,
			// and the app ends unless SailfishExceptions.Unhandled handles it.
			SailfishExceptions.Report(ex, $"event '{name}'");
		}
	}

	/// <summary>The "id" of an adapter event payload (qml/lib/adapter.js); null for a page event ("page") or a
	/// payload that is not a JSON object.</summary>
	private static string? HostIdOf(string payload)
	{
		if (!payload.StartsWith('{'))
			return null;
		try
		{
			using var doc = JsonDocument.Parse(payload);
			return doc.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null;
		}
		catch (JsonException)
		{
			return null;
		}
	}

	/// <summary>Hands an event no built-in control consumes to the host's Sailfish handler.</summary>
	private void RouteAdapterEvent(string name, string payload)
	{
		if (HostIdOf(payload) is null)
			return;   // a page event ("rendered", "pulley-opened", …) or a diagnostic one
		using var doc = JsonDocument.Parse(payload);
		if (_r.TryResolveHost(doc.RootElement, out _, out var host) &&
		    host.Element.Handler is Handlers.ISailfishAdapterHandler handler)
			handler.OnAdapterEvent(name, doc.RootElement);
	}

	/// <summary>
	/// Writes an adapter state payload {id, text|checked|value} back into the MAUI element. A value equal to
	/// the last managed push is an echo and is dropped; otherwise the applied state is updated and the write
	/// runs under <c>_suppressPush</c> so PropertyChanged does not push straight back.
	/// </summary>
	private void ApplyNativeState(string name, string payload)
	{
		using var doc = JsonDocument.Parse(payload);
		var root = doc.RootElement;
		if (!_r.TryResolveHost(root, out var id, out var host))
		{
			QtHostDiag.Warn(QtHostDiagChannel.QmlSignal, $"{name} for unknown host id='{id}'");
			return;
		}

		string nativeProp;
		object incoming;
		switch (name)
		{
			case "text-changed":
				nativeProp = "text";
				incoming = root.TryGetProperty("text", out var t) ? t.GetString() ?? string.Empty : string.Empty;
				break;
			case "toggled":
				nativeProp = "checked";
				incoming = root.TryGetProperty("checked", out var c) && c.ValueKind == JsonValueKind.True;
				break;
			case "value-changed":
				nativeProp = "value";
				incoming = BridgeJson.Num(root, "value");
				break;
			case "selected":   // Picker (Silica ComboBox) menu pick
				nativeProp = "mauiSelectedIndex";
				incoming = BridgeJson.Int(root, "index");
				break;
			default:
				return;
		}

		var json = BridgeValue.Serialize(incoming);
		if (host.IsApplied(nativeProp, json))
		{
			// Echo of our own push that got past the QML suppression flag.
			NativeEventsSuppressed++;
			return;
		}
		// Whatever MAUI does with it, the native object now holds this value.
		host.AppliedProperties[nativeProp] = json;

		using (_r.SuppressPush())
		{
			switch (host.Element)
			{
				case Entry entry when name == "text-changed":
					entry.Text = (string)incoming;
					break;
				case Editor editor when name == "text-changed":
					editor.Text = (string)incoming;
					break;
				case Switch sw when name == "toggled":
					sw.IsToggled = (bool)incoming;
					break;
				case CheckBox checkBox when name == "toggled":
					checkBox.IsChecked = (bool)incoming;
					break;
				case Slider slider when name == "value-changed":
					slider.Value = (double)incoming;   // MAUI clamps to [Minimum, Maximum]
					break;
				case Stepper stepper when name == "value-changed":
					stepper.Value = (double)incoming;
					break;
				case SearchBar searchBar when name == "text-changed":
					searchBar.Text = (string)incoming;
					break;
				case RadioButton radioButton when name == "toggled":
					// MAUI's radio group unchecks the siblings; their pushes ride the normal suppressed path.
					radioButton.IsChecked = (bool)incoming;
					break;
				case Picker picker when name == "selected":
					picker.SelectedIndex = (int)incoming;
					break;
				default:
					QtHostDiag.Warn(QtHostDiagChannel.QmlSignal, $"{name} not mapped for {host} — dropped");
					return;
			}
		}

		// The write-back can change measured sizes (text), so relayout.
		_r.MarkLayoutDirty();
		LayoutDirtyFromWriteback++;

		NativeEventsDelivered++;
		QtHostDiag.Trace(QtHostDiagChannel.QmlSignal, $"native {name} id={id} -> MAUI " +
			$"{host.Element.GetType().Name}={incoming} (write-back OK)");
	}


	// --- Focus, text input and hit-testing ---
	// Qt/Silica is the focus source: adapters report activeFocus and the renderer drives MAUI focus; managed
	// Focus()/Unfocus() push mauiFocus so Maliit opens like on a real tap. Hit-testing uses the same absolute
	// per-host rects (root space, dp) as the geometry pass, all on the Qt thread, so no locking is needed.

	/// <summary>Adapter "focus-changed" event {id, focused} → MAUI logical focus.</summary>
	private void ApplyFocusChanged(string payload)
	{
		using var doc = JsonDocument.Parse(payload);
		var root = doc.RootElement;
		var focused = root.TryGetProperty("focused", out var fEl) && fEl.ValueKind == JsonValueKind.True;
		if (!_r.TryResolveHost(root, out var id, out var host) || host.Element is not VisualElement ve)
		{
			if (QtHostPageRenderer.InputTrace)
				QtHostDiag.Warn(QtHostDiagChannel.Focus, $"focus-changed for unknown host id='{id}' — dropped");
			return;
		}
		var before = ve.IsFocused;
		if (focused)
			ve.Focus();
		else
			ve.Unfocus();
		NativeEventsDelivered++;
		if (ve.IsFocused != before)
			FocusTransitions++;   // real MAUI focus state transition
		// Only when managed refused the native transition: managed is authoritative, so pull native focus back.
		// The common case already synced through the IsFocused PropertyChanged push.
		if (host.IsAttached && ve.IsFocused != focused)
			_r.PushBatch(host, new[] { ("mauiFocus", BridgeValue.Serialize(ve.IsFocused)) });
		if (QtHostPageRenderer.InputTrace)
			QtHostDiag.Trace(QtHostDiagChannel.Focus, $"{host} → MAUI {ve.GetType().Name} focused={focused} (IsFocused={ve.IsFocused})");
	}

	/// <summary>
	/// Adapter "completed" event {id} → MAUI completion. Hardware Return and the VKB enter key both commit
	/// through the Silica editor's accepted signal, so there is one path (Qt 5.6 TextEdit has no accepted
	/// signal, so Editor only completes on demand). Not suppressed: Completed handlers' changes must reach native.
	/// </summary>
	/// <summary>Focuses the first visible, enabled, editable text input after <paramref name="from"/> in the page's
	/// tree order (managed focus pushes mauiFocus, which opens the keyboard on it). None after it: focus stays.</summary>
	internal static bool FocusNextInput(InputView from)
	{
		Page? page = null;
		for (var e = from.Parent; e is not null && page is null; e = e.Parent)
			page = e as Page;
		if (page is null)
			return false;
		var passed = false;
		foreach (var element in Descendants(page))
		{
			if (ReferenceEquals(element, from))
			{
				passed = true;
				continue;
			}
			if (passed && element is InputView { IsVisible: true, IsReadOnly: false } next && QtHostVisualState.EffectiveEnabled(next))
			{
				next.Focus();
				QtHostDiag.Trace(QtHostDiagChannel.Focus, $"ReturnType.Next → focus {next.GetType().Name}");
				return true;
			}
		}
		return false;

		static IEnumerable<Element> Descendants(Element root)
		{
			foreach (var child in ((IVisualTreeElement)root).GetVisualChildren())
			{
				if (child is not Element element || element is VisualElement { IsVisible: false })
					continue;
				yield return element;
				foreach (var inner in Descendants(element))
					yield return inner;
			}
		}
	}

	private void ApplyCompleted(string payload)
	{
		using var doc = JsonDocument.Parse(payload);
		var root = doc.RootElement;
		if (!_r.TryResolveHost(root, out var id, out var host))
		{
			QtHostDiag.Warn(QtHostDiagChannel.QmlSignal, $"text-input completed for unknown host id='{id}'");
			return;
		}
		switch (host.Element)
		{
			case Entry entry:
				entry.SendCompleted();
				// ReturnType.Next moves on to the next text input, as the IME's Next action does on Android and iOS.
				if (entry.ReturnType == ReturnType.Next)
					FocusNextInput(entry);
				break;
			case Editor editor:
				editor.SendCompleted();
				break;
			case SearchBar searchBar:
				// SearchField accepted → SearchButtonPressed; MAUI 11 exposes it only via ISearchBarController.
				((Microsoft.Maui.Controls.ISearchBarController)searchBar).OnSearchButtonPressed();
				break;
			default:
				QtHostDiag.Warn(QtHostDiagChannel.QmlSignal, $"completed not mapped for {host} — dropped");
				return;
		}
		CompletedFired++;
		NativeEventsDelivered++;
		QtHostDiag.Trace(QtHostDiagChannel.QmlSignal, $"completed id={id} -> MAUI {host.Element.GetType().Name}.SendCompleted (Completed + ReturnCommand)");
	}

	/// <summary>
	/// Adapter "date-selected" {id, y, m, d} → DatePicker.Date. The wheel dialog is a separate Silica window, so
	/// the pick arrives as an event, never as pointer input; the mauiDateMs mirror is updated to keep the diff quiet.
	/// </summary>
	private void ApplyDateSelected(string payload)
	{
		using var doc = JsonDocument.Parse(payload);
		var root = doc.RootElement;
		if (!_r.TryResolveHost(root, out var id, out var host) || host.Element is not DatePicker datePicker)
		{
			QtHostDiag.Warn(QtHostDiagChannel.QmlSignal, $"date-selected for unknown/non-date-picker host id='{id}'");
			return;
		}
		var y = root.TryGetProperty("y", out var yEl) ? yEl.GetInt32() : 1;
		var m = root.TryGetProperty("m", out var mEl) ? mEl.GetInt32() : 1;
		var d = root.TryGetProperty("d", out var dEl) ? dEl.GetInt32() : 1;
		var picked = new DateTime(y, m, d);
		host.AppliedProperties["mauiDateMs"] = BridgeValue.Serialize(Handlers.AdapterSnapshots.LocalMidnightMs(picked));
		using (_r.SuppressPush())
		{
			datePicker.Date = picked;
		}
		_r.MarkLayoutDirty();
		NativeEventsDelivered++;
		QtHostDiag.Trace(QtHostDiagChannel.QmlSignal, $"native date-selected id={id} -> MAUI DatePicker.Date={picked:yyyy-MM-dd} (write-back OK)");
	}

	/// <summary>Adapter "picker-open" {id, open}: the menu/dialog opened or closed natively → IsOpen, mirrored so
	/// the reconcile diff stays quiet.</summary>
	private void ApplyPickerOpen(string payload)
	{
		using var doc = JsonDocument.Parse(payload);
		var root = doc.RootElement;
		var open = root.TryGetProperty("open", out var openEl) && openEl.ValueKind == JsonValueKind.True;
		if (!_r.TryResolveHost(root, out var id, out var host))
			return;
		host.AppliedProperties["mauiOpen"] = BridgeValue.Serialize(open);
		using (_r.SuppressPush())
		{
			switch (host.Element)
			{
				case Picker picker: picker.IsOpen = open; break;
				case DatePicker datePicker: datePicker.IsOpen = open; break;
				case TimePicker timePicker: timePicker.IsOpen = open; break;
			}
		}
		NativeEventsDelivered++;
	}

	/// <summary>
	/// Adapter "time-selected" {id, h, mi} → TimePicker.Time; same echo handling as ApplyDateSelected.
	/// </summary>
	private void ApplyTimeSelected(string payload)
	{
		using var doc = JsonDocument.Parse(payload);
		var root = doc.RootElement;
		if (!_r.TryResolveHost(root, out var id, out var host) || host.Element is not TimePicker timePicker)
		{
			QtHostDiag.Warn(QtHostDiagChannel.QmlSignal, $"time-selected for unknown/non-time-picker host id='{id}'");
			return;
		}
		var h = root.TryGetProperty("h", out var hEl) ? hEl.GetInt32() : 0;
		var mi = root.TryGetProperty("mi", out var miEl) ? miEl.GetInt32() : 0;
		host.AppliedProperties["mauiHour"] = BridgeValue.Serialize(h);
		host.AppliedProperties["mauiMinute"] = BridgeValue.Serialize(mi);
		using (_r.SuppressPush())
		{
			timePicker.Time = new TimeSpan(h, mi, 0);
		}
		_r.MarkLayoutDirty();
		NativeEventsDelivered++;
		QtHostDiag.Trace(QtHostDiagChannel.QmlSignal, $"native time-selected id={id} -> MAUI TimePicker.Time={h:00}:{mi:00} (write-back OK)");
	}

	/// <summary>
	/// Adapter "cursor-changed" {id, cursor, selStart, selEnd} → InputView.CursorPosition/SelectionLength
	/// (MAUI selection is [CursorPosition, CursorPosition + SelectionLength)). The mauiCursor/mauiSelLen mirrors
	/// are then re-pushed, since a stale mirror would swallow a later managed set of the same value.
	/// </summary>
	private void ApplyCursorChanged(string payload)
	{
		using var doc = JsonDocument.Parse(payload);
		var root = doc.RootElement;
		if (!_r.TryResolveHost(root, out var id, out var host) || host.Element is not InputView input)
		{
			QtHostDiag.Warn(QtHostDiagChannel.QmlSignal, $"cursor-changed for unknown/non-text host id='{id}'");
			return;
		}
		int cursor = -1, selStart = -1, selEnd = -1;
		var hasCursor = root.TryGetProperty("cursor", out var cursorEl) && cursorEl.TryGetInt32(out cursor);
		var hasStart = root.TryGetProperty("selStart", out var startEl) && startEl.TryGetInt32(out selStart);
		var hasEnd = root.TryGetProperty("selEnd", out var endEl) && endEl.TryGetInt32(out selEnd);
		if (!hasCursor || !hasStart || !hasEnd || cursor < 0 || selStart < 0 || selEnd < 0)
		{
			QtHostDiag.Warn(QtHostDiagChannel.QmlSignal, $"malformed cursor-changed payload id='{id}' — dropped");
			return;
		}
		var wantCursor = selEnd > selStart ? selStart : cursor;
		var wantLen = selEnd > selStart ? selEnd - selStart : 0;

		using (_r.SuppressPush())
		{
			if (input.CursorPosition != wantCursor)
				input.CursorPosition = wantCursor;
			if (input.SelectionLength != wantLen)
				input.SelectionLength = wantLen;
		}
		if (host.IsAttached)
			_r.PushBatch(host, new[]
			{
				("mauiCursor", BridgeValue.Serialize(wantCursor)),
				("mauiSelLen", BridgeValue.Serialize(wantLen)),
			});
		CursorWriteBacks++;
		NativeEventsDelivered++;
		if (QtHostPageRenderer.InputTrace)
			QtHostDiag.Trace(QtHostDiagChannel.QmlSignal, $"{host} cursor-changed → MAUI CursorPosition={wantCursor} SelectionLength={wantLen}");
	}

	/// <summary>The pull gesture released past the threshold: a hosted list answers first, then a scroll-view
	/// host, then the page-armed context. Setting IsRefreshing makes the control raise Refreshing and run the command.</summary>
	private void ApplyRefreshRequested(string payload)
	{
		string id;
		try
		{
			using var doc = JsonDocument.Parse(payload);
			id = doc.RootElement.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? string.Empty : string.Empty;
		}
		catch (Exception ex)
		{
			QtHostDiag.Warn(QtHostDiagChannel.Input, $"bad refresh-requested payload: {ex.Message}");
			return;
		}
		if (_r.Collection.HandleRefreshRequested(id))
			return;
		if (_r.ScrollRefreshView is { } scrollRefresh && id == QtHostPageRenderer.RefreshId)
		{
			QtHostDiag.Trace(QtHostDiagChannel.Input, "pull-to-refresh gesture on the scroll view → RefreshView.IsRefreshing=true");
			((IRefreshView)scrollRefresh).IsRefreshing = true;
			return;
		}
		if (_r.PageRefreshView is not { } refresh || id != QtHostPageRenderer.RefreshId)
			return;
		QtHostDiag.Trace(QtHostDiagChannel.Input, "pull-to-refresh gesture → RefreshView.IsRefreshing=true");
		((IRefreshView)refresh).IsRefreshing = true;
	}

	/// <summary>Gecko navigation / script results → the MAUI WebView and pending EvaluateJavaScriptAsync requests.</summary>
	private void ApplyWebViewEvent(string name, string payload)
	{
		try
		{
			using var doc = JsonDocument.Parse(payload);
			var root = doc.RootElement;
			if (!_r.TryResolveHost(root, out _, out var host))
				return;
			// A HybridWebView rides the same Gecko adapter: its scripts answer here, its page loads are its own.
			if (host.Element is HybridWebView { Handler: Handlers.SailfishHybridWebViewHandler hybrid })
			{
				if (name == "webview-js")
					hybrid.CompleteJs(root.GetProperty("req").GetString() ?? string.Empty,
						root.TryGetProperty("ok", out var hybridOk) && hybridOk.ValueKind == JsonValueKind.True,
						root.TryGetProperty("result", out var hybridResult) ? hybridResult.GetString() : null);
				return;
			}
			if (host.Element is not WebView web)
				return;
			var view = (IWebView)web;
			switch (name)
			{
				case "webview-navigating":
					view.Navigating(WebNavigationEvent.NewPage, root.GetProperty("url").GetString() ?? string.Empty);
					break;
				case "webview-navigated":
					view.CanGoBack = root.TryGetProperty("back", out var b) && b.ValueKind == JsonValueKind.True;
					view.CanGoForward = root.TryGetProperty("fwd", out var f) && f.ValueKind == JsonValueKind.True;
					view.Navigated(WebNavigationEvent.NewPage, root.GetProperty("url").GetString() ?? string.Empty, WebNavigationResult.Success);
					break;
				case "webview-js":
					if (web.Handler is Handlers.SailfishWebViewHandler handler)
						handler.CompleteJs(root.GetProperty("req").GetString() ?? string.Empty,
							root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True,
							root.TryGetProperty("result", out var r) ? r.GetString() : null);
					break;
			}
		}
		catch (Exception ex)
		{
			QtHostDiag.Warn(QtHostDiagChannel.Input, $"bad {name} payload: {ex.Message}");
		}
	}


	/// <summary>A native swipe-item tap (or execute swipe) → SwipeItem Invoked and Command.</summary>
	private void ApplySwipeItemInvoked(string payload)
	{
		try
		{
			using var doc = JsonDocument.Parse(payload);
			var side = doc.RootElement.GetProperty("side").GetString();
			var index = (int)BridgeJson.Num(doc.RootElement, "index");
			if (!_r.TryResolveHost(doc.RootElement, out _, out var host) || host.Element is not SwipeView swipe)
				return;
			var visible = Handlers.AdapterSnapshots.VisibleSwipeItems(side == "left" ? swipe.LeftItems : swipe.RightItems);
			if (index >= 0 && index < visible.Count)
			{
				QtHostDiag.Trace(QtHostDiagChannel.Input, $"swipe item {index} ({visible[index].GetType().Name}, {side}) invoked");
				visible[index].OnInvoked();
			}
		}
		catch (Exception ex)
		{
			QtHostDiag.Warn(QtHostDiagChannel.Input, $"bad swipe-item-invoked payload: {ex.Message}");
		}
	}

	/// <summary>The native swipe row settled open/closed → SwipeStarted/SwipeEnded (direction reveals that side)
	/// and IsOpen.</summary>
	/// <summary>Adapter "pressed-changed" {id, pressed} → Button/ImageButton Pressed or Released (before Clicked, as
	/// the Silica press ends before its click).</summary>
	private void ApplyPressedChanged(string payload)
	{
		using var doc = JsonDocument.Parse(payload);
		var pressed = doc.RootElement.TryGetProperty("pressed", out var p) && p.ValueKind == JsonValueKind.True;
		if (!_r.TryResolveHost(doc.RootElement, out _, out var host))
			return;
		switch (host.Element)
		{
			case Button button:
				if (pressed) button.SendPressed(); else button.SendReleased();
				break;
			case ImageButton imageButton:
				if (pressed) imageButton.SendPressed(); else imageButton.SendReleased();
				break;
			default:
				return;
		}
		NativeEventsDelivered++;
	}

	/// <summary>Adapter "drag-changed" {id, dragging} → Slider DragStarted or DragCompleted.</summary>
	private void ApplyDragChanged(string payload)
	{
		using var doc = JsonDocument.Parse(payload);
		var dragging = doc.RootElement.TryGetProperty("dragging", out var d) && d.ValueKind == JsonValueKind.True;
		if (!_r.TryResolveHost(doc.RootElement, out _, out var host) || host.Element is not ISlider slider)
			return;
		if (dragging)
			slider.DragStarted();
		else
			slider.DragCompleted();
		NativeEventsDelivered++;
	}

	// SwipeViews in a drag: SwipeStarted went out with its first SwipeChanging; swipe-state ends it.
	private readonly HashSet<SwipeView> _swiping = new();

	/// <summary>Adapter "swipe-changing" {id, offset} (Qt units, > 0 revealing the left items) → SwipeStarted on the
	/// first one of a drag, then SwipeChanging with the offset in dp, as the platform SwipeViews report a drag.</summary>
	private void ApplySwipeChanging(string payload)
	{
		using var doc = JsonDocument.Parse(payload);
		if (!_r.TryResolveHost(doc.RootElement, out _, out var host) || host.Element is not SwipeView swipe)
			return;
		var offset = QtHostUnits.ToLogical(BridgeJson.Num(doc.RootElement, "offset"));
		if (offset == 0)
			return;
		var direction = offset > 0 ? SwipeDirection.Right : SwipeDirection.Left;
		var controller = (ISwipeView)swipe;
		if (_swiping.Add(swipe))
			controller.SwipeStarted(new SwipeViewSwipeStarted(direction));
		controller.SwipeChanging(new SwipeViewSwipeChanging(direction, offset));
		NativeEventsDelivered++;
	}

	private void ApplySwipeState(string payload)
	{
		try
		{
			using var doc = JsonDocument.Parse(payload);
			var side = doc.RootElement.GetProperty("side").GetString() ?? string.Empty;
			var open = doc.RootElement.TryGetProperty("open", out var o) && o.ValueKind == JsonValueKind.True;
			if (!_r.TryResolveHost(doc.RootElement, out _, out var host) || host.Element is not SwipeView swipe)
				return;
			var direction = side == "left" ? SwipeDirection.Right : SwipeDirection.Left;
			var controller = (ISwipeView)swipe;
			// A drag already reported SwipeStarted; an open from code (or a drag too short to report) starts here.
			if (!_swiping.Remove(swipe) && open)
				controller.SwipeStarted(new SwipeViewSwipeStarted(direction));
			controller.IsOpen = open;
			controller.SwipeEnded(new SwipeViewSwipeEnded(direction, open));
		}
		catch (Exception ex)
		{
			QtHostDiag.Warn(QtHostDiagChannel.Input, $"bad swipe-state payload: {ex.Message}");
		}
	}

	/// <summary>A tapped IndicatorView dot → IndicatorView.Position (its two-way link moves the carousel).</summary>
	private void ApplyIndicatorTapped(string payload)
	{
		try
		{
			using var doc = JsonDocument.Parse(payload);
			var index = (int)BridgeJson.Num(doc.RootElement, "index");
			if (_r.TryResolveHost(doc.RootElement, out _, out var host) && host.Element is IndicatorView indicator && index >= 0 && index < indicator.Count)
				indicator.Position = index;
		}
		catch (Exception ex)
		{
			QtHostDiag.Warn(QtHostDiagChannel.Input, $"bad indicator-tapped payload: {ex.Message}");
		}
	}

	/// <summary>A nested scroll host moved natively → ScrollView.SetScrolledPosition under suppression; the
	/// relayout keeps the content's root rects in step.</summary>
	private void ApplyNestedScrollChanged(string payload)
	{
		string? id;
		double x, y;
		try
		{
			using var doc = JsonDocument.Parse(payload);
			id = doc.RootElement.GetProperty("id").GetString();
			x = BridgeJson.Num(doc.RootElement, "x");
			y = BridgeJson.Num(doc.RootElement, "y");
		}
		catch (Exception ex)
		{
			QtHostDiag.Warn(QtHostDiagChannel.Geometry, $"bad nested scroll-changed payload: {ex.Message}");
			return;
		}
		if (id is null || !_r.TryGetHost(id, out var host) || host.Element is not ScrollView scrollView)
			return;
		var dpX = QtHostUnits.ToLogical(x);
		var dpY = QtHostUnits.ToLogical(y);
		if (Math.Abs(scrollView.ScrollX - dpX) < 0.5 && Math.Abs(scrollView.ScrollY - dpY) < 0.5)
			return;
		using (_r.SuppressPush())
		{
			((IScrollViewController)scrollView).SetScrolledPosition(dpX, dpY);
		}
		// The adapter already sits there; record it so the next diff doesn't push it back.
		host.AppliedProperties["mauiScrollX"] = BridgeValue.Serialize(x);
		host.AppliedProperties["mauiScrollY"] = BridgeValue.Serialize(y);
		ScrollWriteBacks++;
		_r.RequestScrollGeometry();   // the position moves root rects only; MAUI's layout is unchanged
	}

	/// <summary>A tapped FormattedText span (Label.qml "span:N" link) fires the span's TapGestureRecognizers with the
	/// label as the sender, as MAUI's Android and iOS span taps do (tracker S42).</summary>
	private void ApplySpanTapped(string payload)
	{
		using var doc = JsonDocument.Parse(payload);
		if (!_r.TryResolveHost(doc.RootElement, out var id, out var host) || host.Element is not Label { FormattedText: { } formatted } label)
		{
			QtHostDiag.Warn(QtHostDiagChannel.Input, $"span-tapped for an unknown label id='{id}'");
			return;
		}
		var index = BridgeJson.Int(doc.RootElement, "index");
		if (index < 0 || index >= formatted.Spans.Count)
			return;
		foreach (var tap in formatted.Spans[index].GestureRecognizers.OfType<TapGestureRecognizer>().ToList())
		{
			QtHostDiag.Trace(QtHostDiagChannel.Input, $"span {index} tap -> MAUI TapGestureRecognizer (label id={id})");
			tap.SendTapped(label);
		}
	}
}
