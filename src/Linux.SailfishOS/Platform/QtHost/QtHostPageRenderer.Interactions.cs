using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

// Silica interaction surfaces: pulley and context menus, tabs, dialogs and app-added interaction hosts.
internal sealed partial class QtHostPageRenderer
{
	// --- Sailfish interaction surfaces ---
	// ContextFlyout → Silica ContextMenu (press-and-hold via the input router); Page.ToolbarItems → PullDownMenu /
	// PushUpMenu synthetic hosts driven by native gestures; DockedPanel/Drawer via AddInteractionHost; dialogs
	// are pushed on the Silica pageStack and complete their task from the accept/reject event.

	/// <summary>Appends the page-level synthetic hosts (pulley menus, context menu singleton, interaction hosts);
	/// they reconcile like tree hosts but never enter the geometry pass.</summary>
	private void AddSyntheticHosts(Page page, List<NativeElementHost> desired,
	                               Dictionary<NativeElementHost, Dictionary<string, object?>> props)
	{
		WatchToolbarItems(page);
		var pull = new List<ToolbarItem>();
		var push = new List<ToolbarItem>();
		foreach (var item in ToolbarItemsOf(page))
			(item.Order == ToolbarItemOrder.Secondary ? push : pull).Add(item);
		// Pull-down menu = Shell flyout entries (top) + primary ToolbarItems (nearest the content).
		_pullEntries.Clear();
		foreach (var flyout in FlyoutEntries(page))
			_pullEntries.Add(flyout);
		foreach (var item in pull)
			_pullEntries.Add((ToolbarText(item), item.IsEnabled, () => FireMenuItem(item)));
		// A RefreshView on a page with a pulley: Silica's pull-down menu owns the overscroll, so the pull gesture can
		// never reach the RefreshView (RefreshAncestorOf leaves it unarmed). Silica apps refresh from a pulley item, so
		// the pulley gets one, nearest the content (the quickest to reach), that starts the refresh as the gesture would.
		var pulleyRefresh = _refreshWalk is { } refresh && (pull.Count > 0 || push.Count > 0 || _pullEntries.Count > 0)
			? refresh
			: null;
		WatchPulleyRefresh(pulleyRefresh);
		if (pulleyRefresh is not null)
			_pullEntries.Add((PulleyRefreshText, pulleyRefresh.IsRefreshEnabled, () => StartPulleyRefresh(pulleyRefresh)));

		// Page-scoped hosts: returning to a page creates fresh menu objects so adapters rebuild MenuItems from init props.
		if (!ReferenceEquals(_pullPage, page) || !ReferenceEquals(_pushPage, page) || !ReferenceEquals(_ctxPage, page))
			QtHostDiag.Trace(QtHostDiagChannel.Navigation,
				$"synthetic page-scope switch '{_pullPage?.Title ?? "-"}' → '{page.Title}' (menu hosts dropped)");
		if (!ReferenceEquals(_pullPage, page)) { _pullHost = null; _pullPage = page; }
		if (!ReferenceEquals(_pushPage, page)) { _pushHost = null; _pushPage = page; }
		if (!ReferenceEquals(_ctxPage, page)) { _ctxMenuHost = null; _ctxPage = page; }

		var allocated = new List<string>();
		if (_pullEntries.Count > 0)
		{
			if (_pullHost is null) allocated.Add("pull-down-menu");
			_pullHost ??= new NativeElementHost(SyntheticPrefix + "pulldown", "pull-down-menu", page);
			props[_pullHost] = new Dictionary<string, object?> { ["mauiItems"] = MenuEntriesJson(_pullEntries) };
			desired.Add(_pullHost);
		}
		if (push.Count > 0)
		{
			if (_pushHost is null) allocated.Add("push-up-menu");
			_pushHost ??= new NativeElementHost(SyntheticPrefix + "pushup", "push-up-menu", page);
			props[_pushHost] = new Dictionary<string, object?> { ["mauiItems"] = MenuItemsJson(push) };
			desired.Add(_pushHost);
		}
		if (_contextFlyouts.Count > 0 || ShellFlyoutWantsMenu)
		{
			if (_ctxMenuHost is null) allocated.Add("context-menu");
			_ctxMenuHost ??= new NativeElementHost(SyntheticPrefix + "ctxmenu", "context-menu", page);
			props[_ctxMenuHost] = new Dictionary<string, object?>();
			desired.Add(_ctxMenuHost);
		}
		if (allocated.Count > 0)
			QtHostDiag.Trace(QtHostDiagChannel.Navigation,
				$"synthetic allocated [{string.Join(",", allocated)}] for '{page.Title}' mirrorTop='{(NativeTopPageId ?? "-")}'");
		foreach (var (id, uri) in _interactionUris)
		{
			// A declared host whose NativeElementHost was dropped (push parked the page, dead handle) comes back here.
			if (!_interactionHosts.TryGetValue(id, out var host))
				_interactionHosts[id] = host = new NativeElementHost(id, uri, page);
			props[host] = _interactionProps.TryGetValue(id, out var p)
				? p
				: new Dictionary<string, object?>();
			desired.Add(host);
		}
	}

