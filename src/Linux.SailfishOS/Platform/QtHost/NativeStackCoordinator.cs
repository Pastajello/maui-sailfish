namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

// NativeStackCoordinator: the one owner of MAUI ⇄ Silica pageStack changes.
// - One operation at a time. While it runs no other push or pop starts, whichever side asks.
// - The native pageStack is the truth: each step reads it top → bottom (no page-registry lag) and an operation
//   completes only when that native stack shows its result.
// - Sources: MAUI (the MAUI stack moved, native follows), NATIVE (the pageStack moved without us: a back gesture or
//   key, MAUI follows), RESYNC (the stacks disagree in a way no operation explains: adopt the native truth, then the
//   depth sync repairs it). A failed or timed-out operation ends in a resync, never in a blind push or pop.

internal enum NavOpKind
{
	/// <summary>Push model pages up to the MAUI depth.</summary>
	PushNative,
	/// <summary>Pop model pages down to the MAUI depth.</summary>
	PopNative,
	/// <summary>The pageStack popped by itself; MAUI pops as many levels as it is still ahead.</summary>
	FollowNative,
}

internal sealed class NavOperation
{
	public required long Id { get; init; }
	public required NavOpKind Kind { get; init; }
	public required string Source { get; init; }
	public required int Levels { get; init; }
	public required long Deadline { get; init; }

	/// <summary>FollowNative: the MAUI pops completed.</summary>
	public bool MauiDone { get; set; }

	public override string ToString() => $"#{Id} {Kind}×{Levels} src={Source}";
}

/// <summary>What the coordinator asks of the renderer: the MAUI depth, the native operations, and the host-tree
/// consequences of a stack change. The renderer implements it; tests record it.</summary>
internal interface INativeStackOwner
{
	/// <summary>The native depth the MAUI navigation state calls for.</summary>
	int ExpectedNativeDepth();

	void PushModelPages(int levels);
	void PopModelPages(int levels);

	/// <summary>The pageStack popped by itself: the popped page's hosts went with it; <paramref name="returnedTo"/>
	/// (null when the stack is empty) is visible again.</summary>
	void OnNativePopped(string? returnedTo);

	/// <summary>The mirror was replaced by the native stack; <paramref name="topGone"/>: the previous top page is gone.</summary>
	void OnResynced(bool topGone);

	/// <summary>MAUI pops <paramref name="levels"/> to follow a native pop, and marks <paramref name="op"/> done.</summary>
	void FollowNative(NavOperation op, int levels);

	void KickIn(long ms);
	void RequestPoll();
	void LogNavOp(string op, string source, string pageId, string nativeReport);
}

/// <summary>
/// One coordinator step per settled native snapshot (<see cref="Step"/>): it verifies the operation in flight against
/// the native stack, then starts at most one new one. It owns the confirmed mirror of the native stack
/// (<see cref="Mirror"/>, page ids bottom → top), which the native push/pop code commits to.
/// </summary>
internal sealed class NativeStackCoordinator(INativeStackOwner owner)
{
	private readonly INativeStackOwner _owner = owner;
	private readonly List<string> _nativePageIds = new();

	/// <summary>The confirmed native stack (MauiShell.mauiPages ids, bottom → top).</summary>
	internal List<string> Mirror => _nativePageIds;

	/// <summary>The operation in flight (null = idle).</summary>
	internal NavOperation? Operation => _navOp;

	/// <summary>The last step saw a native pop MAUI has not followed yet (no reconcile until it does); the poll clears it.</summary>
	internal bool PopUnsynced { get; set; }

	/// <summary>Native-side pops (Silica back gesture) synced back into MAUI.</summary>
	public long NativePopSyncs { get; private set; }

	/// <summary>Depth-sync pushes suppressed because a native→MAUI pop was still in flight (the pop-vs-push race).</summary>
	public long NativePopRacesBlocked { get; private set; }



	private NavOperation? _navOp;          // the operation in flight (null = idle)
	private long _navOpIds;

	/// <summary>An operation not confirmed natively within this ends in a resync.</summary>
	private const int NavOpTimeoutMs = 3000;

	/// <summary>How soon an unconfirmed operation looks at the native stack again.</summary>
	private const int NavConfirmRetryMs = 60;

	/// <summary>Navigation operations the native stack confirmed.</summary>
	public long NavOpsCompleted { get; private set; }

	/// <summary>Operations that timed out or failed verification (each followed by a resync).</summary>
	public long NavOpsFailed { get; private set; }

	/// <summary>Times the coordinator adopted a native stack no operation explained.</summary>
	public long NavResyncs { get; private set; }


