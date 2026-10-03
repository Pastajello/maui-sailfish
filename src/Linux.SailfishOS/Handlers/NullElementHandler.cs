using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.SailfishOS.Platform.QtHost;

namespace Microsoft.Maui.SailfishOS.Handlers;

/// <summary>
/// No-op handler for non-view elements (Application, Window) that satisfies MAUI's handler resolution.
/// </summary>
public class NullElementHandler : ElementHandler<IElement, object>
{
	public NullElementHandler() : base(new PropertyMapper<IElement, NullElementHandler>())
	{
	}

	protected override object CreatePlatformElement() => new object();
}

/// <summary>
/// Handler of views without a host of their own: pages (the renderer maps them onto model pages) and views no
/// Sailfish handler serves. Controls with an adapter derive from <see cref="SailfishViewHandler{TVirtualView}"/>.
/// It measures via <see cref="SailfishMeasure"/>; rendering belongs to <see cref="QtHostPageRenderer"/>.
/// </summary>
public class NullViewHandler : ViewHandler<IView, object>
{
	public NullViewHandler() : this(new PropertyMapper<IView, NullViewHandler>())
	{
	}

	/// <summary>Constructor for derived handlers with their own mapper.</summary>
	protected NullViewHandler(PropertyMapper<IView, NullViewHandler> mapper) : base(mapper, SailfishViewMapper.CommandMapper)
	{
	}

	/// <summary>Constructor for handlers that also answer MAUI commands.</summary>
	protected NullViewHandler(IPropertyMapper mapper, CommandMapper? commandMapper)
		: base(mapper, commandMapper ?? SailfishViewMapper.CommandMapper)
	{
	}

	protected override object CreatePlatformView() => new object();

	public override void UpdateValue(string property)
	{
		SailfishHandlerCore.TraceUpdate(this, property);
		base.UpdateValue(property);
	}

	public override void PlatformArrange(Rect frame)
	{
		base.PlatformArrange(frame);
		// A page's content is arranged into the content area by the renderer (below the Silica chrome).
		if (VirtualView is not Page)
			SailfishHandlerCore.ArrangeContent(VirtualView, frame);
	}

	/// <summary>Generic measure.</summary>
	public override Size GetDesiredSize(double widthConstraint, double heightConstraint) =>
		SailfishHandlerCore.DesiredSize(VirtualView, widthConstraint, heightConstraint, null);
}

/// <summary>A handler whose control takes native (Qt) focus; null = not native yet or no native focus.</summary>
internal interface ISailfishNativeFocus
{
	bool? FocusNatively(bool focus);
}

/// <summary>What every Sailfish view handler does the same way, typed or not.</summary>
internal static class SailfishHandlerCore
{
	/// <summary>Hosts of handlers whose services carry no render session (a bare provider in a test); they never
	/// attach natively.</summary>
	private static readonly NativeHostCache Detached = new();

	/// <summary>The render session of a handler's MAUI context.</summary>
	public static SailfishRenderSession? SessionOf(IElementHandler handler) => SailfishRenderSession.Of(handler.MauiContext?.Services);

	/// <summary>The element's host from the session's cache (the same instance the reconcile uses, whether the
	/// renderer runs yet or not), unbound while no adapter is chosen.</summary>
	public static NativeElementHost HostFor(IElementHandler handler, IView view, string? adapterUri)
	{
		if (view is not Element element)
			throw new InvalidOperationException($"{view.GetType()} is not a MAUI Element; Sailfish hosts are keyed on elements");
		return (SessionOf(handler)?.Cache ?? Detached).GetOrAddForHandler(element, adapterUri);
	}

	/// <summary>Traces which property names reach the handler at runtime.</summary>
	public static void TraceUpdate(IElementHandler handler, string property)
	{
		if (QtHostDiag.TraceEnabled && handler.VirtualView is not null)
			QtHostDiag.Trace(QtHostDiagChannel.QmlProperty, $"update-value {handler.GetType().Name} {property}");
	}

	/// <summary>Focus(): Qt decides where the control has native focus (<see cref="ISailfishNativeFocus"/>) and
	/// IsFocused follows, as the platforms write it back from their focus change; otherwise IsFocused is written and
	/// the request granted, since an unanswered FocusRequest throws on Result.</summary>
	public static void MapFocus(IViewHandler handler, IView view, object? args) => ApplyFocus(handler, view, args, true);

	public static void MapUnfocus(IViewHandler handler, IView view, object? args) => ApplyFocus(handler, view, args, false);

	private static void ApplyFocus(IViewHandler handler, IView view, object? args, bool focus)
	{
		var native = (handler as ISailfishNativeFocus)?.FocusNatively(focus);
		var granted = native ?? true;
		if (view is VisualElement visual)
			visual.SetValue(VisualElement.IsFocusedPropertyKey, focus && granted);
		if (focus || native is not null)
			(args as FocusRequest)?.TrySetResult(granted);
	}

	/// <summary>InvalidateMeasure(): a layout pass, as a native view would request one.</summary>
	public static void MapInvalidateMeasure(IViewHandler handler, IView view, object? args)
	{
		SessionOf(handler)?.RequestLayout();
		ViewHandler.MapInvalidateMeasure(handler, view, args);
	}

	/// <summary>
	/// What a native container's layout pass does once its frame is set (Android LayoutViewGroup.OnLayout): arranges
	/// the cross-platform children in its own coordinates, so every child Frame is parent-relative and the recursion
	/// is MAUI's own (padding, a ScrollView's content extent).
	/// </summary>
	public static void ArrangeContent(IView? view, Rect frame)
	{
		if (view is ICrossPlatformLayout layout)
			layout.CrossPlatformArrange(new Rect(0, 0, frame.Width, frame.Height));
	}

	/// <summary>Measure through <see cref="SailfishMeasure.Frame"/>; <paramref name="measure"/> null is the generic one.</summary>
	public static Size DesiredSize(IView? view, double widthConstraint, double heightConstraint,
	                               Func<IView, double, double, Size>? measure) =>
		view is null
			? Size.Zero
			: SailfishMeasure.Frame(view, widthConstraint, heightConstraint, measure ?? SailfishMeasure.Generic);
}
