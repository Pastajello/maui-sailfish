using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>Host lifecycle: the create op of a host, attaching its native object (and what a fresh object must be told again), property pushes and their failures, releasing and destroying hosts, adopting pooled ones, healing dead handles, and the event routes.</summary>
internal sealed partial class QtHostPageRenderer
{
	/// <summary>
	/// Focus decided by Qt, as requestFocus is by the Android view: pushes the focus request to a text adapter and reads
	/// its activeFocus back (a disabled or hidden field refuses). Null when the host is not native yet: the request is
	/// then kept on the host and replayed when its object attaches (<see cref="ReplayPendingFocus"/>). Qt thread.
	/// </summary>
	internal bool? FocusHost(NativeElementHost host, bool focus)
	{
		if (!QtHostRuntime.IsQtThread)
			return null;
		if (!host.IsAttached)
		{
			host.PendingFocus = focus;
			return null;
		}
		host.PendingFocus = null;
		bool Active() => QtHostRuntime.GetProperty(host.NativeHandle, "activeFocus") == "true";
		var json = BridgeValue.Serialize(focus);
		if (Active() == focus)
		{
			host.AppliedProperties["mauiFocus"] = json;   // already there (e.g. the native focus-changed follow-up)
			return true;
		}
		// The adapter acts on a change: re-arm a stale "true" first.
		if (focus && host.IsApplied("mauiFocus", json))
			PushBatch(host, new[] { ("mauiFocus", BridgeValue.Serialize(false)) });
		PushBatch(host, new[] { ("mauiFocus", json) });
		var granted = Active() == focus;
		if (focus && !granted)
			PushBatch(host, new[] { ("mauiFocus", BridgeValue.Serialize(false)) });   // refused: stay consistent
		return granted;
	}

	/// <summary>
	/// Pushes transient native state from a handler mapper (focus; caret and selection as one atomic pair, so the
	/// managed set order cannot leave a wrong selection natively). Skipped while native state is written back into
	/// MAUI, and when native already holds every value.
	/// </summary>
	internal void PushTransient(NativeElementHost host, IReadOnlyList<(string Name, object? Value)> values)
	{
		void Apply()
		{
			if (_suppressPush > 0 || !host.IsAttached)
				return;
			var batch = new (string Name, string ValueJson)[values.Count];
			var changed = false;
			for (var i = 0; i < values.Count; i++)
			{
				batch[i] = (values[i].Name, BridgeValue.Serialize(values[i].Value));
				changed |= !host.IsApplied(batch[i].Name, batch[i].ValueJson);
			}
			if (changed && PushBatch(host, batch))
				_handlerPropertyPushes += batch.Length;
		}
		QtHostRuntime.RunOnQtThread(Apply);
	}

	/// <summary>Sends one ordered, suppressed batch through the shim and records the applied state; failures are
	/// logged once per host/context. False when the shim rejected any of it (nothing is then recorded as applied).</summary>
	internal bool PushBatch(NativeElementHost host, IReadOnlyList<(string Name, string ValueJson)> changed)
	{
		var batch = QtHostBridge.BuildBatch(changed, suppress: true);
		var failed = QtHostRuntime.ApplyProperties(host.NativeHandle, batch);
		if (failed < 0)
		{
			BridgeFailed++;
			LogBridgeFailure(host, "batch", failed);
			return false;
		}
		if (failed > 0)
		{
			// The shim reports only the count, not which properties it rejected: record nothing as applied so the
			// whole batch is diffed again on the next push (as FlushGeometry does for a partial geometry failure).
			BridgeFailed += failed;
			LogBridgeFailure(host, "batch", failed);
			HealIfDead(host);   // dead handle → recreate
			return false;       // not pushed: callers must not count it (W1.7)
		}
		foreach (var (name, json) in changed)
			host.AppliedProperties[name] = json;
		BridgeApplied += changed.Count;
		return true;
	}

	private void LogBridgeFailure(NativeElementHost host, string what, int rc)
	{
		if (!_bridgeFailLogged.Add($"{host.Id}:{what}:{rc}"))
			return;
		QtHostDiag.Error(QtHostDiagChannel.QmlProperty, $"apply failed on {host} ({what}) rc={rc}: {QtHostRuntime.LastErrorText}");
	}

