using Microsoft.Maui.Controls;
using static Microsoft.Maui.SailfishOS.Platform.QtHost.QtHostCollectionBridge;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>Visual states of the item views, as MAUI's Android and iOS lists set them (tracker S27, D4 a): Selected or
/// Normal on a selectable list's cell roots, CurrentItem/PreviousItem/NextItem/DefaultItem on a carousel's; the carousel's
/// IsDragging, IsScrolling and VisibleViews.</summary>
internal sealed partial class QtHostListAdapter
{
	private HashSet<View> _vsmSelected = new();
	private readonly Dictionary<View, string> _carouselStates = new();

	/// <summary>Selected on the cell roots of the selected items, Normal on those that left the selection. The app styles
	/// the Selected state; Silica's selection highlight is not drawn (D4 a).</summary>
	internal void ApplySelectionStates()
	{
		var now = new HashSet<View>();
		if (View is SelectableItemsView { SelectionMode: not SelectionMode.None })
			foreach (var (row, cell) in SelectedCells)
				if (row < Rows.Count && cell < Rows[row].CellViews.Count && Rows[row].CellViews[cell] is { } view)
					now.Add(view);
		foreach (var view in _vsmSelected)
			if (!now.Contains(view))
				VisualStateManager.GoToState(view, VisualStateManager.CommonStates.Normal);
		foreach (var view in now)
			if (!_vsmSelected.Contains(view))
				VisualStateManager.GoToState(view, VisualStateManager.CommonStates.Selected);
		_vsmSelected = now;
	}

	/// <summary>The carousel's item states around page <paramref name="position"/> (a looping carousel wraps), and its
	/// VisibleViews: the current page's view, with its neighbours while peek areas show them.</summary>
	internal void ApplyCarouselStates(int position)
	{
		if (View is not CarouselView carousel || position < 0)
			return;
		var pages = Rows.Count(r => r.Kind == KindItem);
		var peek = PeekStartDp > 0 || PeekEndDp > 0;
		var visible = new List<View>();
		var page = 0;
		foreach (var row in Rows)
		{
			if (row.Kind != KindItem)
				continue;
			var index = page++;
			if (row.CellViews.FirstOrDefault() is not { } view)
				continue;
			var state = index == position ? CarouselView.CurrentItemVisualState
				: index == Neighbour(position - 1) ? CarouselView.PreviousItemVisualState
				: index == Neighbour(position + 1) ? CarouselView.NextItemVisualState
				: CarouselView.DefaultItemVisualState;
			if (!_carouselStates.TryGetValue(view, out var was) || was != state)
			{
				_carouselStates[view] = state;
				VisualStateManager.GoToState(view, state);
			}
			if (state == CarouselView.CurrentItemVisualState || (peek && state != CarouselView.DefaultItemVisualState))
				visible.Add(view);
		}
		var views = carousel.VisibleViews;
		if (!views.SequenceEqual(visible))
		{
			views.Clear();
			foreach (var view in visible)
				views.Add(view);
		}

		int Neighbour(int i) => carousel.Loop && pages > 0 ? (i % pages + pages) % pages : i;
	}

	/// <summary>The native carousel started or stopped a drag or a move: IsDragging and IsScrolling follow.</summary>
	internal void OnCarouselMotion(bool dragging, bool moving)
	{
		if (View is not CarouselView carousel)
			return;
		if (carousel.IsDragging != dragging)
			carousel.SetIsDragging(dragging);
		if (carousel.IsScrolling != moving)
			carousel.IsScrolling = moving;
	}
}
