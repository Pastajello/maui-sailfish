using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Maui.Controls;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

// Shell.SearchHandler (tracker S21, D14 a): a Silica SearchField under the page header, where the other platforms put
// the search box in the navigation bar. Typing writes Query back; the enter key confirms it (Command, OnQueryConfirmed).
internal sealed partial class QtHostPageRenderer
{
	private string _renderedSearch = string.Empty;   // shown + placeholder + enabled + pushed text, last pushed
	private SearchHandler? _searchWatched;
	private string? _searchNativeText;               // the field's text as last reported: not pushed back (no echo)
	private static readonly ConditionalWeakTable<SearchHandler, object> SearchUnsupportedWarned = new();

	/// <summary>Members a Silica SearchField does not take; set ones are reported once per handler.</summary>
	private static readonly BindableProperty[] SearchUnsupported =
	{
		SearchHandler.ClearIconProperty, SearchHandler.ClearIconNameProperty, SearchHandler.ClearIconHelpTextProperty,
		SearchHandler.QueryIconProperty, SearchHandler.QueryIconNameProperty, SearchHandler.QueryIconHelpTextProperty,
		SearchHandler.ClearPlaceholderEnabledProperty, SearchHandler.ClearPlaceholderIconProperty,
		SearchHandler.ClearPlaceholderCommandProperty, SearchHandler.ClearPlaceholderNameProperty,
		SearchHandler.ClearPlaceholderHelpTextProperty, SearchHandler.TextColorProperty, SearchHandler.PlaceholderColorProperty,
		SearchHandler.CancelButtonColorProperty, SearchHandler.BackgroundColorProperty, SearchHandler.FontFamilyProperty,
		SearchHandler.FontSizeProperty, SearchHandler.FontAttributesProperty, SearchHandler.CharacterSpacingProperty,
		SearchHandler.HorizontalTextAlignmentProperty, SearchHandler.VerticalTextAlignmentProperty,
		SearchHandler.TextTransformProperty, SearchHandler.KeyboardProperty,
	};

	/// <summary>The page's search handler as Shell reads it (Shell.GetSearchHandler on a page inside a Shell); null when
	/// it is hidden (SearchBoxVisibility.Hidden) or the header is (the navigation bar carries it elsewhere).</summary>
	internal static SearchHandler? SearchHandlerOf(Page page)
	{
		if (Shell.GetSearchHandler(page) is not { SearchBoxVisibility: not SearchBoxVisibility.Hidden } handler ||
		    !HeaderShownOf(page))
			return null;
		for (Element? e = page.Parent; e is not null; e = e.Parent)
			if (e is Shell)
				return handler;
		return null;
	}

	/// <summary>The search op when the field's state changed. Collapsible shows the field expanded: Silica has no
	/// search icon that opens it.</summary>
	private void AddSearchOps(Page page, List<Dictionary<string, object?>> ops)
	{
		var handler = SearchHandlerOf(page);
		WatchSearchHandler(handler);
		var text = handler?.Query ?? string.Empty;
		var key = handler is null ? "0" : "1|" + handler.Placeholder + "|" + (handler.IsSearchEnabled ? "e" : "d") + "|" + text;
		if (key == _renderedSearch && !_searchBlur)
			return;
		_renderedSearch = key;
		// The field already shows what it reported (a later keystroke may be on its way): only the app's own change of
		// Query is pushed into it.
		string? pushText = null;
		if (handler is not null)
		{
			if (text != _searchNativeText)
				pushText = text;
			_searchNativeText = text;
			WarnUnsupportedSearch(handler);
		}
		ops.Add(BridgeOps.Search(handler is not null, handler?.Placeholder ?? string.Empty, handler?.IsSearchEnabled ?? false, pushText,
			_searchBlur));
		_searchBlur = false;
	}

	private void WatchSearchHandler(SearchHandler? handler)
	{
		if (ReferenceEquals(_searchWatched, handler))
			return;
		if (_searchWatched is not null)
			_searchWatched.PropertyChanged -= OnSearchHandlerChanged;
		_searchWatched = handler;
		_searchNativeText = null;
		if (handler is not null)
			handler.PropertyChanged += OnSearchHandlerChanged;
	}

