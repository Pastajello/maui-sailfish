// On the plain net11.0 TFM, IsLoaded is `Window != null`: Loaded fires the moment a page is parented to a Window,
// before any backend could have created its handler. On Android/iOS/Windows it waits for the platform view.
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;

var label = new Label { Text = "hello" };
var page = new ContentPage { Content = label };
page.Loaded += (_, _) => Console.WriteLine($"page.Loaded:  page.Handler = {page.Handler?.GetType().Name ?? "null"}, label.Handler = {label.Handler?.GetType().Name ?? "null"}");
label.Loaded += (_, _) => Console.WriteLine($"label.Loaded: label.Handler = {label.Handler?.GetType().Name ?? "null"}");

// What every app does in Application.CreateWindow; the backend gets the Window only after this returns.
var window = new Window(page);
Console.WriteLine($"after new Window(page): page.IsLoaded = {page.IsLoaded}");

// The backend now attaches handlers (here: a stub with a platform view). Loaded does not fire again.
var handler = new BackendLabelHandler();
handler.SetMauiContext(new MauiContext(new ServiceCollection().BuildServiceProvider()));
label.Handler = handler;
Console.WriteLine($"after the handler: label.Handler = {label.Handler?.GetType().Name}, label.IsLoaded = {label.IsLoaded}");

sealed class BackendLabelHandler() : ViewHandler<ILabel, object>(ViewHandler.ViewMapper)
{
	protected override object CreatePlatformView() => new object();
}
