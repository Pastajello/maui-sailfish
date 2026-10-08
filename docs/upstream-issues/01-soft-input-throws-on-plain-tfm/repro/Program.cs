// A custom backend on the plain net11.0 TFM: its Entry handler has a platform view (here a plain object).
// The public soft-input API then throws instead of returning false or reaching the backend.
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;

var services = new ServiceCollection().BuildServiceProvider();
var entry = new Entry();
var handler = new BackendEntryHandler();
handler.SetMauiContext(new MauiContext(services));
entry.Handler = handler;
Console.WriteLine($"PlatformView: {entry.Handler?.PlatformView}");

Try("IsSoftInputShowing()", () => entry.IsSoftInputShowing());
Try("HideSoftInputAsync()", () => entry.HideSoftInputAsync(CancellationToken.None).GetAwaiter().GetResult());

static void Try(string name, Func<bool> call)
{
	try { Console.WriteLine($"{name} -> {call()}"); }
	catch (Exception e) { Console.WriteLine($"{name} -> {e.GetType().FullName}"); }
}

sealed class BackendEntryHandler() : ViewHandler<IEntry, object>(ViewHandler.ViewMapper)
{
	protected override object CreatePlatformView() => new BackendTextField();
}

sealed class BackendTextField
{
	public override string ToString() => nameof(BackendTextField);
}
