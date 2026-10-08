using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.SailfishOS.Handlers;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>
/// The platform side of MAUI's layout: starts the measure/arrange of a page or collection item, and the handlers
/// carry the arrange down (PlatformArrange → CrossPlatformArrange), as native containers do on the other platforms.
/// Afterwards every element's Bounds is parent-relative in dp.
/// </summary>
internal static class QtHostLayout
{
	/// <summary>Handler types whose SetVirtualView threw, warned once each.</summary>
	private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, byte> FailedHandlerTypes = new();

	/// <summary>
	/// Attaches a handler to every element that has none; handlers must exist before Measure,
	/// which consults Handler.GetDesiredSize.
	/// </summary>
	public static void AttachHandlers(IElement root, IMauiContext context)
	{
		ArgumentNullException.ThrowIfNull(context);
		AttachHandlersCore(root, context);
	}

	/// <summary>A view no handler can render: its children only, under its own drawing when it draws itself.</summary>
	private static IElementHandler EmptyViewHandler(IElement element) =>
		element is Microsoft.Maui.Graphics.IDrawable ? new SailfishDrawnViewHandler() : new NullViewHandler();

	private static bool IsDrawnContainer(IElement element) =>
		element is Microsoft.Maui.Graphics.IDrawable && element is IContentView or ILayout;

	private static void AttachHandlersCore(IElement element, IMauiContext context)
	{
		if (element.Handler is null)
		{
			// Resolve through the handler factory; instantiating directly would silently bypass every registration.
			IElementHandler? handler = null;
			try
			{
				handler = context.Handlers.GetHandler(element.GetType());
			}
			catch
			{
				handler = null;   // unregistered element: fall back below
			}
			handler ??= element is ScrollView
				? new SailfishScrollViewHandler()
				: element is IView
					? EmptyViewHandler(element)
					: new NullElementHandler();
			try
			{
				handler.SetMauiContext(context);
				handler.SetVirtualView(element);
			}
			catch (Exception ex) when (handler is not (NullViewHandler or NullElementHandler))
			{
				// A library without a Sailfish asset resolves its plain-net handler, whose CreatePlatformView throws
				// (NotImplementedException). The element stays handler-less then, so fall back to the empty one: it
				// renders as an empty container with its children, instead of failing every layout pass.
				// A library handler built on a stock one (UraniumUI's Button) falls back to the Sailfish handler of that
				// control, so it still renders and works; anything else renders empty.
				// A self-drawing container keeps its drawing rather than the stock container it derives from.
				var fallback = element is IView && !IsDrawnContainer(element) ? SailfishHandlersFactory.BuiltInFallback(element.GetType()) : null;
				if (FailedHandlerTypes.TryAdd(handler.GetType(), 0))
					QtHostDiag.Warn(QtHostDiagChannel.QtHost,
						$"{handler.GetType().FullName} failed for {element.GetType().FullName} ({ex.GetType().Name}: {ex.Message}) — " +
						(fallback is not null ? $"falling back to {fallback.GetType().Name}"
							: element is Microsoft.Maui.Graphics.IDrawable ? "rendered from its own drawing (IDrawable) with its children"
							: "rendered as an empty container"));
				handler = fallback ?? (element is IView ? EmptyViewHandler(element) : new NullElementHandler());
				handler.SetMauiContext(context);
				handler.SetVirtualView(element);
			}
		}

		// A collection's item views (its logical children) get their handlers as their rows are measured.
		if (element is IVisualTreeElement visualTreeElement && element is not Microsoft.Maui.Controls.ItemsView)
		{
			foreach (var child in QtHostVisualChildren.Of(visualTreeElement))
			{
				if (child is IElement childElement)
					AttachHandlersCore(childElement, context);
			}
		}
	}

	/// <summary>
	/// Runs the full measure/arrange pass of one page, as a platform's native layout pass would: the page fills the
	/// window, its content the area below the Silica chrome, and every descendant is arranged by its parent's handler
	/// (<see cref="SailfishHandlerCore.ArrangeContent"/>), so Frames are parent-relative in dp.
	/// </summary>
	/// <param name="page">The MAUI page to lay out (ContentPage expected).</param>
	/// <param name="windowDp">The full window size in dp (root space extent before insets).</param>
	/// <param name="contentRectDp">Content area in dp (window minus Silica chrome insets) the content is arranged into.</param>
	public static void MeasureAndArrange(Page page, Size windowDp, Rect contentRectDp)
	{
		// The page gets the content area, below the status area and PageHeader (and a tab row), as a page gets the
		// area below the toolbar and above the tabs on Android and iOS: apps size views from OnSizeAllocated
		// (WhatToEat: carousel HeightRequest = height - 150 ran off the screen with the window height).
		page.Measure(contentRectDp.Width, contentRectDp.Height);
		page.Arrange(contentRectDp);

		if (page is ContentPage contentPage && contentPage.Content is IView contentView)
		{
			// Page.Padding insets the content, as ContentPage's own arrange does on the other platforms.
			var padding = contentPage.Padding;
			var inner = new Rect(contentRectDp.X + padding.Left, contentRectDp.Y + padding.Top,
				Math.Max(0, contentRectDp.Width - padding.HorizontalThickness),
				Math.Max(0, contentRectDp.Height - padding.VerticalThickness));
			contentView.Measure(inner.Width, inner.Height);
			contentView.Arrange(inner);
		}
	}

	/// <summary>Lays out a view that sits outside the page content (a TitleView) in <paramref name="rectDp"/>.</summary>
	internal static void MeasureAndArrangeIn(IView view, Rect rectDp)
	{
		view.Measure(rectDp.Width, rectDp.Height);
		view.Arrange(rectDp);
	}

	/// <summary>
	/// Lays out one collection item view, which lives outside the page tree, at a fixed width.
	/// Returns its height in dp.
	/// </summary>
	internal static double MeasureAndArrangeItem(IView view, double widthDp)
	{
		var desired = view.Measure(widthDp, double.PositiveInfinity);
		var height = Math.Max(0, desired.Height);
		view.Arrange(new Rect(0, 0, widthDp, height));
		return height;
	}

	/// <summary>Lays out a horizontal-list item at a fixed height; returns its width in dp.</summary>
	internal static double MeasureAndArrangeItemAcross(IView view, double heightDp)
	{
		var desired = view.Measure(double.PositiveInfinity, heightDp);
		var width = Math.Max(0, desired.Width);
		view.Arrange(new Rect(0, 0, width, heightDp));
		return width;
	}

	/// <summary>Lays out a carousel item at exactly the given size.</summary>
	internal static void MeasureAndArrangeItemFixed(IView view, double widthDp, double heightDp)
	{
		view.Measure(widthDp, heightDp);
		view.Arrange(new Rect(0, 0, widthDp, heightDp));
	}
}