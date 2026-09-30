using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Internals;
using Microsoft.Maui.Controls.Platform;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>
/// Shows MAUI DisplayAlert/Prompt/ActionSheet as native Silica dialogs. MAUI resolves it from the
/// window handler's services, so the window handler must be attached before the first page.
/// </summary>
internal sealed class QtHostAlertSubscription : IAlertManagerSubscription
{
	public QtHostAlertSubscription() =>
		Console.Error.WriteLine("[Sailfish] Qt dialogs: QtHostAlertSubscription constructed (Window.AlertManager subscribed)");

	public void OnAlertRequested(Page sender, AlertArguments arguments)
	{
		ArgumentNullException.ThrowIfNull(arguments);
		if (ResolveRenderer() is not { } renderer)
		{
			arguments.SetResult(false);
			return;
		}
		_ = CompleteAsync(
			() => renderer.PushAlertAsync(arguments.Title ?? string.Empty, arguments.Message ?? string.Empty,
				arguments.Accept, arguments.Cancel),
			result => arguments.SetResult(result),
			false);
	}

	public void OnPromptRequested(Page sender, PromptArguments arguments)
	{
		ArgumentNullException.ThrowIfNull(arguments);
		if (ResolveRenderer() is not { } renderer)
		{
			arguments.SetResult(null);
			return;
		}
		// Maps to Qt.ImhDigitsOnly, which opens the Maliit number keyboard.
		var numeric = ReferenceEquals(arguments.Keyboard, Keyboard.Numeric) ||
			ReferenceEquals(arguments.Keyboard, Keyboard.Telephone);
		_ = CompleteAsync<string?>(
			() => renderer.PushPromptAsync(arguments.Title, arguments.Message ?? string.Empty,
				arguments.Accept, arguments.Cancel, arguments.Placeholder ?? string.Empty,
				arguments.InitialValue ?? string.Empty, arguments.MaxLength, numeric),
			result => arguments.SetResult(result),
			null);
	}

	public void OnActionSheetRequested(Page sender, ActionSheetArguments arguments)
	{
		ArgumentNullException.ThrowIfNull(arguments);
		if (ResolveRenderer() is not { } renderer)
		{
			arguments.SetResult(arguments.Cancel);
			return;
		}
		var buttons = (arguments.Buttons ?? Enumerable.Empty<string>())
			.Where(b => !string.IsNullOrEmpty(b))
			.ToList();
		_ = CompleteAsync(
			() => renderer.PushActionSheetAsync(arguments.Title ?? string.Empty,
				arguments.Cancel, arguments.Destruction, buttons),
			result => arguments.SetResult(result),
			arguments.Cancel ?? string.Empty);
	}

	private static QtHostPageRenderer? ResolveRenderer()
	{
		var renderer = QtHostPageRenderer.Current;
		if (renderer is null)
			Console.Error.WriteLine("[Sailfish] Qt dialogs: no active Qt-host renderer — completing with the default result");
		return renderer;
	}

	// Completes the MAUI task with the dialog result, or the fallback if the push fails.
	private static async Task CompleteAsync<T>(Func<Task<T>> push, Action<T> setResult, T fallback)
	{
		T result;
		try
		{
			result = await push().ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[Sailfish] Qt dialogs: dialog push failed ({ex.Message}) — completing with the fallback result");
			result = fallback;
		}
		try
		{
			setResult(result);
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[Sailfish] Qt dialogs: SetResult failed: {ex.Message}");
		}
	}
}