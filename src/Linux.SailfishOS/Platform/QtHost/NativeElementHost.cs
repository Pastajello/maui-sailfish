namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>Scene geometry of a native host, as read from the shim (QQuickItem).</summary>
public readonly record struct NativeGeometry(double X, double Y, double Width, double Height)
{
	public override string ToString() => $"({X:F0},{Y:F0} {Width:F0}x{Height:F0})";
}

/// <summary>
/// A persistent QML object hosting one MAUI element; the element is the identity, so the native object
/// (and its focus/scroll state) survives reconciles. The QML scene owns the object; this host only borrows
/// the handle, which the shim revokes when the renderer destroys it.
/// </summary>
public sealed class NativeElementHost
{
	internal NativeElementHost(string id, string qmlUri, Element element)
	{
		Id = id;
		QmlUri = qmlUri;
		Element = element;
	}

	/// <summary>Stable identity ("e&lt;N&gt;"); objectName of the QML object is "maui_&lt;Id&gt;".</summary>
	public string Id { get; }

	/// <summary>QML adapter URI (component kind) this host was created from; empty while the host is unbound (its
	/// handler exists, the reconcile has not chosen an adapter yet).</summary>
	public string QmlUri { get; private set; }

	/// <summary>Whether an adapter URI is chosen; only bound hosts are ever created natively.</summary>
	public bool IsBound => QmlUri.Length > 0;

	/// <summary>Chooses the adapter of an unbound host (the first URI wins, as for any host).</summary>
	internal void Bind(string qmlUri)
	{
		if (!IsBound)
			QmlUri = qmlUri;
	}

	/// <summary>The MAUI logical element (identity; weakly referenced by the cache).</summary>
	public Element Element { get; }

	/// <summary>Opaque native QObject handle (sailfish_host_find_object); 0 = not attached.</summary>
	public long NativeHandle { get; internal set; }

	/// <summary>Property values last applied, in bridge JSON form; the diff basis for updates and for
	/// suppressing adapter event echoes.</summary>
	public Dictionary<string, string> AppliedProperties { get; } = new();

	/// <summary>Snapshots this host's handler mapper sent (architecture counters, A2: one per mapper pass).</summary>
	internal int HandlerSnapshots { get; set; }

	/// <summary>True when <paramref name="json"/> is what the native object already holds for <paramref name="name"/>.</summary>
	public bool IsApplied(string name, string json) =>
		AppliedProperties.TryGetValue(name, out var applied) && applied == json;


	/// <summary>Whether the native handle is attached (object created and resolved).</summary>
	public bool IsAttached => NativeHandle != 0;

	/// <summary>Raised on the Qt thread each time a fresh QML object is attached: the first creation and every
	/// re-creation (navigation back, a recycled collection row, a healed host). State that lives only in the native
	/// object and not in the adapter's properties (a drawing surface's pixels) must be sent again.</summary>
	public event Action<NativeElementHost>? Attached;

	internal void RaiseAttached() => Attached?.Invoke(this);

	/// <summary>Focus asked for before the QML object existed (Focus() right after adding the view); replayed when the
	/// object attaches. Null = no request pending.</summary>
	internal bool? PendingFocus { get; set; }

	/* --- Geometry --- */

	/// <summary>The element's absolute rectangle in root space (page content area, dp).</summary>
	public Microsoft.Maui.Graphics.Rect MauiLogicalBounds { get; internal set; }

	/// <summary>Geometry last applied to the native item (Qt scene units).</summary>
	public NativeGeometry AppliedGeometry { get; internal set; }

	/// <summary>Visibility last applied together with <see cref="AppliedGeometry"/>.</summary>
	public bool AppliedVisible { get; internal set; } = true;

	/// <summary>Whether any geometry was applied yet (diff basis of SetGeometry).</summary>
	public bool AppliedGeometrySet { get; internal set; }

	/* --- Host tree --- */

	/// <summary>Host of the nearest hosted ancestor whose QML item contains this one; null for the page
	/// canvas and for collection rows (parented to the ListView delegate).</summary>
	internal NativeElementHost? Parent { get; set; }

	/// <summary>Root-space viewport of the enclosing scroll host(s); content scrolled out of it must not
	/// hit-test. Null outside a scroll host.</summary>
	internal Microsoft.Maui.Graphics.Rect? HitClipDp { get; set; }

	/// <summary>Local → root transform of the last geometry pass when it is more than a translation (a Scale or
	/// Rotation on the element or an ancestor); hit-testing maps the point back into the element's own rect with it
	/// (tracker S16). Null: <see cref="MauiLogicalBounds"/> is exact.</summary>
	internal Affine2? HitTransform { get; set; }

	/// <summary>Parent id last applied natively, the diff basis for reparent ops; "" is the page canvas.</summary>
	internal string? AppliedParentId { get; set; }

	/// <summary>The native object is gone or handed over: no handle and nothing applied, so a new object gets its
	/// properties, geometry and parent again (W3.1: five hand copies reset different subsets).</summary>
	internal void ResetNative()
	{
		NativeHandle = 0;
		AppliedProperties.Clear();
		AppliedGeometrySet = false;
		AppliedParentId = null;
	}

	public override string ToString() => $"{QmlUri}:{Id}{(IsAttached ? $"@{NativeHandle:x}" : string.Empty)}";
}
