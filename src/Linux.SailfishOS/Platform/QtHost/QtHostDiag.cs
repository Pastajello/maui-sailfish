namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>Diagnostic channels of the Qt host; every <see cref="QtHostDiag"/> message is tagged with exactly one.</summary>
public enum QtHostDiagChannel
{
	QtHost = 0,
	QmlLoad = 1,
	QmlObject = 2,
	QmlProperty = 3,
	QmlSignal = 4,
	Input = 5,
	Focus = 6,
	Navigation = 7,
	Lifecycle = 8,
	Geometry = 9,
	Performance = 10,
}

/// <summary>
/// Native Qt host failure with a stable <c>sfhost_err</c> code, thrown only on the boot path.
/// Reconcile paths report errors through counters instead, so the render loop keeps running.
/// </summary>
public sealed class QtHostException : Exception
{
	/// <summary>Stable native error code (sfhost_err).</summary>
	public int Code { get; }

	/// <summary>Name of the failed operation, e.g. "sailfish_host_load_window".</summary>
	public string Operation { get; }

	/// <summary>Error text from sailfish_host_last_error.</summary>
	public string NativeMessage { get; }

	/// <summary>QML path for load failures, otherwise null.</summary>
	public string? QmlPath { get; }

	public QtHostException(int code, string operation, string nativeMessage, string? qmlPath = null)
		: base($"[QT_HOST] {operation} failed: native code {code}" +
		       $"{(qmlPath is null ? string.Empty : $", qml '{qmlPath}'")}: {nativeMessage}")
	{
		Code = code;
		Operation = operation;
		NativeMessage = nativeMessage;
		QmlPath = qmlPath;
	}
}


/// <summary>
/// Diagnostic log of the Qt host with per-channel counters. Trace prints only with
/// <c>MAUI_SAILFISH_QT_HOST_DIAG=1</c>; Warn and Error always print, as <c>[Sailfish][CHANNEL]...</c> lines.
/// </summary>
public static class QtHostDiag
{
	/// <summary>Number of channels.</summary>
	public const int ChannelCount = 11;

	private static readonly string[] Names =
	{
		"QT_HOST", "QML_LOAD", "QML_OBJECT", "QML_PROPERTY", "QML_SIGNAL",
		"INPUT", "FOCUS", "NAVIGATION", "LIFECYCLE", "GEOMETRY", "PERFORMANCE",
	};

	/// <summary>Stable channel name, used as the log tag and the <see cref="Summary"/> JSON key.</summary>
	public static string NameOf(QtHostDiagChannel channel) => Names[(int)channel];

	/// <summary>All channel names, in enum order.</summary>
	public static IReadOnlyList<string> ChannelNames => Names;

	/// <summary>True when MAUI_SAILFISH_QT_HOST_DIAG=1 (read once at startup).</summary>
	public static readonly bool Enabled = SailfishEnv.Flag("MAUI_SAILFISH_QT_HOST_DIAG");

	/// <summary>Check before building expensive <see cref="Trace"/> messages.</summary>
	public static bool TraceEnabled => Enabled;

	private static readonly long[] Traced = new long[ChannelCount];
	private static readonly long[] Warned = new long[ChannelCount];
	private static readonly long[] Errored = new long[ChannelCount];

	/// <summary>Traces a message; printed only when enabled, but always counted.</summary>
	public static void Trace(QtHostDiagChannel channel, string message)
	{
		Interlocked.Increment(ref Traced[(int)channel]);
		if (!Enabled)
			return;
		var line = $"[Sailfish][{Names[(int)channel]}] {message}";
		Console.Error.WriteLine(line);
		Mirror(line);
	}

	/// <summary>Warning (degraded, still functional); always printed.</summary>
	public static void Warn(QtHostDiagChannel channel, string message)
	{
		Interlocked.Increment(ref Warned[(int)channel]);
		var line = $"[Sailfish][{Names[(int)channel]}][WARN] {message}";
		Console.Error.WriteLine(line);
		Mirror(line);
	}

	/// <summary>Error (lost functionality or a native failure); always printed.</summary>
	public static void Error(QtHostDiagChannel channel, string message)
	{
		Interlocked.Increment(ref Errored[(int)channel]);
		var line = $"[Sailfish][{Names[(int)channel]}][ERROR] {message}";
		Console.Error.WriteLine(line);
		Mirror(line);
	}

	// Mirrored to a file so manual repros can be read over ssh after the process exits.
	private const string DiagFile = "/tmp/kitchen-diag.log";

	private static void Mirror(string line)
	{
		try { File.AppendAllText(DiagFile, line + Environment.NewLine); }
		catch { /* diagnostics must never break the app */ }
	}


	/// <summary>Trace count of a channel since startup or the last <see cref="Reset"/>.</summary>
	public static long TraceCount(QtHostDiagChannel channel) => Interlocked.Read(ref Traced[(int)channel]);

	/// <summary>Warning count of a channel.</summary>
	public static long WarnCount(QtHostDiagChannel channel) => Interlocked.Read(ref Warned[(int)channel]);

	/// <summary>Error count of a channel.</summary>
	public static long ErrorCount(QtHostDiagChannel channel) => Interlocked.Read(ref Errored[(int)channel]);

	/// <summary>Number of channels with any activity.</summary>
	public static int ObservedChannels
	{
		get
		{
			var n = 0;
			for (var i = 0; i < ChannelCount; i++)
				if (Interlocked.Read(ref Traced[i]) + Interlocked.Read(ref Warned[i]) + Interlocked.Read(ref Errored[i]) > 0)
					n++;
			return n;
		}
	}

	/// <summary>Total error count across all channels.</summary>
	public static long TotalErrors
	{
		get
		{
			long sum = 0;
			for (var i = 0; i < ChannelCount; i++)
				sum += Interlocked.Read(ref Errored[i]);
			return sum;
		}
	}

	/// <summary>All counters as JSON.</summary>
	public static string Summary()
	{
		var sb = new System.Text.StringBuilder(512);
		sb.Append("{\"enabled\":").Append(Enabled ? "true" : "false").Append(",\"channels\":{");
		for (var i = 0; i < ChannelCount; i++)
		{
			if (i > 0) sb.Append(',');
			sb.Append('"').Append(Names[i]).Append("\":{\"trace\":")
			  .Append(Interlocked.Read(ref Traced[i])).Append(",\"warn\":")
			  .Append(Interlocked.Read(ref Warned[i])).Append(",\"error\":")
			  .Append(Interlocked.Read(ref Errored[i])).Append('}');
		}
		sb.Append("},\"observedChannels\":").Append(ObservedChannels)
		  .Append(",\"totalErrors\":").Append(TotalErrors).Append('}');
		return sb.ToString();
	}

	/// <summary>Resets all counters (diagnostic runs only).</summary>
	public static void Reset()
	{
		for (var i = 0; i < ChannelCount; i++)
		{
			Interlocked.Exchange(ref Traced[i], 0);
			Interlocked.Exchange(ref Warned[i], 0);
			Interlocked.Exchange(ref Errored[i], 0);
		}
	}
}
