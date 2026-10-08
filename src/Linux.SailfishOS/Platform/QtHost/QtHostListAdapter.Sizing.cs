using Microsoft.Maui.Controls;
using static Microsoft.Maui.SailfishOS.Platform.QtHost.QtHostCollectionBridge;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>ItemSizingStrategy.MeasureFirstItem (tracker S28): the first item is templated and measured, every other
/// row takes its extent and is templated only when its delegate materializes.</summary>
internal sealed partial class QtHostListAdapter
{
	private double _firstItemExtentDp = double.NaN;   // the first item's extent in this build (NaN: none yet)
	private bool _lazyHasTap, _lazyHasGestures;       // the first item's template flags, for rows not templated yet

	/// <summary>Templates are created lazily: MeasureFirstItem on a vertical list (a carousel pages, a horizontal list
	/// needs each item's height) with one template for every item (a selector's may differ in size).</summary>
	private bool MeasureFirstOnly =>
		View is StructuredItemsView { ItemSizingStrategy: ItemSizingStrategy.MeasureFirstItem } &&
		!Carousel && !Horizontal && View.ItemTemplate is not DataTemplateSelector;

	/// <summary>Rows built without views get them before their delegate shows them, laid out at the row's extent.</summary>
	internal void TemplateLazyCells(Row row)
	{
		if (!row.LazyCells)
			return;
		row.LazyCells = false;
		var created = 0;
		using (_bridge.MappingScope())   // laying them out raises measure events of their own
		{
			for (var c = 0; c < row.CellViews.Count && c < row.CellItems.Count; c++)
			{
				if (row.CellViews[c] is not null || CreateItemView(row.CellItems[c]) is not { } view)
					continue;
				QtHostLayout.AttachHandlers(view, _renderer.MauiContext);
				QtHostLayout.MeasureAndArrangeItemFixed(view, row.CellWidthDp, row.HeightDp);
				row.CellViews[c] = view;
				WatchRow(row, view);
				created++;
			}
		}
		if (created == 0)
			return;
		LazyTemplated += created;
		ApplySelectionStates();   // a selected item templated now shows its Selected state
	}

	/// <summary>A vertical list measured without a bound along its axis (inside a ScrollView or a StackLayout, tracker
	/// S29): it is as tall as its rows, so the outer scroller scrolls it (the native list stays still) and every row is on
	/// it, past the usual delegate cap. It cannot virtualize there, as a RecyclerView in a ScrollView cannot.</summary>
	internal bool Unbounded { get; private set; }

	private bool _unboundedWarned;

	internal void SetUnbounded(bool unbounded)
	{
		if (unbounded == Unbounded)
			return;
		Unbounded = unbounded;
		Push("mauiUnbounded", unbounded);
	}

	/// <summary>The delegates this list may hold: all its rows when unbounded.</summary>
	internal int DelegateCap
	{
		get
		{
			if (!Unbounded)
				return MaxDelegates;
			if (Rows.Count > UnboundedRowsWarning && !_unboundedWarned)
			{
				_unboundedWarned = true;
				QtHostDiag.Warn(QtHostDiagChannel.QmlObject,
					$"collection '{Host}' has {Rows.Count} rows and no height bound (inside a ScrollView or StackLayout): every row is built natively — give it a height, or move it out of the ScrollView (Header/Footer) to keep it virtualized");
			}
			return Math.Max(MaxDelegates, Rows.Count + 8);
		}
	}

	internal const int UnboundedRowsWarning = 200;

	/// <summary>Item views templated on materialization rather than at build (diagnostics).</summary>
	internal int LazyTemplated { get; private set; }
}
