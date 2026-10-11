using Microsoft.Maui.Devices;

namespace Trailhead.Features.Lab;

/// <summary>
/// SOS in Morse (··· ––– ···) on the whole screen and the vibra, with the screen kept on while it runs (plan D7: the
/// torch is unreachable from Sailjail's sandbox, so the screen is the light). The torch button shows what the
/// sandbox says when an app asks for it.
/// </summary>
public sealed class SosPage : ContentPage
{
	public const string Route = "lab/sos";

	/// <summary>One Morse unit.</summary>
	public const int UnitMs = 220;

	private static readonly string Pattern = "... --- ...";

	private readonly Grid _flash = new() { BackgroundColor = Colors.Black };
	private readonly Label _status = new() { FontSize = 15, HorizontalTextAlignment = TextAlignment.Center, TextColor = Colors.White };
	private readonly Button _start = new() { Text = "Start SOS" };
	private readonly Label _torch = new() { FontSize = 13, HorizontalTextAlignment = TextAlignment.Center, TextColor = Colors.Gray };
	private CancellationTokenSource? _run;

	public SosPage()
	{
		Title = "SOS";
		_start.Clicked += (_, _) =>
		{
			if (_run is null)
				_ = RunAsync();
			else
				Stop();
		};
		var torch = new Button { Text = "Try the torch" };
		torch.Clicked += async (_, _) => _torch.Text = await TryTorchAsync();
		_flash.Add(new VerticalStackLayout
		{
			VerticalOptions = LayoutOptions.End,
			Padding = new Thickness(16, 32),
			Spacing = 12,
			Children = { _status, _start, torch, _torch },
		});
		Content = _flash;
	}

	public int Flashes { get; private set; }

	public bool IsRunning => _run is not null;

	protected override void OnDisappearing()
	{
		base.OnDisappearing();
		Stop();
	}

	public async Task RunAsync()
	{
		_run = new CancellationTokenSource();
		var token = _run.Token;
		_start.Text = "Stop";
		DeviceDisplay.Current.KeepScreenOn = true;
		try
		{
			while (!token.IsCancellationRequested)
			{
				foreach (var symbol in Pattern)
				{
					_status.Text = Pattern;
					if (symbol == ' ')
					{
						await Task.Delay(UnitMs * 3, token);
						continue;
					}
					var length = symbol == '.' ? UnitMs : UnitMs * 3;
					Flashes++;
					_flash.BackgroundColor = Colors.White;
					_status.TextColor = Colors.Black;
					try
					{
						Vibration.Default.Vibrate(TimeSpan.FromMilliseconds(length));
					}
					catch (FeatureNotSupportedException)
					{
					}
					await Task.Delay(length, token);
					_flash.BackgroundColor = Colors.Black;
					_status.TextColor = Colors.White;
					await Task.Delay(UnitMs, token);
				}
				await Task.Delay(UnitMs * 7, token);
			}
		}
		catch (TaskCanceledException)
		{
		}
		finally
		{
			_flash.BackgroundColor = Colors.Black;
			_status.TextColor = Colors.White;
			DeviceDisplay.Current.KeepScreenOn = false;
		}
	}

	public void Stop()
	{
		_run?.Cancel();
		_run = null;
		_start.Text = "Start SOS";
		_status.Text = "";
	}

	/// <summary>What Flashlight answers: "on" where the torch service is reachable, the reason where it is not.</summary>
	public static async Task<string> TryTorchAsync()
	{
		try
		{
			if (!await Flashlight.Default.IsSupportedAsync())
				return "No torch on this phone";
			await Flashlight.Default.TurnOnAsync();
			await Task.Delay(800);
			await Flashlight.Default.TurnOffAsync();
			return "The torch flashed";
		}
		catch (Exception ex) when (ex is FeatureNotSupportedException or PermissionException or InvalidOperationException)
		{
			return $"Torch refused: {ex.Message}";
		}
	}
}
