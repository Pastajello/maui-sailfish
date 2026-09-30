using System.Globalization;
using System.Text.Json;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// Adapter cost leg (MAUI_SAILFISH_QT_HOST_ADAPTERBENCH_DIAG=1): the creation cost of each QML adapter
/// (createObject per instance, median of rounds) and, for every candidate replacement, a pixel comparison
/// against the adapter it would replace under the same props. A lighter adapter only ships when it is both
/// cheaper and paints the same.
/// MAUI_SAILFISH_ADAPTERBENCH_SRCS=a.qml,b.qml overrides the measured adapters.
/// </summary>
internal sealed partial class QtHostDiagnosticsRunner
{
	private bool _qtAdapterBenchDiag;
	private readonly DiagChecks _qtAdapterBenchChecks = new("Qt adapterbench diag");

	private const int BenchInstances = 60;
	private const int BenchRounds = 5;

	/// <summary>Adapter → creation props (a JS object literal) typical of a list cell.</summary>
	private static readonly (string Src, string Props)[] BenchAdapters =
	[
		("diag/reference/SilicaLabel.qml", "{text:'Lemongrass beef stew with noodles',width:300,mauiWrap:4,mauiMaxLines:2}"),
		("controls/Label.qml", "{text:'Lemongrass beef stew with noodles',width:300,mauiWrap:4,mauiMaxLines:2}"),
		("diag/reference/ButtonEager.qml", "{text:'Load more'}"),
		("controls/Button.qml", "{text:'Load more'}"),
		("diag/candidates/ButtonBare.qml", "{text:'Load more'}"),
		("diag/reference/ImageEager.qml", "{}"),
		("controls/Image.qml", "{}"),
		("containers/Border.qml", "{}"),
		("containers/Grid.qml", "{}"),
		("containers/StackLayout.qml", "{}"),
		("containers/ContentView.qml", "{}"),
	];

	/// <summary>(reference, shipped adapter) pairs whose pixels must match: a lighter adapter replaced the Silica
	/// control it is compared with (the reference ships with the diagnostics sample only).</summary>
	/// <remarks>A property, not a static field: the case arrays are declared further down, and static fields
	/// initialize in text order (a field here saw them null and crashed the leg on device).</remarks>
	private static (string Current, string Candidate, string[] Cases)[] BenchPairs =>
	[
		("diag/reference/SilicaLabel.qml", "controls/Label.qml", LabelPixelCases),
		("diag/reference/ButtonEager.qml", "controls/Button.qml", ButtonPixelCases),
		("diag/reference/ImageEager.qml", "controls/Image.qml", ImagePixelCases),
	];

	/// <summary>Label configurations the pixel comparison covers (JS object literals).</summary>
	private static readonly string[] LabelPixelCases =
	[
		"{text:'Sailfish Kitchen'}",
		"{text:'Lemongrass beef stew with noodles and a long tail',width:300,mauiMaxLines:1,mauiElide:1}",
		"{text:'Lemongrass beef stew with noodles and a long tail',width:300,mauiWrap:4,mauiMaxLines:2,mauiBold:true,mauiPixelSize:40}",
		"{text:'Beef · Thai',mauiEmphasis:'secondary',mauiItalic:true}",
		"{text:'<b>Bold</b> and <i>italic</i> text',mauiTextFormat:1}",
		"{text:'Centered',width:300,mauiHAlign:4,mauiColor:'#ffcc00',mauiUnderline:true}",
		"{text:'Padded',mauiPadL:20,mauiPadT:10,mauiPadR:6,mauiPadB:4,mauiBackground:'#334455',mauiEmphasis:'header'}",
	];

	/// <summary>Button configurations: unstyled, and every style path the lean adapter creates lazily.</summary>
	private static readonly string[] ButtonPixelCases =
	[
		"{text:'Load more'}",
		"{text:'Load more',mauiPixelSize:40,mauiBold:true}",
		"{text:'Retry',mauiCornerRadius:20,mauiStrokeColor:'#ffcc00',mauiStrokeWidth:4}",
		"{text:'Favourite',mauiItalic:true,mauiFamily:'Sans'}",
		"{text:'Later'}|{mauiPixelSize:40,mauiBold:true,mauiCornerRadius:12}",
		"{text:'Reset'}|{mauiPixelSize:40,mauiItalic:true}|{mauiPixelSize:0,mauiItalic:false}",
	];

	private const string BenchImage = "'file:///usr/share/harbour-sample/images/sailfish_logo.png'";

