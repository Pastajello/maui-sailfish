using System.ComponentModel;
using System.Globalization;
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
		foreach (var item in page.ToolbarItems)
			(item.Order == ToolbarItemOrder.Secondary ? push : pull).Add(item);
		// Pull-down menu = Shell flyout entries (top) + primary ToolbarItems (nearest the content).
		_pullEntries.Clear();
		foreach (var flyout in FlyoutEntries(page))
			_pullEntries.Add(flyout);
		foreach (var item in pull)
			_pullEntries.Add((item.Text ?? string.Empty, item.IsEnabled, () => FireMenuItem(item)));

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
			_pullHost ??= new NativeElementHost("synth-pulldown", "pull-down-menu", page);
			props[_pullHost] = new Dictionary<string, object?> { ["mauiItems"] = MenuEntriesJson(_pullEntries) };
			desired.Add(_pullHost);
		}
		if (push.Count > 0)
		{
			if (_pushHost is null) allocated.Add("push-up-menu");
			_pushHost ??= new NativeElementHost("synth-pushup", "push-up-menu", page);
			props[_pushHost] = new Dictionary<string, object?> { ["mauiItems"] = MenuItemsJson(push) };
			desired.Add(_pushHost);
		}
		if (_contextFlyouts.Count > 0)
		{
			if (_ctxMenuHost is null) allocated.Add("context-menu");
			_ctxMenuHost ??= new NativeElementHost("synth-ctxmenu", "context-menu", page);
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
		foreach (var item in page.ToolbarItems)
			if (_toolbarWatched.Add(item))
				item.PropertyChanged += OnToolbarItemChanged;
	}

	private void OnToolbarItemsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) =>
		RequestPoll();

	private void OnToolbarItemChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(ToolbarItem.Text) or nameof(MenuItem.IsEnabled) or nameof(ToolbarItem.Order)
		    or nameof(ToolbarItem.Priority))
			RequestPoll();
	}

	/// <summary>Pull-down menu entries (flyout + primary ToolbarItems), index-aligned with the adapter's MenuItems.</summary>
	private readonly List<(string Text, bool Enabled, Action Activate)> _pullEntries = new();

	/// <summary>The page carries a Silica pulley: the page flickable must stay interactive and pull-to-refresh yields.</summary>
	internal bool PageHasPulley => _pullHost is not null || _pushHost is not null;

	/// <summary>The flyout as pulley entries, as the root container handler offers them (the Shell's flyout items, a
	/// FlyoutPage's "present" entry); none while a modal is open.</summary>
	private IEnumerable<(string Text, bool Enabled, Action Activate)> FlyoutEntries(Page page)
	{
		if (ResolveModalStack() is { Count: > 0 } || Handlers.SailfishPageContainers.Of(RootPage(), _mauiContext) is not { } root)
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
		QtHostRuntime.Eval(QmlPage.Call(TopModelPageJs, "mauiSetTabDrag", BridgeValue.Number(QtHostUnits.ToQtUnits(shown))));
	}

	/// <summary>Ends a tab swipe: slides on to the tab <paramref name="delta"/> away ("tab-swipe-commit" switches it
	/// once the page is out), or back when 0 or no tab lies there.</summary>
	internal void EndTabDrag(int delta)
	{
		if (!HasTabBar)
			return;
		if (delta != 0 && !HasTabAt(delta))
			delta = 0;
		QtHostRuntime.Eval(QmlPage.Call(TopModelPageJs, "mauiEndTabDrag", delta.ToString(CultureInfo.InvariantCulture)));
	}

	/// <summary>Tabs of the rendered page, as the root container handler offers them (a Shell item's sections or a
	/// section's contents, a TabbedPage's children). Shown on the stack's root page only (Sailfish idiom).</summary>
	private (List<string> Titles, int Index, Action<int> Select)? ResolveTabs()
	{
		if (ResolveModalStack() is { Count: > 0 } || ResolveRootStack().Pages.Count != 1)
			return null;
		return Handlers.SailfishPageContainers.Of(RootPage(), _mauiContext)?.Tabs;
	}

	/// <summary>The second tab row (a Shell section's contents), on the same pages as <see cref="ResolveTabs"/>.</summary>
	private (List<string> Titles, int Index, Action<int> Select)? ResolveSubTabs()
	{
		if (ResolveModalStack() is { Count: > 0 } || ResolveRootStack().Pages.Count != 1)
			return null;
		return Handlers.SailfishPageContainers.Of(RootPage(), _mauiContext)?.SubTabs;
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
			sb.Append("{\"text\":").Append(BridgeValue.Quote(items[i].Text ?? string.Empty))
			  .Append(",\"enabled\":").Append(items[i].IsEnabled ? "true" : "false").Append('}');
		}
		return sb.Append(']').ToString();
	}

	private static string MenuItemsJson(MenuFlyout flyout)
	{
		var sb = new StringBuilder("[");
		for (var i = 0; i < flyout.Count; i++)
		{
			if (i > 0)
				sb.Append(',');
			var item = flyout[i] as MenuItem;
			sb.Append("{\"text\":").Append(BridgeValue.Quote(item?.Text ?? string.Empty))
			  .Append(",\"enabled\":").Append(item?.IsEnabled ?? true ? "true" : "false").Append('}');
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
		var rc = QtHostRuntime.Eval(
			$"{QmlPage.Model}.__openContextMenu('{hostId}',{BridgeValue.Quote(MenuItemsJson(flyout))})");
		QtHostDiag.Trace(QtHostDiagChannel.Input, $"long-press → Silica ContextMenu open rc={rc} " +
			$"items={flyout.Count} target={hostId}");
	}

	/// <summary>ContextMenu item pick → the MAUI MenuFlyoutItem.</summary>
	internal void ApplyContextActivated(string payload)
	{
		using var doc = JsonDocument.Parse(payload);
		var index = BridgeJson.Int(doc.RootElement, "index");
		var flyout = _openFlyout;
		if (flyout is null || index < 0 || index >= flyout.Count)
		{
			QtHostDiag.Warn(QtHostDiagChannel.Input, $"context-activated index={index} with no open flyout");
			return;
		}
		ContextMenuActivations++;
		var picked = flyout[index] as MenuItem;
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
		var items = page.ToolbarItems
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
		string placeholder, string initialValue, int maxLength, bool numeric) =>
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

	/// <summary>Shared dialog open: one dialog at a time (the busy gate and navigation sync wait while one is
	/// pending). Push failures complete with null.</summary>
	private Task<object?> PushDialogCore(string uri, object props, string label)
	{
		var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
		if (!QtHostAdapters.TryGetSrc(uri, out var src))
		{
			QtHostDiag.Error(QtHostDiagChannel.QmlLoad, $"adapter '{uri}' missing — {label} dialog cancelled");
			tcs.SetResult(null);
			return tcs.Task;
		}
		_dialogTcs = tcs;
		DialogPushes++;
		var json = BridgeValue.Serialize(props);
		var rc = QtHostRuntime.Eval($"{QmlPage.Model}.__pushDialog('{src}',{BridgeValue.Quote(json)})");
		QtHostDiag.Trace(QtHostDiagChannel.QmlSignal, $"{label} dialog open rc={rc}");
		if (rc != "ok")
		{
			_dialogTcs = null;
			tcs.SetResult(null);
		}
		return tcs.Task;
	}

	internal void CompleteDialog(object? result)
	{
		var tcs = _dialogTcs;
		_dialogTcs = null;
		DialogResults++;
		QtHostDiag.Trace(QtHostDiagChannel.QmlSignal, $"Dialog result={result?.ToString() ?? "<null>"}");
		tcs?.TrySetResult(result);
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
		var id = $"synth-{uri}-{++_interactionSeq}";   // monotonic: a removed id is never reused
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
