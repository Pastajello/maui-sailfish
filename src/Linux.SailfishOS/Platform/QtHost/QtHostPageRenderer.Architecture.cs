namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>
/// How far the renderer is from MAUI's handler-driven model: each counter has a target (mostly 0), and the diag legs
/// print them next to the bridge counters.
/// </summary>
/// <param name="ReconcileDiffPropertyPushes">Values the reconcile diff pushed to existing hosts of Sailfish handlers
/// (target 0: such a host changes only through its handler). Synthetic page surfaces (pulleys, panels) are not
/// counted: they have no handler, the reconcile is their channel.</param>
/// <param name="HandlerSnapshots">Batches the handler mappers sent (one per host per connect, then one per change).</param>
/// <param name="HandlerPropertyPushes">Values those batches and the transient pushes actually changed natively.</param>
/// <param name="TimerPolls">Heartbeat polls (MAUI_SAILFISH_POLL_MS, 2 s by default).</param>
/// <param name="KickedPolls">Polls requested by an event (navigation, tree change, visibility).</param>
/// <param name="TimerPollsWithWork">Safety-net polls that changed native state, i.e. work nothing else scheduled
/// (target 0; the heartbeat only verifies).</param>
/// <param name="LayoutPasses">Dirty layout passes.</param>
/// <param name="LayoutRequests">Layout passes handlers requested (MAUI's InvalidateMeasure, the geometry keys);
/// the only trigger besides tree changes, 0 at rest.</param>
/// <param name="SubtreeReconciles">Tree changes a container handler applied to its own subtree.</param>
/// <param name="SubtreeFallbacks">Tree changes handed to the full reconcile (page context needed, host not live).</param>
/// <param name="TreeFixups">Full reconciles on a steady page that still changed the host tree: changes no handler
/// reported (target 0).</param>
internal readonly record struct QtHostArchitectureCounters(
	long ReconcileDiffPropertyPushes,
	long HandlerSnapshots,
	long HandlerPropertyPushes,
	long TimerPolls,
	long KickedPolls,
	long TimerPollsWithWork,
	long LayoutPasses,
	long LayoutRequests,
	long SubtreeReconciles,
	long SubtreeFallbacks,
	long TreeFixups)
{
	public override string ToString() =>
		$"reconcileDiffPushes={ReconcileDiffPropertyPushes} " +
		$"handlerSnapshots={HandlerSnapshots} handlerPushes={HandlerPropertyPushes} " +
		$"polls timer={TimerPolls} kicked={KickedPolls} timerWithWork={TimerPollsWithWork} " +
		$"layoutPasses={LayoutPasses} layoutRequests={LayoutRequests} " +
		$"subtree={SubtreeReconciles} subtreeFallbacks={SubtreeFallbacks} treeFixups={TreeFixups}";
}

internal sealed partial class QtHostPageRenderer
{
	private long _reconcileDiffPropertyPushes;
	private long _handlerSnapshots;
	private long _handlerPropertyPushes;
	private long _timerPolls;
	private long _kickedPolls;
	private long _timerPollsWithWork;
	private long _opsEvals;   // applyMauiOps batches (creates, destroys, reorders)

	/// <summary>The architecture-alignment counters (see <see cref="QtHostArchitectureCounters"/>).</summary>
	public QtHostArchitectureCounters ArchitectureCounters => new(
		_reconcileDiffPropertyPushes, _handlerSnapshots, _handlerPropertyPushes,
		_timerPolls, _kickedPolls, _timerPollsWithWork, LayoutPasses, LayoutRequests,
		SubtreeReconciles, SubtreeFallbacks, TreeFixups);

	/// <summary>Every native mutation so far: property sets and batches, geometry batches, op batches.</summary>
	private long NativeWork => QtHostRuntime.Mutations + _opsEvals;
}
