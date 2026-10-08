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
	private readonly SailfishRenderSession _session;

	/// <param name="session">The session of the overlay that serves this subscription (SailfishServiceOverlay, the
	/// only place it is created; an app's own IAlertManagerSubscription registration wins over it).</param>
	public QtHostAlertSubscription(SailfishRenderSession session)
	{
		_session = session;
		Console.Error.WriteLine("[Sailfish] Qt dialogs: QtHostAlertSubscription constructed (Window.AlertManager subscribed)");
	}

	public void OnAlertRequested(Page sender, AlertArguments arguments)
	{
		ArgumentNullException.ThrowIfNull(arguments);
		if (ResolveRenderer() is not { } renderer)
		{
			arguments.SetResult(false);
			return;
		}
		_ = CompleteAsync(
			() => OnQt(() => renderer.PushAlertAsync(arguments.Title ?? string.Empty, arguments.Message ?? string.Empty,
				arguments.Accept, arguments.Cancel)),
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
		// Email and Url open Maliit's address layouts (Qt.ImhEmailCharactersOnly / Qt.ImhUrlCharactersOnly).
		var hints = ReferenceEquals(arguments.Keyboard, Keyboard.Email) ? EmailHints
			: ReferenceEquals(arguments.Keyboard, Keyboard.Url) ? UrlHints
			: 0;
		_ = CompleteAsync<string?>(
			() => OnQt(() => renderer.PushPromptAsync(arguments.Title, arguments.Message ?? string.Empty,
				arguments.Accept, arguments.Cancel, arguments.Placeholder ?? string.Empty,
				arguments.InitialValue ?? string.Empty, arguments.MaxLength, numeric, hints)),
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
			() => OnQt(() => renderer.PushActionSheetAsync(arguments.Title ?? string.Empty,
				arguments.Cancel, arguments.Destruction, buttons)),
			result => arguments.SetResult(result),
			arguments.Cancel ?? string.Empty);
	}

	private const int EmailHints = 0x200000;   // Qt::ImhEmailCharactersOnly
	private const int UrlHints = 0x400000;     // Qt::ImhUrlCharactersOnly

	/// <summary>A dialog asked for off the UI thread (DisplayAlertAsync from a Task.Run) opens on the Qt thread, as the
	/// platforms marshal it (tracker S38; it used to touch QML from the caller's thread and complete with the fallback).</summary>
	private static Task<T> OnQt<T>(Func<Task<T>> push) =>
		QtThread.IsCurrent ? push() : QtThread.RunAsync(push).Unwrap();

	private QtHostPageRenderer? ResolveRenderer()
	{
		var renderer = _session.Renderer;
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