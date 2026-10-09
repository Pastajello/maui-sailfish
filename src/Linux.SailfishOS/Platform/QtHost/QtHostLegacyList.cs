// The legacy ListView and its cells are obsolete in MAUI 11; supporting apps that still use them is the point here.
#pragma warning disable CS0618

using System.Collections;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Maui.Controls;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>
/// The legacy ListView (tracker S32, D2 b) on the CollectionView list adapter: each ListView gets a mirror
/// CollectionView — its only visual child for the walk, the layout and the geometry pass — whose rows are the
/// ListView's own cells (ViewCell's view; TextCell and ImageCell as labels and an image bound to the cell). A row tap
/// goes back through <see cref="ListView.NotifyRowTapped(int, int, Cell)"/>, so SelectedItem, ItemTapped, ItemSelected
/// and the cell's Tapped come from MAUI itself.
/// </summary>
internal sealed class LegacyListMirror
{
	private static readonly ConditionalWeakTable<ListView, LegacyListMirror> Mirrors = new();

	public ListView Source { get; }
	public CollectionView View { get; }
	private bool _syncing;
	private RefreshView? _refresh;
	private bool _refreshSyncing;
	private HashSet<object> _visible = new();

	/// <summary>What stands in the ListView's place: the mirror, inside a RefreshView while pull-to-refresh is on
	/// (tracker S34).</summary>
	public View Root
	{
		get
		{
			if (!Source.IsPullToRefreshEnabled)
				return View;
			if (_refresh is null)
			{
				_refresh = new RefreshView { Content = View };
				_refresh.Parent = Source;
				_refresh.Refreshing += OnPulled;
				_refresh.PropertyChanged += OnRefreshChanged;
			}
			SyncRefresh();
			return _refresh;
		}
	}

	/// <summary>The mirror of <paramref name="list"/>, created on first use.</summary>
	public static LegacyListMirror Of(ListView list) => Mirrors.GetValue(list, l => new LegacyListMirror(l));

	/// <summary>The mirror when one exists (no creation).</summary>
	public static CollectionView? ExistingView(ListView list) => Mirrors.TryGetValue(list, out var mirror) ? mirror.View : null;

	private LegacyListMirror(ListView source)
	{
		Source = source;
		View = new CollectionView { SelectionMode = SelectionMode.Single };
		View.ItemTemplate = new DataTemplate(() => new LegacyCellView(this));
		View.Parent = source;   // the ListView's BindingContext and handler context; not a logical child of it
		Sync();
		source.PropertyChanged += OnSourceChanged;
		source.ScrollToRequested += OnScrollToRequested;
		View.SelectionChanged += OnMirrorSelection;
		View.Scrolled += OnMirrorScrolled;
	}

	/// <summary>ListView.IsRefreshing and RefreshControlColor → the RefreshView.</summary>
	private void SyncRefresh()
	{
		if (_refresh is null)
			return;
		_refreshSyncing = true;
		try
		{
			_refresh.IsRefreshing = Source.IsRefreshing;
			_refresh.RefreshColor = Source.RefreshControlColor;
		}
		finally
		{
			_refreshSyncing = false;
		}
	}

	/// <summary>A pull → ListView.BeginRefresh (IsRefreshing, Refreshing, RefreshCommand); a refresh the ListView does
	/// not allow (no command that can run) ends at once.</summary>
	private void OnPulled(object? sender, EventArgs e)
	{
		if (_refreshSyncing)
			return;
		Source.BeginRefresh();
		if (!Source.IsRefreshing)
			SyncRefresh();
	}

	private void OnRefreshChanged(object? sender, PropertyChangedEventArgs e)
	{
		// The RefreshView clears IsRefreshing only when the app does; a pull it starts goes through OnPulled.
		if (!_refreshSyncing && e.PropertyName == nameof(RefreshView.IsRefreshing) && _refresh is { IsRefreshing: false } && Source.IsRefreshing)
			Source.EndRefresh();
	}

	/// <summary>The mirror scrolled: ListView.Scrolled, and ItemAppearing/ItemDisappearing for the rows that came into
	/// view or left it (SendCellAppearing/Disappearing, as the platforms' list adapters report bound rows).</summary>
	private void OnMirrorScrolled(object? sender, ItemsViewScrolledEventArgs e)
	{
		Source.SendScrolled(new ScrolledEventArgs(e.HorizontalOffset, e.VerticalOffset));
		var items = FlatItems();
		var now = new HashSet<object>();
		for (var i = Math.Max(0, e.FirstVisibleItemIndex); i <= e.LastVisibleItemIndex && i < items.Count; i++)
			if (items[i] is { } item)
				now.Add(item);
		foreach (var gone in _visible)
			if (!now.Contains(gone) && LegacyCellView.CellOf(gone) is { } cell)
				Source.SendCellDisappearing(cell);
		foreach (var shown in now)
			if (!_visible.Contains(shown) && LegacyCellView.CellOf(shown) is { } cell)
				Source.SendCellAppearing(cell);
		_visible = now;
	}

