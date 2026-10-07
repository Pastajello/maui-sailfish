using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.SailfishOS.Platform.QtHost;

namespace Microsoft.Maui.SailfishOS.Handlers;

/// <summary>
/// Base of the layout handlers, as ILayoutHandler is on the other platforms: Controls' Layout reports each child
/// change through Handler.Invoke(Add/Insert/Remove/Update/UpdateZIndex/Clear), and the host tree follows at once
/// (the reconcile creates, reorders or destroys exactly those hosts) instead of waiting for a tree scan.
/// </summary>
public abstract class SailfishLayoutHandlerBase<TLayout> : SailfishSnapshotHandler<TLayout>, ILayoutHandler<NativeElementHost>
	where TLayout : class, ILayout
{
	protected SailfishLayoutHandlerBase(IPropertyMapper mapper, CommandMapper? commandMapper,
		Func<IView, double, double, Size>? measure = null)
		: base(mapper, commandMapper, measure)
	{
	}

	/// <summary>The keys every layout host takes: its background and clip.</summary>
	protected static readonly string[] ContainerKeys =
	{
		nameof(VisualElement.BackgroundColor), nameof(VisualElement.Background),
		nameof(ILayout.ClipsToBounds), nameof(Layout.IsClippedToBounds),
	};

	/// <summary>A command mapper answering the layout commands Controls raises.</summary>
	protected static CommandMapper<TLayout, THandler> LayoutCommands<THandler>()
		where THandler : SailfishLayoutHandlerBase<TLayout> =>
		new(SailfishViewMapper.CommandMapper)
		{
			[nameof(ILayoutHandler.Add)] = static (handler, _, args) => handler.Add(ChildOf(args)!),
			[nameof(ILayoutHandler.Insert)] = static (handler, _, args) =>
				handler.Insert((args as LayoutHandlerUpdate)?.Index ?? -1, ChildOf(args)!),
			[nameof(ILayoutHandler.Remove)] = static (handler, _, args) => handler.Remove(ChildOf(args)!),
			[nameof(ILayoutHandler.Update)] = static (handler, _, args) =>
				handler.Update((args as LayoutHandlerUpdate)?.Index ?? -1, ChildOf(args)!),
			[nameof(ILayoutHandler.UpdateZIndex)] = static (handler, _, args) => handler.UpdateZIndex(ChildOf(args)!),
			[nameof(ILayoutHandler.Clear)] = static (handler, _, _) => handler.Clear(),
		};

	private static IView? ChildOf(object? args) => args switch
	{
		LayoutHandlerUpdate update => update.View,
		IView view => view,
		_ => null,
	};

	// Each change is a change of this container's host subtree, applied as one batch (create/destroy/order ops) on
	// the next loop turn; several changes in one turn coalesce.
	public void Add(IView view) => ChildrenChanged();

	public void Remove(IView view) => ChildrenChanged();

	public void Clear() => ChildrenChanged();

	public void Insert(int index, IView view) => ChildrenChanged();

	public void Update(int index, IView view) => ChildrenChanged();

	public void UpdateZIndex(IView view) => ChildrenChanged();

	private void ChildrenChanged()
	{
		if (ConnectedView is { } layout)
			SailfishHandlerCore.SessionOf(this)?.RequestSubtree(layout);
	}

	// The obsolete Compatibility layouts (Compatibility.StackLayout/Grid/AbsoluteLayout…) never invoke the layout
	// commands: a child added at runtime showed only at the next poll. Their element events stand in for them.
#pragma warning disable CS0618
	private Microsoft.Maui.Controls.Compatibility.Layout? _compatWatched;

	private void WatchCompatibility(IView? view)
	{
		var layout = view as Microsoft.Maui.Controls.Compatibility.Layout;
		if (ReferenceEquals(layout, _compatWatched))
			return;
		if (_compatWatched is { } old)
		{
			old.ChildAdded -= OnCompatibilityChildChanged;
			old.ChildRemoved -= OnCompatibilityChildChanged;
			old.ChildrenReordered -= OnCompatibilityReordered;
		}
		_compatWatched = layout;
		if (layout is not null)
		{
			layout.ChildAdded += OnCompatibilityChildChanged;
			layout.ChildRemoved += OnCompatibilityChildChanged;
			layout.ChildrenReordered += OnCompatibilityReordered;
		}
	}
#pragma warning restore CS0618

	private void OnCompatibilityChildChanged(object? sender, ElementEventArgs e) => ChildrenChanged();

	private void OnCompatibilityReordered(object? sender, EventArgs e) => ChildrenChanged();

	public override void SetVirtualView(IView view)
	{
		base.SetVirtualView(view);
		WatchCompatibility(view);
	}

	protected override void DisconnectHandler(NativeElementHost platformView)
	{
		WatchCompatibility(null);
		base.DisconnectHandler(platformView);
	}

	ILayout IElementHandler<ILayout, NativeElementHost>.VirtualView => VirtualView;

	ILayout IViewHandler<ILayout, NativeElementHost>.VirtualView => VirtualView;

	NativeElementHost IElementHandler<ILayout, NativeElementHost>.PlatformView => PlatformView;
}

