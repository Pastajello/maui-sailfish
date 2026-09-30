using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;
using SailfishKitchen.Helpers;
using SailfishKitchen.Services;

namespace SailfishKitchen.ViewModels;

/// <summary>A home-screen tile whose thumb resolves off the UI thread, showing the placeholder until it lands.</summary>
public abstract partial class ThumbnailViewModel : ObservableObject
{
	private readonly string _url;
	private readonly IImageCache _images;
	private readonly IDispatcher _dispatcher;
	private volatile bool _isResolved;

	[ObservableProperty]
	private ImageSource? _thumbnail;

	[ObservableProperty]
	private bool _isImageLoading = true;

	protected ThumbnailViewModel(string url, IImageCache images, bool offline, CancellationToken lifetime, IDispatcher dispatcher)
	{
		_url = url;
		_images = images;
		_dispatcher = dispatcher;
		Thumbnail = images.Placeholder;
		LoadThumbnail(offline, lifetime);
	}

	/// <summary>Starts resolving the thumb on the UI thread; a no-op once it landed, so a returning page can retry a cancelled one.</summary>
	public void LoadThumbnail(bool offline, CancellationToken lifetime)
	{
		if (_isResolved)
			return;

		if (offline)
		{
			IsImageLoading = false;
			return;
		}

		IsImageLoading = true;
		_ = ResolveAsync(lifetime);
	}

	private async Task ResolveAsync(CancellationToken ct)
	{
		try
		{
			var resolved = await _images.ResolveAsync(_url, ct).ConfigureAwait(false);
			_isResolved = true;
			_dispatcher.RunOnUi(() => Thumbnail = resolved);
		}
		catch (OperationCanceledException)
		{
		}
		finally
		{
			_dispatcher.RunOnUi(() => IsImageLoading = false);
		}
	}
}