	/// <summary>Image configurations: aspect modes, the corner caps (a clip push), the ImageButton frame.</summary>
	private static readonly string[] ImagePixelCases =
	[
		"{mauiSource:" + BenchImage + ",width:240,height:160,mauiAspect:1}",
		"{mauiSource:" + BenchImage + ",width:240,height:160,mauiAspect:2}",
		"{mauiSource:" + BenchImage + ",width:240,height:160,mauiAspect:0}",
		"{mauiSource:" + BenchImage + ",width:240,height:160,mauiAspect:2,mauiClipRadius:28,mauiClipCorners:3,mauiClipColor:'#223344'}",
		"{mauiSource:" + BenchImage + ",width:240,height:160,mauiAspect:1,mauiBackground:'#445566',mauiCornerRadius:18,mauiStrokeColor:'#ffcc00',mauiStrokeWidth:4,mauiPadL:12,mauiPadT:12,mauiPadR:12,mauiPadB:12}",
		"{mauiSource:" + BenchImage + ",width:240,height:160,mauiAspect:2}|{mauiClipRadius:28,mauiClipCorners:12,mauiClipColor:'#223344'}",
		"{mauiSource:" + BenchImage + ",width:240,height:160,mauiAspect:2,mauiClipRadius:28,mauiClipCorners:15,mauiClipColor:'#223344'}|{mauiClipColor:'#aa3344'}",
	];

