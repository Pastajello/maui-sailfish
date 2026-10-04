using System.Reflection;
using System.Text;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.SailfishOS.Handlers;
using Xunit;
using Xunit.Abstractions;

namespace Linux.SailfishOS.Tests;

/// <summary>
/// Handler parity: every key the official MAUI handler maps must be covered by the Sailfish
/// handler's mapper (its snapshot keys, the generic view keys) or the renderer (<see cref="ViewKeyCoverage"/>).
/// Known gaps are a ratchet: a new gap fails, and so does a closed one still listed.
///
/// Regenerate docs/handler-parity.md with SF_WRITE_PARITY=1 dotnet test.
/// </summary>
public class HandlerParityTests
{
	private readonly ITestOutputHelper _output;

	public HandlerParityTests(ITestOutputHelper output) => _output = output;

	/// <summary>Control type → the official MAUI handler whose Mapper defines parity.</summary>
	private static readonly (Type Control, string OfficialHandler)[] Pairs =
	{
		(typeof(Label), "Microsoft.Maui.Handlers.LabelHandler"),
		(typeof(Button), "Microsoft.Maui.Handlers.ButtonHandler"),
		(typeof(ImageButton), "Microsoft.Maui.Handlers.ImageButtonHandler"),
		(typeof(Entry), "Microsoft.Maui.Handlers.EntryHandler"),
		(typeof(Editor), "Microsoft.Maui.Handlers.EditorHandler"),
		(typeof(SearchBar), "Microsoft.Maui.Handlers.SearchBarHandler"),
		(typeof(Switch), "Microsoft.Maui.Handlers.SwitchHandler"),
		(typeof(CheckBox), "Microsoft.Maui.Handlers.CheckBoxHandler"),
		(typeof(Slider), "Microsoft.Maui.Handlers.SliderHandler"),
		(typeof(Stepper), "Microsoft.Maui.Handlers.StepperHandler"),
		(typeof(ProgressBar), "Microsoft.Maui.Handlers.ProgressBarHandler"),
		(typeof(ActivityIndicator), "Microsoft.Maui.Handlers.ActivityIndicatorHandler"),
		(typeof(Picker), "Microsoft.Maui.Handlers.PickerHandler"),
		(typeof(DatePicker), "Microsoft.Maui.Handlers.DatePickerHandler"),
		(typeof(TimePicker), "Microsoft.Maui.Handlers.TimePickerHandler"),
		(typeof(RadioButton), "Microsoft.Maui.Handlers.RadioButtonHandler"),
		(typeof(Image), "Microsoft.Maui.Handlers.ImageHandler"),
		(typeof(Border), "Microsoft.Maui.Handlers.BorderHandler"),
		(typeof(BoxView), "Microsoft.Maui.Handlers.ShapeViewHandler"),
		(typeof(GraphicsView), "Microsoft.Maui.Handlers.GraphicsViewHandler"),
		(typeof(Grid), "Microsoft.Maui.Handlers.LayoutHandler"),
		(typeof(ContentView), "Microsoft.Maui.Handlers.ContentViewHandler"),
		(typeof(ScrollView), "Microsoft.Maui.Handlers.ScrollViewHandler"),
		(typeof(RefreshView), "Microsoft.Maui.Handlers.RefreshViewHandler"),
		(typeof(SwipeView), "Microsoft.Maui.Handlers.SwipeViewHandler"),
		(typeof(IndicatorView), "Microsoft.Maui.Handlers.IndicatorViewHandler"),
		(typeof(WebView), "Microsoft.Maui.Handlers.WebViewHandler"),
	};

	/// <summary>Pinned gaps; delete an entry in the same change that closes it.</summary>
	private static readonly Dictionary<Type, string> KnownGaps = new();

	// Creating the first control makes MAUI Controls remap the handler mappers (semantics, background…); a
	// running app is always remapped, so parity is measured against the remapped keys, whatever ran first.
	private static readonly Label RemapTrigger = new();

	private static IReadOnlyList<string> OfficialKeys(string handlerTypeName)
	{
		GC.KeepAlive(RemapTrigger);
		var handler = typeof(Microsoft.Maui.Handlers.ViewHandler).Assembly.GetType(handlerTypeName, throwOnError: true)!;
		var field = handler.GetField("Mapper", BindingFlags.Public | BindingFlags.Static)
			?? throw new InvalidOperationException($"{handlerTypeName} has no public static Mapper");
		var mapper = (IPropertyMapper)field.GetValue(null)!;
		return mapper.GetKeys().Distinct(StringComparer.Ordinal).ToList();
	}