	/// <summary>
	/// One coordinator step on a settled snapshot (not animating, no transient page on top). Verifies the operation in
	/// flight, then starts at most one new one.
	/// </summary>
	internal void Step(List<string> native, bool topModel, long version)
	{
		if (_navOp is { } op)
		{
			if (!NavOpDone(op, native))
			{
				if (op.Kind == NavOpKind.FollowNative && _owner.ExpectedNativeDepth() > native.Count)
					NativePopRacesBlocked++;   // MAUI still ahead: no re-push of the page the user left
				if (Environment.TickCount64 < op.Deadline)
				{
					// Still in flight. The native stack can trail the event that woke us (a popped page stays findable
					// until its deferred delete): look again shortly, not at the next heartbeat.
					if (QtHostDiag.TraceEnabled)
						QtHostDiag.Trace(QtHostDiagChannel.Navigation,
							$"navigation {op} not confirmed yet: mirror=[{string.Join(",", _nativePageIds)}] native=[{string.Join(",", native)}] " +
							$"mauiDepth={_owner.ExpectedNativeDepth()} ver={version} — re-check in {NavConfirmRetryMs} ms");
					_owner.KickIn(NavConfirmRetryMs);
					return;
				}
				NavOpsFailed++;
				QtHostDiag.Warn(QtHostDiagChannel.Navigation,
					$"navigation {op} not confirmed within {NavOpTimeoutMs} ms (mauiDepth={_owner.ExpectedNativeDepth()} " +
					$"mirror=[{string.Join(",", _nativePageIds)}] native=[{string.Join(",", native)}] ver={version}) — resync");
				_navOp = null;
				Resync(native, version, $"{op} timed out");
			}
			else
			{
				NavOpsCompleted++;
				_owner.LogNavOp("DONE", op.Source, (_nativePageIds.Count > 0 ? _nativePageIds[^1] : "-"), $"{op} ver={version}");
				_navOp = null;
			}
		}

		// --- native moved without us: a pop is a prefix of the confirmed stack ---
		if (native.Count < _nativePageIds.Count && IsPrefixOfMirror(native))
		{
			FollowNativePop(native, version);
			return;
		}
		// --- anything else the stacks disagree on: the native stack is the truth ---
		if (!native.SequenceEqual(_nativePageIds))
			Resync(native, version, "native stack differs from the confirmed mirror");

		// --- MAUI moved: bring native to the MAUI depth ---
		var expected = _owner.ExpectedNativeDepth();
		if (expected > _nativePageIds.Count)
		{
			_navOp = StartNavOp(NavOpKind.PushNative, "MAUI", expected - _nativePageIds.Count);
			_owner.PushModelPages(_navOp.Levels);
			_owner.RequestPoll();   // our own operation: confirm it on the next loop turn, not on a native event
		}
		else if (expected < _nativePageIds.Count && topModel)
		{
			_navOp = StartNavOp(NavOpKind.PopNative, "MAUI", _nativePageIds.Count - expected);
			_owner.PopModelPages(_navOp.Levels);
			// An Immediate pop (closing a modal) raises no depth notification in Silica: confirm it ourselves.
			_owner.RequestPoll();
		}
	}

	/// <summary>Whether the native stack shows the operation's result.</summary>
	private bool NavOpDone(NavOperation op, List<string> native) => op.Kind switch
	{
		// The mirror holds what was committed (a rejected push or a rolled-back pop left it unchanged, so the
		// operation still completes and the next step retries at the MAUI depth).
		NavOpKind.PushNative or NavOpKind.PopNative => native.SequenceEqual(_nativePageIds),
		NavOpKind.FollowNative => op.MauiDone && native.SequenceEqual(_nativePageIds) &&
		                          _owner.ExpectedNativeDepth() <= _nativePageIds.Count,
		_ => true,
	};

	private NavOperation StartNavOp(NavOpKind kind, string source, int levels)
	{
		// The native events (transition end, depth change) confirm it; this check catches an operation they never
		// confirm, at its deadline rather than at a poll.
		_owner.KickIn(NavOpTimeoutMs + 10);
		return new()
		{
			Id = ++_navOpIds,
			Kind = kind,
			Source = source,
			Levels = levels,
			Deadline = Environment.TickCount64 + NavOpTimeoutMs,
		};
	}

	private bool IsPrefixOfMirror(List<string> native)
	{
		for (var i = 0; i < native.Count; i++)
			if (native[i] != _nativePageIds[i])
				return false;
		return true;
	}

	/// <summary>
	/// The pageStack popped by itself (back gesture, Back key). Commits the native stack, drops the popped page's
	/// hosts, restores the returned-to page's parked ones, and has MAUI pop exactly the levels it is still ahead —
	/// none when MAUI already popped (the Back key routed through TryPop while Silica popped on the same key).
	/// </summary>
	private void FollowNativePop(List<string> native, long version)
	{
		NativePopSyncs++;
		var levels = _nativePageIds.Count - native.Count;
		var poppedId = _nativePageIds[^1];
		QtHostDiag.Trace(QtHostDiagChannel.Navigation,
			$"native pop detected ([{string.Join(",", _nativePageIds)}] → [{string.Join(",", native)}] ver={version}) — syncing MAUI");
		_nativePageIds.Clear();
		_nativePageIds.AddRange(native);
		_owner.OnNativePopped(native.Count > 0 ? native[^1] : null);
		var mauiLevels = Math.Min(levels, Math.Max(0, _owner.ExpectedNativeDepth() - native.Count));
		if (mauiLevels == 0)
		{
			_owner.LogNavOp("POP", "NATIVE_FOLLOWS_MAUI", poppedId, $"levels={levels} ver={version}");
			return;
		}
		// MAUI still sits on the page the user backed out of: no reconcile until it follows.
		PopUnsynced = true;
		_navOp = StartNavOp(NavOpKind.FollowNative, "NATIVE", mauiLevels);
		_owner.LogNavOp("POP", "NATIVE_BACK", poppedId, $"{_navOp} ver={version}");
		_owner.FollowNative(_navOp, mauiLevels);
	}

	/// <summary>Adopts the native stack as the confirmed one; the depth sync in the same step repairs it to MAUI.</summary>
	private void Resync(List<string> native, long version, string reason)
	{
		NavResyncs++;
		QtHostDiag.Warn(QtHostDiagChannel.Navigation,
			$"navigation resync ({reason}): mirror [{string.Join(",", _nativePageIds)}] → native [{string.Join(",", native)}] " +
			$"ver={version} mauiDepth={_owner.ExpectedNativeDepth()}");
		var top = _nativePageIds.Count > 0 ? _nativePageIds[^1] : null;
		_nativePageIds.Clear();
		_nativePageIds.AddRange(native);
		// The hosts belong to the page that was on top; if it is gone, so are they.
		_owner.OnResynced(topGone: top is not null && !native.Contains(top));
	}
}
