#:package Microsoft.Diagnostics.Tracing.TraceEvent@3.1.23
// Summarizes an EventPipe .nettrace from the device (tools/sf-trace.sh): threads, the UI thread split
// (Qt event loop / shim P/Invoke / managed), a UI-thread timeline, per-window phase costs, caller/callee
// chains, GC pauses and exceptions. How to read the stacks: docs/profiling.md.
//
// Usage: dotnet run tools/sf-trace-analyze.cs -- TRACE.nettrace [option=value ...]
//   anchor=<frame>    t=0 at the first sample containing this frame (default: trace start), e.g.
//                     anchor=DemoTour so windows match the Kitchen tour's "TOUR +ms" log lines
//   bucket=250        timeline bucket, ms
//   window=FROM:TO    report UI-thread work in this window (ms from the anchor; repeatable)
//   phase=<frame>     with window=: inclusive UI-thread ms per window for this frame (repeatable)
//   focus=<frame>     callers, caller chains and callees of this frame on the UI thread (repeatable)
//   focuswin=FROM:TO  limit focus= to this window; depth=3 is the caller-chain length
using System.Text;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;

var path = args[0];
var opt = args.Skip(1).Select(a => a.Split('=', 2)).ToLookup(a => a[0], a => a.Length > 1 ? a[1] : "");
var bucketMs = opt["bucket"].Select(double.Parse).DefaultIfEmpty(250).First();
var anchor = opt["anchor"].FirstOrDefault();
var windows = opt["window"].Select(w => w.Split(':')).Select(w => (from: double.Parse(w[0]), to: double.Parse(w[1]))).ToList();
var focuses = opt["focus"].ToList();
var focusWin = opt["focuswin"].Select(w => w.Split(':')).Select(w => (from: double.Parse(w[0]), to: double.Parse(w[1]))).DefaultIfEmpty((from: 0.0, to: 1e12)).First();
var depth = opt["depth"].Select(int.Parse).DefaultIfEmpty(3).First();

var etlx = Path.ChangeExtension(path, ".etlx");
if (!File.Exists(etlx) || File.GetLastWriteTimeUtc(etlx) < File.GetLastWriteTimeUtc(path))
	etlx = TraceLog.CreateFromEventPipeDataFile(path, etlx);
using var log = new TraceLog(etlx);

string[] waitLeaf = { "TimedWait", "ThreadWaitInfo.Wait", "WaitAndAcquire", "LowLevelLifoSemaphore", "WaitOneCore",
	"WaitForSignal", "Monitor.Wait", "Thread.Sleep", "SleepInternal", "WaitNative", "Interop+Sys.Poll", "Interop+Sys.WaitForSocketEvents",
	"epoll", "WaitOne", "WaitMultiple", "Interop+Sys.Read(" };
bool IsWait(string leaf) => waitLeaf.Any(w => leaf.Contains(w));

var samples = new List<(int tid, double t, string[] frames)>();
var gcs = new List<(double t, int gen, string reason, string type)>();
var gcEnds = new List<double>();
var suspends = new List<(double start, double end, string reason)>();
double? suspendStart = null; string suspendReason = "";
var jits = new List<(double t, string name)>();
var exceptions = new List<(double t, string type, string msg)>();
var contention = new List<(double t, int tid)>();
var eventNames = new Dictionary<string, int>();

