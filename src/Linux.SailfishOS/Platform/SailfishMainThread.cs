using System;
using System.Runtime.CompilerServices;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// Backs MAUI Essentials' MainThread (IsMainThread, BeginInvokeOnMainThread, InvokeOnMainThreadAsync) with the Qt loop
/// thread. Its plain-net asset has no platform part and threw NotImplementedInReferenceAssemblyException, which an
/// async startup swallowed (EmployeeDirectory sat on its loading page). MAUI 11 keeps the hook internal
/// (MainThread.SetCustomImplementation); the accessor binds it without reflection, so trimming keeps it.
/// </summary>
internal static class SailfishMainThread
{
	private const string MainThreadType = "Microsoft.Maui.ApplicationModel.MainThread, Microsoft.Maui.Essentials";

	[UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "SetCustomImplementation")]
	private static extern void SetCustomImplementation([UnsafeAccessorType(MainThreadType)] object? mainThread,
		Func<bool> isMainThread, Action<Action> beginInvokeOnMainThread);

	[UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "ClearCustomImplementation")]
	private static extern void ClearCustomImplementation([UnsafeAccessorType(MainThreadType)] object? mainThread);

	/// <summary>Makes the calling thread MainThread's main thread, with <paramref name="dispatch"/> queueing onto it.</summary>
	public static bool Install(Action<Action> dispatch)
	{
		var mainThreadId = Environment.CurrentManagedThreadId;
		try
		{
			SetCustomImplementation(null, () => Environment.CurrentManagedThreadId == mainThreadId, dispatch);
			return true;
		}
		catch (Exception ex) when (ex is MissingMethodException or MissingMemberException or TypeLoadException)
		{
			Console.Error.WriteLine($"[Sailfish] MainThread hook unavailable ({ex.GetType().Name}: {ex.Message}); MainThread APIs stay unsupported");
			return false;
		}
	}

	/// <summary>Tests: back to MAUI's own (unsupported) implementation.</summary>
	internal static void Clear() => ClearCustomImplementation(null);
}