	/// <summary>
	/// Property-push entry point for MAUI handler mappers: the same diffed apply plus a Qt-thread hop, since
	/// Handler.UpdateValue runs on whichever thread wrote the property.
	/// </summary>
	/// <param name="yieldToNative">Skip while native state is written back into MAUI (the value came from native).</param>
	internal void PushHostProps(NativeElementHost host, Dictionary<string, object?> want, bool yieldToNative = false)
	{
		// Trace distinguishes the mapper path from the reconcile diff.
		void Apply()
		{
			if (yieldToNative && _suppressPush > 0)
			{
				// A native event is being written back: the reconcile diff pushes this on the next loop turn. Left to
				// the heartbeat, WhatToEat's Save stayed disabled up to 2 s after the name was typed (Entry.Text →
				// the view model → CanExecute → IsEnabled, all inside the write-back).
				if (!_inLayoutPass)
					RequestPoll();
				return;
			}
			if (!host.IsAttached && host.Element is VisualElement flat && IsFlattened(flat))
			{
				OnFlattenedPush(flat);   // a flattened row layout (no host) may need one now
				return;
			}
			_handlerSnapshots++;
			host.HandlerSnapshots++;
			var changed = ApplyUpdates(host, want);
			_handlerPropertyPushes += changed;
			if (changed > 0)
				QtHostDiag.Trace(QtHostDiagChannel.QmlProperty, $"handler push {host} changed={changed}");
		}
		QtHostRuntime.RunOnQtThread(Apply);
	}

	/// <summary>Applies one host's property diff in place as a single typed, suppressed batch; returns the
	/// number of changed properties pushed.</summary>
	/// <param name="traceAs">Traces the pushed names under this source (the reconcile diff should push none).</param>
	private int ApplyUpdates(NativeElementHost host, Dictionary<string, object?> want, string? traceAs = null)
	{
		if (!host.IsAttached)
			return 0;
		List<(string Name, string ValueJson)>? changed = null;
		foreach (var kv in want)
		{
			// The diff basis is the serialized bridge JSON.
			var json = BridgeValue.Serialize(kv.Value);
			if (host.IsApplied(kv.Key, json))
				continue;
			(changed ??= new List<(string, string)>()).Add((kv.Key, json));
		}
		if (changed is null)
			return 0;
		if (traceAs is not null && QtHostDiag.TraceEnabled)
			QtHostDiag.Trace(QtHostDiagChannel.QmlProperty, $"{traceAs} push {host} {host.Element.GetType().Name}: {string.Join(",", changed.Select(c => c.Name))}");
		return PushBatch(host, changed) ? changed.Count : 0;
	}

	private static Dictionary<string, object?> CreateOp(NativeElementHost host, Dictionary<string, object?> props,
	                                                   string parentId = "")
	{
		// Colors ride the op JSON as "#AARRGGBB": the QML create init assigns props directly to color-typed
		// properties, and a serialized Color object would fail that.
		Dictionary<string, object?>? normalized = null;
		foreach (var kv in props)
		{
			if (kv.Value is Color color)
			{
				normalized ??= new Dictionary<string, object?>(props);
				normalized[kv.Key] = BridgeValue.ColorString(color);
			}
		}
		var op = new Dictionary<string, object?>
		{
			["op"] = "create",
			["id"] = host.Id,
			["uri"] = host.QmlUri,
			["props"] = normalized ?? new Dictionary<string, object?>(props),
		};
		// The host of the nearest hosted MAUI ancestor ("" = page canvas).
		if (parentId.Length > 0)
			op["parent"] = parentId;
		// QML instantiates the adapter file resolved from qml/adapters.json.
		if (QtHostAdapters.TryGetSrc(host.QmlUri, out var src))
			op["src"] = src;
		else
			QtHostDiag.Warn(QtHostDiagChannel.QmlLoad, $"no adapter src for uri '{host.QmlUri}' (fallback component)");
		return op;
	}

