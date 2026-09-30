using Microsoft.Maui.Controls;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// Features-hub round trips (MAUI_SAILFISH_QT_HOST_FEATURES_DIAG=1): opens each feature row
/// and back-swipes; after every return the hub's rows must be materialized and painted.
/// </summary>
internal sealed partial class QtHostDiagnosticsRunner
{
	private bool _qtFeaturesDiag;
	private readonly DiagChecks _qtFeaturesChecks = new("Qt features diag");


	/// <summary>The hub list's painted state: every delegate holds a visible, non-empty row root.</summary>
	private const string HubRowsJs = """
		(function(){
		  var p=pageStack.currentPage; var lv=null;
		  for(var k in p.__hosts){ if(p.__hosts[k].uri==='list-view'){ lv=p.__hosts[k].item; break; } }
		  if(!lv) return JSON.stringify({err:'no list-view host'});
		  var ci=lv.contentItem, dg=0, shown=0, detail=[];
		  for(var i=0;i<ci.children.length;i++){
		    var d=ci.children[i];
		    if(!d.objectName||d.objectName.indexOf('__r')<0) continue;   // row delegates (not header/footer slots)
		    dg++;
		    var root=null;
		    for(var j=0;j<d.children.length;j++){ var c=d.children[j]; if(c.objectName&&c.objectName.indexOf('maui_')===0){ root=c; break; } }
		    var ok=d.visible&&d.opacity>0&&root&&root.visible&&root.opacity>0&&root.width>0&&root.height>0;
		    if(ok) shown++;
		    if(detail.length<4) detail.push(d.objectName+' kids='+d.children.length+' root='+(root?(root.objectName+' '+root.visible+'/'+root.opacity+' '+Math.round(root.width)+'x'+Math.round(root.height)+'@'+Math.round(root.x)+','+Math.round(root.y)+' kids='+root.children.length):'none'));
		  }
		  // Selection highlight: the model's s flags, and delegates painting the
		  // highlight (a stale one after SelectedItem=null is a bug).
		  var sel=[], lit=[];
		  for(var r=0;r<lv.model.count;r++) if(lv.model.get(r).s) sel.push(r);
		  for(var q=0;q<ci.children.length;q++){ var dd=ci.children[q];
		    if(!dd.objectName||dd.objectName.indexOf('__r')<0) continue;
		    for(var z=0;z<dd.children.length;z++){ var cc=dd.children[z];
		      if(cc.radius!==undefined&&cc.color!==undefined&&!cc.objectName&&cc.visible){ lit.push(dd.objectName.split('__r')[1]); } } }
		  return JSON.stringify({count:lv.count,dg:dg,shown:shown,sel:sel,lit:lit,sr:lv.mauiSelectedRows,cy:lv.contentY,vis:lv.visible,op:lv.opacity,h:Math.round(lv.height),d:detail});
		})()
		""";

	private void CheckHubRows(string when)
	{
		var json = QtHost.QtHostRuntime.Eval(HubRowsJs);
		int dg = 0, shown = 0, lit = -1;
		try
		{
			using var doc = System.Text.Json.JsonDocument.Parse(json);
			if (doc.RootElement.TryGetProperty("dg", out var d)) dg = d.GetInt32();
			if (doc.RootElement.TryGetProperty("shown", out var s)) shown = s.GetInt32();
			if (doc.RootElement.TryGetProperty("lit", out var l)) lit = l.GetArrayLength();
		}
		catch
		{
		}
		_qtFeaturesChecks.Check($"{when}: hub rows painted {shown}/{dg} delegates ({json})", dg > 0 && shown == dg);
		// The hub clears SelectedItem after each push, so no row may stay highlighted on return.
		_qtFeaturesChecks.Check($"{when}: no stale selection highlight (lit rows {lit}==0)", lit == 0);
		if (shown != dg)
			Console.Error.WriteLine($"[Sailfish] Qt features diag: row 0 state: {_context.Renderer?.Collection.DescribeRow(0)}");
	}

	private static readonly string[] FeatureTitles =
	{
		"Text", "Text input", "Pickers", "Controls", "Dialogs", "Gestures", "Visual",
		"Collections", "Navigation", "Shapes & images", "Essentials",
	};