	private void OnSearchHandlerChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(SearchHandler.Query) or nameof(SearchHandler.Placeholder) or
		    nameof(SearchHandler.IsSearchEnabled) or nameof(SearchHandler.SearchBoxVisibility) or
		    nameof(SearchHandler.ShowsResults) or nameof(SearchHandler.ItemsSource) or nameof(SearchHandler.ItemTemplate) or
		    "DisplayMemberName")   // obsolete in MAUI 11, still honoured
			RequestPoll();
	}

	private static void WarnUnsupportedSearch(SearchHandler handler)
	{
		if (SearchUnsupportedWarned.TryGetValue(handler, out _))
			return;
		SearchUnsupportedWarned.Add(handler, SearchUnsupportedWarned);
		var set = SearchUnsupported.Where(handler.IsSet).Select(p => p.PropertyName).ToList();
		if (set.Count > 0)
			QtHostDiag.Warn(QtHostDiagChannel.QtHost,
				$"SearchHandler: {string.Join(", ", set)} not supported on Sailfish (a Silica SearchField keeps its own look)");
	}

	// --- Results (tracker S22): ShowsResults + ItemsSource as a CollectionView over the content area, on the list adapter ---

	private View? _searchResultsView;               // the rendered page's results overlay, walked after the TitleView
	private Grid? _resultsBox;
	private CollectionView? _resultsList;
	private SearchHandler? _resultsFor;
	private System.Collections.Specialized.INotifyCollectionChanged? _resultsSource;
	private string? _resultsDismissedQuery;          // a pick or the enter key closed the list for this query
	private bool _searchBlur;                       // the next search op takes the focus off the field

	/// <summary>The results overlay of the page's search handler, or null while it shows none: ShowsResults with items
	/// in ItemsSource, until an item is picked or the query confirmed (typing on brings it back).</summary>
	private View? SearchResultsViewOf(Page page)
	{
		var handler = SearchHandlerOf(page);
		WatchResultsSource(handler?.ShowsResults == true ? handler.ItemsSource : null);
		if (handler is not { ShowsResults: true, ItemsSource: { } source } || !source.Cast<object>().Any())
			return null;
		if (_resultsDismissedQuery is not null && _resultsDismissedQuery == (handler.Query ?? string.Empty))
			return null;
		_resultsDismissedQuery = null;
		if (!ReferenceEquals(_resultsFor, handler) || _resultsList is null || _resultsBox is null)
		{
			_resultsFor = handler;
			_resultsList = new CollectionView { SelectionMode = SelectionMode.Single };
			_resultsList.SelectionChanged += OnSearchResultSelected;
			_resultsBox = new Grid { Children = { _resultsList } };
		}
#pragma warning disable CS0618   // DisplayMemberName is obsolete in MAUI 11 ("use ItemTemplate"); apps that set it keep working
		var template = handler.ItemTemplate ?? DefaultResultTemplate(handler.DisplayMemberName);
#pragma warning restore CS0618
		if (!ReferenceEquals(_resultsList.ItemsSource, source))
			_resultsList.ItemsSource = source;
		if (!ReferenceEquals(_resultsList.ItemTemplate, template))
			_resultsList.ItemTemplate = template;
		// Opaque: the content stays below and a translucent list let its text show through the rows (phone, S22).
		_resultsBox.BackgroundColor = Application.Current?.RequestedTheme == AppTheme.Light
			? Microsoft.Maui.Graphics.Color.FromRgb(250, 250, 250)
			: Microsoft.Maui.Graphics.Color.FromRgb(10, 12, 14);
		if (!ReferenceEquals(_resultsBox.Parent, page))
			_resultsBox.Parent = page;   // the page's BindingContext and handler context; no logical child of it
		return _resultsBox;
	}

	private DataTemplate? _defaultResultTemplate;
	private string? _defaultResultMember;

	/// <summary>A row per item as the other platforms draw it without an ItemTemplate: its DisplayMemberName, else its
	/// text.</summary>
	[System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026",
		Justification = "DisplayMemberName is a path the app names on its own item type, as MAUI's own default template binds it.")]
	private DataTemplate DefaultResultTemplate(string? member)
	{
		if (_defaultResultTemplate is not null && _defaultResultMember == member)
			return _defaultResultTemplate;
		_defaultResultMember = member;
		return _defaultResultTemplate = new DataTemplate(() =>
		{
			var label = new Label { Padding = new Thickness(24, 14), VerticalTextAlignment = TextAlignment.Center };
			label.SetBinding(Label.TextProperty, new Binding(string.IsNullOrEmpty(member) ? "." : member, stringFormat: "{0}"));
			return label;
		});
	}

	private void WatchResultsSource(System.Collections.IEnumerable? source)
	{
		var observable = source as System.Collections.Specialized.INotifyCollectionChanged;
		if (ReferenceEquals(observable, _resultsSource))
			return;
		if (_resultsSource is not null)
			_resultsSource.CollectionChanged -= OnResultsSourceChanged;
		_resultsSource = observable;
		if (observable is not null)
			observable.CollectionChanged += OnResultsSourceChanged;
	}

	private void OnResultsSourceChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) =>
		RequestPoll();

	/// <summary>A tapped result → SearchHandler.ItemSelected (SelectedItem, OnItemSelected, then the query confirmed, as
	/// on Android and iOS); the list closes and the keyboard goes.</summary>
	private void OnSearchResultSelected(object? sender, SelectionChangedEventArgs e)
	{
		if (_resultsList is null || _resultsList.SelectedItem is not { } item || _resultsFor is not { } handler)
			return;
		_resultsList.SelectedItem = null;
		_resultsDismissedQuery = handler.Query ?? string.Empty;
		_searchBlur = true;
		QtHostDiag.Trace(QtHostDiagChannel.Input, $"search result '{item}' → SearchHandler");
		((ISearchHandlerController)handler).ItemSelected(item);
		RequestPoll();
	}

	/// <summary>The field's text → SearchHandler.Query (OnQueryChanged follows from MAUI).</summary>
	internal void ApplySearchChanged(string payload)
	{
		if (ResolveCurrentPage() is not { } page || SearchHandlerOf(page) is not { } handler)
			return;
		using var doc = JsonDocument.Parse(payload);
		var text = doc.RootElement.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? "" : "";
		_searchNativeText = text;
		if (handler.Query != text)
			handler.Query = text;
	}

	/// <summary>The enter key → the query confirmed (Command with CommandParameter, OnQueryConfirmed).</summary>
	internal void ApplySearchSubmit(string payload)
	{
		ApplySearchChanged(payload);
		if (ResolveCurrentPage() is { } page && SearchHandlerOf(page) is { } search and ISearchHandlerController handler)
		{
			_resultsDismissedQuery = search.Query ?? string.Empty;   // the results list closes with the keyboard
			RequestPoll();
			QtHostDiag.Trace(QtHostDiagChannel.Input, "search confirmed → SearchHandler");
			handler.QueryConfirmed();
		}
	}
}
