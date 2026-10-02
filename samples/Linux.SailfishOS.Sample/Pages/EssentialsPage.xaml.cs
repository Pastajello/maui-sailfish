using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.Communication;
using Microsoft.Maui.Media;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Storage;

namespace Microsoft.Maui.SailfishOS.Sample.Pages;

/// <summary>
/// Gallery of MAUI Essentials; every call is guarded so an unsupported API shows as text, not a crash.
/// </summary>
public partial class EssentialsPage : ContentPage
{
	public EssentialsPage()
	{
		InitializeComponent();
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		RefreshInfo();
	}

	private void RefreshInfo()
	{
		DeviceLabel.Text = Safe(() =>
			$"device: {DeviceInfo.Manufacturer} {DeviceInfo.Model} | {DeviceInfo.Platform} {DeviceInfo.VersionString} | {DeviceInfo.Idiom}");

		DisplayLabel.Text = Safe(() =>
		{
			var d = DeviceDisplay.MainDisplayInfo;
			return $"display: {d.Width}x{d.Height} @ {d.Density:0.00}x {d.RefreshRate:0}Hz rot={d.Rotation}";
		});

		AppLabel.Text = Safe(() =>
			$"app: {AppInfo.Name} {AppInfo.VersionString} pkg={AppInfo.PackageName} theme={AppInfo.RequestedTheme}");

		StateLabel.Text = Safe(() =>
			$"state: conn={Connectivity.NetworkAccess} battery={Battery.ChargeLevel:0%} {Battery.State} src={Battery.PowerSource}");
	}

	private static string Safe(Func<string> probe)
	{
		try
		{
			return probe();
		}
		catch (Exception ex)
		{
			return $"unsupported: {ex.GetType().Name}";
		}
	}

	private void OnWritePrefsClicked(object? sender, EventArgs e) =>
		PrefsLabel.Text = Safe(() =>
		{
			Preferences.Default.Set("launch_count", Preferences.Default.Get("launch_count", 0) + 1);
			return $"prefs: launch_count={Preferences.Default.Get("launch_count", 0)}";
		});

	private void OnReadPrefsClicked(object? sender, EventArgs e) =>
		PrefsLabel.Text = Safe(() => $"prefs: launch_count={Preferences.Default.Get("launch_count", 0)}");

	private async void OnCopyClicked(object? sender, EventArgs e) =>
		ClipboardLabel.Text = await SafeAsync(async () =>
		{
			await Clipboard.SetTextAsync("sailfish-maui");
			return "clipboard: copied 'sailfish-maui'";
		});

	private async void OnPasteClicked(object? sender, EventArgs e) =>
		ClipboardLabel.Text = await SafeAsync(async () => $"clipboard: '{await Clipboard.GetTextAsync()}'");

	private async void OnFileClicked(object? sender, EventArgs e) =>
		FilesLabel.Text = await SafeAsync(async () =>
		{
			var path = Path.Combine(FileSystem.CacheDirectory, "note.txt");
			await File.WriteAllTextAsync(path, $"written {DateTime.Now:HH:mm:ss}");
			var content = await File.ReadAllTextAsync(path);
			return $"files: {path} -> {content}";
		});

	private void OnVibrateClicked(object? sender, EventArgs e) =>
		StateLabel.Text = Safe(() =>
		{
			Vibration.Vibrate(TimeSpan.FromMilliseconds(200));
			return "state: vibrated";
		});

	private async void OnLocationClicked(object? sender, EventArgs e) =>
		StateLabel.Text = await SafeAsync(async () =>
		{
			var loc = await Geolocation.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(5)));
			return loc is null ? "state: no location" : $"state: {loc.Latitude:0.000},{loc.Longitude:0.000} ±{loc.Accuracy:0}m";
		});

	private async void OnBrowserClicked(object? sender, EventArgs e) =>
		StateLabel.Text = await SafeAsync(async () =>
		{
			await Browser.OpenAsync("https://sailfishos.org", BrowserLaunchMode.SystemPreferred);
			return "state: browser opened";
		});

	// SecureStorage by hand: save a timestamped value, read it back (also after restarting the app), remove it.
	private async void OnSecureSaveClicked(object? sender, EventArgs e) =>
		SecureLabel.Text = await SafeAsync(async () =>
		{
			var value = $"saved {DateTime.Now:HH:mm:ss}";
			await SecureStorage.Default.SetAsync("sample_secret", value);
			return $"secure: '{value}' saved";
		});

	private async void OnSecureReadClicked(object? sender, EventArgs e) =>
		SecureLabel.Text = await SafeAsync(async () =>
			$"secure: read '{await SecureStorage.Default.GetAsync("sample_secret") ?? "(none)"}'");

	private void OnSecureRemoveClicked(object? sender, EventArgs e) =>
		SecureLabel.Text = Safe(() => $"secure: removed={SecureStorage.Default.Remove("sample_secret")}");

	// Essentials added for Sailfish: flashlight (system torch), contacts (address book + Silica picker), app actions
	// (cover buttons), text-to-speech (no engine: reports unsupported).
	private async void OnTorchOnClicked(object? sender, EventArgs e) =>
		MoreLabel.Text = await SafeAsync(async () => { await Flashlight.Default.TurnOnAsync(); return "more: torch on"; });

	private async void OnTorchOffClicked(object? sender, EventArgs e) =>
		MoreLabel.Text = await SafeAsync(async () => { await Flashlight.Default.TurnOffAsync(); return "more: torch off"; });

	private async void OnPickContactClicked(object? sender, EventArgs e) =>
		MoreLabel.Text = await SafeAsync(async () =>
		{
			var contact = await Contacts.Default.PickContactAsync();
			return contact is null
				? "more: no contact picked"
				: $"more: {contact.DisplayName} — {contact.Phones.FirstOrDefault()?.PhoneNumber ?? "no phone"}, {contact.Emails.FirstOrDefault()?.EmailAddress ?? "no email"}";
		});

	private async void OnCountContactsClicked(object? sender, EventArgs e) =>
		MoreLabel.Text = await SafeAsync(async () => $"more: {(await Contacts.Default.GetAllAsync()).Count()} contacts");

	private async void OnCoverActionsClicked(object? sender, EventArgs e) =>
		MoreLabel.Text = await SafeAsync(async () =>
		{
			AppActions.Current.AppActionActivated -= OnAppAction;
			AppActions.Current.AppActionActivated += OnAppAction;
			await AppActions.Current.SetAsync(new[]
			{
				new AppAction("refresh", "Refresh", icon: "icon-cover-refresh"),
				new AppAction("search", "Search", icon: "icon-cover-search"),
			});
			return "more: cover actions set — minimize the app and tap a cover button";
		});

	private void OnAppAction(object? sender, AppActionEventArgs e) =>
		MoreLabel.Text = $"more: cover action '{e.AppAction.Id}' at {DateTime.Now:HH:mm:ss}";

	private async void OnSpeakClicked(object? sender, EventArgs e) =>
		MoreLabel.Text = await SafeAsync(async () => { await TextToSpeech.Default.SpeakAsync("Hello from Sailfish"); return "more: spoken"; });

	private static async Task<string> SafeAsync(Func<Task<string>> probe)
	{
		try
		{
			return await probe();
		}
		catch (Exception ex)
		{
			return $"unsupported: {ex.GetType().Name}: {ex.Message}";
		}
	}
}
