using System.Reflection;
using Microsoft.Maui;
using Microsoft.Maui.SailfishOS.Handlers;

namespace Linux.SailfishOS.Tests;

/// <summary>
/// How the backend covers MAUI's mapper keys, for <see cref="HandlerParityTests"/>: the keys the renderer handles for
/// every view, the ones it owns per control outside the handler mapper, the ones with no meaning here, and a handler's
/// own coverage read from its public mapper. Moved out of the production assembly (W2.4), where only the parity test
/// read them.
/// </summary>
internal static class ViewKeyCoverage
{
	/// <summary>Covered for every view.</summary>
	public static readonly IReadOnlySet<string> Handled = new HashSet<string>(StringComparer.Ordinal)
	{
		// geometry and size constraints
		nameof(IView.Visibility), nameof(IView.Width), nameof(IView.Height),
		nameof(IView.MinimumWidth), nameof(IView.MinimumHeight),
		nameof(IView.MaximumWidth), nameof(IView.MaximumHeight),
		// visual state on native QQuickItem properties
		nameof(IView.IsEnabled), nameof(IView.Opacity),
		nameof(IView.TranslationX), nameof(IView.TranslationY),
		nameof(IView.Scale), nameof(IView.ScaleX), nameof(IView.ScaleY),
		nameof(IView.Rotation), nameof(IView.RotationX), nameof(IView.RotationY),
		nameof(IView.AnchorX), nameof(IView.AnchorY),
		// input router hit-test
		nameof(IView.InputTransparent),
		// presented content is a nested host
		nameof(IContentView.Content),
		// Qt accessibility (SemanticProperties Description/Hint/HeadingLevel → Accessible.name/description/role,
		// AutomationProperties IsInAccessibleTree/ExcludedWithChildren → Accessible.ignored) and the AutomationId
		// dynamic property
		nameof(IView.Semantics), "Description", "Hint", "HeadingLevel", nameof(IView.AutomationId),
		"IsInAccessibleTree", "ExcludedWithChildren",
		// Controls forwards it to Background; only Page has the property, painted by the model page.
		"BackgroundImageSource",
		// RTL: layouts mirror their children in the geometry walk, text controls map Start/End, leaf controls
		// mirror their Silica internals (LayoutMirroring; Slider/ProgressBar also flip their value axis).
		nameof(IView.FlowDirection),
		// layer effects (DropShadow / OpacityMask)
		nameof(IView.Shadow), nameof(IView.Clip),
	};

	/// <summary>Per-control keys the renderer covers outside the handler mapper (transient or structural state).</summary>
	public static readonly IReadOnlyDictionary<Type, IReadOnlySet<string>> RendererOwned = BuildRendererOwned();

	private static Dictionary<Type, IReadOnlySet<string>> BuildRendererOwned()
	{
		var owned = new Dictionary<Type, IReadOnlySet<string>>
		{
			// Pull surface and spinner state: pushed to the wrapped scroll/list host, which the RefreshView's handler
			// does not own (QtHostRefreshBinding).
			[typeof(Microsoft.Maui.Controls.RefreshView)] = new HashSet<string> { nameof(IRefreshView.IsRefreshing),
				nameof(IRefreshView.IsRefreshEnabled), nameof(IRefreshView.RefreshColor) },
		};
		// Generic background fill (QtHostVisualState).
		foreach (var type in Microsoft.Maui.SailfishOS.Platform.QtHost.QtHostVisualState.GenericBackgroundTypes)
		{
			var keys = owned.TryGetValue(type, out var existing) ? new HashSet<string>(existing) : new HashSet<string>();
			keys.Add(nameof(IView.Background));
			keys.Add(nameof(Microsoft.Maui.Controls.VisualElement.BackgroundColor));
			owned[type] = keys;
		}
		return owned;
	}

	/// <summary>Keys with no meaning on this platform (not counted as gaps).</summary>
	public static readonly IReadOnlySet<string> NotApplicable = new HashSet<string>(StringComparer.Ordinal)
	{
		"ContainerView",          // platform wrapper view for shadow/clip/border — the host tree needs none
		"Border",                 // IBorder, obsolete and implemented by no control; it only re-maps ContainerView
		nameof(IToolTipElement.ToolTip),   // pointer-hover desktop concept; Sailfish is touch-only
	};

	/// <summary>Whether the handler pushes <paramref name="key"/>: its public static Mapper (snapshot keys, transient
	/// input keys, the chained generic view keys), or its snapshot owns the property without a key of its own (a
	/// snapshot handler's public <c>OwnsProperty</c>: the Border's visual properties, every key of a shape or a
	/// GraphicsView).</summary>
	public static bool HandlerCovers(Type handlerType, string key)
	{
		var mapper = handlerType.GetField("Mapper", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
			?.GetValue(null) as IPropertyMapper ?? SailfishViewMapper.Mapper;
		if (mapper.GetKeys().Contains(key))
			return true;
		var owns = handlerType.GetMethod("OwnsProperty", BindingFlags.Public | BindingFlags.Instance, new[] { typeof(string) });
		return owns is not null && (bool)owns.Invoke(Activator.CreateInstance(handlerType), new object[] { key })!;
	}
}
