using System.Collections.ObjectModel;
using Microsoft.Maui.Controls;

namespace Microsoft.Maui.SailfishOS.Sample.Pages;

/// <summary>A grouped source item for the CollectionView gallery.</summary>
public sealed class ItemGroup : ObservableCollection<string>
{
	public ItemGroup(string header, IEnumerable<string> items) : base(items)
	{
		Header = header;
	}

	public string Header { get; }
}

/// <summary>Picks the highlighted card for items 0 and 5 of a group, the plain card otherwise.</summary>
public sealed class ItemCardSelector : DataTemplateSelector
{
	public DataTemplate? Plain { get; set; }

	public DataTemplate? Highlight { get; set; }

	protected override DataTemplate OnSelectTemplate(object item, BindableObject container) =>
		item is string text && (text.EndsWith(" 0", StringComparison.Ordinal) || text.EndsWith(" 5", StringComparison.Ordinal))
			? Highlight!
			: Plain!;
}

/// <summary>
/// Gallery of advanced CollectionView features (grid, grouping, header/footer, empty view, scroll-to, multi-select,
/// a DataTemplateSelector).
/// </summary>
public partial class CollectionAdvancedPage : ContentPage
{
	private readonly ObservableCollection<ItemGroup> _groups = new();
	private bool _empty;
	private bool _multi;

	public CollectionAdvancedPage()
	{
		InitializeComponent();

		for (var g = 0; g < 5; g++)
		{
			var items = Enumerable.Range(0, 10).Select(i => $"G{g} item {i}");
			_groups.Add(new ItemGroup($"Group {g}", items));
		}

		Grid.ItemsSource = _groups;
		Grid.IsGrouped = true;
	}

	private void OnScrollToClicked(object? sender, EventArgs e) =>
		Grid.ScrollTo(20, 2, ScrollToPosition.MakeVisible, true);

	private void OnEmptyClicked(object? sender, EventArgs e)
	{
		_empty = !_empty;
		Grid.ItemsSource = _empty ? new ObservableCollection<ItemGroup>() : _groups;
	}

	private void OnMultiClicked(object? sender, EventArgs e)
	{
		_multi = !_multi;
		Grid.SelectionMode = _multi ? SelectionMode.Multiple : SelectionMode.Single;
	}

	private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
	{
		if (_multi)
			Title = $"Collections ({e.CurrentSelection.Count} selected)";
	}
}
