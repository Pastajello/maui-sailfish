using System.Text.Json;
using Microsoft.Maui.Controls;
using Microsoft.Maui.SailfishOS.Platform.QtHost;

namespace Microsoft.Maui.SailfishOS.Handlers;

/// <summary>Image handler: the source resolves to a URL Qt loads itself; an unresolvable source gets no host.</summary>
public class SailfishImageHandler : SailfishSnapshotHandler<IImage>
{
	private static readonly string[] Keys =
	{
		nameof(Image.Source), nameof(Image.Aspect), nameof(Image.BackgroundColor), nameof(IView.Background),
		nameof(IImage.IsAnimationPlaying),
		// ImageButton frame (the same handler serves both)
		nameof(IButtonStroke.CornerRadius), nameof(IButtonStroke.StrokeColor), nameof(ImageButton.BorderColor),
		nameof(IButtonStroke.StrokeThickness), nameof(ImageButton.BorderWidth), nameof(IPadding.Padding),
	};

	public static readonly PropertyMapper<IImage, SailfishImageHandler> Mapper = WithLoading(SnapshotMapper<SailfishImageHandler>(Keys));

	private static PropertyMapper<IImage, SailfishImageHandler> WithLoading(PropertyMapper<IImage, SailfishImageHandler> mapper)
	{
		mapper.AppendToMapping(nameof(Image.Source), static (handler, view) => handler.TrackLoading(view));
		return mapper;
	}

	private ImageSource? _source;      // the source the handler last saw (its pending load is cancelled on a change)
	private string? _loadingUrl;        // the URL whose load the adapter has not reported yet
	private string? _loadedUrl;         // the URL the adapter last reported loaded (or failed)

	/// <summary>
	/// Image.IsLoading, as MAUI's platform handlers report it: true from a source change until the image is shown or
	/// has failed. The previous source's pending read (a stream, a service) is cancelled, as MAUI's
	/// ImageSourceServiceResultManager cancels the previous load.
	/// </summary>
	private void TrackLoading(IImage view)
	{
		var source = view.Source as ImageSource;
		if (!ReferenceEquals(source, _source))
		{
			QtHostImages.CancelPending(_source);
			_source = source;
		}
		var pending = QtHostImages.IsPending(source);
		view.UpdateIsLoading(Wait(QtHostImages.Resolve(source)) || pending);
		if (pending)
			QtHostImages.WhenSettled(source, this, () =>
			{
				if (!ReferenceEquals(source, _source) || ConnectedView is not { } shown)
					return;
				// The read failed (nothing will load), or the image already loaded: a local GIF's AnimatedImage loads
				// synchronously while its host is created, before this callback runs.
				if (!Wait(QtHostImages.Resolve(source)))
					shown.UpdateIsLoading(false);
			});
	}

	/// <summary>Whether the adapter still has to load <paramref name="url"/>: not when there is none, nor when it already
	/// reported this URL (a re-mapped Source, or a load faster than the mapping).</summary>
	private bool Wait(string? url)
	{
		_loadingUrl = url is not null && url != _loadedUrl ? url : null;
		return _loadingUrl is not null;
	}

	public static readonly CommandMapper<IImage, SailfishImageHandler> CommandMapper = new(SailfishViewMapper.CommandMapper);

	public SailfishImageHandler() : this(null)
	{
	}

	public SailfishImageHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
		: base(mapper ?? Mapper, commandMapper ?? CommandMapper, SailfishMeasure.Image)
	{
	}

	// The arranged size sets the decode size (QtHostImages).
	protected override bool SnapshotDependsOnSize => true;

	// ImageButton implements IImage without deriving from Image, so key on the interface.
	protected override string? AdapterUri =>
		ConnectedView is { } resolved && QtHostImages.Resolve(resolved.Source as ImageSource) is not null ? SailfishKeys.Adapter.Image : null;

	protected override Dictionary<string, object?>? Snapshot(IImage view) =>
		QtHostImages.Props(view);

	// A remote image measured 0 × 0 until it loaded; now its own size is known. A failed load has no MAUI event (Image
	// exposes none on any platform): it is reported in the device log once per source, instead of nowhere.
	protected override void OnAdapterEvent(string name, JsonElement payload)
	{
		var url = payload.TryGetProperty(SailfishKeys.Event.Source, out var source) ? source.GetString() : null;
		if ((name == SailfishKeys.Event.ImageLoaded || name == SailfishKeys.Event.ImageFailed) && url is not null)
		{
			_loadedUrl = url;
			if (url == _loadingUrl && ConnectedView is { } loaded)
			{
				_loadingUrl = null;
				loaded.UpdateIsLoading(false);
			}
		}
		if (name == SailfishKeys.Event.ImageNatural && url is not null)
		{
			// A new size re-measures the view; so does a view that measured 0 × 0 while the URL was loading, when another
			// Image with the same URL reported it first (it found the size already known).
			var changed = QtHostImages.ReportNaturalSize(url, (int)BridgeJson.Num(payload, SailfishKeys.Event.Width),
				(int)BridgeJson.Num(payload, SailfishKeys.Event.Height));
			if (ConnectedView is { } view && (changed || view.DesiredSize.Width <= 0 || view.DesiredSize.Height <= 0))
				QtHostImages.InvalidateIntrinsicSize(view);
		}
		else if (name == SailfishKeys.Event.ImageFailed && url is not null && FirstFailure(url))
			QtHostDiag.Warn(QtHostDiagChannel.QmlLoad, $"image failed to load: {url} ({ConnectedView?.GetType().Name})");
	}