	private static (List<string> Covered, List<string> Gaps) Classify(Type control, IReadOnlyList<string> keys)
	{
		var handlerType = SailfishHandlersFactory.ResolveViewRow(control).Handler;
		var covered = new List<string>();
		var gaps = new List<string>();
		foreach (var key in keys)
		{
			if (ViewKeyCoverage.NotApplicable.Contains(key))
				continue;
			if (ViewKeyCoverage.Handled.Contains(key) || ViewKeyCoverage.HandlerCovers(handlerType, key) ||
			    (ViewKeyCoverage.RendererOwned.TryGetValue(control, out var owned) && owned.Contains(key)))
				covered.Add(key);
			else
				gaps.Add(key);
		}
		gaps.Sort(StringComparer.Ordinal);
		return (covered, gaps);
	}

	[Fact]
	public void Mapper_gaps_match_the_pinned_list()
	{
		var report = new StringBuilder();
		var failures = new List<string>();
		int totalKeys = 0, totalCovered = 0;
		report.AppendLine("| Control | Covered | Gaps |").AppendLine("|---|---|---|");
		foreach (var (control, official) in Pairs)
		{
			var keys = OfficialKeys(official);
			var (covered, gaps) = Classify(control, keys);
			var counted = covered.Count + gaps.Count;
			totalKeys += counted;
			totalCovered += covered.Count;
			report.AppendLine($"| {control.Name} | {covered.Count}/{counted} | {string.Join(", ", gaps)} |");

			var pinned = KnownGaps.TryGetValue(control, out var list)
				? list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal)
				: new HashSet<string>(StringComparer.Ordinal);
			var added = gaps.Where(g => !pinned.Contains(g)).ToList();
			var closed = pinned.Where(p => !gaps.Contains(p)).ToList();
			if (added.Count > 0)
				failures.Add($"{control.Name}: NEW gaps {string.Join(", ", added)}");
			if (closed.Count > 0)
				failures.Add($"{control.Name}: closed gaps still pinned {string.Join(", ", closed)} — remove them from KnownGaps");
		}
		var summary = $"Overall: {totalCovered}/{totalKeys} official mapper keys covered ({100.0 * totalCovered / totalKeys:F0}%).";
		_output.WriteLine(summary);
		_output.WriteLine(report.ToString());

		if (Environment.GetEnvironmentVariable("SF_WRITE_PARITY") == "1")
		{
			var doc = new StringBuilder()
				.AppendLine("# Handler parity vs the official MAUI mappers")
				.AppendLine()
				.AppendLine("Generated by `SF_WRITE_PARITY=1 dotnet test tests/Linux.SailfishOS.Tests` " +
				            "(HandlerParityTests). A key is covered when the control's Sailfish handler " +
				            "mapper owns it or the renderer covers it for every view (ViewKeyCoverage); " +
				            "ContainerView, ToolTip and the obsolete IBorder key Border are not applicable on Sailfish.")
				.AppendLine()
				.AppendLine(summary)
				.AppendLine()
				.Append(report);
			File.WriteAllText(Path.Combine(Repo.Root, "docs/handler-parity.md"), doc.ToString());
		}

		Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
	}
}

/// <summary>Transient text-input state (focus, caret, selection) is mapped, but outside the snapshot: a snapshot
/// re-applied by the reconcile must never reset native focus or caret.</summary>
public class PropertyOwnershipTests
{
	[Theory]
	[InlineData(typeof(Entry))]
	[InlineData(typeof(Editor))]
	[InlineData(typeof(SearchBar))]
	public void Transient_input_state_is_mapped_outside_the_snapshot(Type control)
	{
		var handlerType = SailfishHandlersFactory.ResolveViewRow(control).Handler;
		dynamic handler = Activator.CreateInstance(handlerType)!;
		var mapper = (IPropertyMapper)handlerType.GetField("Mapper")!.GetValue(null)!;
		foreach (var name in Microsoft.Maui.SailfishOS.Handlers.SailfishViewKeys.TransientInput)
		{
			Assert.False((bool)handler.OwnsProperty(name), $"{handlerType.Name} snapshots transient {name}");
			Assert.Contains(name, mapper.GetKeys());
			Assert.True(ViewKeyCoverage.HandlerCovers(handlerType, name), $"{handlerType.Name} does not cover {name}");
		}
	}

	[Fact]
	public void Every_view_handler_row_is_reachable()
	{
		// A base type listed before its derived type would shadow the derived row forever.
		var rows = SailfishHandlersFactory.ViewHandlers;
		for (var i = 0; i < rows.Length; i++)
			for (var j = 0; j < i; j++)
				Assert.False(rows[j].View.IsAssignableFrom(rows[i].View) && rows[j].Handler != rows[i].Handler,
					$"{rows[i].View.Name} is shadowed by {rows[j].View.Name}");
	}
}
