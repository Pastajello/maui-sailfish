namespace Trailhead.Features.Diagnostics;

public partial class DiagnosticsPage : ContentPage
{
	private readonly DiagnosticsViewModel _viewModel;

	public DiagnosticsPage(DiagnosticsViewModel viewModel)
	{
		_viewModel = viewModel;
		BindingContext = viewModel;
		InitializeComponent();
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		await _viewModel.LoadAsync();
		await _viewModel.LoadSensorsAsync();
	}
}
