using Microsoft.Maui.SailfishOS.Handlers;
using Microsoft.Maui.SailfishOS.Platform;
using Microsoft.Maui.SailfishOS.Platform.QtHost;

namespace Linux.SailfishOS.Tests;

/// <summary>
/// Captures the process-wide state a test may change (the test shim, the renderer's activation settle time, text
/// measurement, the adapter, library-handler and image-source registries, the diagnostics hook) and puts it back on
/// dispose, so a test's registrations never leak into the next one. <see cref="Renderer.RendererHarness"/> owns one;
/// a test that registers something without a harness wraps itself in its own.
/// </summary>
internal sealed class TestStatics : IDisposable
{
	private readonly IQtHostShim? _shim = QtHostRuntime.TestShim;
	private readonly int _activationSettleMs = QtHostPageRenderer.ActivationSettleMs;
	private readonly bool _textMetrics = QtHostTextMetrics.Enabled;
	private readonly bool _textCache = QtHostTextMetrics.CacheEnabled;
	private readonly IQtHostDiagnostics? _diagnostics = SailfishMauiApplication.Diagnostics;
	private readonly Action _adapters = QtHostAdapters.CaptureForTests();
	private readonly Action _libraryHandlers = SailfishHandlersFactory.CaptureLibraryReplacementsForTests();
	private readonly Action _imageSources = QtHostImageSources.CaptureForTests();
	private readonly double _devicePixelRatio = QtHostUnits.DevicePixelRatio;
	private readonly bool _rowPool = QtHostListAdapter.RowPoolEnabled;
	private readonly Action _services = QtHostServices.CaptureForTests();

	public void Dispose()
	{
		QtHostRuntime.TestShim = _shim;
		QtHostPageRenderer.ActivationSettleMs = _activationSettleMs;
		QtHostTextMetrics.Enabled = _textMetrics;
		QtHostTextMetrics.CacheEnabled = _textCache;
		QtHostTextMetrics.ClearCache();
		SailfishMauiApplication.Diagnostics = _diagnostics;
		_adapters();
		_libraryHandlers();
		_imageSources();
		QtHostUnits.DevicePixelRatio = _devicePixelRatio;
		QtHostListAdapter.RowPoolEnabled = _rowPool;
		_services();
	}
}
