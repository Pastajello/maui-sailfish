using System.Collections.ObjectModel;
using Microsoft.Maui.Controls;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// API demo clips for docs/sailfish-apis.md (MAUI_SAILFISH_QT_HOST_APIDEMO=&lt;name&gt;, recorded with
/// <c>tools/sf record out.mp4 --env MAUI_SAILFISH_QT_HOST_APIDEMO=&lt;name&gt;</c>): one slow, readable scene per
/// Sailfish API, then the app quits so the recording stops. Not a matrix leg: nothing is checked here.
/// Names: remorse-popup, remorse-item, bottom-sheet, notification, page-busy, pulley-busy, placeholder.
/// </summary>
internal sealed partial class QtHostDiagnosticsRunner
{
	private string? _qtApiDemo;

	private void RunQtApiDemo(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		var nav = RootNav;
		if (nav is null)
		{
			QtHost.QtHostRuntime.Shutdown();
			return;
		}
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1500), async () =>
		{
			try
			{
				await (_qtApiDemo switch
				{
					"remorse-popup" => DemoRemorsePopup(dispatcher, nav),
					"remorse-item" => DemoRemorseItem(renderer, dispatcher, nav),
					"bottom-sheet" => DemoBottomSheet(dispatcher, nav),
					"notification" => DemoNotification(dispatcher, nav),
					"page-busy" => DemoPageBusy(dispatcher, nav),
					"pulley-busy" => DemoPulleyBusy(dispatcher, nav),
					"placeholder" => DemoPlaceholder(dispatcher, nav),
					_ => Task.CompletedTask,
				});
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine($"[Sailfish] api demo '{_qtApiDemo}' failed: {ex}");
			}
			Console.Error.WriteLine($"[Sailfish] api demo '{_qtApiDemo}' done");
			QtHost.QtHostRuntime.Shutdown();
		});
	}

	private static ObservableCollection<string> DemoItems() =>
		new(new[] { "Buy milk", "Call the bank", "Book train tickets", "Water the plants", "Pay the rent", "Renew passport" });

	private static async Task<CollectionView> DemoListPage(SailfishDispatcher dispatcher, NavigationPage nav, string title,
		ObservableCollection<string> items, Action<ContentPage>? setup = null)
	{
		var list = SilicaList(items);
		var page = SilicaPage(title, list);
		setup?.Invoke(page);
		await nav.PushAsync(page);
		await SilicaWait(dispatcher, 1800);
		return list;
	}

	private async Task DemoRemorsePopup(SailfishDispatcher dispatcher, NavigationPage nav)
	{
		var items = DemoItems();
		await DemoListPage(dispatcher, nav, "Tasks", items);
		await SailfishRemorse.ExecuteAsync("Clearing all tasks", items.Clear);
		await SilicaWait(dispatcher, 1500);
	}

	private async Task DemoRemorseItem(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, NavigationPage nav)
	{
		var items = DemoItems();
		await DemoListPage(dispatcher, nav, "Tasks", items);
		if (renderer.Collection.RowView(1) is { } row)
			await SailfishRemorse.ExecuteAsync(row, "Deleting", () => items.RemoveAt(1));
		await SilicaWait(dispatcher, 1500);
	}

	private async Task DemoBottomSheet(SailfishDispatcher dispatcher, NavigationPage nav)
	{
		await DemoListPage(dispatcher, nav, "Tasks", DemoItems());
		using var sheet = new SailfishBottomSheet { Text = "3 tasks due today" };
		sheet.Show();
		await SilicaWait(dispatcher, 2600);
		sheet.Hide();
		await SilicaWait(dispatcher, 1200);
	}

	private async Task DemoNotification(SailfishDispatcher dispatcher, NavigationPage nav)
	{
		await DemoListPage(dispatcher, nav, "Tasks", DemoItems());
		var id = SailfishNotifications.Show("Reminder", "Pay the rent today");
		await SilicaWait(dispatcher, 4500);
		SailfishNotifications.Close(id);
		await SilicaWait(dispatcher, 800);
	}

	private async Task DemoPageBusy(SailfishDispatcher dispatcher, NavigationPage nav)
	{
		var items = new ObservableCollection<string>();
		ContentPage? page = null;
		await DemoListPage(dispatcher, nav, "Tasks", items, p => { page = p; p.IsBusy = true; });
		await SilicaWait(dispatcher, 1500);
		foreach (var item in DemoItems())
			items.Add(item);
		page!.IsBusy = false;
		await SilicaWait(dispatcher, 1800);
	}

	private async Task DemoPulleyBusy(SailfishDispatcher dispatcher, NavigationPage nav)
	{
		ContentPage? page = null;
		await DemoListPage(dispatcher, nav, "Tasks", DemoItems(), p =>
		{
			page = p;
			p.ToolbarItems.Add(new ToolbarItem { Text = "Sync now" });
			p.IsBusy = true;
		});
		await SilicaWait(dispatcher, 3000);
		page!.IsBusy = false;
		await SilicaWait(dispatcher, 1200);
	}

	private async Task DemoPlaceholder(SailfishDispatcher dispatcher, NavigationPage nav)
	{
		var items = new ObservableCollection<string>();
		var list = await DemoListPage(dispatcher, nav, "Tasks", items);
		list.EmptyView = "No tasks yet";
		await SilicaWait(dispatcher, 2200);
		foreach (var item in DemoItems().Take(3))
		{
			items.Add(item);
			await SilicaWait(dispatcher, 500);
		}
		await SilicaWait(dispatcher, 1200);
	}
}
