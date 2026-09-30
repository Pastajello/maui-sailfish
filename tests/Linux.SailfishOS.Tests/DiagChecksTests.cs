using Microsoft.Maui.SailfishOS.Platform;
using Xunit;

namespace Linux.SailfishOS.Tests;

[CollectionDefinition("console", DisableParallelization = true)]
public sealed class ConsoleCollection
{
}

/// <summary>tools/sf-matrix.sh greps "&lt;tag&gt;: ACCEPTANCE", "=> OK", "failed=[1-9]" and "CHECK FAIL" out of the
/// device log, so these lines are contract.</summary>
[Collection("console")]
public class DiagChecksTests
{
	private static string Capture(Action<DiagChecks> run)
	{
		var original = Console.Error;
		var sw = new StringWriter();
		Console.SetError(sw);
		try { run(new DiagChecks("Qt f3 diag")); }
		finally { Console.SetError(original); }
		return sw.ToString().Replace("\r\n", "\n");
	}

	[Fact]
	public void Passing_leg_prints_check_and_ok_acceptance() =>
		Assert.Equal(
			"[Sailfish] Qt f3 diag: CHECK OK   — a\n" +
			"[Sailfish] Qt f3 diag: ACCEPTANCE checks=1 failed=0 => OK — done\n",
			Capture(c => { c.Check("a", true); c.Accept("OK — done"); }));

	[Fact]
	public void Failing_leg_prints_check_fail_and_failed_count() =>
		Assert.Equal(
			"[Sailfish] Qt f3 diag: CHECK OK   — a\n" +
			"[Sailfish] Qt f3 diag: CHECK FAIL — b\n" +
			"[Sailfish] Qt f3 diag: ACCEPTANCE checks=2 failed=1 => FAIL — see the CHECK lines above\n",
			Capture(c => { c.Check("a", true); c.Check("b", false); c.Accept("OK — done"); }));
}
