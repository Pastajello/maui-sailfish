using System.Runtime.ExceptionServices;
using Microsoft.Maui.SailfishOS.Platform.QtHost;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// Exceptions thrown by app code that runs on the UI thread: a dispatched action or an <c>async void</c> handler's
/// continuation, a dispatcher timer, an event handler raised from a native event (a Clicked, a gesture, a service
/// event). As on Android, such an exception ends the app; <see cref="Unhandled"/> lets the app decide otherwise.
/// </summary>
public static class SailfishExceptions
{
	/// <summary>
	/// Raised on the UI thread for each such exception, before the app ends. Set
	/// <see cref="SailfishUnhandledExceptionEventArgs.Handled"/> to keep the app running (the failed callback is
	/// abandoned, the loop goes on); leave it to end the app with the exception and its stack in the log, which also
	/// raises <see cref="AppDomain.UnhandledException"/>.
	/// </summary>
	public static event EventHandler<SailfishUnhandledExceptionEventArgs>? Unhandled;

	/// <summary>Exceptions an <see cref="Unhandled"/> handler marked handled (diagnostics).</summary>
	internal static long HandledCount;

	/// <summary>Tests: called instead of ending the process.</summary>
	internal static Action<Exception, string>? CrashOverride;

	/// <summary>Reports an exception from app code on the UI thread: logged, offered to <see cref="Unhandled"/>, and the
	/// app ends unless a handler handled it. Returns when it was handled.</summary>
	internal static void Report(Exception exception, string source)
	{
		// Already reported and on its way out (rethrown from an inner catch site through an outer one): keep it going.
		if (exception.Data.Contains(EndingKey))
			ExceptionDispatchInfo.Capture(exception).Throw();
		QtHostDiag.Error(QtHostDiagChannel.QtHost, $"unhandled exception in {source}: {exception}");
		var args = new SailfishUnhandledExceptionEventArgs(exception, source);
		try
		{
			Unhandled?.Invoke(null, args);
		}
		catch (Exception hookFailure)
		{
			QtHostDiag.Error(QtHostDiagChannel.QtHost, $"SailfishExceptions.Unhandled handler threw: {hookFailure}");
			args.Handled = false;
		}
		if (args.Handled)
		{
			Interlocked.Increment(ref HandledCount);
			return;
		}
		Crash(exception, source);
	}

	private const string EndingKey = "Microsoft.Maui.SailfishOS.UnhandledEnding";

	private static void Crash(Exception exception, string source)
	{
		if (CrashOverride is { } crash)
		{
			crash(exception, source);
			return;
		}
		Console.Error.WriteLine($"[Sailfish] ending the app: unhandled exception in {source} " +
			"(SailfishExceptions.Unhandled can mark such an exception handled)");
		Console.Error.Flush();
		// Rethrown with its original stack: it leaves the catch sites (marked, so none reports it again) and reaches the
		// native Qt callback that ran this code, where the runtime treats it as unhandled: it prints it, raises
		// AppDomain.UnhandledException and ends the process, as for any unhandled .NET exception.
		exception.Data[EndingKey] = true;
		ExceptionDispatchInfo.Capture(exception).Throw();
	}
}

/// <summary>An exception from app code on the UI thread (<see cref="SailfishExceptions.Unhandled"/>).</summary>
public sealed class SailfishUnhandledExceptionEventArgs : EventArgs
{
	internal SailfishUnhandledExceptionEventArgs(Exception exception, string source)
	{
		Exception = exception;
		Source = source;
	}

	/// <summary>The exception.</summary>
	public Exception Exception { get; }

	/// <summary>Where it was caught, for the log: "dispatched work", "a dispatcher timer", "event 'tap'", …</summary>
	public string Source { get; }

	/// <summary>Set to keep the app running.</summary>
	public bool Handled { get; set; }
}
