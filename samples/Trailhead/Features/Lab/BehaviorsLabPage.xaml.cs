using System.Windows.Input;
using Trailhead.Data;

namespace Trailhead.Features.Lab;

public partial class BehaviorsLabPage : ContentPage
{
	public const string Route = "lab/behaviors";

	public static readonly BindableProperty SearchResultProperty =
		BindableProperty.Create(nameof(SearchResult), typeof(string), typeof(BehaviorsLabPage), "");

	public BehaviorsLabPage()
	{
		SearchCommand = new Command<string>(text =>
		{
			Searches++;
			var hits = DemoRoutes.Gazetteer.Where(p => p.Name.Contains(text, StringComparison.OrdinalIgnoreCase)).Take(4).ToList();
			SearchResult = hits.Count == 0 ? $"Nothing for '{text}'" : string.Join(" · ", hits.Select(h => $"{h.Name} ({h.Ele:0} m)"));
		});
		BindingContext = this;
		InitializeComponent();
		// A behavior does not inherit the page's BindingContext: the command binding needs it handed over.
		StoppedTyping.BindingContext = this;
		Bouncer.Behaviors.Add(new CommunityToolkit.Maui.Behaviors.AnimationBehavior
		{
			AnimationType = new CommunityToolkit.Maui.Animations.FadeAnimation { Opacity = 0.3, Length = 300 },
		});
	}

	public ICommand SearchCommand { get; }

	public string SearchResult
	{
		get => (string)GetValue(SearchResultProperty);
		set => SetValue(SearchResultProperty, value);
	}

	/// <summary>How many times the debounced search ran (tour checks: one per pause, not per keystroke).</summary>
	public int Searches { get; private set; }

	public Entry SearchEntry => Search;

	public CommunityToolkit.Maui.Behaviors.EmailValidationBehavior EmailCheck => EmailValidation;

	public Entry EmailEntry => Email;
}
