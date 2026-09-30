using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using SailfishKitchen.Messaging;
using SailfishKitchen.Models;

namespace SailfishKitchen.Services;

/// <summary>
/// Owns the persisted <see cref="AppSettings"/>. Reads come from a volatile snapshot; writes clone, mutate,
/// save and publish so a bound view never sees a half-applied change.
/// </summary>
public interface ISettingsService
{
	/// <summary>The current snapshot; never null.</summary>
	AppSettings Current { get; }

	/// <summary>Loads from disk, falling back to defaults. Safe to call more than once.</summary>
	AppSettings Load();

	/// <summary>Applies <paramref name="mutate"/> to a clone, persists it and broadcasts <see cref="SettingsChangedMessage"/>.</summary>
	AppSettings Update(Action<AppSettings> mutate, string? changedProperty = null);

	/// <summary>Restores factory defaults, persists them, and broadcasts the change.</summary>
	AppSettings Reset();

	/// <summary>
	/// A session-only override (KITCHEN_* env knobs), re-applied after every update but never saved, so a scripted
	/// KITCHEN_OFFLINE=1 run does not leave the app offline.
	/// </summary>
	void Override(Action<AppSettings> mutate, string reason);
}

public sealed class SettingsService : ISettingsService
{
	internal const string FileName = "settings.json";

	private readonly IJsonFileStore _store;
	private readonly IMessenger _messenger;
	private readonly ILogger<SettingsService> _logger;
	private readonly object _gate = new();
	private AppSettings _current;   // what the app sees: _stored + the session overrides
	private AppSettings _stored;    // what is on disk
	private readonly List<Action<AppSettings>> _overrides = new();

	public SettingsService(IJsonFileStore store, IMessenger messenger, ILogger<SettingsService> logger)
	{
		_store = store;
		_messenger = messenger;
		_logger = logger;
		_current = AppSettings.GetDefault();
		_stored = _current;
	}

	private AppSettings WithOverrides(AppSettings stored)
	{
		if (_overrides.Count == 0)
			return stored;
		var draft = stored.Clone();
		foreach (var apply in _overrides)
			apply(draft);
		return draft.Sanitized();
	}

	public void Override(Action<AppSettings> mutate, string reason)
	{
		ArgumentNullException.ThrowIfNull(mutate);
		AppSettings next;
		lock (_gate)
		{
			_overrides.Add(mutate);
			next = WithOverrides(_stored);
			Volatile.Write(ref _current, next);
		}
		_logger.LogInformation("settings: session override {Reason} (not saved)", reason);
		_messenger.Send(new SettingsChangedMessage(next, null));
	}

	public AppSettings Current => Volatile.Read(ref _current);

	public AppSettings Load()
	{
		lock (_gate)
		{
			var loaded = _store.Load<AppSettings>(FileName);
			if (loaded is null)
			{
				_stored = AppSettings.GetDefault();
				_current = WithOverrides(_stored);
				_logger.LogInformation("settings: none on disk, using defaults (page size {PageSize})", _current.PageSize);
				return _current;
			}

			_stored = loaded.Sanitized();
			_current = WithOverrides(_stored);
			_logger.LogInformation("settings: loaded (page size {PageSize}, layout {Layout}, offline {Offline})",
				_current.PageSize, _current.Layout, _current.OfflineMode);
			return _current;
		}
	}

	public AppSettings Update(Action<AppSettings> mutate, string? changedProperty = null)
	{
		ArgumentNullException.ThrowIfNull(mutate);

		AppSettings next, stored;
		lock (_gate)
		{
			var draft = _stored.Clone();
			mutate(draft);
			stored = _stored = draft.Sanitized();
			next = WithOverrides(stored);
			Volatile.Write(ref _current, next);
		}

		Persist(stored, next, changedProperty);
		return next;
	}

	public AppSettings Reset()
	{
		AppSettings next, stored;
		lock (_gate)
		{
			stored = _stored = AppSettings.GetDefault();
			next = WithOverrides(stored);
			Volatile.Write(ref _current, next);
		}

		Persist(stored, next, changedProperty: null);
		_logger.LogInformation("settings: reset to defaults");
		return next;
	}

	private void Persist(AppSettings stored, AppSettings value, string? changedProperty)
	{
		try
		{
			_store.Save(FileName, stored);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			// Keep the in-memory value so the session stays consistent, and tell the user.
			_logger.LogError(ex, "settings: could not persist, continuing in memory");
			_messenger.Send(new ErrorRaisedMessage("settings", "Could not save settings to disk.", IsRetryable: true, ex));
		}

		_messenger.Send(new SettingsChangedMessage(value, changedProperty));
	}
}