	/// <summary>
	/// Resolves the native handle of a freshly created host (objectName "maui_&lt;Id&gt;") and seeds the applied state.
	/// </summary>
	private static void AttachNative(NativeElementHost host, Dictionary<string, object?> props, long scopeHandle = 0)
	{
		// Collection rows re-create the same host id, and a ListView may keep a stale twin alive in a cached
		// delegate, so the placeholder is searched first (QtHostRuntime.FindScoped).
		host.NativeHandle = QtHostRuntime.FindScoped($"maui_{host.Id}", scopeHandle);
		host.AppliedProperties.Clear();
		host.AppliedGeometrySet = false;   // fresh QML object: geometry must be re-pushed
		host.AppliedVisible = true;
		foreach (var kv in props)
			host.AppliedProperties[kv.Key] = BridgeValue.Serialize(kv.Value);
		if (!host.IsAttached)
			QtHostDiag.Warn(QtHostDiagChannel.QmlObject, $"native handle not resolved for {host}");
		// Create props are plain QML assignments, so the shim's native QFont letter-spacing write never runs and the
		// seeded diff would suppress later pushes. Re-push through set_property (Qt 5.6 QML only has PercentageSpacing).
		if (host.IsAttached &&
		    props.TryGetValue("mauiLetterSpacing", out var spacing) &&
		    spacing is double spacingPx && spacingPx > 0)
			QtHostRuntime.SetProperty(host.NativeHandle, "mauiLetterSpacing",
				BridgeValue.Serialize(spacingPx));
		// Generic view props (background fill, semantics, automation id) are shim-side special cases too; the
		// defaults ("" and false) are what a fresh QML object already has.
		if (host.IsAttached)
			foreach (var key in GenericNativeKeys)
				if (props.TryGetValue(key, out var generic) && generic is not ("" or false))
					QtHostRuntime.SetProperty(host.NativeHandle, key, BridgeValue.Serialize(generic));
		if (host.IsAttached)
		{
			ReplayPendingFocus(host);
			host.RaiseAttached();
		}
	}

	/// <summary>A Focus() that arrived before the object existed: MAUI already holds IsFocused (the request was answered
	/// optimistically), so give native the focus now and take IsFocused back when Qt refuses it.</summary>
	private static void ReplayPendingFocus(NativeElementHost host)
	{
		if (host.PendingFocus is not { } focus)
			return;
		host.PendingFocus = null;
		if (!focus)
			return;   // a fresh object has no focus
		var json = BridgeValue.Serialize(true);
		QtHostRuntime.SetProperty(host.NativeHandle, "mauiFocus", json);
		host.AppliedProperties["mauiFocus"] = json;
		if (QtHostRuntime.GetProperty(host.NativeHandle, "activeFocus") != "true")
		{
			QtHostRuntime.SetProperty(host.NativeHandle, "mauiFocus", BridgeValue.Serialize(false));
			host.AppliedProperties["mauiFocus"] = BridgeValue.Serialize(false);
			if (host.Element is VisualElement visual && visual.IsFocused)
				visual.SetValue(VisualElement.IsFocusedPropertyKey, false);
		}
	}

	/// <summary>
	/// Deterministic destroy: shim deleteLater through the handle (revoking it in the QPointer registry), then
	/// drop the handle and property state.
	/// </summary>
	private void DetachNative(NativeElementHost host)
	{
		if (host.NativeHandle != 0)
			DestroyNative(host.NativeHandle);
		host.ResetNative();
	}

	/// <summary>Destroys a native object, after the transition when a popped page slides out: the shim hides and
	/// unparents a destroyed object at once, and the sliding page must keep painting.</summary>
	private void DestroyNative(long handle)
	{
		if (_deferNativeDestroy)
			_pendingNativeDestroys.Add(handle);
		else
			QtHostRuntime.DestroyObject(handle);
	}

	/// <summary>Native objects whose destroy waits for a popped page's slide-out (diagnostics, tests).</summary>
	internal int DeferredNativeDestroys => _pendingNativeDestroys.Count;

	private void FlushDeferredNativeDestroys()
	{
		if (_pendingNativeDestroys.Count == 0)
			return;
		foreach (var handle in _pendingNativeDestroys)
			QtHostRuntime.DestroyObject(handle);
		_pendingNativeDestroys.Clear();
	}

	/// <summary>
	/// The one release path for hosts (W3.1): with <paramref name="sendOps"/> the destroy ops go to the page instance
	/// that owns them (<paramref name="pageId"/>, null = the top model page), descendants first as the caller lists them;
	/// without, the caller's own batch carries them (the tree diffs) or the page died with its objects. Then for each:
	/// the native object destroyed (after a slide-out when deferred), native state reset, the event route, the live set
	/// and a synthetic slot it held dropped.
	/// </summary>
	internal void ReleaseHosts(IReadOnlyList<NativeElementHost> hosts, string? pageId, bool sendOps)
	{
		if (hosts.Count == 0)
			return;
		if (sendOps)
			ApplyOps(hosts.Select(h => BridgeOps.Destroy(h.Id)).ToList(), pageId);
		foreach (var host in hosts)
		{
			DetachNative(host);
			_byId.Remove(host.Id);
			_current.Remove(host);
			ReleaseSyntheticSlot(host);
		}
	}

	/// <summary>A row host whose QML object moves to the row pool: the managed side forgets it, the object lives on.</summary>
	internal void ReleaseToPool(NativeElementHost host)
	{
		host.ResetNative();
		_byId.Remove(host.Id);
	}