foreach (var ev in log.Events)
{
	var key = ev.ProviderName + "/" + ev.EventName;
	eventNames[key] = eventNames.GetValueOrDefault(key) + 1;
	var t = ev.TimeStampRelativeMSec;
	if (ev.ProviderName == "Microsoft-DotNETCore-SampleProfiler")
	{
		var frames = new List<string>();
		for (var cs = ev.CallStack(); cs != null; cs = cs.Caller)
		{
			var n = cs.CodeAddress.FullMethodName;
			if (string.IsNullOrEmpty(n)) n = "?" + cs.CodeAddress.ModuleName;
			frames.Add(n);
		}
		samples.Add((ev.ThreadID, t, frames.ToArray()));
		continue;
	}
	if (ev.ProviderName != "Microsoft-Windows-DotNETRuntime") continue;
	switch (ev.EventName)
	{
		case "GC/Start":
			gcs.Add((t, Convert.ToInt32(ev.PayloadByName("Depth")), ev.PayloadStringByName("Reason"), ev.PayloadStringByName("Type")));
			break;
		case "GC/Stop": gcEnds.Add(t); break;
		case "GC/SuspendEEStart": suspendStart = t; suspendReason = ev.PayloadStringByName("Reason"); break;
		case "GC/RestartEEStop":
			if (suspendStart is { } s) suspends.Add((s, t, suspendReason));
			suspendStart = null; break;
		case "Method/JittingStarted":
			jits.Add((t, ev.PayloadStringByName("MethodNamespace") + "." + ev.PayloadStringByName("MethodName")));
			break;
		case "Exception/Start":
			exceptions.Add((t, ev.PayloadStringByName("ExceptionType"), ev.PayloadStringByName("ExceptionMessage")));
			break;
		case "Contention/Start": contention.Add((t, ev.ThreadID)); break;
	}
}

var sb = new StringBuilder();
void P(string s = "") => sb.AppendLine(s);
string Short(string n) => n.Length > 110 ? n[..110] + "…" : n;

var t0 = anchor is null
	? samples.Select(s => s.t).DefaultIfEmpty(0).Min()
	: samples.Where(s => s.frames.Any(f => f.Contains(anchor))).Select(s => s.t).DefaultIfEmpty(0).Min();
double Rel(double t) => t - t0;
var interval = samples.GroupBy(s => s.tid).Where(g => g.Count() > 100)
	.Select(g => { var ts = g.Select(x => x.t).OrderBy(x => x).ToArray(); return (ts[^1] - ts[0]) / (ts.Length - 1); })
	.DefaultIfEmpty(1).Min();
P($"trace: {path}");
P($"samples={samples.Count} span={samples.Max(s => s.t) - samples.Min(s => s.t):F0} ms sampleInterval≈{interval:F2} ms anchor '{anchor ?? "trace start"}' at {t0:F0} ms");
P();

var uiTid = samples.Where(s => s.frames.Any(f => f.Contains("sailfish_host_exec"))).GroupBy(s => s.tid)
	.OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();

string Role(IEnumerable<string[]> stacks)
{
	var roots = stacks.Select(f => f.Reverse().FirstOrDefault(x => !x.StartsWith("?")) ?? "?").GroupBy(x => x)
		.OrderByDescending(g => g.Count()).First().Key;
	return Short(roots);
}

P("== threads (busy = non-wait samples) ==");
var threads = samples.GroupBy(s => s.tid).Select(g => new
{
	tid = g.Key, total = g.Count(), busy = g.Count(s => s.frames.Length > 0 && !IsWait(s.frames[0])),
	first = g.Min(s => s.t), last = g.Max(s => s.t), role = Role(g.Select(s => s.frames))
}).OrderByDescending(x => x.busy).ToList();
foreach (var th in threads)
	P($"  tid {th.tid,6}{(th.tid == uiTid ? " [UI]" : "     ")} samples {th.total,6} busy {th.busy,6} ({Rel(th.first),7:F0}..{Rel(th.last),7:F0} ms)  root: {th.role}");
P();

void TopFor(IEnumerable<(int tid, double t, string[] frames)> set, string title, int n = 25)
{
	var list = set.ToList();
	if (list.Count == 0) return;
	P($"== {title}: {list.Count} samples ≈ {list.Count * interval:F0} ms ==");
	P("  exclusive:");
	foreach (var g in list.GroupBy(s => s.frames.FirstOrDefault() ?? "?").OrderByDescending(g => g.Count()).Take(n))
		P($"    {g.Count() * 100.0 / list.Count,5:F1}%  {Short(g.Key)}");
	P("  inclusive:");
	var inc = new Dictionary<string, int>();
	foreach (var s in list)
		foreach (var f in s.frames.Distinct()) inc[f] = inc.GetValueOrDefault(f) + 1;
	foreach (var kv in inc.OrderByDescending(k => k.Value).Take(n))
		P($"    {kv.Value * 100.0 / list.Count,5:F1}%  {Short(kv.Key)}");
	P();
}