	private List<object?> FlatItems()
	{
		var items = new List<object?>();
		if (Source.ItemsSource is null)
			return items;
		foreach (var entry in Source.ItemsSource)
		{
			if (Source.IsGroupingEnabled && entry is IEnumerable group)
				foreach (var item in group)
					items.Add(item);
			else if (!Source.IsGroupingEnabled)
				items.Add(entry);
		}
		return items;
	}

	private void Sync()
	{
		_syncing = true;
		try
		{
			View.IsGrouped = Source.IsGroupingEnabled;
			View.GroupHeaderTemplate = Source.IsGroupingEnabled ? new DataTemplate(() => new LegacyGroupHeaderView(this)) : null;
			View.Header = Source.Header;
			View.HeaderTemplate = Source.HeaderTemplate;
			View.Footer = Source.Footer;
			View.FooterTemplate = Source.FooterTemplate;
			View.ItemsSource = Source.ItemsSource;
			View.SelectedItem = Source.SelectedItem;
		}
		finally
		{
			_syncing = false;
		}
	}

	/// <summary>ListView.ScrollTo(item[, group], position, animated) → the mirror's ScrollTo.</summary>
	private void OnScrollToRequested(object? sender, ScrollToRequestedEventArgs e)
	{
		var request = (ITemplatedItemsListScrollToRequestedEventArgs)e;
		if (request.Item is not { } item)
			return;
		if (Source.IsGroupingEnabled && request.Group is { } group)
			View.ScrollTo(item, group, e.Position, e.ShouldAnimate);
		else
			View.ScrollTo(item, position: e.Position, animate: e.ShouldAnimate);
	}

	private void OnSourceChanged(object? sender, PropertyChangedEventArgs e)
	{
		switch (e.PropertyName)
		{
			case nameof(ListView.ItemsSource):
			case nameof(ListView.SelectedItem):
			case nameof(ListView.IsGroupingEnabled):
			case nameof(ListView.GroupHeaderTemplate):
			case nameof(ListView.GroupDisplayBinding):
			case nameof(ListView.Header):
			case nameof(ListView.HeaderTemplate):
			case nameof(ListView.Footer):
			case nameof(ListView.FooterTemplate):
				Sync();
				break;
			case nameof(ListView.IsRefreshing):
			case nameof(ListView.RefreshControlColor):
				SyncRefresh();
				break;
			case nameof(ListView.ItemTemplate):
			case nameof(ListView.RowHeight):
			case nameof(ListView.HasUnevenRows):
			case nameof(ListView.SeparatorVisibility):
			case nameof(ListView.SeparatorColor):
				// The rows are built from the template and the row height: build them again.
				View.ItemTemplate = new DataTemplate(() => new LegacyCellView(this));
				break;
		}
	}

	/// <summary>A tapped row → ListView.NotifyRowTapped with the row's cell; a ListView without selection keeps
	/// none.</summary>
	private void OnMirrorSelection(object? sender, SelectionChangedEventArgs e)
	{
		if (_syncing || View.SelectedItem is not { } item)
			return;
		var (group, index) = Locate(item);
		if (index >= 0)
			Source.NotifyRowTapped(group, index, LegacyCellView.CellOf(item));
		if (Source.SelectionMode == ListViewSelectionMode.None || !Equals(Source.SelectedItem, item))
			Sync();
	}

	/// <summary>The item's group and index in it (group 0 when the list is not grouped).</summary>
	private (int Group, int Index) Locate(object item)
	{
		if (!Source.IsGroupingEnabled)
			return (0, IndexOf(Source.ItemsSource, item));
		var g = 0;
		if (Source.ItemsSource is not null)
			foreach (var group in Source.ItemsSource)
			{
				if (group is IEnumerable items && IndexOf(items, item) is var i and >= 0)
					return (g, i);
				g++;
			}
		return (-1, -1);
	}

	private static int IndexOf(IEnumerable? items, object item)
	{
		if (items is IList list)
			return list.IndexOf(item);
		var i = 0;
		if (items is not null)
			foreach (var candidate in items)
			{
				if (Equals(candidate, item))
					return i;
				i++;
			}
		return -1;
	}

	/// <summary>The cell for <paramref name="item"/>, as the ListView would template it: its ItemTemplate (a
	/// selector's choice), else its default cell (a TextCell showing the item).</summary>
	internal Cell CreateCell(object? item)
	{
		var template = Source.ItemTemplate is DataTemplateSelector selector ? selector.SelectTemplate(item, Source) : Source.ItemTemplate;
		var cell = template?.CreateContent() as Cell ?? Source.CreateDefaultCell(item!);
		cell.BindingContext = item;
		cell.Parent = Source;
		return cell;
	}
}

/// <summary>A group header row: the ListView's GroupHeaderTemplate cell, else its GroupDisplayBinding (or the group's
/// text) as a section label.</summary>
internal sealed class LegacyGroupHeaderView : ContentView
{
	private readonly LegacyListMirror _mirror;
	private object? _built;

	public LegacyGroupHeaderView(LegacyListMirror mirror) => _mirror = mirror;

