using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.SailfishOS.Platform;
using Microsoft.Maui.SailfishOS.Platform.QtHost;
using Xunit;

namespace Linux.SailfishOS.Tests.Renderer;

/// <summary>Tracker S24 (plan M6 steps 1–4): an image source type with its own <see cref="ISailfishImageSourceService"/>
/// in the app's <see cref="IImageSourceServiceProvider"/> renders through it; one registered for a stock interface
/// replaces the built-in loading.</summary>
[Collection("renderer")]
public sealed class ImageSourceServiceTests : IDisposable
{
	private readonly IPlatformApplication? _previousApp = IPlatformApplication.Current;

	public ImageSourceServiceTests() => QtHostImages.ResetServicesForTests();

	public void Dispose()
	{
		IPlatformApplication.Current = _previousApp;
		QtHostImages.ResetServicesForTests();
	}

	// The app's services, as IPlatformApplication.Current exposes them on the phone.
	private sealed class App(IServiceProvider services) : IPlatformApplication
	{
		public IServiceProvider Services => services;

		public IApplication Application => null!;
	}

	private sealed class AvatarImageSource : ImageSource
	{
		public string User { get; set; } = "";

		public override bool IsEmpty => string.IsNullOrEmpty(User);
	}

	private sealed class AvatarService : ISailfishImageSourceService<AvatarImageSource>
	{
		public readonly TaskCompletionSource<IImageSourceServiceResult<string>?> Answer = new();
		public int Asked;
		public bool Disposed;

		public Task<IImageSourceServiceResult<string>?> GetUrlAsync(IImageSource source, CancellationToken cancellationToken = default)
		{
			Asked++;
			return Answer.Task;
		}
	}

	private sealed class Provider(Type type, IImageSourceService service) : IImageSourceServiceProvider
	{
		public IServiceProvider HostServiceProvider => this;

		public IImageSourceService? GetImageSourceService(Type imageSource) =>
			type.IsAssignableFrom(imageSource) ? service : throw new InvalidOperationException($"no service for {imageSource}");

		public object? GetService(Type serviceType) => null;
	}

	private static IServiceProvider AppServices(Type type, IImageSourceService service)
	{
		var services = new ServiceCollection().AddSingleton<IImageSourceServiceProvider>(new Provider(type, service)).BuildServiceProvider();
		IPlatformApplication.Current = new App(services);
		return services;
	}

	[Fact]
	public void A_custom_source_renders_through_its_service_once_it_answers()
	{
		var service = new AvatarService();
		var image = new Image { Source = new AvatarImageSource { User = "mer" }, WidthRequest = 64, HeightRequest = 64 };
		using var h = new RendererHarness(new ContentPage { Title = "T", Content = new VerticalStackLayout { Children = { image } } },
			AppServices(typeof(AvatarImageSource), service));
		for (var i = 0; i < 4; i++)
			h.Poll();
		Assert.Equal(1, service.Asked);
		Assert.Empty(h.Shim.ByUri("image").Where(o => !o.Destroyed));   // pending: nothing hosted, no placeholder

		const string url = "file:///tmp/avatar-mer.png";
		service.Answer.SetResult(new SailfishImageSourceServiceResult(url, () => service.Disposed = true));
		for (var i = 0; i < 4; i++)
			h.Poll();

		var host = Assert.Single(h.Shim.ByUri("image").Where(o => !o.Destroyed));
		Assert.Contains(host.Props.Values, v => v.ValueKind == System.Text.Json.JsonValueKind.String && v.GetString() == url);
		Assert.Equal(1, service.Asked);   // asked once per source object
		Assert.False(service.Disposed);
	}

	[Fact]
	public void A_service_for_a_stock_interface_replaces_the_built_in_loading()
	{
		var service = new AvatarService();
		service.Answer.SetResult(new SailfishImageSourceServiceResult("https://example.org/via-service.png"));
		using var h = new RendererHarness(new ContentPage { Title = "T", Content = new Label { Text = "x" } },
			AppServices(typeof(IUriImageSource), service));

		var source = new UriImageSource { Uri = new Uri("https://example.org/original.png") };
		Assert.Null(QtHostImages.Resolve(source));   // first ask: the service answers asynchronously
		Assert.Equal("https://example.org/via-service.png", QtHostImages.Resolve(source));
		Assert.Equal(1, service.Asked);
	}

	[Fact]
	public void Without_a_sailfish_service_the_stock_handling_stays()
	{
		using var h = new RendererHarness(new ContentPage { Title = "T", Content = new Label { Text = "x" } },
			AppServices(typeof(IUriImageSource), new UriImageSourceService()));

		var url = QtHostImages.Resolve(new UriImageSource { Uri = new Uri("https://example.org/a.png") });
		Assert.StartsWith("https://example.org/a.png#maui-cache=", url);
	}
}
