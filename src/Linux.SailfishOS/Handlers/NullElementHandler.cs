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
	protected NullViewHandler(PropertyMapper<IView, NullViewHandler> mapper) : base(mapper)
	{
	}

	/// <summary>Constructor for handlers that also answer MAUI commands.</summary>
	protected NullViewHandler(IPropertyMapper mapper, CommandMapper? commandMapper) : base(mapper, commandMapper)
	{
	}

	protected override object CreatePlatformView() => new object();

	public override void UpdateValue(string property)
	{
		SailfishHandlerCore.TraceUpdate(this, property);
		base.UpdateValue(property);
	}

	public override void Invoke(string command, object? args = null)
	{
		if (!SailfishHandlerCore.TryInvoke(this, command, args))
			base.Invoke(command, args);
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

/// <summary>What every Sailfish view handler does the same way, typed or not.</summary>
internal static class SailfishHandlerCore
{
	/// <summary>Hosts of handlers connected while no renderer runs; they never attach natively.</summary>
	private static readonly NativeHostCache Detached = new();

	/// <summary>The element's host: the renderer's (the same instance the reconcile uses), unbound while no adapter
	/// is chosen.</summary>
	public static NativeElementHost HostFor(IView view, string? adapterUri)
	{
		if (view is not Element element)
			throw new InvalidOperationException($"{view.GetType()} is not a MAUI Element; Sailfish hosts are keyed on elements");
		return (QtHostPageRenderer.Current?.Cache ?? Detached).GetOrAddForHandler(element, adapterUri);
	}

	/// <summary>Traces which property names reach the handler at runtime.</summary>
	public static void TraceUpdate(IElementHandler handler, string property)
	{
		if (QtHostDiag.TraceEnabled && handler.VirtualView is not null)
			QtHostDiag.Trace(QtHostDiagChannel.QmlProperty, $"update-value {handler.GetType().Name} {property}");
	}

	/// <summary>
	/// The commands every Sailfish view handler answers the same way: InvalidateMeasure asks for a layout pass (a
	/// native view would request one), and Focus/Unfocus write IsFocused, since an unanswered FocusRequest throws on
	/// Result. Qt is the focus source; the renderer mirrors adapter focus changes into MAUI through these commands.
	/// Returns true when the command is consumed.
	/// </summary>
	public static bool TryInvoke(IViewHandler handler, string command, object? args)
	{
		switch (command)
		{
			case nameof(IView.InvalidateMeasure):
				QtHostPageRenderer.Current?.RequestLayout();
				return false;
			case nameof(IView.Focus):
				if (handler.VirtualView is VisualElement focusing)
					focusing.SetValue(VisualElement.IsFocusedPropertyKey, true);
				if (args is FocusRequest focusRequest)
					focusRequest.TrySetResult(true);
				return true;
			case nameof(IView.Unfocus):
				if (handler.VirtualView is VisualElement unfocusing)
					unfocusing.SetValue(VisualElement.IsFocusedPropertyKey, false);
				return true;
			default:
				return false;
		}
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
