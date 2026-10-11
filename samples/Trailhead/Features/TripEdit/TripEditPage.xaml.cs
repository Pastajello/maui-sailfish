namespace Trailhead.Features.TripEdit;

public partial class TripEditPage : ContentPage
{
	private readonly TripEditViewModel _viewModel;

	public TripEditPage(TripEditViewModel viewModel)
	{
		_viewModel = viewModel;
		BindingContext = viewModel;
		InitializeComponent();
		// A behavior does not inherit the page's BindingContext (and the toolkit no longer copies it), so its bindings
		// (IsValid, Command) need the view model handed over.
		NameValidation.BindingContext = viewModel;
		EnterSaves.BindingContext = viewModel;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		await _viewModel.LoadAsync();
	}
}
