using System.Collections.ObjectModel;
using Microsoft.Maui.Controls;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// Collection leg, chat (tracker S26): a CollectionView with ItemsUpdatingScrollMode.KeepLastItemInView gets messages
/// appended; the native list follows the end each time, as a chat does; then an animated ScrollTo back to the top.
/// </summary>
internal sealed partial class QtHostDiagnosticsRunner
{
	private void RunColChatChecks(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, Action done)
	{
		if (renderer.CurrentPage?.Navigation is not { } navigation)
		{
			done();
			return;
		}
		var messages = new ObservableCollection<string>(Enumerable.Range(1, 30).Select(i => $"chat message {i}"));
		var list = new CollectionView
		{
			ItemsSource = messages,
			ItemsUpdatingScrollMode = ItemsUpdatingScrollMode.KeepLastItemInView,
			ItemTemplate = new DataTemplate(() =>
			{
				var label = new Label { Padding = new Microsoft.Maui.Thickness(16, 10) };
				label.SetBinding(Label.TextProperty, ".");
				return label;
			}),
		};
		var lastVisible = -1;
		var firstVisible = -1;
		list.Scrolled += (_, e) =>
		{
			lastVisible = e.LastVisibleItemIndex;
			firstVisible = e.FirstVisibleItemIndex;
		};
		var page = new ContentPage { Title = "Chat", Content = list };
		Console.Error.WriteLine("[Sailfish] Qt collection diag: chat — pushing a KeepLastItemInView list of 30 messages");
		_ = navigation.PushAsync(page, false);

		string ListProp(string name) =>
			renderer.CurrentHosts.FirstOrDefault(h => h.IsAttached && ReferenceEquals(h.Element, list)) is { } host
				? QtHost.QtHostRuntime.GetProperty(host.NativeHandle, name) ?? "?"
				: "?";

		var added = 0;
		void AddOne()
		{
			messages.Add($"chat message {messages.Count + 1}");
			added++;
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(700), added < 3 ? AddOne : Check);
		}

		void Check()
		{
			var atEnd = ListProp("atYEnd");
			_qtColChecks.Check($"chat S26: KeepLastItemInView follows 3 appended messages to the end (atYEnd {atEnd}, last visible item {lastVisible}=={messages.Count - 1}; " +
				$"contentY {ListProp("contentY")}, contentHeight {ListProp("contentHeight")}, height {ListProp("height")})",
				atEnd == "true" && lastVisible == messages.Count - 1);
			Shot(dispatcher, "collection-chat", () =>
			{
				list.ScrollTo(0, position: ScrollToPosition.Start, animate: true);
				// Mid-animation the list is between the end and the top; at rest it is at the top.
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(120), () =>
				{
					var mid = DiagQml.Num(ListProp("contentY"));
					dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(700), () =>
					{
						var atTop = ListProp("atYBeginning");
						_qtColChecks.Check($"chat S26: an animated ScrollTo(0, Start) eases to the first message (mid-way contentY {mid:F0} > 0, " +
							$"then first visible item {firstVisible}==0; atYBeginning {atTop}, contentY {ListProp("contentY")}, originY {ListProp("originY")})",
							mid > 0 && firstVisible == 0);
						_ = navigation.PopAsync(false);
						dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1200), done);
					});
				});
			});
		}

		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1800), AddOne);
	}
}
