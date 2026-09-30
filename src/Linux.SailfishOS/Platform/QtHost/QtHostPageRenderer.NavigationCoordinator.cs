namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

// NavigationCoordinator: the one owner of MAUI ⇄ Silica pageStack changes.
// - One operation at a time. While it runs no other push or pop starts, whichever side asks.
// - The native pageStack is the truth: each step reads it top → bottom (no page-registry lag) and an operation
//   completes only when that native stack shows its result.
// - Sources: MAUI (the MAUI stack moved, native follows), NATIVE (the pageStack moved without us: a back gesture or
//   key, MAUI follows), RESYNC (the stacks disagree in a way no operation explains: adopt the native truth, then the
//   depth sync repairs it). A failed or timed-out operation ends in a resync, never in a blind push or pop.
public sealed partial class QtHostPageRenderer
{
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

	/// <summary>The operation in flight, for diagnostics.</summary>
	internal string? NavOperationInFlight => _navOp?.ToString();

	/// <summary>
	/// One coordinator step on a settled snapshot (not animating, no transient page on top). Verifies the operation in
	/// flight, then starts at most one new one.
	/// </summary>
	private void StepNavigation(List<string> native, bool topModel, long version)
	{
		if (_navOp is { } op)
		{
			if (!NavOpDone(op, native))
			{
				if (op.Kind == NavOpKind.FollowNative && ExpectedNativeDepth() > native.Count)
					NativePopRacesBlocked++;   // MAUI still ahead: no re-push of the page the user left
				if (Environment.TickCount64 < op.Deadline)
				{
					// Still in flight. The native stack can trail the event that woke us (a popped page stays findable
					// until its deferred delete): look again shortly, not at the next heartbeat.
					if (QtHostDiag.TraceEnabled)
						QtHostDiag.Trace(QtHostDiagChannel.Navigation,
							$"navigation {op} not confirmed yet: mirror=[{string.Join(",", _nativePageIds)}] native=[{string.Join(",", native)}] " +
							$"mauiDepth={ExpectedNativeDepth()} ver={version} — re-check in {NavConfirmRetryMs} ms");
					KickIn(NavConfirmRetryMs);
					return;
				}
				NavOpsFailed++;
				QtHostDiag.Warn(QtHostDiagChannel.Navigation,
					$"navigation {op} not confirmed within {NavOpTimeoutMs} ms (mauiDepth={ExpectedNativeDepth()} " +
					$"mirror=[{string.Join(",", _nativePageIds)}] native=[{string.Join(",", native)}] ver={version}) — resync");
				_navOp = null;
				Resync(native, version, $"{op} timed out");
			}
			else
			{
				NavOpsCompleted++;
				LogNavOp("DONE", op.Source, NativeTopPageId ?? "-", $"{op} ver={version}");
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
		var expected = ExpectedNativeDepth();
		if (expected > _nativePageIds.Count)
		{
			_navOp = StartNavOp(NavOpKind.PushNative, "MAUI", expected - _nativePageIds.Count);
			PushModelPages(_navOp.Levels);
			RequestPoll();   // our own operation: confirm it on the next loop turn, not on a native event
		}
		else if (expected < _nativePageIds.Count && topModel)
		{
			_navOp = StartNavOp(NavOpKind.PopNative, "MAUI", _nativePageIds.Count - expected);
			PopModelPages(_navOp.Levels);
			// An Immediate pop (closing a modal) raises no depth notification in Silica: confirm it ourselves.
			RequestPoll();
		}
	}

	/// <summary>Whether the native stack shows the operation's result.</summary>
	private bool NavOpDone(NavOperation op, List<string> native) => op.Kind switch
	{
		// The mirror holds what was committed (a rejected push or a rolled-back pop left it unchanged, so the
		// operation still completes and the next step retries at the MAUI depth).
		NavOpKind.PushNative or NavOpKind.PopNative => native.SequenceEqual(_nativePageIds),
		NavOpKind.FollowNative => op.MauiDone && native.SequenceEqual(_nativePageIds) &&
		                          ExpectedNativeDepth() <= _nativePageIds.Count,
		_ => true,
	};

	private NavOperation StartNavOp(NavOpKind kind, string source, int levels)
	{
		// The native events (transition end, depth change) confirm it; this check catches an operation they never
		// confirm, at its deadline rather than at a poll.
		KickIn(NavOpTimeoutMs + 10);
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
		// The popped page took its QML hosts with it: drop the mirror without an eval (dead handles resolve to null).
		TearDownHosts(pageId: null, pageAlive: false);
		PruneParked();
		// The returned-to page is already visible under the dying one: restore its parked hosts now.
		if (native.Count > 0)
		{
			_tlStart = 0;
			TimelineStart("gesture-pop");
			RestoreRetention(native[^1]);
		}
		// Dead handles counted mid-transition belonged to the dying page; don't let them wipe the revealed page.
		_fullResetPending = false;
		_healedSinceReconcile = 0;
		var mauiLevels = Math.Min(levels, Math.Max(0, ExpectedNativeDepth() - native.Count));
		if (mauiLevels == 0)
		{
			LogNavOp("POP", "NATIVE_FOLLOWS_MAUI", poppedId, $"levels={levels} ver={version}");
			return;
		}
		// MAUI still sits on the page the user backed out of: no reconcile until it follows.
		_nativePopUnsynced = true;
		_navOp = StartNavOp(NavOpKind.FollowNative, "NATIVE", mauiLevels);
		LogNavOp("POP", "NATIVE_BACK", poppedId, $"{_navOp} ver={version}");
		_ = PopMauiLevelsAsync(_navOp, mauiLevels);
	}

	/// <summary>Adopts the native stack as the confirmed one; the depth sync in the same step repairs it to MAUI.</summary>
	private void Resync(List<string> native, long version, string reason)
	{
		NavResyncs++;
		QtHostDiag.Warn(QtHostDiagChannel.Navigation,
			$"navigation resync ({reason}): mirror [{string.Join(",", _nativePageIds)}] → native [{string.Join(",", native)}] " +
			$"ver={version} mauiDepth={ExpectedNativeDepth()}");
		var top = _nativePageIds.Count > 0 ? _nativePageIds[^1] : null;
		_nativePageIds.Clear();
		_nativePageIds.AddRange(native);
		// The hosts belong to the page that was on top; if it is gone, so are they.
		if (top is not null && !native.Contains(top))
			TearDownHosts(pageId: null, pageAlive: false);
		PruneParked();
		_layoutDirty = true;
	}
}