	/// <summary>
	/// A pooled QML object takes <paramref name="host"/>'s place: the old property state seeds the diff, so only what
	/// differs from the previous row is pushed. An image whose source changes is emptied first, so the previous row's
	/// picture never shows while the new one loads (as RecyclerView image loaders clear a recycled view).
	/// </summary>
	internal void AdoptPooledHost(NativeElementHost host, long handle, Dictionary<string, string> applied,
	                              Dictionary<string, object?> props)
	{
		host.NativeHandle = handle;
		host.AppliedProperties.Clear();
		foreach (var kv in applied)
			host.AppliedProperties[kv.Key] = kv.Value;
		host.AppliedGeometrySet = false;   // the new delegate: place it again
		host.AppliedVisible = true;
		if (host.QmlUri == "image" && props.TryGetValue("mauiSource", out var source) &&
		    !host.IsApplied("mauiSource", BridgeValue.Serialize(source)))
			ApplyUpdates(host, new Dictionary<string, object?> { ["mauiSource"] = string.Empty });
		ApplyUpdates(host, props);
		host.RaiseAttached();
	}

	/// <summary>Destroys pooled row hosts by id and handle (they have no managed host any more).</summary>
	internal void DestroyPooledHosts(IReadOnlyList<(string Id, long Handle)> hosts, string? pageId)
	{
		if (hosts.Count == 0)
			return;
		ApplyOps(hosts.Select(h => BridgeOps.Destroy(h.Id)).ToList(), pageId);
		foreach (var (_, handle) in hosts)
			DestroyNative(handle);
	}

	/// <summary>Frees the slot field a synthetic host (pulley, push-up menu, context menu, interaction) sat in.</summary>
	private void ReleaseSyntheticSlot(NativeElementHost host)
	{
		if (ReferenceEquals(host, _pullHost)) _pullHost = null;
		else if (ReferenceEquals(host, _pushHost)) _pushHost = null;
		else if (ReferenceEquals(host, _ctxMenuHost)) _ctxMenuHost = null;
		else _interactionHosts.Remove(host.Id);
	}


	/// <summary>
	/// Self-heal: hosts attached to a model page object that Silica later rebuilt look attached, but every push
	/// dies silently. Probe liveness and drop the attachment so the next reconcile re-creates the host (rows
	/// re-materialize through <see cref="QtHostCollectionBridge.OnHostHealed"/>).
	/// </summary>
	internal bool HealIfDead(NativeElementHost host)
	{
		if (!host.IsAttached)
			return false;
		if (QtHostRuntime.TryItemGeometry(host.NativeHandle, out _))
			return false;   // handle alive: the failure was something else

		// Classify before OnHostHealed, which drops the host from its delegate/slot registry.
		var collectionCell = _collection.IsCollectionCellHost(host);
		host.ResetNative();   // no DestroyObject: the QML object is already gone
		_current.Remove(host);   // next reconcile sees it as new → create op
		_collection.OnHostHealed(host);
		_layoutDirty = true;
		if (!collectionCell)
			RequestPoll();   // the next reconcile recreates it (a list's own resync re-materializes its cells)
		// Deaths come in bursts when Silica rebuilds a page: three per reconcile ask for a full-page rebuild.
		// Collection cells don't count, since ListView recycling kills them one at a time during normal scrolling.
		if (!collectionCell && ++_healedSinceReconcile >= 3)
			_fullResetPending = true;
		QtHostDiag.Warn(QtHostDiagChannel.Geometry,
			$"healed dead host {host} — QML object died before first window report; next reconcile recreates it");
		return true;
	}

	internal static Dictionary<string, object?> CreateChildOp(NativeElementHost host,
	                                                            Dictionary<string, object?> props, string parentObj)
	{
		// A row/slot root lands in the placeholder (parentObj, resolved by objectName); other hosts nest in their parent host.
		if (host.Parent is { } parent)
			return CreateOp(host, props, parent.Id);
		var op = CreateOp(host, props);
		op["parentObj"] = parentObj;   // MauiModelPage.__mauiFindByName re-parents the create
		return op;
	}

	/// <summary>Attaches a row/slot host; <paramref name="scopeHandle"/> scopes the name lookup to its placeholder.</summary>
	internal static void AttachHost(NativeElementHost host, Dictionary<string, object?> props, long scopeHandle = 0) =>
		AttachNative(host, props, scopeHandle);

	internal void RegisterRoute(string id, NativeElementHost host) => _byId[id] = host;
}
