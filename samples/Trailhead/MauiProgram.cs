using CommunityToolkit.Maui;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.SailfishOS.Camera;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.SailfishOS.Hosting;
using SkiaSharp.Views.Maui.Controls.Hosting;
using Trailhead.Data;
using Trailhead.Features.Diagnostics;
using Trailhead.Features.Photos;
using Trailhead.Features.Recording;
using Trailhead.Features.TripDetail;
using Trailhead.Features.TripEdit;
using Trailhead.Features.TripMap;
using Trailhead.Features.Trips;

namespace Trailhead;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();

		builder
			.UseMauiAppSailfish<App>()
			.UseMauiCommunityToolkit()
			.UseSkiaSharp();
		builder.UseMauiMaps();
		builder.UseSailfishCamera();
		builder.ConfigureMauiHandlers(Trailhead.Controls.CompassViewHandler.Register);
		// Lab: the same glow as an Effect and as a mapper key.
		builder.ConfigureEffects(effects => effects.Add<Trailhead.Features.Lab.GlowEffect, Trailhead.Features.Lab.SailfishGlowEffect>());
		Trailhead.Features.Lab.Glow.Register();

		var services = builder.Services;
		services.AddSingleton<TripStore>();
		services.AddSingleton<RecordingService>();
		services.AddTransient<RecordingPage>();
		services.AddTransient<Trailhead.Features.Camera.CameraPage>();
		services.AddTransient<Trailhead.Features.Statistics.StatisticsPage>();
		services.AddTransient<Trailhead.Features.Settings.SettingsPage>();
		services.AddSingleton<AppShell>();
		services.AddSingleton<TripsPage>();
		services.AddSingleton<TripsViewModel>();
		// Shell resolves routed pages from the container, so their view models are injected too.
		services.AddTransient<TripDetailPage>();
		services.AddTransient<TripDetailViewModel>();
		services.AddTransient<TripEditPage>();
		services.AddTransient<TripEditViewModel>();
		services.AddTransient<TripMapPage>();
		services.AddTransient<PhotoViewerPage>();
		services.AddTransient<PhotoViewerViewModel>();
		services.AddTransient<DiagnosticsPage>();
		services.AddTransient<DiagnosticsViewModel>();

		return builder.Build();
	}
}
