using Microsoft.Maui.Controls;
using Microsoft.Maui.SailfishOS.Sample.Pages;
using Microsoft.Maui.SailfishOS.Sample.Services;

namespace Microsoft.Maui.SailfishOS.Sample;

/// <summary>
/// Sample application root; the window hosts a NavigationPage over the injected task store.
/// </summary>
public class App : Application
{
	private readonly TaskStore _store;

	public App(TaskStore store)
	{
		_store = store;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var navigation = new NavigationPage(new TasksPage(_store));
		return new Window(navigation) { Title = "MAUI Sailfish Sample" };
	}
}