// UI thread split: leaf == exec → in Qt (idle or busy, EventPipe cannot tell); other leaf under exec → managed callback work
string UiClass(string[] f)
{
	if (f.Length == 0) return "?";
	if (f[0].Contains("sailfish_host_exec")) return "qt";
	if (f[0].Contains("QtHostNative.")) return "shim";
	if (IsWait(f[0])) return "wait";
	return "managed";
}
var ui = samples.Where(s => s.tid == uiTid).ToList();
P("== UI thread breakdown (whole trace) ==");
foreach (var g in ui.GroupBy(s => UiClass(s.frames)).OrderByDescending(g => g.Count()))
	P($"  {g.Key,-8} {g.Count(),6} samples ≈ {g.Count() * interval,7:F0} ms ({g.Count() * 100.0 / ui.Count:F1}%)");
P("  (qt = inside sailfish_host_exec's event loop: Qt work OR idle; shim = managed→QtHostNative P/Invoke; managed = .NET code)");
P();
TopFor(ui.Where(s => UiClass(s.frames) is "managed" or "shim"), "UI thread, managed+shim work");
TopFor(ui.Where(s => UiClass(s.frames) is "managed" or "shim" && Rel(s.t) >= 0), "UI thread, managed+shim work after anchor");
foreach (var th in threads.Where(x => x.tid != uiTid && x.busy * interval > 200).Take(4))
	TopFor(samples.Where(s => s.tid == th.tid && s.frames.Length > 0 && !IsWait(s.frames[0])), $"thread {th.tid} busy");

P($"== UI thread timeline, {bucketMs:F0} ms buckets, ms from anchor: managed+shim / qt ==");
foreach (var g in ui.GroupBy(s => Math.Floor(Rel(s.t) / bucketMs)).OrderBy(g => g.Key))
{
	var m = g.Count(s => UiClass(s.frames) is "managed" or "shim") * interval;
	var q = g.Count(s => UiClass(s.frames) == "qt") * interval;
	var top = g.Where(s => UiClass(s.frames) is "managed" or "shim")
		.SelectMany(s => s.frames.Where(f => f.Contains("Maui") || f.Contains("Kitchen") || f.Contains("QtHost")).Take(1))
		.GroupBy(f => f).OrderByDescending(x => x.Count()).FirstOrDefault()?.Key ?? "";
	var bar = new string('#', (int)Math.Round(m / bucketMs * 40));
	P($"  {g.Key * bucketMs,7:F0} m{m,5:F0} q{q,5:F0} |{bar,-40}| {Short(top)}");
}
P();

foreach (var w in windows)
	TopFor(ui.Where(s => UiClass(s.frames) is "managed" or "shim" && Rel(s.t) >= w.from && Rel(s.t) < w.to), $"UI thread work in window {w.from}..{w.to} ms", 40);
foreach (var f in focuses)
{
	var hits = ui.Where(s => Rel(s.t) >= focusWin.from && Rel(s.t) < focusWin.to && s.frames.Any(x => x.Contains(f))).ToList();
	P($"== focus '{f}' (UI thread {focusWin.from}..{focusWin.to} ms): {hits.Count} samples ≈ {hits.Count * interval:F0} ms ==");
	P("  callers (frame just above the outermost match):");
	foreach (var g in hits.GroupBy(s => { var i = Array.FindLastIndex(s.frames, x => x.Contains(f)); return i + 1 < s.frames.Length ? s.frames[i + 1] : "(root)"; })
		.OrderByDescending(g => g.Count()).Take(15))
		P($"    {g.Count() * interval,6:F0} ms  {Short(g.Key)}");
	P($"  caller chains ({depth} frames above the match):");
	foreach (var g in hits.GroupBy(s => { var i = Array.FindLastIndex(s.frames, x => x.Contains(f)); return string.Join(" <- ", s.frames.Skip(i + 1).Take(depth).Select(x => x.Split('(')[0].Split('.').Last())); })
		.OrderByDescending(g => g.Count()).Take(15))
		P($"    {g.Count() * interval,6:F0} ms  {Short(g.Key)}");
	P("  callees (frame just below the innermost match; (self) = match is leaf):");
	foreach (var g in hits.GroupBy(s => { var i = Array.FindIndex(s.frames, x => x.Contains(f)); return i > 0 ? s.frames[i - 1] : "(self)"; })
		.OrderByDescending(g => g.Count()).Take(15))
		P($"    {g.Count() * interval,6:F0} ms  {Short(g.Key)}");
	P();
}

