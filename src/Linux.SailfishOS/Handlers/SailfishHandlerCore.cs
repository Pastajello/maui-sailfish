using System.Runtime.CompilerServices;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.SailfishOS.Platform.QtHost;

namespace Microsoft.Maui.SailfishOS.Handlers;

/// <summary>A handler whose control takes native (Qt) focus; null = not native yet or no native focus.</summary>
internal interface ISailfishNativeFocus
{
	bool? FocusNatively(bool focus);
}

/// <summary>What every Sailfish view handler does the same way, typed or not.</summary>
internal static class SailfishHandlerCore
{
	/// <summary>The keys that push a snapshot, by the mapper <c>SnapshotMapper</c> built.</summary>
	private static readonly ConditionalWeakTable<IPropertyMapper, string[]> OwnedKeySets = new();

	public static void RegisterOwnedKeys(IPropertyMapper mapper, string[] keys) => OwnedKeySets.AddOrUpdate(mapper, keys);

	/// <summary>The snapshot keys of <paramref name="mapper"/> and of the mappers it chains from: an app's mapper
	/// chained from a built-in one (a subclass handler) owns the same keys.</summary>
	public static IEnumerable<string> OwnedKeys(IPropertyMapper mapper)
	{
		var keys = new HashSet<string>(StringComparer.Ordinal);
		var pending = new Stack<IPropertyMapper>();
		var seen = new HashSet<IPropertyMapper>(ReferenceEqualityComparer.Instance);
		pending.Push(mapper);
		while (pending.Count > 0)
		{
			var next = pending.Pop();
			if (!seen.Add(next))
				continue;
			if (OwnedKeySets.TryGetValue(next, out var owned))
			{
				keys.UnionWith(owned);
				continue;   // what it chains from is the generic view mapper
			}
			if (next is PropertyMapper { Chained: { } chained })
				foreach (var parent in chained)
					pending.Push(parent);
		}
		return keys;
	}

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
		var wasFocused = view is VisualElement { IsFocused: true };
		if (view is VisualElement visual)
			visual.SetValue(VisualElement.IsFocusedPropertyKey, focus && granted);
		// Editor completes when it loses focus (Return is a newline), also when the app unfocuses it; the native echo
		// of this push is suppressed, so the focus-changed path does not see it.
		if (!focus && wasFocused && view is Editor editor)
			editor.SendCompleted();
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
