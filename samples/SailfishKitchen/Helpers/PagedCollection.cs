using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Microsoft.Maui.Dispatching;

namespace SailfishKitchen.Helpers;

/// <summary>
/// An <see cref="ObservableCollection{T}"/> that fetches its next page on demand. Items are appended one
/// at a time because a <c>Reset</c> makes a virtualized CollectionView drop its scroll offset, and every
/// mutation goes through the dispatcher because the single UI thread owns the Qt scene graph.
/// </summary>
/// <typeparam name="TItem">Row type.</typeparam>
public sealed class PagedCollection<TItem> : ObservableCollection<TItem>
{
	private readonly IDispatcher _dispatcher;
	private readonly Func<int, int, CancellationToken, Task<Page<TItem>?>> _fetch;
	private readonly object _gate = new();
	private bool _isLoading;

	public PagedCollection(IDispatcher dispatcher, Func<int, int, CancellationToken, Task<Page<TItem>?>> fetch)
	{
		_dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
		_fetch = fetch ?? throw new ArgumentNullException(nameof(fetch));
	}

	/// <summary>Rows per page, read on every load so a settings change applies immediately.</summary>
	public int PageSize { get; set; } = 12;

	public bool HasMore { get; private set; } = true;

	public int? TotalCount { get; private set; }

	public bool IsLoading
	{
		get { lock (_gate) return _isLoading; }
	}

	/// <summary>0..1 through the whole set when the total is known, else null.</summary>
	public double? Progress => TotalCount is null or 0 ? null : Math.Clamp((double)Count / TotalCount.Value, 0d, 1d);

	/// <summary>Raised after a page lands, with how many rows were added.</summary>
	public event EventHandler<PageLoadedEventArgs>? PageLoaded;

	/// <summary>Raised when a load fails; the message is user-facing.</summary>
	public event EventHandler<PageFailedEventArgs>? PageFailed;

	public Task InitializeAsync(CancellationToken ct = default) => LoadPageAsync(skip: 0, reset: true, ct);

	/// <summary>Pull-to-refresh; the old rows stay until the new page lands.</summary>
	public Task RefreshAsync(CancellationToken ct = default) => LoadPageAsync(skip: 0, reset: true, ct);

	public Task LoadMoreAsync(CancellationToken ct = default) =>
		HasMore && !IsLoading ? LoadPageAsync(skip: Count, reset: false, ct) : Task.CompletedTask;

	/// <summary>Empties the list on the UI thread; named apart from <c>ClearItems</c> so the dispatcher is not bypassed.</summary>
	public void ClearAll() => _dispatcher.RunOnUi(() => base.ClearItems());

	private async Task LoadPageAsync(int skip, bool reset, CancellationToken ct)
	{
		lock (_gate)
		{
			if (_isLoading)
				return;

			_isLoading = true;
		}

		try
		{
			var page = await _fetch(skip, PageSize, ct).ConfigureAwait(false);
			if (page is null)
			{
				// The fetcher already reported why.
				return;
			}

			// Handlers subscribe from views, and touching a BindableProperty off the UI thread crashes this host.
			_dispatcher.RunOnUi(() =>
			{
				if (reset)
					base.ClearItems();

				foreach (var item in page.Items)
					base.InsertItem(Count, item);

				HasMore = page.HasMore;
				TotalCount = page.TotalCount;

				PageLoaded?.Invoke(this, new PageLoadedEventArgs(page.Items.Count, Count, page.TotalCount, page.HasMore));
			});
		}
		catch (OperationCanceledException) when (ct.IsCancellationRequested)
		{
			// Navigating away mid-load is normal.
		}
		catch (Exception ex)
		{
			_dispatcher.RunOnUi(() => PageFailed?.Invoke(this, new PageFailedEventArgs(ex.Message, ex)));
		}
		finally
		{
			lock (_gate) { _isLoading = false; }
		}
	}

	protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
	{
		// Kept only to make the threading contract visible to editors of this class.
		base.OnCollectionChanged(e);
	}
}

/// <summary>One page of results from a paged source.</summary>
public sealed record Page<TItem>(IReadOnlyList<TItem> Items, bool HasMore, int? TotalCount);

public sealed class PageLoadedEventArgs : EventArgs
{
	public PageLoadedEventArgs(int added, int totalCountLoaded, int? knownTotal, bool hasMore)
	{
		Added = added;
		TotalCountLoaded = totalCountLoaded;
		KnownTotal = knownTotal;
		HasMore = hasMore;
	}

	public int Added { get; }

	public int TotalCountLoaded { get; }

	public int? KnownTotal { get; }

	public bool HasMore { get; }
}

public sealed class PageFailedEventArgs : EventArgs
{
	public PageFailedEventArgs(string message, Exception? exception)
	{
		Message = message;
		Exception = exception;
	}

	public string Message { get; }

	public Exception? Exception { get; }
}
