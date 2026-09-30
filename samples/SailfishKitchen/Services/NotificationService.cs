using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls;
using SailfishKitchen.Messaging;

namespace SailfishKitchen.Services;

public enum NotificationKind
{
	Info,
	Success,
	Warning,
	Error,
}

/// <summary>Transient user feedback, behind an interface because a native toast is not a given on this backend.</summary>
public interface INotificationService
{
	Task ShowAsync(string message, NotificationKind kind = NotificationKind.Info);
}

/// <summary>
/// Tries the CommunityToolkit toast and, after its first failure (no native handler on the Qt/Silica host),
/// relies on the in-app banner only.
/// </summary>
public sealed class NotificationService : INotificationService
{
	private readonly IDialogService _dialogs;
	private readonly IMessenger _messenger;
	private readonly ILogger<NotificationService> _logger;
	private int _toastFailed;

	public NotificationService(IDialogService dialogs, IMessenger messenger, ILogger<NotificationService> logger)
	{
		_dialogs = dialogs;
		_messenger = messenger;
		_logger = logger;
	}

	public async Task ShowAsync(string message, NotificationKind kind = NotificationKind.Info)
	{
		if (string.IsNullOrWhiteSpace(message))
			return;

		// Always publish: the banner shows the message even when a native toast did show.
		_messenger.Send(new NotificationMessage(message, kind));

		if (Volatile.Read(ref _toastFailed) == 1)
			return;

		try
		{
			await CommunityToolkit.Maui.Alerts.Toast
				.Make(message, MapDuration(kind))
				.Show(CancellationToken.None)
				.ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			// A missing handler is a platform property, so stop retrying after one warning.
			Volatile.Write(ref _toastFailed, 1);
			_logger.LogWarning(ex, "toast unavailable on this host; falling back to the in-app banner");
		}
	}

	private static CommunityToolkit.Maui.Core.ToastDuration MapDuration(NotificationKind kind) =>
		kind is NotificationKind.Error or NotificationKind.Warning
			? CommunityToolkit.Maui.Core.ToastDuration.Long
			: CommunityToolkit.Maui.Core.ToastDuration.Short;
}

/// <summary>Modal conversation with the user, routed to the current page.</summary>
public interface IDialogService
{
	Task AlertAsync(string title, string message, string cancel = "OK");

	Task<bool> ConfirmAsync(string title, string message, string accept = "Yes", string cancel = "Cancel");

	Task<string?> PromptAsync(string title, string message, string accept = "OK", string cancel = "Cancel", string placeholder = "");

	Task<string> ActionSheetAsync(string title, string cancel, string? destructive, params string[] buttons);

	/// <summary>The page every modal is presented from; null before the first window exists.</summary>
	Page? CurrentPage { get; }
}

public sealed class DialogService : IDialogService
{
	private readonly ILogger<DialogService> _logger;

	public DialogService(ILogger<DialogService> logger) => _logger = logger;

	public Page? CurrentPage => ResolvePage();

	public Task AlertAsync(string title, string message, string cancel = "OK")
	{
		var page = CurrentPage;
		return page is null
			? Log(page, title, message)
			: page.DisplayAlertAsync(title, message, cancel);
	}

	public Task<bool> ConfirmAsync(string title, string message, string accept = "Yes", string cancel = "Cancel")
	{
		var page = CurrentPage;
		return page is null
			? Task.FromResult(false)
			: page.DisplayAlertAsync(title, message, accept, cancel);
	}

	public Task<string?> PromptAsync(string title, string message, string accept = "OK", string cancel = "Cancel", string placeholder = "")
	{
		var page = CurrentPage;
		return page is null
			? Task.FromResult<string?>(null)
			: page.DisplayPromptAsync(title, message, accept, cancel, placeholder);
	}

	public Task<string> ActionSheetAsync(string title, string cancel, string? destructive, params string[] buttons)
	{
		var page = CurrentPage;
		return page is null
			? Task.FromResult(cancel)
			: page.DisplayActionSheetAsync(title, cancel, destructive, buttons);
	}

	private Task Log(Page? page, string title, string message)
	{
		// No page yet (startup or a background service); log instead of losing the message silently.
		_logger.LogWarning("dialog suppressed, no page: {Title} / {Message}", title, message);
		return Task.CompletedTask;
	}

	internal static Page? ResolvePage()
	{
		var window = Application.Current?.Windows.FirstOrDefault(w => w.Page is not null);
		var page = window?.Page;

		// An alert presented from the NavigationPage rather than its visible child lands behind the pushed page.
		return page switch
		{
			NavigationPage { CurrentPage: not null } nav => nav.CurrentPage,
			FlyoutPage { Detail: not null } flyout => ResolveFrom(flyout.Detail),
			_ => page,
		};
	}

	private static Page ResolveFrom(Page page) => page switch
	{
		NavigationPage { CurrentPage: not null } nav => nav.CurrentPage,
		_ => page,
	};
}
