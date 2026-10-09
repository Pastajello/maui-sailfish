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
/// Handler of views without a host of their own: pages (the renderer maps them onto model pages) and views whose
/// handler cannot run here (a library's plain-net handler that throws; the renderer then hosts only their children).
/// Controls with an adapter derive from <see cref="SailfishViewHandler{TVirtualView}"/>, views no Sailfish handler
/// serves get <see cref="SailfishContainerHandler"/>. It measures via <see cref="SailfishMeasure"/>.
/// </summary>
public class NullViewHandler : ViewHandler<IView, object>
{
	/// <summary>Chained from <see cref="SailfishViewMapper.Mapper"/>, as every Sailfish view mapper.</summary>
	public static readonly PropertyMapper<IView, NullViewHandler> Mapper = new(SailfishViewMapper.Mapper);

	public NullViewHandler() : this(Mapper, null)
	{
	}

	/// <summary>Constructor for handlers that also answer MAUI commands.</summary>
	protected NullViewHandler(IPropertyMapper mapper, CommandMapper? commandMapper)
		: base(mapper, commandMapper ?? SailfishViewMapper.CommandMapper)
	{
	}

	/// <summary>The virtual view, or null once disconnected (the typed <c>VirtualView</c> throws then).</summary>
	protected IView? ConnectedView => ((IElementHandler)this).VirtualView as IView;

	/// <summary>No platform container: clip and shadow ride the host's own layer effect, so HasContainer stays false
	/// instead of reporting a container that never exists (ContainerView stays null, tracker S13).</summary>
	public override bool NeedsContainer => false;

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
		if (ConnectedView is not Page)
			SailfishHandlerCore.ArrangeContent(ConnectedView, frame);
	}

	/// <summary>Generic measure.</summary>
	public override Size GetDesiredSize(double widthConstraint, double heightConstraint) =>
		SailfishHandlerCore.DesiredSize(ConnectedView, widthConstraint, heightConstraint, null);
}