var phases = opt["phase"].ToList();
if (phases.Count > 0 && windows.Count > 0)
{
	P("== phases: inclusive UI-thread ms per window (a sample counts once per phase) ==");
	P("  " + "phase".PadRight(34) + string.Join("", windows.Select(w => $"{w.from:F0}..{w.to:F0}".PadLeft(16))));
	P("  " + "(window length)".PadRight(34) + string.Join("", windows.Select(w => $"{w.to - w.from:F0}".PadLeft(16))));
	P("  " + "(UI busy: managed+shim)".PadRight(34) + string.Join("", windows.Select(w => $"{ui.Count(s => UiClass(s.frames) is "managed" or "shim" && Rel(s.t) >= w.from && Rel(s.t) < w.to) * interval:F0}".PadLeft(16))));
	foreach (var ph in phases)
		P("  " + ph.PadRight(34) + string.Join("", windows.Select(w => $"{ui.Count(s => Rel(s.t) >= w.from && Rel(s.t) < w.to && s.frames.Any(f => f.Contains(ph))) * interval:F0}".PadLeft(16))));
	P();
}

P($"== GC: {gcs.Count} collections ==");
foreach (var g in gcs.GroupBy(x => x.gen).OrderBy(g => g.Key)) P($"  gen{g.Key}: {g.Count()}");
if (suspends.Count > 0)
{
	foreach (var r in suspends.GroupBy(s => s.reason))
	{
		var pauses = r.Select(s => s.end - s.start).ToList();
		P($"  EE suspensions ({r.Key}): {pauses.Count}, total {pauses.Sum():F1} ms, avg {pauses.Average():F2} ms, max {pauses.Max():F1} ms, >5 ms: {pauses.Count(p => p > 5)}");
	}
	foreach (var s in suspends.Where(s => s.reason.Contains("GC") && !s.reason.Contains("Sampl")).OrderByDescending(s => s.end - s.start).Take(8))
		P($"    at {Rel(s.start),7:F0} ms pause {s.end - s.start,6:F1} ms ({s.reason})");
}
foreach (var g in gcs.Where(g => g.gen >= 1)) P($"    gen{g.gen} at {Rel(g.t),7:F0} ms reason {g.reason} type {g.type}");
P();

P($"== JIT: {jits.Count} methods jitted; {jits.Count(j => Rel(j.t) >= 0)} after anchor; " +
  $"{eventNames.GetValueOrDefault("Microsoft-Windows-DotNETRuntime/TieredCompilation/BackgroundJitStart")} background tier-up batches ==");
if (jits.Count == 0)
	P("  (Method/JittingStarted needs the JIT keyword at level 5; dotnet-counters shows dotnet.jit.* rates)");
foreach (var g in jits.Where(j => Rel(j.t) >= 0).GroupBy(j => Math.Floor(Rel(j.t) / 1000)).OrderBy(g => g.Key))
	P($"  {g.Key,4:F0} s: {g.Count(),4}  e.g. {Short(string.Join(", ", g.Take(3).Select(x => x.name)))}");
P("  by namespace (after anchor):");
foreach (var g in jits.Where(j => Rel(j.t) >= 0).GroupBy(j => string.Join('.', j.name.Split('.').Take(3))).OrderByDescending(g => g.Count()).Take(15))
	P($"    {g.Count(),4}  {g.Key}");
P();

P($"== exceptions: {exceptions.Count} ==");
foreach (var g in exceptions.GroupBy(e => e.type + ": " + e.msg).OrderByDescending(g => g.Count()).Take(10))
	P($"  {g.Count(),4}× {Short(g.Key)} (first at {Rel(g.Min(x => x.t)):F0} ms)");
if (contention.Count > 0) P($"== lock contention: {contention.Count} (UI thread: {contention.Count(c => c.tid == uiTid)}) ==");
P();
P("== event counts ==");
foreach (var kv in eventNames.OrderByDescending(k => k.Value).Take(25)) P($"  {kv.Value,8}  {kv.Key}");

Console.Write(sb.ToString());
