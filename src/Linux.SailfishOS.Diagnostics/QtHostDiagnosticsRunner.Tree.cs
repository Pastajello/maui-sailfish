using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// Host-tree leg (MAUI_SAILFISH_QT_HOST_TREE_DIAG=1): pushes a page and checks that the QML
/// parent chain mirrors MAUI ancestry, that opacity/visibility/clip/geometry compose natively,
/// that large pages render whole, and that reparent/reorder keep the same QML objects.
/// </summary>
internal sealed partial class QtHostDiagnosticsRunner
{
	private bool _qtTreeDiag;
	private readonly DiagChecks _qtTreeChecks = new("Qt tree diag");

	private const int TreeLongLabels = 450;

	private sealed class TreeFixture
	{
		public required ContentPage Page;
		public required Grid Dimmed;               // Opacity 0.5
		public required Label DimmedChild;
		public required Border Shifted;            // TranslationX 10
		public required Label ShiftedChild;
		public required VerticalStackLayout Collapsible;
		public required Label CollapsibleChild;
		public required VerticalStackLayout BoxA;
		public required VerticalStackLayout BoxB;
		public required Label Mover;
		public required Label Anchor;
		public required List<Label> LongLabels;
		public required Grid Clipped;              // IsClippedToBounds
		public required ScrollView Scroll;
		public required ScrollView Strip;          // horizontal
		public required HorizontalStackLayout StripContent;
		public required Button Styled;
		public required Entry StyledEntry;
		public required Switch ColorSwitch;
		public required Slider ColorSlider;
		public required ProgressBar ColorProgress;
		public required ActivityIndicator Spinner;
		public required Label RtlFirst;            // FlowDirection
		public required Label RtlSecond;
	}

	private double _treeSpin0 = double.NaN;




	private static TreeFixture BuildTreeFixture()
	{
		var dimmedChild = new Label { Text = "dimmed child" };
		var dimmed = new Grid { Opacity = 0.5, Padding = new Thickness(8), Children = { dimmedChild } };
		var clipped = new Grid { IsClippedToBounds = true, HeightRequest = 20, Children = { new Label { Text = "clipped", TranslationY = 30 } } };
		var shiftedChild = new Label { Text = "shifted child" };
		var shifted = new Border { TranslationX = 10, Padding = new Thickness(6), Content = shiftedChild };
		var collapsibleChild = new Label { Text = "collapsible child" };
		var collapsible = new VerticalStackLayout { Children = { collapsibleChild } };
		var mover = new Label { Text = "mover" };
		var anchor = new Label { Text = "anchor" };
		var boxA = new VerticalStackLayout { Children = { mover } };
		var boxB = new VerticalStackLayout { Children = { anchor } };
		var longLabels = new List<Label>();
		var longStack = new VerticalStackLayout();
		for (var i = 0; i < TreeLongLabels; i++)
		{
			var label = new Label { Text = $"row {i}" };
			longLabels.Add(label);
			longStack.Children.Add(label);
		}
		var stripContent = new HorizontalStackLayout { Spacing = 12 };
		for (var i = 0; i < 12; i++)
			stripContent.Children.Add(new Label { Text = $"chip {i}", WidthRequest = 90 });
		var strip = new ScrollView { Orientation = ScrollOrientation.Horizontal, HeightRequest = 40, Content = stripContent };
		var styled = new Button
		{
			Text = "styled", FontSize = 20, FontAttributes = FontAttributes.Bold,
			CornerRadius = 12, BorderColor = Colors.Red, BorderWidth = 3,
		};
		var styledEntry = new Entry
		{
			Text = "styled entry", TextColor = Colors.Lime, FontSize = 22,
			FontAttributes = FontAttributes.Italic, HorizontalTextAlignment = TextAlignment.Center,
		};
		var colorSwitch = new Switch { IsToggled = true, OnColor = Colors.Magenta, ThumbColor = Colors.Cyan };
		var colorSlider = new Slider { Value = 0.5, MinimumTrackColor = Colors.Red, MaximumTrackColor = Colors.Blue, ThumbColor = Colors.Yellow };
		var colorProgress = new ProgressBar { Progress = 0.6, ProgressColor = Colors.Orange };
		var spinner = new ActivityIndicator { IsRunning = true, Color = Colors.Lime };
		var rtlFirst = new Label { Text = "first" };
		var rtlSecond = new Label { Text = "second" };
		var rtlRow = new HorizontalStackLayout { FlowDirection = FlowDirection.RightToLeft, Spacing = 10, Children = { rtlFirst, rtlSecond } };
		var scroll = new ScrollView
		{
			Content = new VerticalStackLayout
			{
				Padding = new Thickness(16),
				Spacing = 4,
				Children = { dimmed, clipped, strip, styled, styledEntry, colorSwitch, colorSlider, colorProgress, spinner, rtlRow, shifted, collapsible, boxA, boxB, longStack },
			},
		};
		var page = new ContentPage { Title = "F1 Tree", Content = scroll };
		return new TreeFixture
		{
			Page = page, Dimmed = dimmed, DimmedChild = dimmedChild, Shifted = shifted,
			ShiftedChild = shiftedChild, Collapsible = collapsible, CollapsibleChild = collapsibleChild,
			BoxA = boxA, BoxB = boxB, Mover = mover, Anchor = anchor, LongLabels = longLabels,
			Clipped = clipped, Scroll = scroll, Strip = strip, StripContent = stripContent, Styled = styled,
			StyledEntry = styledEntry, ColorSwitch = colorSwitch, ColorSlider = colorSlider,
			ColorProgress = colorProgress, Spinner = spinner, RtlFirst = rtlFirst, RtlSecond = rtlSecond,
		};
	}

