namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// One diagnostics leg's check list. The CHECK and ACCEPTANCE line formats are what tools/sf matrix and
/// the device logs are read by, so they must stay byte-identical.
/// </summary>
internal sealed class DiagChecks(string tag)
{
	private readonly List<(string Check, bool Ok)> _checks = new();

	public int Count => _checks.Count;

	public int Failed => _checks.Count(c => !c.Ok);

	/// <summary>Records one check and prints its CHECK line; returns <paramref name="ok"/>.</summary>
	public bool Check(string name, bool ok)
	{
		_checks.Add((name, ok));
		Console.Error.WriteLine($"[Sailfish] {tag}: CHECK {(ok ? "OK  " : "FAIL")} — {name}");
		return ok;
	}

	/// <summary>Shim calls off the Qt thread silently corrupt the QV4 heap, so every leg ends with this check.</summary>
	public bool CheckNoOffThreadCalls() =>
		Check($"no shim call off the Qt thread: {QtHost.QtHostRuntime.OffThreadCalls}==0", QtHost.QtHostRuntime.OffThreadCalls == 0);

	/// <summary>Prints the leg's ACCEPTANCE line: <paramref name="okText"/> when every check passed.</summary>
	public void Accept(string okText)
	{
		var failed = Failed;
		Console.Error.WriteLine($"[Sailfish] {tag}: ACCEPTANCE checks={Count} failed={failed} => " +
			(failed == 0 ? okText : "FAIL — see the CHECK lines above"));
	}
}
