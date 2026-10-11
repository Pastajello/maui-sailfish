using Trailhead.Features.Shared;

namespace Trailhead.Features.Lab;

public sealed record LabEntry(string Title, string Detail, string Route);

/// <summary>
/// What the trip story cannot hold: one page per MAUI feature, each showing it working (or saying why it does not
/// on Sailfish). A plain list of entries; a tap opens the page.
/// </summary>
public sealed class LabPage : ContentPage
{
	public const string Route = "lab";

	public static readonly LabEntry[] Entries =
	{
		new("Animations", "Easings, sequences, parallel runs, cancelling mid-way", AnimationsLabPage.Route),
		new("Gestures", "Pan, pinch, swipe, long press, drag and drop", GesturesLabPage.Route),
		new("Behaviors", "CommunityToolkit validation, masks, typing debounce, animation on tap", BehaviorsLabPage.Route),
		new("Effects and mappers", "The same look as a RoutingEffect and as a handler mapper", EffectsLabPage.Route),
		new("SwipeView", "Works, but sideways swipes are Silica's: the Sailfish idiom is press and hold", SwipeLabPage.Route),
		new("SOS", "Morse on the screen and the vibra; the flashlight from a sandbox", SosPage.Route),
		new("Stress", "10,000 trips, a 50,000-point track, an hour of recording at once", StressLabPage.Route),
	};

	public LabPage()
	{
		Title = "Lab";
		var list = new CollectionView
		{
			ItemsSource = Entries,
			SelectionMode = SelectionMode.Single,
			ItemTemplate = new DataTemplate(() =>
			{
				var title = new Label();
				title.SetBinding(Label.TextProperty, static (LabEntry e) => e.Title);
				var detail = new Label { FontSize = 13 };
				detail.SetBinding(Label.TextProperty, static (LabEntry e) => e.Detail);
				detail.SetDynamicResource(Label.TextColorProperty, ThemeResources.Secondary);
				return new VerticalStackLayout { Padding = new Thickness(16, 10), Spacing = 2, Children = { title, detail } };
			}),
		};
		list.SelectionChanged += async (_, e) =>
		{
			if (e.CurrentSelection.FirstOrDefault() is not LabEntry entry)
				return;
			list.SelectedItem = null;
			await Shell.Current.GoToAsync(entry.Route);
		};
		Content = list;
	}

	/// <summary>Registers every Lab page's route (AppShell).</summary>
	public static void RegisterRoutes()
	{
		Routing.RegisterRoute(Route, typeof(LabPage));
		Routing.RegisterRoute(AnimationsLabPage.Route, typeof(AnimationsLabPage));
		Routing.RegisterRoute(GesturesLabPage.Route, typeof(GesturesLabPage));
		Routing.RegisterRoute(BehaviorsLabPage.Route, typeof(BehaviorsLabPage));
		Routing.RegisterRoute(EffectsLabPage.Route, typeof(EffectsLabPage));
		Routing.RegisterRoute(SwipeLabPage.Route, typeof(SwipeLabPage));
		Routing.RegisterRoute(SosPage.Route, typeof(SosPage));
		Routing.RegisterRoute(StressLabPage.Route, typeof(StressLabPage));
	}
}