	protected override void OnBindingContextChanged()
	{
		base.OnBindingContextChanged();
		var group = BindingContext;
		if (group is null || ReferenceEquals(group, _built))
			return;
		_built = group;
		var list = _mirror.Source;
		if (list.GroupHeaderTemplate?.CreateContent() is Cell cell)
		{
			cell.BindingContext = group;
			cell.Parent = list;
			Content = LegacyCellView.ViewOf(cell);
			return;
		}
		var label = new Label { FontAttributes = FontAttributes.Bold, Padding = new Thickness(16, 12, 16, 4) };
		if (list.GroupDisplayBinding is Binding binding)
			label.SetBinding(Label.TextProperty, new Binding(binding.Path, binding.Mode, binding.Converter, binding.ConverterParameter, binding.StringFormat));
		else
			label.Text = group.ToString();
		Content = label;
	}
}

/// <summary>A mirror row: the cell's view (ViewCell), or labels and an image bound to a TextCell/ImageCell.</summary>
internal sealed class LegacyCellView : ContentView
{
	private static readonly ConditionalWeakTable<object, Cell> Cells = new();
	private static readonly HashSet<Type> UnsupportedWarned = new();

	private readonly LegacyListMirror _mirror;
	private object? _built;

	public LegacyCellView(LegacyListMirror mirror) => _mirror = mirror;

	/// <summary>The cell a row was built from (the tap passes it to NotifyRowTapped).</summary>
	internal static Cell? CellOf(object item) => Cells.TryGetValue(item, out var cell) ? cell : null;

	protected override void OnBindingContextChanged()
	{
		base.OnBindingContextChanged();
		var item = BindingContext;
		if (item is null || ReferenceEquals(item, _built))
			return;
		_built = item;
		var cell = _mirror.CreateCell(item);
		Cells.AddOrUpdate(item, cell);
		var list = _mirror.Source;
		var view = ViewOf(cell);
		// SeparatorVisibility.Default draws a line under each row, as the ListView does on Android and iOS.
		Content = list.SeparatorVisibility == SeparatorVisibility.Default
			? new Grid
			{
				RowDefinitions = { new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) },
				Children = { view, Separator(list) },
			}
			: view;
		HeightRequest = cell.Height > 0 && list.HasUnevenRows ? cell.Height
			: !list.HasUnevenRows && list.RowHeight > 0 ? list.RowHeight
			: -1;
	}

	private static BoxView Separator(ListView list)
	{
		var faint = Application.Current?.RequestedTheme == AppTheme.Light
			? Microsoft.Maui.Graphics.Color.FromRgba(0, 0, 0, 40)
			: Microsoft.Maui.Graphics.Color.FromRgba(255, 255, 255, 40);
		var line = new BoxView { HeightRequest = 1, Color = list.SeparatorColor ?? faint };
		Grid.SetRow(line, 1);
		return line;
	}

	internal static View ViewOf(Cell cell)
	{
		switch (cell)
		{
			case ViewCell viewCell:
				return viewCell.View ?? new ContentView();
			case ImageCell imageCell:
			{
				var image = new Image { WidthRequest = 48, HeightRequest = 48, Aspect = Aspect.AspectFit, VerticalOptions = LayoutOptions.Center };
				image.SetBinding(Image.SourceProperty, new Binding(nameof(ImageCell.ImageSource), source: imageCell));
				return new HorizontalStackLayout { Padding = new Thickness(16, 8), Spacing = 16, Children = { image, TextStack(imageCell) } };
			}
			case TextCell textCell:
			{
				var stack = TextStack(textCell);
				stack.Padding = new Thickness(16, 8);
				return stack;
			}
			default:
				lock (UnsupportedWarned)
					if (UnsupportedWarned.Add(cell.GetType()))
						QtHostDiag.Warn(QtHostDiagChannel.QmlObject,
							$"ListView cell {cell.GetType().Name} is not supported on Sailfish (TextCell, ImageCell and ViewCell are); the row shows nothing — use a ViewCell or a CollectionView");
				return new ContentView();
		}
	}

	private static VerticalStackLayout TextStack(TextCell cell)
	{
		var text = new Label();
		text.SetBinding(Label.TextProperty, new Binding(nameof(TextCell.Text), source: cell));
		var detail = new Label { FontSize = 14 };
		detail.SetBinding(Label.TextProperty, new Binding(nameof(TextCell.Detail), source: cell));
		detail.SetBinding(IsVisibleProperty, new Binding(nameof(TextCell.Detail), source: cell, converter: NotEmpty.Instance));
		if (cell.IsSet(TextCell.TextColorProperty))
			text.SetBinding(Label.TextColorProperty, new Binding(nameof(TextCell.TextColor), source: cell));
		if (cell.IsSet(TextCell.DetailColorProperty))
			detail.SetBinding(Label.TextColorProperty, new Binding(nameof(TextCell.DetailColor), source: cell));
		return new VerticalStackLayout { VerticalOptions = LayoutOptions.Center, Children = { text, detail } };
	}

	private sealed class NotEmpty : IValueConverter
	{
		public static readonly NotEmpty Instance = new();
		public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
			value is string { Length: > 0 };
		public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
			throw new NotSupportedException();
	}
}