	private void RunQtFeaturesDiagnostics(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		var nav = RootNav;
		var rootTitle = nav?.RootPage?.Title ?? "Sailfish Tasks";
		bool OnTitle(string t) => PageTitle(renderer) == t;
		void Finish()
		{
			_qtFeaturesChecks.CheckNoOffThreadCalls();
			_qtFeaturesChecks.Accept("OK — the Features hub keeps painting its rows across every feature round trip");
			QtHost.QtHostRuntime.Shutdown();
		}
		// PushAsync/PopAsync tasks must complete once platform navigation finishes.
		void ProbeTasks()
		{
			var push = nav!.PushAsync(new ContentPage { Title = "Probe" });
			WaitFor(dispatcher, () => push.IsCompleted, 4000, () =>
			{
				_qtFeaturesChecks.Check($"PushAsync task completed={push.IsCompleted} (status {push.Status})", push.IsCompleted);
				var pop = nav.PopAsync();
				WaitFor(dispatcher, () => pop.IsCompleted, 4000, () =>
				{
					_qtFeaturesChecks.Check($"PopAsync task completed={pop.IsCompleted} (status {pop.Status})", pop.IsCompleted);
					Finish();
				});
			});
		}
		void Row(int i)
		{
			if (i >= FeatureTitles.Length)
			{
				ProbeTasks();
				return;
			}
			if (!renderer.Collection.TryGetRowPoint(i, out var x, out var y) || y > 2200)
			{
				// Rows below the fold: scroll the hub so the row is on screen.
				if (renderer.CurrentPage is ContentPage { Content: CollectionView hub })
				{
					hub.ScrollTo(i, position: ScrollToPosition.Center, animate: false);
					dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(800), () => TapRow(i));
					return;
				}
			}
			TapRow(i);
		}
		void TapRow(int i)
		{
			if (!renderer.Collection.TryGetRowPoint(i, out var x, out var y))
			{
				_qtFeaturesChecks.Check($"row {i} '{FeatureTitles[i]}': row point available", false);
				Row(i + 1);
				return;
			}
			Console.Error.WriteLine($"[Sailfish] Qt features diag: tap row {i} '{FeatureTitles[i]}' at {x:F0},{y:F0}");
			QtHost.QtHostRuntime.InjectPointer(0, x, y);
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(150), () => QtHost.QtHostRuntime.InjectPointer(1, x, y));
			WaitFor(dispatcher, () => !OnTitle("Features") && renderer.NativePageIds.Count == 3, 6000, () =>
			{
				_qtFeaturesChecks.Check($"row {i} '{FeatureTitles[i]}' opened '{PageTitle(renderer)}' (depth {renderer.NativePageIds.Count}==3)",
					!OnTitle("Features") && renderer.NativePageIds.Count == 3);
				InjectBackSwipe(dispatcher, () => WaitFor(dispatcher, () => OnTitle("Features") && renderer.NativePageIds.Count == 2, 6000, () =>
				{
					_qtFeaturesChecks.Check($"back from '{FeatureTitles[i]}': on 'Features', depth {renderer.NativePageIds.Count}==2", OnTitle("Features") && renderer.NativePageIds.Count == 2);
					CheckHubRows($"after '{FeatureTitles[i]}'");
					// The hub clears the selection after `await PushAsync`, so this proves the task completed.
					var hubSelected = (renderer.CurrentPage as ContentPage)?.Content is CollectionView hubList ? hubList.SelectedItem : "?";
					_qtFeaturesChecks.Check($"after '{FeatureTitles[i]}': the app code after `await PushAsync` ran (hub SelectedItem={(hubSelected is null ? "null" : "set")})", hubSelected is null);
					Shot(dispatcher, $"features-{i:D2}-back", () => Row(i + 1));
				}));
			});
		}
		// The startup tap opened Statistics: go back, then tap "Features…".
		InjectBackSwipe(dispatcher, () => WaitFor(dispatcher, () => OnTitle(rootTitle), 6000, () =>
		{
			var tapped = TapButton(renderer, dispatcher, "Features…");
			WaitFor(dispatcher, () => OnTitle("Features"), 6000, () =>
			{
				_qtFeaturesChecks.Check($"'Features…' tapped={tapped} opened '{PageTitle(renderer)}'", tapped && OnTitle("Features"));
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(800), () =>
				{
					CheckHubRows("first visit");
					Shot(dispatcher, "features-first-visit", () => Row(0));
				});
			});
		}));
	}
}