	private void RunQtTreeDiagnostics(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		var nav = RootNav;
		if (nav is null)
		{
			Console.Error.WriteLine("[Sailfish] Qt tree diag: no NavigationPage — leg skipped; auto-shutdown");
			QtHost.QtHostRuntime.Shutdown();
			return;
		}
		var fixture = BuildTreeFixture();
		Console.Error.WriteLine($"[Sailfish] Qt tree diag: leg A — push 'F1 Tree' ({TreeLongLabels} labels + nested containers)");
		_ = nav.PushAsync(fixture.Page);
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1500), () => VerifyTreeStructure(renderer, dispatcher, nav, fixture));
	}

	private QtHost.NativeElementHost? TreeHost(QtHost.QtHostPageRenderer renderer, Element element) =>
		renderer.Cache.TryGet(element, out var host) && host is { IsAttached: true } && renderer.CurrentHosts.Contains(host)
			? host
			: null;

	/// <summary>The id of the nearest hosted MAUI ancestor's host ("canvas" when none).</summary>
	private string ExpectedTreeParent(QtHost.QtHostPageRenderer renderer, Element element)
	{
		for (var p = element.Parent; p is not null and not Page; p = p.Parent)
			if (TreeHost(renderer, p) is { } host)
				return host.Id;
		return "canvas";
	}

	/// <summary>Host id → actual QML parent, read in chunks because the shim caps eval results at 8 KiB.</summary>
	private static Dictionary<string, string> TreeQmlParents(IEnumerable<QtHost.NativeElementHost> hosts)
	{
		var result = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var chunk in hosts.Select(h => h.Id).Chunk(100))
		{
			var ids = string.Join(",", chunk.Select(id => "'" + id + "'"));
			var text = QtHost.QtHostRuntime.Eval(
				"(function(){var p=pageStack.currentPage;var ids=[" + ids + "];var r=[];" +
				"for(var i=0;i<ids.length;++i){var h=p.__hosts[ids[i]];var par=h&&h.item?h.item.parent:null;" +
				"if(par&&!par.objectName&&par.parent&&par.parent.mauiChildHost===par)par=par.parent;" +
				"var n=!par?'null':(par.objectName==='mauiCanvas'?'canvas':(par.objectName.indexOf('maui_')===0?par.objectName.substring(5):(par.objectName||'other')));" +
				"r.push(ids[i]+'='+n);}return r.join(';');})()");
			foreach (var pair in text.Split(';', StringSplitOptions.RemoveEmptyEntries))
			{
				var eq = pair.IndexOf('=');
				if (eq > 0)
					result[pair[..eq]] = pair[(eq + 1)..];
			}
		}
		return result;
	}

	private void VerifyTreeStructure(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher,
		NavigationPage nav, TreeFixture f)
	{
		// The native parent chain mirrors the hosted MAUI ancestry.
		var qmlParents = TreeQmlParents(renderer.CurrentHosts.Where(h => h.IsAttached));
		int matched = 0, mismatched = 0;
		string? firstMismatch = null;
		foreach (var host in renderer.CurrentHosts)
		{
			if (!host.IsAttached || host.Element is not View || !qmlParents.TryGetValue(host.Id, out var actual))
				continue;
			if (host.QmlUri.StartsWith("synth", StringComparison.Ordinal))
				continue;
			var expected = ExpectedTreeParent(renderer, host.Element);
			if (expected == actual)
				matched++;
			else
			{
				mismatched++;
				firstMismatch ??= $"{host} expected parent {expected}, QML says {actual}";
			}
		}
		_qtTreeChecks.Check($"structure: QML parent == nearest hosted MAUI ancestor for {matched} hosts, mismatched={mismatched}{(firstMismatch is null ? "" : $" (first: {firstMismatch})")}",
			matched > TreeLongLabels && mismatched == 0);

		// No host cap: every long label is hosted.
		var longHosted = f.LongLabels.Count(l => TreeHost(renderer, l) is not null);
		_qtTreeChecks.Check($"no host cap: {longHosted}=={TreeLongLabels} long-page labels hosted (page total {renderer.CurrentHosts.Count} hosts, old cap 400)",
			longHosted == TreeLongLabels);

		// Opacity cascades natively: the child keeps 1.0, the parent carries 0.5 (Qt multiplies).
		var dimmed = TreeHost(renderer, f.Dimmed);
		var dimmedChild = TreeHost(renderer, f.DimmedChild);
		var parentOpacity = dimmed is null ? double.NaN : DiagQml.Num(QtHost.QtHostRuntime.GetProperty(dimmed.NativeHandle, "opacity"));
		var childOpacity = dimmedChild is null ? double.NaN : DiagQml.Num(QtHost.QtHostRuntime.GetProperty(dimmedChild.NativeHandle, "opacity"));
		_qtTreeChecks.Check($"opacity cascade native: parent Grid opacity={parentOpacity:F2}==0.50, child Label own opacity={childOpacity:F2}==1.00 (effective product by Qt)",
			Math.Abs(parentOpacity - 0.5) < 0.01 && Math.Abs(childOpacity - 1.0) < 0.01);

		// Under a translated parent the child's scene rect equals its MAUI root-space rect
		// because Qt composes the parent's transform.
		var shiftedChild = TreeHost(renderer, f.ShiftedChild);
		var flickY = DiagQml.Num(QtHost.QtHostRuntime.Eval("pageStack.currentPage.flickY"));
		var flickYDp = double.IsNaN(flickY) ? 0 : QtHost.QtHostUnits.ToLogical(flickY);
		if (shiftedChild is not null && QtHost.QtHostRuntime.TryItemGeometry(shiftedChild.NativeHandle, out var scene))
		{
			var onScreen = QtHost.QtHostUnits.ToLogical(scene);
			var maui = shiftedChild.MauiLogicalBounds;
			var dx = Math.Abs(onScreen.X - maui.X);
			var dy = Math.Abs(onScreen.Y + flickYDp - maui.Y);
			_qtTreeChecks.Check($"nested geometry: label in a TranslationX=10 Border at scene ({onScreen.X:F1},{onScreen.Y + flickYDp:F1})dp vs MAUI ({maui.X:F1},{maui.Y:F1})dp Δ=({dx:F2},{dy:F2}) ≤1dp",
				dx <= 1.0 && dy <= 1.0);
		}
		else
			_qtTreeChecks.Check("nested geometry: shifted child host readback", false);

		// IsClippedToBounds, Border and ScrollView clip natively.
		var clipGrid = HostProp(TreeHost(renderer, f.Clipped), "clip");
		// The Border clips on its inner box so the root stays unclipped for the shadow.
		var clipBorder = HostProp(TreeHost(renderer, f.Shifted), "mauiChildHost.clip");
		var scrollHost = TreeHost(renderer, f.Scroll);
		var clipScroll = HostProp(scrollHost, "clip");
		_qtTreeChecks.Check($"clip native: IsClippedToBounds Grid clip={clipGrid}==true, Border clip={clipBorder}==true, main ScrollView ({scrollHost?.QmlUri}) clip={clipScroll}==true",
			clipGrid == "true" && clipBorder == "true" && clipScroll == "true" && scrollHost?.QmlUri == "scroll-view");

		// A horizontal ScrollView is its own flickable host; ScrollToAsync moves it natively.
		var stripHost = TreeHost(renderer, f.Strip);
		var stripContentHost = TreeHost(renderer, f.StripContent);
		var stripParents = TreeQmlParents(stripContentHost is null ? Array.Empty<QtHost.NativeElementHost>() : new[] { stripContentHost });
		var contentParent = stripContentHost is not null && stripParents.TryGetValue(stripContentHost.Id, out var cp) ? cp : "?";
		var contentW = DiagQml.Num(HostProp(stripHost, "contentWidth"));
		var viewW = DiagQml.Num(HostProp(stripHost, "width"));
		_qtTreeChecks.Check($"nested scroll host: horizontal ScrollView uri={stripHost?.QmlUri}==scroll-view, content parent {contentParent}=={stripHost?.Id}, contentWidth {contentW:F0} > width {viewW:F0}",
			stripHost?.QmlUri == "scroll-view" && contentParent == stripHost?.Id && contentW > viewW);
		_ = f.Strip.ScrollToAsync(120, 0, false);

		// Button styling reaches the Silica Button internals.
		var styledHost = TreeHost(renderer, f.Styled);
		var diag = styledHost is null ? "" : QtHost.QtHostRuntime.Eval(
			"(function(){var b=pageStack.currentPage.__mauiFindByName('maui_" + styledHost.Id + "');return b&&b.mauiDiag?b.mauiDiag():'';})()");
		double px = double.NaN, radius = double.NaN, strokeW = double.NaN;
		var bold = false;
		var stroke = string.Empty;
		try
		{
			using var doc = System.Text.Json.JsonDocument.Parse(diag);
			px = doc.RootElement.GetProperty("pixel").GetDouble();
			bold = doc.RootElement.GetProperty("bold").GetBoolean();
			radius = doc.RootElement.GetProperty("radius").GetDouble();
			strokeW = doc.RootElement.GetProperty("strokeWidth").GetDouble();
			stroke = doc.RootElement.GetProperty("stroke").GetString() ?? string.Empty;
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[Sailfish] Qt tree diag: button diag unreadable ({ex.Message}): '{diag}'");
		}
		var density = SailfishDisplay.Density;
		_qtTreeChecks.Check($"button styling: font {px:F1}=={20 * density:F1}px bold={bold}, radius {radius:F1}=={12 * density:F1}, stroke {stroke}==#ff0000 width {strokeW:F1}=={3 * density:F1}",
			Math.Abs(px - 20 * density) < 0.6 && bold && Math.Abs(radius - 12 * density) < 0.6 &&
			stroke.Equals("#ff0000", StringComparison.OrdinalIgnoreCase) && Math.Abs(strokeW - 3 * density) < 0.6);

		// Entry text styling reaches the Silica TextField.
		var entryHost = TreeHost(renderer, f.StyledEntry);
		var entryDiag = entryHost is null ? "" : QtHost.QtHostRuntime.Eval(
			"(function(){var e=pageStack.currentPage.__mauiFindByName('maui_" + entryHost.Id + "');" +
			"return e?JSON.stringify({c:e.color.toString(),px:e.font.pixelSize,it:e.font.italic,al:e.horizontalAlignment}):'';})()");
		var entryOk = false;
		try
		{
			using var doc = System.Text.Json.JsonDocument.Parse(entryDiag);
			var r = doc.RootElement;
			entryOk = r.GetProperty("c").GetString()!.Equals("#00ff00", StringComparison.OrdinalIgnoreCase)
			          && Math.Abs(r.GetProperty("px").GetDouble() - 22 * density) < 0.6
			          && r.GetProperty("it").GetBoolean()
			          && r.GetProperty("al").GetInt32() == 4;   // Text.AlignHCenter
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[Sailfish] Qt tree diag: entry diag unreadable ({ex.Message})");
		}
		_qtTreeChecks.Check($"entry styling: {entryDiag} == color #00ff00, {22 * density:F1}px, italic, AlignHCenter(4)", entryOk);

		// Value-control colors on the Silica items; spinner rotation is read now and in the next leg.
		string Js(Element e, string expr)
		{
			var h = TreeHost(renderer, e);
			return h is null ? "" : QtHost.QtHostRuntime.Eval(
				"(function(){var r=pageStack.currentPage.__mauiFindByName('maui_" + h.Id + "');return r?String(" + expr + "):'';})()");
		}
		var swColor = Js(f.ColorSwitch, "r.__glass.length?r.__glass[0].color:''");
		var slColor = Js(f.ColorSlider, "r.color+'/'+r.backgroundColor+'/'+(r.__glass.length>2?r.__glass[2].color:'')");
		var pbColor = Js(f.ColorProgress, "r.__glass.length>1?r.__glass[1].color:''");
		var aiColor = Js(f.Spinner, "r.mauiSpinColor");
		_treeSpin0 = DiagQml.Num(Js(f.Spinner, "r.mauiSpinRotation"));
		_qtTreeChecks.Check($"value colors: switch light {swColor}==#ff00ff, slider {slColor}==#ff0000/#0000ff/#ffff00, progress {pbColor}==#ffa500, spinner {aiColor}==#00ff00",
			swColor.Equals("#ff00ff", StringComparison.OrdinalIgnoreCase) &&
			slColor.Equals("#ff0000/#0000ff/#ffff00", StringComparison.OrdinalIgnoreCase) &&
			pbColor.Equals("#ffa500", StringComparison.OrdinalIgnoreCase) &&
			aiColor.Equals("#00ff00", StringComparison.OrdinalIgnoreCase));

		// RTL: MAUI mirrors the row and a Start-aligned label aligns right.
		var rtlA = TreeHost(renderer, f.RtlFirst);
		var rtlB = TreeHost(renderer, f.RtlSecond);
		var rtlAlign = HostProp(rtlA, "mauiHAlign");
		_qtTreeChecks.Check($"RTL: first x {rtlA?.MauiLogicalBounds.X:F1} > second x {rtlB?.MauiLogicalBounds.X:F1}, Start label mauiHAlign={rtlAlign}==2 (AlignRight)",
			rtlA is not null && rtlB is not null && rtlA.MauiLogicalBounds.X > rtlB.MauiLogicalBounds.X && rtlAlign == "2");

		// Mutations: collapse a container, move a label between containers, reorder.
		var mover = TreeHost(renderer, f.Mover);
		var moverHandle = mover?.NativeHandle ?? 0;
		var moverId = mover?.Id;
		Console.Error.WriteLine("[Sailfish] Qt tree diag: leg B — collapse a container, reparent a label A→B, reorder B");
		f.Collapsible.IsVisible = false;
		f.BoxA.Children.Remove(f.Mover);
		f.BoxB.Children.Add(f.Mover);
		f.BoxB.Children.Remove(f.Mover);
		f.BoxB.Children.Insert(0, f.Mover);
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1200),
			() => VerifyTreeMutations(renderer, dispatcher, nav, f, moverId, moverHandle));
	}

	private void VerifyTreeMutations(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher,
		NavigationPage nav, TreeFixture f, string? moverId, long moverHandle)
	{
		// The adapter uses its own RotationAnimation since Silica's is gated on Qt.application.active.
		var spinnerHost = TreeHost(renderer, f.Spinner);
		var spin1 = spinnerHost is null ? double.NaN : DiagQml.Num(QtHost.QtHostRuntime.Eval(
			"(function(){var r=pageStack.currentPage.__mauiFindByName('maui_" + spinnerHost.Id + "');return r?String(r.mauiSpinRotation):'';})()"));
		_qtTreeChecks.Check($"spinner animates: rotation {_treeSpin0:F1} → {spin1:F1} (changed)",
			!double.IsNaN(_treeSpin0) && !double.IsNaN(spin1) && Math.Abs(spin1 - _treeSpin0) > 1);

		// A collapsed container hides its subtree natively: the child's effective `visible` is false.
		var collapsibleChild = TreeHost(renderer, f.CollapsibleChild);
		var childVisible = collapsibleChild is null ? "?" : QtHost.QtHostRuntime.GetProperty(collapsibleChild.NativeHandle, "visible");
		_qtTreeChecks.Check($"collapse native: child of a Collapsed stack reads visible={childVisible}==false (own applied vis={collapsibleChild?.AppliedVisible})",
			childVisible == "false" && collapsibleChild?.AppliedVisible == true);

		// Reparent keeps the same QML object.
		var mover = TreeHost(renderer, f.Mover);
		var boxB = TreeHost(renderer, f.BoxB);
		var qmlParents = TreeQmlParents(mover is null ? Array.Empty<QtHost.NativeElementHost>() : new[] { mover });
		var moverParent = mover is not null && qmlParents.TryGetValue(mover.Id, out var mp) ? mp : "?";
		_qtTreeChecks.Check($"reparent: mover host {mover?.Id}=={moverId} handle same={mover?.NativeHandle == moverHandle}, QML parent {moverParent}=={boxB?.Id}",
			mover is not null && mover.Id == moverId && mover.NativeHandle == moverHandle && moverParent == boxB?.Id);

		// Native child order follows MAUI children order.
		var order = boxB is null ? string.Empty : QtHost.QtHostRuntime.Eval(
			"(function(){var b=pageStack.currentPage.__mauiFindByName('maui_" + boxB.Id + "');if(!b)return '';var o=[];" +
			"for(var i=0;i<b.children.length;++i){var n=b.children[i].objectName;if(n&&n.indexOf('maui_')===0)o.push(n.substring(5));}return o.join(',');})()");
		var anchor = TreeHost(renderer, f.Anchor);
		var want = $"{mover?.Id},{anchor?.Id}";
		_qtTreeChecks.Check($"order per parent: box B children [{order}]==[{want}] (Insert(0) moved the mover first)", order == want);

		// ScrollToAsync(120) reached the flickable; a native move writes back into ScrollX.
		var stripHost = TreeHost(renderer, f.Strip);
		var contentX = DiagQml.Num(HostProp(stripHost, "contentX"));
		var expectX = QtHost.QtHostUnits.ToQtUnits(120);
		_qtTreeChecks.Check($"scroll managed→native: ScrollToAsync(120dp) → contentX {contentX:F1}=={expectX:F1}qt", Math.Abs(contentX - expectX) < 1.0);
		if (stripHost is not null)
			QtHost.QtHostRuntime.Eval("(function(){var s=pageStack.currentPage.__mauiFindByName('maui_" + stripHost.Id + "');if(s)s.contentX=" +
				QtHost.QtHostUnits.ToQtUnits(40).ToString(System.Globalization.CultureInfo.InvariantCulture) + ";})()");
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(600), () => FinishTree(f));
	}

	private void FinishTree(TreeFixture f)
	{
		_qtTreeChecks.Check($"scroll native→managed: native contentX=40dp → MAUI ScrollX={f.Strip.ScrollX:F1}==40.0", Math.Abs(f.Strip.ScrollX - 40) < 0.6);
		_qtTreeChecks.Accept("OK — F1 host tree: native parent chain mirrors MAUI, cascades and moves are native, no host cap");
		QtHost.QtHostRuntime.Shutdown();
	}
}