	private void RunQtAdapterBenchDiagnostics(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		// Let the startup page settle so its own creation does not share the measured turns.
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(2000), () =>
		{
			var only = SailfishEnv.Get("MAUI_SAILFISH_ADAPTERBENCH_SRCS")?.Split(',', StringSplitOptions.RemoveEmptyEntries);
			var adapters = only is null ? BenchAdapters : BenchAdapters.Where(a => only.Contains(a.Src)).ToArray();
			var results = new Dictionary<string, double>();
			foreach (var (src, props) in adapters)
			{
				var us = BenchAdapter(src, props);
				results[src] = us;
				Console.Error.WriteLine($"[Sailfish] ADAPTERBENCH src={src} usPerInstance={us.ToString("F0", CultureInfo.InvariantCulture)}");
				_qtAdapterBenchChecks.Check($"bench {src}: {us:F0} µs per createObject (median of {BenchRounds}×{BenchInstances})", us > 0);
			}
			foreach (var (current, candidate, _) in BenchPairs)
				if (results.TryGetValue(current, out var a) && results.TryGetValue(candidate, out var b))
					Console.Error.WriteLine($"[Sailfish] ADAPTERBENCH pair {current} → {candidate}: {a:F0} → {b:F0} µs ({(a > 0 ? (b - a) * 100 / a : 0):+0;-0}%)");
			ComparePairs(dispatcher, 0, 0, () => ImageBehaviour(dispatcher, 0, () =>
			{
				_qtAdapterBenchChecks.CheckNoOffThreadCalls();
				_qtAdapterBenchChecks.Accept("OK — adapter creation costs measured; candidate adapters paint the same as the ones they replace");
				QtHost.QtHostRuntime.Shutdown();
			}));
		});
	}

	/// <summary>A 32×32 three-frame animated GIF (red, green, blue, 80 ms per frame).</summary>
	private const string BenchGif =
		"R0lGODlhIAAgAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQACAAAACwAAAAAIAAgAAAINQABCBxIsKDBgwgTKlzIsKHDhxAjSpxIsaLFixgzatzIsaPHjyBDihxJsqTJkyhTqlzJUmRAACH5BAEIAAEALAAAAAAgACAAgQD/AAAAAAAAAAAAAAg1AAEIHEiwoMGDCBMqXMiwocOHECNKnEixosWLGDNq3Mixo8ePIEOKHEmypMmTKFOqXMlSZEAAIfkEAQgAAQAsAAAAACAAIACBAAD/AAAAAAAAAAAACDUAAQgcSLCgwYMIEypcyLChw4cQI0qcSLGixYsYM2rcyLGjx48gQ4ocSbKkyZMoU6pcyVJkQAA7";

	/// <summary>Behaviour the pixel comparison cannot see, for the reference and the shipped Image adapter: a GIF
	/// source plays in the (lazily created) AnimatedImage, and a tappable image (ImageButton) reports a real tap.</summary>
	private void ImageBehaviour(SailfishDispatcher dispatcher, int index, Action done)
	{
		string[] srcs = ["diag/reference/ImageEager.qml", "controls/Image.qml"];
		if (index >= srcs.Length)
		{
			done();
			return;
		}
		var src = srcs[index];
		File.WriteAllBytes("/tmp/adapterbench.gif", Convert.FromBase64String(BenchGif));
		const string find = "(function(){var q=[pageStack.currentPage];while(q.length){var n=q.shift();if(n.objectName==='adapterbench-behaviour')return n;" +
		                    "var k=n.children;if(k)for(var i=0;i<k.length;++i)q.push(k[i]);}return null;})()";
		var created = QtHost.QtHostRuntime.Eval("(function(){var p=pageStack.currentPage;var c=p.__componentFor('" + src + "');if(!c)return 'missing';" +
			"var pl=Qt.createQmlObject('import QtQuick 2.6; Rectangle{objectName:\"adapterbench-behaviour\";color:\"#101418\";z:100000;width:parent.width;height:parent.height;property var gif;property var btn;property int taps:0;property string seen:\"\";Timer{interval:50;repeat:true;running:true;onTriggered:if(parent.gif){var f=\",\"+parent.gif.mauiFrame;if(parent.seen.indexOf(f)<0)parent.seen+=f;}}}',p,'adapterbench');" +
			"pl.gif=c.createObject(pl,{mauiSource:'file:///tmp/adapterbench.gif',mauiPlaying:true,x:200,y:400,width:64,height:64});" +
			"pl.btn=c.createObject(pl,{mauiSource:" + BenchImage + ",mauiTappable:true,x:200,y:600,width:240,height:160});" +
			"if(!pl.gif||!pl.btn)return 'create failed';pl.btn.mauiEvent.connect(function(n,x){if(n==='tap')pl.taps++;});return 'ok';})()");
		if (created != "ok")
		{
			_qtAdapterBenchChecks.Check($"behaviour {src}: instances created ({created})", false);
			ImageBehaviour(dispatcher, index + 1, done);
			return;
		}
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(400), () =>
		{
			var at = QtHost.QtHostRuntime.Eval("(function(){var pl=" + find + ";if(!pl)return '';var s=pl.btn.mapToItem(null,pl.btn.width/2,pl.btn.height/2);" +
			                                   "return Math.round(s.x)+','+Math.round(s.y);})()").Split(',');
			if (at.Length == 2)
				DiagQml.Tap(double.Parse(at[0], CultureInfo.InvariantCulture), double.Parse(at[1], CultureInfo.InvariantCulture));
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(700), () =>
			{
				var state = QtHost.QtHostRuntime.Eval("(function(){var pl=" + find + ";if(!pl)return '{}';var r=JSON.stringify({loaded:pl.gif.mauiLoaded,frames:pl.gif.mauiFrameCount," +
				                                      "frame:pl.gif.mauiFrame,seen:pl.seen,taps:pl.taps});pl.destroy();return r;})()");
				try
				{
					using var doc = JsonDocument.Parse(state);
					var r = doc.RootElement;
					var loaded = r.GetProperty("loaded").GetBoolean();
					var frames = r.GetProperty("frames").GetInt32();
					var frame = r.GetProperty("frame").GetInt32().ToString(CultureInfo.InvariantCulture);
					var taps = r.GetProperty("taps").GetInt32();
					var seen = r.GetProperty("seen").GetString() ?? string.Empty;
					var distinct = seen.Split(',', StringSplitOptions.RemoveEmptyEntries).Length;
					_qtAdapterBenchChecks.Check($"behaviour {src}: GIF plays (loaded={loaded}, frames={frames}>=3, frames seen over ~1 s '{seen}' ({distinct}>=2), now {frame})",
						loaded && frames >= 3 && distinct >= 2);
					_qtAdapterBenchChecks.Check($"behaviour {src}: tappable image reports the injected tap (taps={taps}>=1)", taps >= 1);
				}
				catch (Exception ex)
				{
					_qtAdapterBenchChecks.Check($"behaviour {src}: state readable ({state}: {ex.Message})", false);
				}
				ImageBehaviour(dispatcher, index + 1, done);
			});
		});
	}

	/// <summary>Median µs per createObject of <paramref name="src"/> over <see cref="BenchRounds"/> rounds of
	/// <see cref="BenchInstances"/> instances in a hidden parent on the current page; -1 when it does not load.</summary>
	private static double BenchAdapter(string src, string props)
	{
		var js = "(function(){var p=pageStack.currentPage;var c=p.__componentFor('" + src + "');if(!c)return '-1';" +
		         "var h=Qt.createQmlObject('import QtQuick 2.6; Item{visible:false;width:540;height:100}',p,'adapterbench');" +
		         "var o=c.createObject(h," + props + ");if(!o){h.destroy();return '-1';}o.destroy();" +   // warm-up: compile + caches
		         "var t=[];for(var r=0;r<" + BenchRounds + ";++r){var a=[];var s=Date.now();" +
		         "for(var i=0;i<" + BenchInstances + ";++i)a.push(c.createObject(h," + props + "));" +
		         "t.push(Date.now()-s);for(var j=0;j<a.length;++j)if(a[j])a[j].destroy();}" +
		         "h.destroy();t.sort(function(x,y){return x-y;});return ''+(t[" + (BenchRounds / 2) + "]*1000/" + BenchInstances + ");})()";
		var result = QtHost.QtHostRuntime.Eval(js);
		return double.TryParse(result, NumberStyles.Float, CultureInfo.InvariantCulture, out var us) ? us : -1;
	}

	/// <summary>Paints the pair's two adapters one above the other with the same props on an opaque plate over the
	/// page, grabs the window and compares the two regions pixel by pixel. Both sit clear of the top-left corner,
	/// where Silica's page indicator glow paints above any page content.</summary>
	private void ComparePairs(SailfishDispatcher dispatcher, int pair, int testCase, Action done)
	{
		if (pair >= BenchPairs.Length)
		{
			done();
			return;
		}
		var (current, candidate, cases) = BenchPairs[pair];
		if (testCase >= cases.Length)
		{
			ComparePairs(dispatcher, pair + 1, 0, done);
			return;
		}
		// "create|later|later…": creation props, then pushes applied in order after creation to both (the in-place
		// update path managed uses, e.g. a style arriving or returning to unset).
		var steps = cases[testCase].Split('|');
		var props = steps[0];
		var later = string.Concat(steps.Skip(1).Select(u =>
			"(function(u){for(var k in u){plate.a[k]=u[k];plate.b[k]=u[k];}})(" + u + ");"));
		var js = "(function(){var p=pageStack.currentPage;var ca=p.__componentFor('" + current + "'),cb=p.__componentFor('" + candidate + "');" +
		         "if(!ca||!cb)return 'missing';" +
		         "var plate=Qt.createQmlObject('import QtQuick 2.6; Rectangle{objectName:\"adapterbench-plate\";color:\"#101418\";z:100000;x:0;y:0;width:parent.width;height:parent.height;property var a;property var b}',p,'adapterbench');" +
		         "plate.a=ca.createObject(plate," + props + ");plate.b=cb.createObject(plate," + props + ");" +
		         "if(!plate.a||!plate.b)return 'create failed';" + later +
		         "plate.a.x=160;plate.a.y=320;plate.b.x=160;plate.b.y=360+Math.max(plate.a.height,plate.b.height);return 'ok';})()";
		var created = QtHost.QtHostRuntime.Eval(js);
		if (created != "ok")
		{
			_qtAdapterBenchChecks.Check($"pixels {candidate} case {testCase}: pair created ({created})", false);
			ComparePairs(dispatcher, pair, testCase + 1, done);
			return;
		}
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(500), () =>
		{
			const string find = "(function(){var q=[pageStack.currentPage];while(q.length){var n=q.shift();if(n.objectName==='adapterbench-plate')return n;" +
			                    "var k=n.children;if(k)for(var i=0;i<k.length;++i)q.push(k[i]);}return null;})()";
			var rects = QtHost.QtHostRuntime.Eval("(function(){var pl=" + find + ";if(!pl)return '';function R(o){var s=o.mapToItem(null,0,0);" +
			                                      "return [Math.round(s.x),Math.round(s.y),Math.ceil(o.width),Math.ceil(o.height)].join(',');}" +
			                                      "return R(pl.a)+';'+R(pl.b);})()");
			var path = $"/tmp/adapterbench-{pair}-{testCase}.png";
			QtHost.QtHostRuntime.GrabPng(path);
			QtHost.QtHostRuntime.Eval("(function(){var pl=" + find + ";if(pl)pl.destroy();return '';})()");
			var png = DiagPng.TryLoad(path);
			var parts = rects.Split(';');
			if (png is null || parts.Length != 2)
			{
				_qtAdapterBenchChecks.Check($"pixels {candidate} case {testCase}: grab decodes and both rects read ({rects})", false);
			}
			else
			{
				var ra = parts[0].Split(',').Select(int.Parse).ToArray();
				var rb = parts[1].Split(',').Select(int.Parse).ToArray();
				var w = Math.Max(ra[2], rb[2]);
				var h = Math.Max(ra[3], rb[3]);
				long diff = 0, total = 0;
				var maxDelta = 0;
				for (var y = 0; y < h; y++)
					for (var x = 0; x < w; x++)
					{
						var pa = png.At(ra[0] + x, ra[1] + y);
						var pb = png.At(rb[0] + x, rb[1] + y);
						var d = Math.Max(Math.Abs(pa.R - pb.R), Math.Max(Math.Abs(pa.G - pb.G), Math.Abs(pa.B - pb.B)));
						maxDelta = Math.Max(maxDelta, d);
						total++;
						if (d > 40)
							diff++;
					}
				var sameSize = ra[2] == rb[2] && ra[3] == rb[3];
				var share = total > 0 ? diff * 100.0 / total : 100;
				_qtAdapterBenchChecks.Check(
					$"pixels {current} vs {candidate} case {testCase} {cases[testCase]}: size {ra[2]}x{ra[3]} vs {rb[2]}x{rb[3]}, " +
					$"{diff}/{total} px differ ({share:F2}%), max Δ {maxDelta}",
					sameSize && share <= 0.2);
			}
			ComparePairs(dispatcher, pair, testCase + 1, done);
		});
	}
}