	// The rendered page's ToolbarItems and their items: a Text/IsEnabled/Order change or an added item re-syncs the
	// pulley in place, as a Silica MenuItem binding would (nothing else asks for a reconcile then).
	private Page? _toolbarPage;
	private readonly HashSet<ToolbarItem> _toolbarWatched = new();

	private void WatchToolbarItems(Page page)
	{
		if (!ReferenceEquals(_toolbarPage, page))
		{
			if (_toolbarPage is not null)
				((System.Collections.Specialized.INotifyCollectionChanged)_toolbarPage.ToolbarItems).CollectionChanged -= OnToolbarItemsChanged;
			foreach (var old in _toolbarWatched)
				old.PropertyChanged -= OnToolbarItemChanged;
			_toolbarWatched.Clear();
			_toolbarPage = page;
			((System.Collections.Specialized.INotifyCollectionChanged)page.ToolbarItems).CollectionChanged += OnToolbarItemsChanged;
		}
		foreach (var item in ToolbarItemsOf(page))
			if (_toolbarWatched.Add(item))
				item.PropertyChanged += OnToolbarItemChanged;
	}

	/// <summary>
	/// The rendered page's ToolbarItems as the platforms' app bars show them (tracker S19): MAUI's toolbar computes
	/// them (sorted by Priority, with the Shell's and a flyout's items) when it describes this page, the top of the
	/// root stack; otherwise (a modal page, no toolbar, a toolbar that has not followed a navigation yet) the page's own,
	/// sorted the same way.
	/// </summary>
	internal IReadOnlyList<ToolbarItem> ToolbarItemsOf(Page page)
	{
		var own = page.ToolbarItems;
		if (ToolbarOf(page) is { ToolbarItems: { } fromToolbar })
		{
			var items = fromToolbar.ToList();
			// A toolbar still describing the previous page lacks this page's items: not used until it follows.
			if (own.All(items.Contains))
				return items;
		}
		return own.Count < 2 ? own.ToList() : own.OrderBy(i => i.Priority).ToList();   // stable: equal priorities keep order
	}

	/// <summary>MAUI's toolbar when it describes <paramref name="page"/> (the root stack's top, no modal over it).</summary>
	private Toolbar? ToolbarOf(Page page)
	{
		// A NavigationPage under the window puts it on the window; a Shell (and a NavigationPage under a page) on that page.
		if (((_window as IToolbarElement).Toolbar ?? (RootPage() as IToolbarElement)?.Toolbar) is not Toolbar toolbar ||
		    ResolveModalStack() is { Count: > 0 })
			return null;
		// The window handler attaches it (MapToolbar); a window without one (a test host) gets it here.
		if (toolbar.Handler is null)
			Handlers.SailfishHandlersFactory.AttachToolbarHandler(toolbar, _mauiContext);
		var stack = ResolveRootStack().Pages;
		return stack.Count > 0 && ReferenceEquals(stack[^1], page) ? toolbar : null;
	}

