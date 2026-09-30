using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using SailfishKitchen.Messaging;

namespace SailfishKitchen.Services;

/// <summary>
/// Whether the device can reach the internet. Used for UI labels and retry offers, never to gate a load,
/// so a wrong "offline" verdict cannot hide cached data.
/// </summary>
public interface IConnectivityService
{
	bool IsOnline { get; }

	/// <summary>Starts watching for changes. Idempotent.</summary>
	void Start();

	void Stop();
}

public sealed class ConnectivityService : IConnectivityService, IDisposable
{
	private readonly IMessenger _messenger;
	private readonly ILogger<ConnectivityService> _logger;
	private bool _online = true;
	private bool _watching;

	public ConnectivityService(IMessenger messenger, ILogger<ConnectivityService> logger)
	{
		_messenger = messenger;
		_logger = logger;
	}

	/// <summary>
	/// Optimistic by default: where the Essentials implementation is missing, attempt the request and let HTTP classify failures.
	/// </summary>
	public bool IsOnline
	{
		get
		{
			try
			{
				return Microsoft.Maui.Networking.Connectivity.NetworkAccess
					is Microsoft.Maui.Networking.NetworkAccess.Internet
					or Microsoft.Maui.Networking.NetworkAccess.ConstrainedInternet;
			}
			catch (Exception ex) when (ex is NotImplementedException or NotSupportedException or PlatformNotSupportedException)
			{
				_logger.LogDebug(ex, "connectivity probe unavailable on this host; assuming online");
				return true;
			}
		}
	}

	public void Start()
	{
		if (_watching)
			return;

		try
		{
			Microsoft.Maui.Networking.Connectivity.ConnectivityChanged += OnChanged;
			_watching = true;
			_online = IsOnline;
			_logger.LogInformation("connectivity: watching (online={Online})", _online);
		}
		catch (Exception ex) when (ex is NotImplementedException or NotSupportedException or PlatformNotSupportedException)
		{
			_logger.LogInformation("connectivity: no change notifications on this host");
		}
	}

	public void Stop()
	{
		if (!_watching)
			return;

		try { Microsoft.Maui.Networking.Connectivity.ConnectivityChanged -= OnChanged; }
		catch (Exception ex) { _logger.LogDebug(ex, "connectivity: could not detach"); }

		_watching = false;
	}

	public void Dispose() => Stop();

	private void OnChanged(object? sender, Microsoft.Maui.Networking.ConnectivityChangedEventArgs e)
	{
		var online = IsOnline;
		if (online == _online)
			return;

		_online = online;
		_logger.LogInformation("connectivity: online={Online}", online);
		_messenger.Send(new ConnectivityChangedMessage(online));
	}
}
