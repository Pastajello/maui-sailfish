using Microsoft.Maui.Dispatching;

namespace SailfishKitchen.Helpers;

public static class UiThreadExtensions
{
	/// <summary>
	/// Runs <paramref name="action"/> on the UI thread, inline when already there so synchronous callers see the
	/// change at once. The single UI thread owns the Qt scene graph; off-thread BindableProperty writes never render.
	/// </summary>
	public static void RunOnUi(this IDispatcher dispatcher, Action action)
	{
		if (dispatcher.IsDispatchRequired)
			dispatcher.Dispatch(action);
		else
			action();
	}
}
