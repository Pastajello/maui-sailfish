namespace Microsoft.Maui.SailfishOS.Handlers;

/// <summary>
/// How the backend covers MAUI's view-level mapper keys (handled for every view by the renderer and layout).
/// HandlerParityTests reads these to report gaps against the official handlers.
/// </summary>
internal static class SailfishViewKeys
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
		foreach (var type in Platform.QtHost.QtHostVisualState.GenericBackgroundTypes)
		{
			var keys = owned.TryGetValue(type, out var existing) ? new HashSet<string>(existing) : new HashSet<string>();
			keys.Add(nameof(IView.Background));
			keys.Add(nameof(Microsoft.Maui.Controls.VisualElement.BackgroundColor));
			owned[type] = keys;
		}
		return owned;
	}

	/// <summary>Text-input properties the handlers leave out of their snapshots, so native focus and caret survive the
	/// reconcile poll; their mappers push them on their own (QtHostPageRenderer.PushTransient).</summary>
	internal static readonly string[] TransientInput =
		{ nameof(Microsoft.Maui.Controls.VisualElement.IsFocused), nameof(Microsoft.Maui.Controls.InputView.CursorPosition),
		  nameof(Microsoft.Maui.Controls.InputView.SelectionLength) };

	/// <summary>Keys with no meaning on this platform (not counted as gaps).</summary>
	public static readonly IReadOnlySet<string> NotApplicable = new HashSet<string>(StringComparer.Ordinal)
	{
		"ContainerView",          // platform wrapper view for shadow/clip/border — the host tree needs none
		"Border",                 // IBorder, obsolete and implemented by no control; it only re-maps ContainerView
		nameof(IToolTipElement.ToolTip),   // pointer-hover desktop concept; Sailfish is touch-only
	};
}
