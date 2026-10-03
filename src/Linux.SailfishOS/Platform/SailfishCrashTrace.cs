namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// The device trace for what happens off the debugger: unhandled exceptions, unobserved tasks, the process exit and,
/// with MAUI_SAILFISH_FIRST_CHANCE=N, the first N first-chance exceptions. Every line goes to stderr (the app log
/// tools/sf run streams) and is appended to <see cref="FilePath"/>, which the shim's own crash trap writes too.
/// </summary>
internal static class SailfishCrashTrace
{
	public const string FilePath = "/tmp/maui_trace.log";

	private static int _installed;

	/// <summary>Hooks the process events once (SailfishMauiApplication.Run).</summary>
	public static void Install()
	{
		if (Interlocked.Exchange(ref _installed, 1) != 0)
			return;
		AppDomain.CurrentDomain.UnhandledException += (_, e) => Crash("AppDomain", e.ExceptionObject as Exception);
		TaskScheduler.UnobservedTaskException += (_, e) => Crash("UnobservedTask", e.Exception);
		// Thrown and caught exceptions are expensive and otherwise invisible (inside fire-and-forget animations).
		if (SailfishEnv.Int("MAUI_SAILFISH_FIRST_CHANCE") is > 0 and var firstChanceMax)
		{
			var firstChanceSeen = 0;
			AppDomain.CurrentDomain.FirstChanceException += (_, e) =>
			{
				if (Interlocked.Increment(ref firstChanceSeen) <= firstChanceMax)
					Console.Error.WriteLine($"[Sailfish] FIRST-CHANCE {e.Exception.GetType().Name}: {e.Exception.Message}{Environment.NewLine}{Environment.StackTrace}");
			};
		}
		// ProcessExit fires on clean exit and SIGTERM; its absence in the trace means SIGKILL.
		AppDomain.CurrentDomain.ProcessExit += (_, _) => Line("[Sailfish] EXIT ProcessExit (clean exit or SIGTERM)");
	}

	/// <summary>An unhandled exception, with its stack.</summary>
	public static void Crash(string source, Exception? ex) => Line($"[Sailfish] CRASH {source}: {ex}");

	/// <summary>One line to stderr and the trace file (best effort: the stderr line already carries it).</summary>
	public static void Line(string text, bool stderr = true)
	{
		if (stderr)
			Console.Error.WriteLine(text);
		try
		{
			File.AppendAllText(FilePath, text + Environment.NewLine);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
		}
	}
}
