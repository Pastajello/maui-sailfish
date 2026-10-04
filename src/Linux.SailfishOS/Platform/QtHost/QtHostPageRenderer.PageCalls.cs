using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>Op batches and the model page's functions, called on a page by its id (sailfish_host_invoke), with the eval of the same call as the fallback.</summary>
internal sealed partial class QtHostPageRenderer
{
	/// <summary>Applies an op batch on model page <paramref name="pageId"/> (null = the top one). Objects on another
	/// page instance (parked or popped) must be addressed there; at the top they would leak.</summary>
	/// <returns>The ops the page could not apply (MauiModelPage's "unknown": a refused rekey, an id it does not
	/// hold); -1 when the page did not answer.</returns>
	internal int ApplyOps(IReadOnlyList<Dictionary<string, object?>> ops, string? pageId = null)
	{
		if (ops.Count == 0)
			return 0;
		var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
		var json = BridgeValue.Serialize(ops);
		var t1 = System.Diagnostics.Stopwatch.GetTimestamp();
		var answer = CallPage(pageId, "applyMauiOps", json);
		var t2 = System.Diagnostics.Stopwatch.GetTimestamp();
		_opsEvals++;
		NoteOps(ops);
		var t3 = System.Diagnostics.Stopwatch.GetTimestamp();
		LastApplyOpsSplit = (Ms(t0, t1), Ms(t1, t2), Ms(t2, t3), json.Length);
		LastOpsExpression = json;
		var unknownAt = answer.LastIndexOf(':');
		return unknownAt >= 0 && int.TryParse(answer.AsSpan(unknownAt + 1).Trim('"'), out var unknown) ? unknown : -1;
	}

	/// <summary>The handle of model page <paramref name="pageId"/> (objectName "mauiPage_&lt;id&gt;"); 0 when the page is
	/// not found.</summary>
	private long PageHandle(string pageId)
	{
		if (_pageHandles.TryGetValue(pageId, out var handle))
			return handle;
		handle = QtHostRuntime.FindObject("mauiPage_" + pageId);
		if (handle != 0)
			_pageHandles[pageId] = handle;
		return handle;
	}

	/// <summary>
	/// Calls <paramref name="method"/>(<paramref name="arg"/>) — a string argument, or none — on model page
	/// <paramref name="pageId"/> (null = the top one) without compiling JS. A page that has no handle yet, or a method
	/// it lacks, falls back to the eval of the same call (a page whose objectName is unset, a stale shell).
	/// </summary>
	internal string CallPage(string? pageId, string method, string? arg = null)
	{
		if ((pageId ?? NativeTopPageId) is { } id)
		{
			for (var attempt = 0; attempt < 2; attempt++)
			{
				var handle = PageHandle(id);
				if (handle == 0)
					break;
				var result = QtHostRuntime.Invoke(handle, method, arg, out var rc);
				if (result is not null)
				{
					PageInvokes++;
					return result;
				}
				if (rc != QtHostRuntime.SfhostEDeadHandle)
					break;
				_pageHandles.Remove(id);   // the page object went (popped, rebuilt): look it up again
			}
		}
		PageCallFallbacks++;
		var pageJs = pageId is null ? TopModelPageJs : QmlPage.ById(pageId);
		return QtHostRuntime.Eval(QmlPage.Call(pageJs, method, arg is null ? string.Empty : BridgeValue.Quote(arg)));
	}

	private long _shellHandle;
	private bool _shellMissing;   // a shell without the "mauiShell" object (an app's own shell QML): eval from then on

	/// <summary>Calls <paramref name="method"/>(<paramref name="arg"/>) on the shell's "mauiShell" object without
	/// compiling JS; null when the shell has no such object or method (the caller evals instead).</summary>
	internal string? CallShell(string method, string? arg = null)
	{
		if (_shellMissing)
			return null;
		for (var attempt = 0; attempt < 2; attempt++)
		{
			if (_shellHandle == 0 && (_shellHandle = QtHostRuntime.FindObject("mauiShell")) == 0)
			{
				_shellMissing = true;   // findChild walks the whole tree: not again every poll
				return null;
			}
			var result = QtHostRuntime.Invoke(_shellHandle, method, arg, out var rc);
			if (result is not null)
			{
				PageInvokes++;
				return result;
			}
			if (rc != QtHostRuntime.SfhostEDeadHandle)
				return null;
			_shellHandle = 0;
		}
		return null;
	}

	private void NoteOps(IReadOnlyList<Dictionary<string, object?>> ops)
	{
		if (!QtHostDiag.TraceEnabled)
			return;
		_lastOps = string.Join(",", ops.Take(6).Select(o =>
			$"{o.GetValueOrDefault("op")}:{o.GetValueOrDefault("uri") ?? string.Empty}:{o.GetValueOrDefault("id") ?? o.GetValueOrDefault("parent")}"))
			+ (ops.Count > 6 ? $",+{ops.Count - 6}" : string.Empty);
	}
}
