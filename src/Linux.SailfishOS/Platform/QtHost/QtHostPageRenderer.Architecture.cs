namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>
/// How far the renderer still is from MAUI's handler-driven model (alignment stage A0).
/// Each alignment stage drives one of these to its target; the diag legs print them next to the bridge counters.
/// </summary>
/// <param name="ReconcileDiffPropertyPushes">Values the reconcile diff pushed to existing hosts of Sailfish handlers
/// (0 since A2: such a host changes only through its handler). Synthetic page surfaces are not counted: they have
/// no handler until A3.</param>
/// <param name="HandlerSnapshots">Batches the handler mappers sent (one per host per connect, then one per change).</param>
/// <param name="HandlerPropertyPushes">Values those batches and the transient pushes actually changed natively.</param>
/// <param name="TimerPolls">250 ms safety-net polls.</param>
/// <param name="KickedPolls">Polls requested by an event (navigation, tree change, visibility).</param>
/// <param name="TimerPollsWithWork">Safety-net polls that changed native state, i.e. work nothing else scheduled
/// (target 0 before A7 removes the timer).</param>
/// <param name="LayoutPasses">Dirty layout passes.</param>
/// <param name="LayoutRequests">Layout passes handlers requested (MAUI's InvalidateMeasure, the geometry keys);
/// since A4 the only trigger besides tree changes, 0 at rest.</param>
/// <param name="SubtreeReconciles">Tree changes a container handler applied to its own subtree (A3).</param>
/// <param name="SubtreeFallbacks">Tree changes handed to the full reconcile (page context needed, host not live).</param>
/// <param name="TreeFixups">Full reconciles on a steady page that still changed the host tree: changes no handler
/// reported (target 0).</param>
public readonly record struct QtHostArchitectureCounters(
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

public sealed partial class QtHostPageRenderer
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