/// <summary>Handler of the layouts without an adapter of their own (FlexLayout, AbsoluteLayout, custom layouts):
/// a plain container host whose children MAUI's layout manager arranges.</summary>
public class SailfishLayoutHandler : SailfishLayoutHandlerBase<ILayout>
{
	public static readonly PropertyMapper<ILayout, SailfishLayoutHandler> Mapper = SnapshotMapper<SailfishLayoutHandler>(ContainerKeys);

	public static readonly CommandMapper<ILayout, SailfishLayoutHandler> CommandMapper = LayoutCommands<SailfishLayoutHandler>();

	public SailfishLayoutHandler() : this(null)
	{
	}

	public SailfishLayoutHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
		: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
	{
	}

	protected override Dictionary<string, object?>? Snapshot(ILayout view) =>
		view is VisualElement ve ? AdapterSnapshots.ContainerProps(ve) : null;
}

/// <summary>Grid container handler.</summary>
public class SailfishGridHandler : SailfishLayoutHandlerBase<IGridLayout>
{
	// Rows and columns are MAUI's layout; the adapter only mirrors the column count for diagnostics.
	private static readonly string[] Keys = [.. ContainerKeys, nameof(Grid.ColumnDefinitions)];

	public static readonly PropertyMapper<IGridLayout, SailfishGridHandler> Mapper = SnapshotMapper<SailfishGridHandler>(Keys);

	public static readonly CommandMapper<IGridLayout, SailfishGridHandler> CommandMapper = LayoutCommands<SailfishGridHandler>();

	public SailfishGridHandler() : this(null)
	{
	}

	public SailfishGridHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
		: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
	{
	}

	protected override string? AdapterUri => SailfishKeys.Adapter.Grid;

	protected override Dictionary<string, object?>? Snapshot(IGridLayout view) =>
		view is Grid grid ? AdapterSnapshots.GridProps(grid) : null;
}

/// <summary>Stack container handler (StackBase covers the oriented stacks and StackLayout).</summary>
public class SailfishStackHandler : SailfishLayoutHandlerBase<IStackLayout>
{
	private static readonly string[] Keys = [.. ContainerKeys, nameof(StackBase.Spacing), nameof(StackLayout.Orientation)];

	public static readonly PropertyMapper<IStackLayout, SailfishStackHandler> Mapper = SnapshotMapper<SailfishStackHandler>(Keys);

	public static readonly CommandMapper<IStackLayout, SailfishStackHandler> CommandMapper = LayoutCommands<SailfishStackHandler>();

	public SailfishStackHandler() : this(null)
	{
	}

	public SailfishStackHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
		: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
	{
	}

	protected override string? AdapterUri => SailfishKeys.Adapter.StackLayout;

	protected override Dictionary<string, object?>? Snapshot(IStackLayout view) =>
		view is StackBase stack ? AdapterSnapshots.StackProps(stack) : null;
}

/// <summary>Container handler: only the background crosses the bridge, and only painted containers get a host.</summary>
public class SailfishContentViewHandler : SailfishSnapshotHandler<IContentView>
{
	private static readonly string[] Keys = { nameof(VisualElement.BackgroundColor), nameof(VisualElement.Background) };

	public static readonly PropertyMapper<IContentView, SailfishContentViewHandler> Mapper = SnapshotMapper<SailfishContentViewHandler>(Keys);

	public static readonly CommandMapper<IContentView, SailfishContentViewHandler> CommandMapper = new(SailfishViewMapper.CommandMapper);

	public SailfishContentViewHandler() : this(null)
	{
	}

	public SailfishContentViewHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
		: base(mapper ?? Mapper, commandMapper ?? CommandMapper, SailfishMeasure.Content)
	{
	}

	// RefreshView paints nothing itself; the wrapped scroll surface carries the spinner and gesture.
	protected override string? AdapterUri => ConnectedView is RefreshView ? null : SailfishKeys.Adapter.ContentView;

	protected override Dictionary<string, object?>? Snapshot(IContentView view) =>
		view is VisualElement ve ? AdapterSnapshots.ContainerProps(ve) : null;
}

/// <summary>
/// Handler of a view no Sailfish handler serves (another layout, a templated view, an unknown control): the
/// reconcile hosts it as a plain container, so only its background crosses, with the generic view state.
/// </summary>
public class SailfishContainerHandler : SailfishSnapshotHandler<IView>
{
	private static readonly string[] Keys = { nameof(VisualElement.BackgroundColor), nameof(VisualElement.Background) };

	public static readonly PropertyMapper<IView, SailfishContainerHandler> Mapper = SnapshotMapper<SailfishContainerHandler>(Keys);

	public static readonly CommandMapper<IView, SailfishContainerHandler> CommandMapper = new(SailfishViewMapper.CommandMapper);

	// The generic measure, as the NullViewHandler these views had before.
	public SailfishContainerHandler() : this(null)
	{
	}

	public SailfishContainerHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
		: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
	{
	}

	protected override Dictionary<string, object?>? Snapshot(IView view) =>
		view is VisualElement ve ? AdapterSnapshots.ContainerProps(ve) : null;
}