	private void OnToolbarItemsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) =>
		RequestPoll();

	private void OnToolbarItemChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(ToolbarItem.Text) or nameof(MenuItem.IsEnabled) or nameof(ToolbarItem.Order)
		    or nameof(ToolbarItem.Priority))
			RequestPoll();
	}

	/// <summary>Text of the pull-down entry that refreshes a RefreshView the pulley shadows.</summary>
	internal const string PulleyRefreshText = "Refresh";

	/// <summary>The RefreshView refreshed from the pulley entry (null when the page arms the pull gesture itself).</summary>
	private RefreshView? _pulleyRefresh;

	private void WatchPulleyRefresh(RefreshView? refresh)
	{
		if (ReferenceEquals(refresh, _pulleyRefresh))
			return;
		if (_pulleyRefresh is not null)
			_pulleyRefresh.PropertyChanged -= OnPulleyRefreshChanged;
		_pulleyRefresh = refresh;
		if (refresh is not null)
			refresh.PropertyChanged += OnPulleyRefreshChanged;
	}

	// IsRefreshing pulses the pulley bar; IsRefreshEnabled enables the entry.
	private void OnPulleyRefreshChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(RefreshView.IsRefreshing) or nameof(RefreshView.IsRefreshEnabled))
			RequestPoll();
	}

	/// <summary>What the pull gesture does on an armed RefreshView: IsRefreshing on, which raises Refreshing and runs
	/// the Command.</summary>
	private static void StartPulleyRefresh(RefreshView refresh)
	{
		if (refresh.IsRefreshEnabled && !refresh.IsRefreshing)
			refresh.IsRefreshing = true;
	}

	/// <summary>Pull-down menu entries (flyout + primary ToolbarItems), index-aligned with the adapter's MenuItems.</summary>
	private readonly List<(string Text, bool Enabled, Action Activate)> _pullEntries = new();

	/// <summary>The page carries a Silica pulley: the page flickable must stay interactive and pull-to-refresh yields.</summary>
	internal bool PageHasPulley => _pullHost is not null || _pushHost is not null;

	/// <summary>The flyout as pulley entries, as the root container handler offers them (the Shell's flyout items, a
	/// FlyoutPage's "present" entry); none while a modal is open.</summary>
	private IEnumerable<(string Text, bool Enabled, Action Activate)> FlyoutEntries(Page page)
	{
		if (ResolveModalStack() is { Count: > 0 } || Handlers.SailfishPageContainers.Of(RootPage()) is not { } root)
			return Enumerable.Empty<(string, bool, Action)>();
		return root.FlyoutMenu(page);
	}

	private string _renderedTabs = string.Empty;
	private Action<int>? _tabSelect;
	private Action<int>? _subTabSelect;

	/// <summary>A tap in the page's tab bar (level 0) or its section bar (level 1) selects that MAUI tab.</summary>
	internal void SelectTab(int level, int index)
	{
		if (level == 1)
			_subTabSelect?.Invoke(index);
		else
			_tabSelect?.Invoke(index);
	}

	/// <summary>The cancel text an action sheet dismissed without a pick completes with.</summary>
	internal string SheetCancel => _sheetCancel;
	private int _tabIndex;
	private int _tabCount;

	/// <summary>The page shows a tab bar (the input router arms tab swipes only then).</summary>
	internal bool HasTabBar => _tabSelect is not null && _tabCount > 1;

	/// <summary>Tabs a row shows side by side; with more, MauiModelPage.qml's row flicks sideways.</summary>
	internal const int TabRowFits = 4;

	private int _subTabCount;

	/// <summary>A tab row is wider than the page: a horizontal drag on it scrolls the row, not the tab.</summary>
	internal bool TabRowScrolls => _tabCount > TabRowFits || _subTabCount > TabRowFits;

	/// <summary>Bottom of the page chrome (status area, header, tab rows) in root dp, as the page last reported it.</summary>
	internal double ChromeBottomDp => _lastHeader < 0 ? 0 : QtHostUnits.ToLogical(Math.Max(0, _lastStatus) + _lastHeader);

	internal double WindowWidthDp => _windowDp.Width;

	/// <summary>A page swipe → the neighbouring tab (clamped).</summary>
	internal void SwipeTab(int delta)
	{
		if (!HasTabBar)
			return;
		var target = Math.Clamp(_tabIndex + delta, 0, _tabCount - 1);
		if (target == _tabIndex)
			return;
		QtHostDiag.Trace(QtHostDiagChannel.Navigation, $"tab swipe {delta:+#;-#} → tab {target}");
		_tabSelect!(target);
	}

	/// <summary>Whether a tab lies <paramref name="delta"/> tabs away from the shown one.</summary>
	internal bool HasTabAt(int delta) => HasTabBar && _tabIndex + delta >= 0 && _tabIndex + delta < _tabCount;

	/// <summary>A tab swipe in progress: the page follows the finger (<paramref name="dxDp"/>), with resistance where
	/// no tab lies in that direction. Qt thread.</summary>
	internal void SetTabDrag(double dxDp)
	{
		if (!HasTabBar)
			return;
		var shown = HasTabAt(dxDp < 0 ? 1 : -1) ? dxDp : dxDp / 3;
		CallPage(null, "mauiSetTabDrag", BridgeValue.Number(QtHostUnits.ToQtUnits(shown)));
	}

	/// <summary>A view's captured drag (tracker S60): while held, the page's back swipe and its flickable (the pulley)
	/// leave the drag to the view. Qt thread.</summary>
	internal void HoldPageDrag(bool on) => CallPage(null, "mauiHoldDrag", on ? "true" : "false");

	/// <summary>Ends a tab swipe: slides on to the tab <paramref name="delta"/> away ("tab-swipe-commit" switches it
	/// once the page is out), or back when 0 or no tab lies there.</summary>
	internal void EndTabDrag(int delta)
	{
		if (!HasTabBar)
			return;
		if (delta != 0 && !HasTabAt(delta))
			delta = 0;
		CallPage(null, "mauiEndTabDrag", delta.ToString(CultureInfo.InvariantCulture));
	}

	/// <summary>Tabs of the rendered page, as the root container handler offers them (a Shell item's sections or a
	/// section's contents, a TabbedPage's children). Shown on the stack's root page only (Sailfish idiom).</summary>
	private Handlers.SailfishTabRow? ResolveTabs()
	{
		if (ResolveModalStack() is { Count: > 0 } || ResolveRootStack().Pages.Count != 1)
			return null;
		return Handlers.SailfishPageContainers.Of(RootPage())?.Tabs;
	}

	/// <summary>The second tab row (a Shell section's contents), on the same pages as <see cref="ResolveTabs"/>.</summary>
	private Handlers.SailfishTabRow? ResolveSubTabs()
	{
		if (ResolveModalStack() is { Count: > 0 } || ResolveRootStack().Pages.Count != 1)
			return null;
		return Handlers.SailfishPageContainers.Of(RootPage())?.SubTabs;
	}

	/// <summary><c>,"badges":[{text,bg,fg}|null,…]</c> for a row where some tab has a badge; empty otherwise, so a row
	/// without badges sends the JSON it sent before badges existed.</summary>
	internal static string TabBadgesJson(Handlers.SailfishTabRow row)
	{
		if (row.Badges is not { } badges)
			return string.Empty;
		var list = new List<object?>(badges.Count);
		foreach (var badge in badges)
			list.Add(badge is { } b
				? new Dictionary<string, object?> { ["text"] = b.Text, ["bg"] = b.Color, ["fg"] = b.TextColor }
				: null);
		return ",\"badges\":" + BridgeValue.Serialize(list);
	}

	/// <summary>The page shows the Shell flyout as its pulley.</summary>
	internal bool HasFlyoutPulley(Page page) => FlyoutEntries(page).Any();

	private static string MenuEntriesJson(IReadOnlyList<(string Text, bool Enabled, Action Activate)> entries)
	{
		var sb = new StringBuilder("[");
		for (var i = 0; i < entries.Count; i++)
		{
			if (i > 0)
				sb.Append(',');
			sb.Append("{\"text\":").Append(BridgeValue.Quote(entries[i].Text))
			  .Append(",\"enabled\":").Append(entries[i].Enabled ? "true" : "false").Append('}');
		}
		return sb.Append(']').ToString();
	}

	private static string MenuItemsJson(IReadOnlyList<ToolbarItem> items)
	{
		var sb = new StringBuilder("[");
		for (var i = 0; i < items.Count; i++)
		{
			if (i > 0)
				sb.Append(',');
			sb.Append("{\"text\":").Append(BridgeValue.Quote(ToolbarText(items[i])))
			  .Append(",\"enabled\":").Append(items[i].IsEnabled ? "true" : "false").Append('}');
		}
		return sb.Append(']').ToString();
	}

	private static readonly ConditionalWeakTable<ToolbarItem, object> IconOnlyWarned = new();

	/// <summary>A pulley entry's text. Silica pulley menus are text only, so an icon-only ToolbarItem (Text empty) uses
	/// its AutomationId, its SemanticProperties.Description, or its icon file's name; with none of them it stays blank
	/// and one warning names it.</summary>
	internal static string ToolbarText(ToolbarItem item)
	{
		if (!string.IsNullOrEmpty(item.Text))
			return item.Text;
		var fallback = !string.IsNullOrEmpty(item.AutomationId) ? item.AutomationId
			: SemanticProperties.GetDescription(item) is { Length: > 0 } description ? description
			: item.IconImageSource is FileImageSource { File: { Length: > 0 } file } ? System.IO.Path.GetFileNameWithoutExtension(file)
			: null;
		if (fallback is null && !IconOnlyWarned.TryGetValue(item, out _))
		{
			IconOnlyWarned.Add(item, IconOnlyWarned);
			QtHostDiag.Warn(QtHostDiagChannel.Navigation,
				"a ToolbarItem has no Text, AutomationId or Description: its pulley entry is blank (Silica pulleys show text only)");
		}
		return fallback ?? string.Empty;
	}

	/// <summary>The rows a ContextFlyout opens with: a MenuFlyoutSubItem becomes a label row followed by its own items
	/// (Silica's ContextMenu has no submenus), separators are dropped. Item is null on a label row.</summary>
	internal static List<(string Text, bool Enabled, MenuItem? Item)> ContextEntries(MenuFlyout flyout)
	{
		var rows = new List<(string, bool, MenuItem?)>();
		void Add(IEnumerable<IMenuElement> elements)
		{
			foreach (var element in elements)
			{
				switch (element)
				{
					case MenuFlyoutSeparator:
						break;
					case MenuFlyoutSubItem sub:
						rows.Add((sub.Text ?? string.Empty, false, null));
						Add(sub);
						break;
					case MenuItem item:
						rows.Add((item.Text ?? string.Empty, item.IsEnabled, item));
						break;
				}
			}
		}
		Add(flyout);
		return rows;
	}

	private static string ContextItemsJson(IReadOnlyList<(string Text, bool Enabled, MenuItem? Item)> rows)
	{
		var sb = new StringBuilder("[");
		for (var i = 0; i < rows.Count; i++)
		{
			if (i > 0)
				sb.Append(',');
			sb.Append("{\"text\":").Append(BridgeValue.Quote(rows[i].Text))
			  .Append(",\"enabled\":").Append(rows[i].Enabled ? "true" : "false");
			if (rows[i].Item is null)
				sb.Append(",\"label\":true");
			sb.Append('}');
		}
		return sb.Append(']').ToString();
	}

	/// <summary>The ContextFlyout of the element or its nearest ancestor (context menus bubble like gestures).
	/// Qt thread, from the router's press path.</summary>
	internal bool TryContextFlyout(VisualElement element, out VisualElement owner, out MenuFlyout flyout)
	{
		for (var v = element; v is not null; v = v.Parent as VisualElement)
		{
			if (_contextFlyouts.TryGetValue(v, out var f))
			{
				owner = v;
				flyout = f;
				return true;
			}
		}
		owner = null!;
		flyout = null!;
		return false;
	}

	/// <summary>The long-press hold fired: open the page's Silica ContextMenu adapter around the target host.</summary>
	internal void FireContextMenu(string hostId, MenuFlyout flyout)
	{
		_openFlyout = flyout;
		_openFlyoutRows = ContextEntries(flyout);
		_shellFlyoutRows = null;
		var rc = QtHostRuntime.Eval(
			$"{TopModelPageJs}.__openContextMenu('{hostId}',{BridgeValue.Quote(ContextItemsJson(_openFlyoutRows))})");
		QtHostDiag.Trace(QtHostDiagChannel.Input, $"long-press → Silica ContextMenu open rc={rc} " +
			$"items={flyout.Count} target={hostId}");
	}

	/// <summary>A ContextMenu is open: native stack steps wait until it closes.</summary>
	internal bool ContextMenuOpen => AnyMenuOpen;

	/// <summary>The ContextMenu closed (picked or dismissed): navigation, held while it was open, may follow the native
	/// stack again. The rows stay for the pick, which Silica may deliver after the menu closed.</summary>
	internal void ApplyContextClosed()
	{
		if (CloseShellFlyoutMenu())
			return;
		if (_openFlyout is null)
			return;
		_openFlyout = null;
		QtHostDiag.Trace(QtHostDiagChannel.Input, "ContextMenu closed");
		RequestPoll();
	}

	/// <summary>ContextMenu item pick → the MAUI MenuFlyoutItem.</summary>
	internal void ApplyContextActivated(string payload)
	{
		using var doc = JsonDocument.Parse(payload);
		var index = BridgeJson.Int(doc.RootElement, "index");
		if (ActivateShellFlyoutRow(index))
			return;
		var rows = _openFlyoutRows;
		if (rows is null || index < 0 || index >= rows.Count)
		{
			QtHostDiag.Warn(QtHostDiagChannel.Input, $"context-activated index={index} with no open flyout");
			return;
		}
		ContextMenuActivations++;
		var picked = rows[index].Item;
		QtHostDiag.Trace(QtHostDiagChannel.Input, $"ContextMenu item {index} ('{picked?.Text}') → MAUI MenuFlyoutItem");
		if (picked is not null)
			FireMenuItem(picked);
	}

	/// <summary>Pulley-menu pick → the matching entry (pull = flyout + primary items, push = Secondary items).</summary>
	internal void ApplyToolbarActivated(string payload)
	{
		using var doc = JsonDocument.Parse(payload);
		var root = doc.RootElement;
		var menu = root.TryGetProperty("menu", out var mEl) ? mEl.GetString() : null;
		var index = BridgeJson.Int(root, "index");
		if (ResolveCurrentPage() is not { } page)
			return;
		if (menu != "push")
		{
			if (index < 0 || index >= _pullEntries.Count)
			{
				QtHostDiag.Warn(QtHostDiagChannel.Input, $"toolbar-activated menu=pull index={index} out of range ({_pullEntries.Count})");
				return;
			}
			ToolbarActivations++;
			QtHostDiag.Trace(QtHostDiagChannel.Input, $"pull menu item {index} ('{_pullEntries[index].Text}') → MAUI");
			_pullEntries[index].Activate();
			return;
		}
		var items = ToolbarItemsOf(page)
			.Where(t => t.Order == ToolbarItemOrder.Secondary)
			.ToList();
		if (index < 0 || index >= items.Count)
		{
			QtHostDiag.Warn(QtHostDiagChannel.Input, $"toolbar-activated menu={menu} index={index} out of range ({items.Count})");
			return;
		}
		ToolbarActivations++;
		QtHostDiag.Trace(QtHostDiagChannel.Input, $"{menu} menu item {index} ('{items[index].Text}') → MAUI ToolbarItem");
		FireMenuItem(items[index]);
	}

	/// <summary>Fires a MenuItem like the in-box platforms (Activate → Clicked + Command; the public Clicked
	/// event can't be raised from a backend).</summary>
	private static void FireMenuItem(MenuItem item) =>
		((IMenuItemController)item).Activate();

	/// <summary>Opens the AlertDialog adapter over the page (a system-dialog panel) and awaits accept/reject; the
	/// dialog closes itself. A null accept means a single-button alert (its one button accepts).</summary>
	public async Task<bool> PushAlertAsync(string title, string message, string? accept, string cancel) =>
		await PushDialogCore("alert-dialog", new Dictionary<string, object?>
		{
			["mauiId"] = "dialog",
			["mauiTitle"] = title,
			["mauiMessage"] = message,
			["mauiAccept"] = accept ?? string.Empty,
			["mauiCancel"] = cancel,
		}, "Alert") is true;

	/// <summary>DisplayPromptAsync: opens the PromptDialog adapter (Silica TextField; Maliit follows focus).
	/// Returns the entered text, or null when dismissed.</summary>
	public async Task<string?> PushPromptAsync(string title, string message, string accept, string cancel,
		string placeholder, string initialValue, int maxLength, bool numeric, int hints = 0) =>
		(string?)await PushDialogCore("prompt-dialog", new Dictionary<string, object?>
		{
			["mauiId"] = "dialog",
			["mauiTitle"] = title,
			["mauiMessage"] = message,
			["mauiAccept"] = accept,
			["mauiCancel"] = cancel,
			["mauiPlaceholder"] = placeholder,
			["mauiInitial"] = initialValue,
			["mauiMaxLength"] = maxLength,
			["mauiNumeric"] = numeric,
			["mauiHints"] = hints,
		}, "Prompt");

	/// <summary>DisplayActionSheetAsync: opens the ActionSheet adapter. Returns the picked entry text, or the
	/// cancel text when dismissed.</summary>
	public async Task<string> PushActionSheetAsync(string title, string? cancel, string? destruction,
		IReadOnlyList<string> buttons)
	{
		_sheetCancel = cancel ?? string.Empty;
		var result = (string?)await PushDialogCore("action-sheet", new Dictionary<string, object?>
		{
			["mauiId"] = "dialog",
			["mauiTitle"] = title,
			["mauiCancel"] = _sheetCancel,
			["mauiDestruction"] = destruction ?? string.Empty,
			["mauiActions"] = buttons,
		}, "ActionSheet");
		return result ?? _sheetCancel;
	}

	private readonly Queue<(string Uri, Dictionary<string, object?> Props, string Label, TaskCompletionSource<object?> Tcs)> _dialogQueue = new();

	/// <summary>Shared dialog open: one dialog at a time; a dialog asked for while one is open waits its turn and opens
	/// when it closes, as the platforms stack them (tracker S38; it used to complete at once with the negative result).
	/// The navigation sync waits while one is open. Push failures complete with null.</summary>
	private Task<object?> PushDialogCore(string uri, Dictionary<string, object?> props, string label)
	{
		var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
		// RTL pages mirror the dialog's buttons and text, as the platforms' dialogs follow the layout direction.
		props["mauiMirrored"] = QtHostVisualState.IsRightToLeft(ResolveCurrentPage());
		if (_dialogTcs is not null)
		{
			_dialogQueue.Enqueue((uri, props, label, tcs));
			QtHostDiag.Trace(QtHostDiagChannel.QmlSignal, $"{label} dialog queued behind the open one ({_dialogQueue.Count} waiting)");
			return tcs.Task;
		}
		OpenDialog(uri, props, label, tcs);
		return tcs.Task;
	}

	private void OpenDialog(string uri, Dictionary<string, object?> props, string label, TaskCompletionSource<object?> tcs)
	{
		if (!QtHostAdapters.TryGetSrc(uri, out var src))
		{
			QtHostDiag.Error(QtHostDiagChannel.QmlLoad, $"adapter '{uri}' missing — {label} dialog cancelled");
			tcs.SetResult(null);
			OpenNextDialog();
			return;
		}
		_dialogTcs = tcs;
		DialogPushes++;
		var json = BridgeValue.Serialize(props);
		var rc = QtHostRuntime.Eval($"{TopModelPageJs}.__pushDialog('{src}',{BridgeValue.Quote(json)})");
		QtHostDiag.Trace(QtHostDiagChannel.QmlSignal, $"{label} dialog open rc={rc}");
		if (rc != "ok")
		{
			_dialogTcs = null;
			tcs.SetResult(null);
			OpenNextDialog();
		}
	}

	private void OpenNextDialog()
	{
		if (_dialogTcs is null && _dialogQueue.TryDequeue(out var next))
			OpenDialog(next.Uri, next.Props, next.Label, next.Tcs);
	}

	internal void CompleteDialog(object? result)
	{
		var tcs = _dialogTcs;
		_dialogTcs = null;
		DialogResults++;
		QtHostDiag.Trace(QtHostDiagChannel.QmlSignal, $"Dialog result={result?.ToString() ?? "<null>"}");
		tcs?.TrySetResult(result);
		OpenNextDialog();   // the closed panel already left the page (its event comes after __dialog was cleared)
	}

	/// <summary>Extracts "text" from a dialog payload; missing text becomes "".</summary>
	internal static string ParseDialogText(string payload)
	{
		try
		{
			using var doc = JsonDocument.Parse(payload);
			return doc.RootElement.TryGetProperty("text", out var text) ? text.GetString() ?? string.Empty : string.Empty;
		}
		catch (Exception ex)
		{
			QtHostDiag.Warn(QtHostDiagChannel.QmlSignal, $"dialog payload parse failed: {ex.Message}");
			return string.Empty;
		}
	}

	/// <summary>Routes a DockedPanel open write-back ({id,open}) to managed listeners (SailfishBottomSheet).</summary>
	internal void RoutePanelOpenChanged(string payload)
	{
		if (PanelOpenChanged is null)
			return;
		try
		{
			using var doc = JsonDocument.Parse(payload);
			var root = doc.RootElement;
			var id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
			var open = root.TryGetProperty("open", out var openEl) && openEl.ValueKind == JsonValueKind.True;
			if (id is not null)
				PanelOpenChanged(id, open);
		}
		catch (Exception ex)
		{
			QtHostDiag.Warn(QtHostDiagChannel.QmlSignal, $"panel-open-changed payload parse failed: {ex.Message}");
		}
	}

	/// <summary>Creates a page-level interaction host (docked panel / drawer) outside the MAUI tree, picked up
	/// by the next reconcile. Returns the host id.</summary>
	public string AddInteractionHost(string uri, Dictionary<string, object?> props)
	{
		var page = ResolveCurrentPage() ?? _window.Page
			?? throw new InvalidOperationException("AddInteractionHost before any MAUI page");
		var id = $"{SyntheticPrefix}{uri}-{++_interactionSeq}";   // monotonic: a removed id is never reused
		_interactionUris[id] = uri;
		_interactionHosts[id] = new NativeElementHost(id, uri, page);
		_interactionProps[id] = props;
		RequestPoll();   // page-level surface: the page reconcile creates it on the next loop turn
		return id;
	}

	/// <summary>Replaces an interaction host's property snapshot (applied on the next loop turn).</summary>
	public void UpdateInteractionHost(string id, Dictionary<string, object?> props)
	{
		_interactionProps[id] = props;
		RequestPoll();
	}

	/// <summary>Removes an interaction host (destroyed on the next loop turn).</summary>
	public void RemoveInteractionHost(string id)
	{
		_interactionUris.Remove(id);
		_interactionHosts.Remove(id);
		_interactionProps.Remove(id);
		RequestPoll();
	}
}
