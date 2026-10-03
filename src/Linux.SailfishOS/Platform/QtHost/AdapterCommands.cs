namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>
/// Managed → adapter actions (a scroll, a script, a navigation step, opening a swipe): the adapter's
/// <c>mauiCommand(json)</c> function is called directly (<see cref="QtHostRuntime.Invoke"/>), once per request, with
/// <c>{"name": …, …args}</c>. State still crosses as properties; an action used to cross as a property plus a counter
/// that re-fired equal values (docs/custom-controls.md).
/// </summary>
internal static class AdapterCommands
{
	/// <summary>False when the adapter object does not exist yet or has no <c>mauiCommand</c> (logged).</summary>
	public static bool Send(NativeElementHost host, string name, Dictionary<string, object?>? args = null)
	{
		if (!host.IsAttached)
			return false;
		var command = new Dictionary<string, object?>(args ?? new Dictionary<string, object?>()) { ["name"] = name };
		QtHostRuntime.Invoke(host.NativeHandle, "mauiCommand", BridgeValue.Serialize(command), out var rc);
		if (rc < 0)
			QtHostDiag.Warn(QtHostDiagChannel.QmlProperty, $"command '{name}' to {host} failed: native code {rc} {QtHostRuntime.LastErrorText}");
		return rc >= 0;
	}
}
