using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using SailfishKitchen.Helpers;
using SailfishKitchen.Messaging;
using SailfishKitchen.Services;

namespace SailfishKitchen.ViewModels;

/// <summary>
/// Drives the app-wide banner that stands in for a native toast; it is a plain Border, so it renders on any host.
/// </summary>
public partial class BannerViewModel : ObservableObject
{
	private readonly IDispatcher _dispatcher;
	private readonly ILogger<BannerViewModel> _logger;
	private CancellationTokenSource? _hide;
	private bool _isActive;

	public BannerViewModel(IMessenger messenger, IDispatcher dispatcher, ILogger<BannerViewModel> logger)
	{
		Messenger = messenger;
		_dispatcher = dispatcher;
		_logger = logger;
	}

	protected IMessenger Messenger { get; }

	/// <summary>Hand-rolled activation, for the same trim reason as <see cref="ViewModelBase"/>.</summary>
	public bool IsActive
	{
		get => _isActive;
		set
		{
			if (_isActive == value)
				return;

			_isActive = value;
			if (value)
				OnActivated();
			else
				OnDeactivated();
		}
	}

	[ObservableProperty]
	private string _text = string.Empty;

	[ObservableProperty]
	private bool _isVisible;

	[ObservableProperty]
	private Color _accent = Palette.Accent;

	/// <summary>How long the banner stays up; warnings and errors stay longer.</summary>
	public TimeSpan Duration { get; private set; } = TimeSpan.FromSeconds(2.6);

	private void OnActivated() =>
		Messenger.Register<BannerViewModel, NotificationMessage>(this, static (vm, message) => vm.Show(message));

	private void OnDeactivated()
	{
		Messenger.UnregisterAll(this);
		CancelHide();
	}

	private void Show(NotificationMessage message)
	{
		// Notifications come from awaited continuations; off-thread BindableProperty writes crash this host.
		_dispatcher.RunOnUi(() =>
		{
			Text = message.Text;
			Accent = message.Kind switch
			{
				NotificationKind.Success => Palette.Success,
				NotificationKind.Warning => Palette.Warning,
				NotificationKind.Error => Palette.Error,
				_ => Palette.Accent,
			};
			Duration = message.Kind is NotificationKind.Error or NotificationKind.Warning
				? TimeSpan.FromSeconds(4.5)
				: TimeSpan.FromSeconds(2.6);

			IsVisible = true;
			RestartHideTimer();
		});
	}

	public void Dismiss()
	{
		CancelHide();
		_dispatcher.RunOnUi(() => IsVisible = false);
	}

	private void RestartHideTimer()
	{
		CancelHide();
		var cts = new CancellationTokenSource();
		_hide = cts;
		_ = HideAfterAsync(Duration, cts.Token);
	}

	private async Task HideAfterAsync(TimeSpan delay, CancellationToken ct)
	{
		try
		{
			await Task.Delay(delay, ct).ConfigureAwait(false);
			_dispatcher.RunOnUi(() => IsVisible = false);
		}
		catch (OperationCanceledException)
		{
			// Superseded by a newer notification.
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "banner: hide timer failed");
		}
	}

	private void CancelHide()
	{
		_hide?.Cancel();
		_hide?.Dispose();
		_hide = null;
	}
}
