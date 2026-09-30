using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Dispatching;
using SailfishKitchen.Helpers;
using SailfishKitchen.Messaging;
using SailfishKitchen.Models;
using SailfishKitchen.Services;

namespace SailfishKitchen.ViewModels;

/// <summary>
/// Shared view-model plumbing: busy/error/status state, messenger lifetime, UI-thread writes and
/// turning a <see cref="Result{T}"/> failure into UI state.
/// Activation is hand-rolled instead of deriving from <see cref="ObservableRecipient"/>, whose
/// <c>[RequiresDynamicCode]</c> members raise IL3050/IL3051 under the Release trim analyzers.
/// </summary>
public abstract partial class ViewModelBase : ObservableObject
{
	protected ViewModelBase(
		IDialogService dialogs,
		INotificationService notifications,
		IDispatcher dispatcher,
		ILogger logger,
		IMessenger? messenger = null)
	{
		Dialogs = dialogs;
		Notifications = notifications;
		Dispatcher = dispatcher;
		Logger = logger;
		Messenger = messenger ?? WeakReferenceMessenger.Default;
	}

	protected IMessenger Messenger { get; }

	private bool _isActive;

	/// <summary>Set by the page on appear/disappear so a popped page stops receiving broadcasts.</summary>
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

	protected virtual void OnActivated()
	{
	}

	protected virtual void OnDeactivated()
	{
	}

	protected IDialogService Dialogs { get; }

	protected INotificationService Notifications { get; }

	protected IDispatcher Dispatcher { get; }

	protected ILogger Logger { get; }

	private CancellationTokenSource? _lifetime;

	/// <summary>
	/// Token for work bound to page visibility, recreated after <see cref="Shutdown"/> because a covered
	/// page can come back. Retired sources are cancelled, never disposed: cards may still hold the token.
	/// </summary>
	protected CancellationToken Lifetime => (_lifetime ??= new CancellationTokenSource()).Token;

	/// <summary>
	/// Stops page-bound work by retiring <see cref="Lifetime"/>. Idempotent, since OnDisappearing also fires
	/// on a push the user pops back from.
	/// </summary>
	public virtual void Shutdown() => Interlocked.Exchange(ref _lifetime, null)?.Cancel();

	[ObservableProperty]
	private string _title = string.Empty;

	[ObservableProperty]
	private bool _isBusy;

	/// <summary>Short line under the title: "24 of 128 recipes", "Offline — cached".</summary>
	[ObservableProperty]
	private string? _statusText;

	[ObservableProperty]
	private bool _hasError;

	[ObservableProperty]
	private string? _errorMessage;

	/// <summary>Whether a Retry affordance makes sense for the current error.</summary>
	[ObservableProperty]
	private bool _canRetry;

	/// <summary>True while the first page loads with nothing on screen yet (skeleton vs spinner).</summary>
	public bool IsInitialLoad => IsBusy && !HasContent;

	/// <summary>Overridden by screens that own a list, so the skeleton knows when to hide.</summary>
	public virtual bool HasContent => false;

	/// <summary>An error with content already shown: overlay a retry card rather than replacing the rows.</summary>
	public bool ShowErrorOverlay => HasError && HasContent;

	/// <summary>
	/// The empty state, rendered outside the CollectionView because on this backend EmptyView children
	/// get their own native hosts and stay visible even when the list has items.
	/// </summary>
	public bool ShowEmpty => !HasContent && !IsBusy && !HasError;

	/// <summary>The single state a screen's <c>StateSlot</c> renders; computed so states cannot overlap.</summary>
	public LoadState LoadState => IsBusy
		? LoadState.Loading
		: HasError
			? LoadState.Error
			: HasContent ? LoadState.Content : LoadState.Empty;

	/// <summary>
	/// Raises every state derived from <see cref="HasContent"/>; call it after the list behind it changes,
	/// since a collection edit raises no PropertyChanged.
	/// </summary>
	protected void RaiseContentStateChanged()
	{
		OnPropertyChanged(nameof(HasContent));
		OnPropertyChanged(nameof(IsInitialLoad));
		OnPropertyChanged(nameof(ShowErrorOverlay));
		OnPropertyChanged(nameof(ShowEmpty));
		OnPropertyChanged(nameof(LoadState));
	}

	/// <summary>Raises the computed states, which have no backing fields.</summary>
	protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
	{
		base.OnPropertyChanged(e);

		// The string overload on ObservableObject is not virtual, hence this one.
		switch (e.PropertyName)
		{
			case nameof(IsBusy):
				base.OnPropertyChanged(nameof(IsInitialLoad));
				base.OnPropertyChanged(nameof(ShowEmpty));
				base.OnPropertyChanged(nameof(LoadState));
				break;
			case nameof(HasError):
				base.OnPropertyChanged(nameof(ShowErrorOverlay));
				base.OnPropertyChanged(nameof(ShowEmpty));
				base.OnPropertyChanged(nameof(LoadState));
				break;
		}
	}

	/// <summary>
	/// Runs <paramref name="action"/> on the UI thread; fetches complete on the thread pool, and the
	/// single UI thread owns the Qt scene graph.
	/// </summary>
	protected void Ui(Action action) => Dispatcher.RunOnUi(action);

	/// <summary>
	/// Runs <paramref name="work"/> with <see cref="IsBusy"/> set and routes failures through
	/// <see cref="ApplyFailure"/>. Returns <c>default</c> on failure or cancellation.
	/// </summary>
	protected async Task<T?> RunBusyAsync<T>(Func<CancellationToken, Task<Result<T>>> work, CancellationToken ct = default)
	{
		// A concurrent trigger is a no-op: pull-to-refresh and the OnAppearing load routinely race.
		if (IsBusy)
			return default;

		Ui(() => IsBusy = true);
		try
		{
			var result = await work(ct).ConfigureAwait(false);
			if (!result.IsSuccess)
			{
				Ui(() => ApplyFailure(result.Failure!));
				return default;
			}

			Ui(ClearError);
			return result.Value;
		}
		catch (OperationCanceledException) when (ct.IsCancellationRequested)
		{
			Logger.LogDebug("{Vm}: cancelled", GetType().Name);
			return default;
		}
		catch (Exception ex)
		{
			Logger.LogError(ex, "{Vm}: unhandled failure", GetType().Name);
			Ui(() => ApplyFailure(ResultFailure.Unknown(ex.Message, ex)));
			return default;
		}
		finally
		{
			Ui(() => IsBusy = false);
		}
	}

	protected void ApplyFailure(ResultFailure failure)
	{
		if (failure.IsSilent)
			return;

		HasError = true;
		CanRetry = failure.IsRetryable;
		ErrorMessage = failure.Message;
		Logger.LogWarning("{Vm}: {Kind} — {Message}", GetType().Name, failure.Kind, failure.Message);

		Messenger.Send(new ErrorRaisedMessage(GetType().Name, failure.Message, failure.IsRetryable, failure.Exception));
	}

	protected void ClearError()
	{
		if (!HasError)
			return;

		HasError = false;
		CanRetry = false;
		ErrorMessage = null;
	}
}
