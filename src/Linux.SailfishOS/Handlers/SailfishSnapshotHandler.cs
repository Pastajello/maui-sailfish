using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.SailfishOS.Platform.QtHost;

namespace Microsoft.Maui.SailfishOS.Handlers;

/// <summary>
/// Base for per-control handlers: every owned property re-pushes the family's full snapshot in one batch,
/// so paired state (hour+minute, cursor+selection) cannot tear. Each handler publishes its mapper as a public
/// static <c>Mapper</c> chained from <see cref="SailfishViewMapper.Mapper"/> (<see cref="SnapshotMapper{THandler}"/>).
/// <para>The snapshot is the unit: a change of any owned key pushes every key of the family, so a value an app wrote
/// to the adapter itself (<c>handler.PlatformView</c> in an <c>AppendToMapping</c>) is replaced by the next push of
/// any key (docs/custom-controls.md, "Mapper semantics").</para>
/// </summary>
public abstract class SailfishSnapshotHandler<TVirtualView> : SailfishViewHandler<TVirtualView>
	where TVirtualView : class, IView
{
	private readonly HashSet<string> _keys;

	/// <summary>The font keys of every text control; ITextStyle.Font rides as "Font".</summary>
	protected static readonly string[] FontKeys =
		{ "Font", nameof(Label.FontSize), nameof(Label.FontFamily), nameof(Label.FontAttributes) };

	/// <param name="mapper">The handler's mapper: one built by <see cref="SnapshotMapper{THandler}"/>, or one chained
	/// from it (an app's subclass); the keys that push the snapshot are read from it.</param>
	/// <param name="commandMapper">The handler's command mapper; null answers the view commands only.</param>
	/// <param name="measure">Control-specific measure run inside <see cref="SailfishMeasure.Frame"/>; null keeps
	/// the generic measure.</param>
	protected SailfishSnapshotHandler(IPropertyMapper mapper, CommandMapper? commandMapper,
		Func<IView, double, double, Size>? measure = null)
		: this(mapper, commandMapper, SailfishHandlerCore.OwnedKeys(mapper), measure)
	{
	}

	/// <param name="mapper">The handler's mapper.</param>
	/// <param name="commandMapper">The handler's command mapper; null answers the view commands only.</param>
	/// <param name="ownedKeys">The mapper keys that push the snapshot (for a mapper not built by
	/// <see cref="SnapshotMapper{THandler}"/>).</param>
	/// <param name="measure">Control-specific measure run inside <see cref="SailfishMeasure.Frame"/>; null keeps
	/// the generic measure.</param>
	protected SailfishSnapshotHandler(IPropertyMapper mapper, CommandMapper? commandMapper,
		IEnumerable<string> ownedKeys, Func<IView, double, double, Size>? measure = null)
		: base(mapper, commandMapper, measure)
	{
		_keys = new HashSet<string>(ownedKeys, StringComparer.Ordinal);
	}

	/// <summary>A mapper chained from <see cref="SailfishViewMapper.Mapper"/> whose <paramref name="keys"/> all push
	/// the snapshot (<see cref="MapSnapshot"/>). A key the generic view mapper handles too (Background on an Entry)
	/// runs both: the handler's key replaces the generic one in the mapper, and a snapshot that does not carry the
	/// generic state (the shim-applied background fill) would otherwise leave it until the next full reconcile.</summary>
	protected static PropertyMapper<TVirtualView, THandler> SnapshotMapper<THandler>(IEnumerable<string> keys)
		where THandler : SailfishSnapshotHandler<TVirtualView>
	{
		var mapper = new PropertyMapper<TVirtualView, THandler>(SailfishViewMapper.Mapper);
		var owned = keys as string[] ?? keys.ToArray();
		foreach (var key in owned)
			mapper[key] = Array.IndexOf(SailfishViewMapper.Keys, key) >= 0 ? MapSnapshotAndViewState : MapSnapshot;
		SailfishHandlerCore.RegisterOwnedKeys(mapper, owned);
		return mapper;
	}

	/// <summary>An owned key the generic view mapper handles too: the snapshot, then the generic view state (diffed,
	/// so what the snapshot already carried is not pushed twice).</summary>
	public static void MapSnapshotAndViewState(SailfishSnapshotHandler<TVirtualView> handler, TVirtualView view)
	{
		handler.PushSnapshot();
		SailfishViewMapper.MapViewState(handler, view);
	}

	/// <summary>The action of every owned key: pushes the family's snapshot to the host.</summary>
	public static void MapSnapshot(SailfishSnapshotHandler<TVirtualView> handler, TVirtualView view) =>
		handler.PushSnapshot();

	/// <summary>Whether a change of <paramref name="propertyName"/> changes the snapshot. The mapper keys by default;
	/// a family whose snapshot depends on properties it cannot list (a shape's geometry) widens it, and those
	/// properties push the snapshot too.</summary>
	public virtual bool OwnsProperty(string propertyName) => _keys.Contains(propertyName);

	public override void UpdateValue(string property)
	{
		base.UpdateValue(property);
		if (!_keys.Contains(property) && OwnsProperty(property))
			PushSnapshot();
		// A new content view or template changes which hosts exist: its subtree follows on the next loop turn.
		if (property is nameof(IContentView.Content) or nameof(TemplatedView.ControlTemplate) && ConnectedView is { } view)
			SailfishHandlerCore.SessionOf(this)?.RequestSubtree(view);
	}

	/// <summary>Pushes the snapshot now; during the connect pass it joins the connect batch instead.</summary>
	protected void PushSnapshot()
	{
		if (!IsConnecting && ConnectedView is { } view && Snapshot(view) is { } props)
			PushProps(props, SnapshotYieldsToNative);
	}

	/// <summary>True when the snapshot mirrors state native writes back (a scroll position): it is then not pushed
	/// while that write-back runs.</summary>
	protected virtual bool SnapshotYieldsToNative => false;

	/// <summary>True when the snapshot depends on the arranged size (an image's decode size): it is pushed again when
	/// an arrange changes the frame size, as a native view learns its size from its layout pass.</summary>
	protected virtual bool SnapshotDependsOnSize => false;

	private Size _arrangedSize;

	public override void PlatformArrange(Rect frame)
	{
		base.PlatformArrange(frame);
		// Only a live host needs it (collection rows are measured before their hosts exist, and their create op carries
		// the arranged state); an arrange before the host exists leaves the size unconsumed, so the first arrange after
		// it does push (a page's create op is built by the walk, before the layout pass).
		if (SnapshotDependsOnSize && frame.Size != _arrangedSize && Host is { IsAttached: true })
		{
			_arrangedSize = frame.Size;
			PushSnapshot();
		}
	}

	/// <summary>The family's full adapter snapshot, or null when not reducible yet (the reconcile poll retries).</summary>
	protected abstract Dictionary<string, object?>? Snapshot(TVirtualView view);

	protected override Dictionary<string, object?>? AdapterState() => ConnectedView is { } view ? Snapshot(view) : null;
}

/// <summary>
/// Snapshot handler for a control without a Core interface of its own (a library control): the owned keys become
/// an instance mapper chained from <see cref="SailfishViewMapper.Mapper"/>.
/// </summary>
public abstract class SailfishSnapshotHandler : SailfishSnapshotHandler<IView>
{
	protected SailfishSnapshotHandler(IEnumerable<string> keys, Func<IView, double, double, Size>? measure = null)
		: this(keys, null, measure)
	{
	}

	/// <summary>Snapshot handler that also answers MAUI commands.</summary>
	protected SailfishSnapshotHandler(IEnumerable<string> keys, CommandMapper? commands,
		Func<IView, double, double, Size>? measure = null)
		: this(keys as string[] ?? keys.ToArray(), commands, measure)
	{
	}

	private SailfishSnapshotHandler(string[] keys, CommandMapper? commands, Func<IView, double, double, Size>? measure)
		: base(SnapshotMapper<SailfishSnapshotHandler>(keys), commands, keys, measure)
	{
	}
}
