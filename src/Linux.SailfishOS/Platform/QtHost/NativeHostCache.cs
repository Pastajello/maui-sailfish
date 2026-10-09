using System.Runtime.CompilerServices;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>
/// Weak cache of <see cref="NativeElementHost"/>s keyed by element instance, so hosts survive
/// rebuilds without keeping elements alive. Native objects are destroyed by the renderer's diff, not by GC.
/// </summary>
internal sealed class NativeHostCache
{
	private readonly ConditionalWeakTable<Element, NativeElementHost> _hosts = new();
	private int _nextId;

	/// <summary>Returns the element's host, creating one on first sight; the first URI wins, and binds a host its
	/// handler created unbound.</summary>
	public NativeElementHost GetOrAdd(Element element, string qmlUri)
	{
		ArgumentNullException.ThrowIfNull(element);
		ArgumentException.ThrowIfNullOrEmpty(qmlUri);
		var host = _hosts.GetValue(element, e => new NativeElementHost(NextId(), qmlUri, e));
		host.Bind(qmlUri);
		return host;
	}

	/// <summary>The element's host for its handler, unbound when no adapter was chosen yet: the handler's platform
	/// view exists from the first connect, the reconcile picks the adapter later.</summary>
	internal NativeElementHost GetOrAddForHandler(Element element, string? qmlUri)
	{
		ArgumentNullException.ThrowIfNull(element);
		if (!string.IsNullOrEmpty(qmlUri))
			return GetOrAdd(element, qmlUri);
		return _hosts.GetValue(element, e => new NativeElementHost(NextId(), string.Empty, e));
	}

	/// <summary>Looks up the bound host of an element without creating one.</summary>
	public bool TryGet(Element element, out NativeElementHost? host)
	{
		if (_hosts.TryGetValue(element, out host) && host.IsBound)
			return true;
		host = null;
		return false;
	}

	/// <summary>Drops the element's host: the next lookup creates a fresh one (its adapter changed; the renderer's
	/// diff destroys the old object).</summary>
	internal void Forget(Element element) => _hosts.Remove(element);

	private string NextId() => $"e{Interlocked.Increment(ref _nextId)}";
}
