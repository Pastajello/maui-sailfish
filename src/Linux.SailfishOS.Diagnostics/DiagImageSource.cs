using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.SailfishOS.Platform.QtHost;

namespace Microsoft.Maui.SailfishOS.Diagnostics;

/// <summary>An image source type only its own service understands (tracker S24): the f3 leg shows one; it renders only
/// when the backend asks the app's IImageSourceServiceProvider, as a library's (CommunityToolkit's Gravatar) would.</summary>
internal sealed class DiagImageSource : ImageSource
{
	public string Name { get; init; } = "sailfish_logo.png";

	public override bool IsEmpty => string.IsNullOrEmpty(Name);
}

/// <summary>The service for <see cref="DiagImageSource"/>: answers after a short delay (an asynchronous load) with the
/// file URL of an app image.</summary>
internal sealed class DiagImageSourceService : ISailfishImageSourceService<DiagImageSource>
{
	public static int Asked;

	public async Task<IImageSourceServiceResult<string>?> GetUrlAsync(IImageSource source, CancellationToken cancellationToken = default)
	{
		Asked++;
		await Task.Delay(150, cancellationToken);
		var path = Path.Combine(AppContext.BaseDirectory, "images", ((DiagImageSource)source).Name);
		return File.Exists(path) ? new SailfishImageSourceServiceResult(new Uri(path).AbsoluteUri) : null;
	}
}

public static partial class SailfishDiagnostics
{
	/// <summary>Registers the diagnostics' own image source service (the f3 leg's custom ImageSource). Call from
	/// CreateMauiApp: <c>builder.ConfigureImageSources(SailfishDiagnostics.ConfigureImageSources)</c>.</summary>
	public static void ConfigureImageSources(IImageSourceServiceCollection services) =>
		services.AddService<DiagImageSource, DiagImageSourceService>();
}
