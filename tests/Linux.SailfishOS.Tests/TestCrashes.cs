using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Maui.SailfishOS.Platform;

namespace Linux.SailfishOS.Tests;

/// <summary>An unhandled app exception ends the process (SailfishExceptions, D11); in the test host it is recorded
/// instead, so a test that throws on purpose does not end the run.</summary>
internal static class TestCrashes
{
	public static readonly ConcurrentQueue<(Exception Exception, string Source)> Seen = new();

	[ModuleInitializer]
	internal static void Install() => SailfishExceptions.CrashOverride = (exception, source) => Seen.Enqueue((exception, source));
}