	// Sources already reported, bounded: an app cycling through many broken URLs logs the first ones only.
	private const int FailedSourcesLimit = 256;
	private static readonly HashSet<string> FailedSources = new(StringComparer.Ordinal);

	private static bool FirstFailure(string url)
	{
		lock (FailedSources)
			return FailedSources.Count < FailedSourcesLimit && FailedSources.Add(url);
	}
}

/// <summary>Border handler; the obsolete Frame uses the same adapter.</summary>
public class SailfishBorderHandler : SailfishSnapshotHandler<IContentView>
{
	private static readonly string[] Keys =
	{
		nameof(Border.Stroke), nameof(Border.StrokeThickness), nameof(Border.StrokeShape),
		nameof(Border.BackgroundColor), nameof(Border.Background), nameof(Border.Padding),
		// The IBorderStroke names MAUI maps
		nameof(IBorderStroke.Shape), nameof(IBorderStroke.StrokeDashPattern), nameof(IBorderStroke.StrokeDashOffset),
		nameof(IBorderStroke.StrokeLineCap), nameof(IBorderStroke.StrokeLineJoin), nameof(IBorderStroke.StrokeMiterLimit),
		nameof(IView.Shadow),
#pragma warning disable CS0618 // Frame is obsolete but its props must re-diff.
		nameof(Frame.BorderColor), nameof(Frame.CornerRadius), nameof(Frame.HasShadow),
#pragma warning restore CS0618
	};

	public static readonly PropertyMapper<IContentView, SailfishBorderHandler> Mapper = SnapshotMapper<SailfishBorderHandler>(Keys);

	public static readonly CommandMapper<IContentView, SailfishBorderHandler> CommandMapper = new(SailfishViewMapper.CommandMapper);

	public SailfishBorderHandler() : this(null)
	{
	}

	public SailfishBorderHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
		: base(mapper ?? Mapper, commandMapper ?? CommandMapper, SailfishMeasure.Boxed)
	{
	}

	protected override string? AdapterUri => SailfishKeys.Adapter.Border;

	public override bool OwnsProperty(string propertyName) =>
		base.OwnsProperty(propertyName) || AdapterSnapshots.IsBorderVisualProperty(propertyName);

	/// <summary>The path/drawing is built for the arranged size.</summary>
	protected override bool SnapshotDependsOnSize => true;

	protected override Dictionary<string, object?>? Snapshot(IContentView view) =>
		view switch
		{
			Border border => AdapterSnapshots.BorderProps(border),
#pragma warning disable CS0618 // Frame is obsolete but must stay paintable.
			Frame frame => AdapterSnapshots.FrameProps(frame),
#pragma warning restore CS0618
			_ => null,
		};
}

/// <summary>Shape/BoxView handler: every non-visual-state property re-diffs the paint snapshot.</summary>
public class SailfishShapeHandler : SailfishSnapshotHandler<IShapeView>
{
	private static readonly string[] Keys = [];

	public static readonly PropertyMapper<IShapeView, SailfishShapeHandler> Mapper = SnapshotMapper<SailfishShapeHandler>(Keys);

	public static readonly CommandMapper<IShapeView, SailfishShapeHandler> CommandMapper = new(SailfishViewMapper.CommandMapper);

	public SailfishShapeHandler() : this(null)
	{
	}

	public SailfishShapeHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
		: base(mapper ?? Mapper, commandMapper ?? CommandMapper, SailfishMeasure.Shape)
	{
	}

	protected override string? AdapterUri => SailfishKeys.Adapter.Shape;

	public override bool OwnsProperty(string propertyName) =>
		!QtHostVisualState.IsStateProperty(propertyName);

	/// <summary>The path/drawing is built for the arranged size.</summary>
	protected override bool SnapshotDependsOnSize => true;

	protected override Dictionary<string, object?>? Snapshot(IShapeView view) => view switch
	{
		BoxView box => QtHostShapes.BoxViewProps(box),
		Microsoft.Maui.Controls.Shapes.Shape shape => QtHostShapes.ShapeProps(shape),
		_ => null,
	};
}

/// <summary>GraphicsView handler; the adapter state is the recorded IDrawable stream.</summary>
public class SailfishGraphicsHandler : SailfishSnapshotHandler<IGraphicsView>
{
	private static readonly string[] Keys = [];

	public static readonly PropertyMapper<IGraphicsView, SailfishGraphicsHandler> Mapper = SnapshotMapper<SailfishGraphicsHandler>(Keys);

	/// <summary>GraphicsView.Invalidate re-records the drawable.</summary>
	public static readonly CommandMapper<IGraphicsView, SailfishGraphicsHandler> CommandMapper = new(SailfishViewMapper.CommandMapper)
	{
		[nameof(IGraphicsView.Invalidate)] = static (handler, view, _) => MapSnapshot(handler, view),
	};

	public SailfishGraphicsHandler() : this(null)
	{
	}

	public SailfishGraphicsHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
		: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
	{
	}

	protected override string? AdapterUri => SailfishKeys.Adapter.GraphicsView;

	public override bool OwnsProperty(string propertyName) =>
		!QtHostVisualState.IsStateProperty(propertyName);

	/// <summary>The path/drawing is built for the arranged size.</summary>
	protected override bool SnapshotDependsOnSize => true;

	protected override Dictionary<string, object?>? Snapshot(IGraphicsView view) =>
		view is GraphicsView graphics ? QtHostGraphics.Props(graphics) : null;
}
