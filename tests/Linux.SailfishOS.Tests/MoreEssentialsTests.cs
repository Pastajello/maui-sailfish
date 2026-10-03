using Linux.SailfishOS.Tests.Renderer;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.Communication;
using Microsoft.Maui.Authentication;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Media;
using Microsoft.Maui.SailfishOS.Platform;
using Microsoft.Maui.Storage;
using Xunit;

namespace Linux.SailfishOS.Tests;

/// <summary>The Essentials that threw the reference-assembly exception before: Flashlight, TextToSpeech, Geocoding,
/// Passkeys, AppActions, WebAuthenticator, Contacts; and FileResult from the pickers.</summary>
[Collection("renderer")]
public class MoreEssentialsTests
{
	private sealed class TestApp : Application
	{
	}

	[Fact]
	public void Build_puts_the_sailfish_implementations_behind_the_statics()
	{
		var builder = Microsoft.Maui.Hosting.MauiApp.CreateBuilder();
		Microsoft.Maui.SailfishOS.Hosting.AppHostBuilderExtensions.UseMauiAppSailfish<TestApp>(builder);
		using var app = builder.Build();

		Assert.IsType<SailfishFlashlight>(Flashlight.Default);
		Assert.IsType<SailfishTextToSpeech>(TextToSpeech.Default);
		Assert.IsType<SailfishGeocoding>(Geocoding.Default);
		Assert.IsType<SailfishPasskeys>(Passkeys.Default);
		Assert.IsType<SailfishAppActions>(AppActions.Current);
		Assert.IsType<SailfishWebAuthenticator>(WebAuthenticator.Default);
		Assert.IsType<SailfishContacts>(Contacts.Default);
	}

	// No TTS engine, geocoder or passkey authenticator on Sailfish OS: the documented "not supported" answer, which
	// apps catch, instead of the reference assembly's NotImplementedInReferenceAssemblyException.
	[Fact]
	public async Task Features_the_platform_lacks_report_FeatureNotSupported()
	{
		Assert.Empty(await new SailfishTextToSpeech().GetLocalesAsync());
		await Assert.ThrowsAsync<FeatureNotSupportedException>(() => new SailfishTextToSpeech().SpeakAsync("hello"));
		await Assert.ThrowsAsync<FeatureNotSupportedException>(() => new SailfishGeocoding().GetLocationsAsync("Tampere"));
		await Assert.ThrowsAsync<FeatureNotSupportedException>(() => new SailfishGeocoding().GetPlacemarksAsync(61.5, 23.8));
		var passkeys = new SailfishPasskeys();
		Assert.False(passkeys.IsSupported);
		await Assert.ThrowsAsync<FeatureNotSupportedException>(() => passkeys.AssertAsync(new PasskeyRequestOptions("{}")));
	}

	[Fact]
	public async Task The_flashlight_toggles_the_system_torch_only_when_it_must()
	{
		using var h = new RendererHarness(new ContentPage { Content = new Label { Text = "x" } });
		var on = false;
		var toggles = 0;
		h.Shim.EvalHook = js =>
		{
			if (js.Contains("mauiService(\"flashlight\"", StringComparison.Ordinal))
				return "ok";
			if (js.Contains("s.state()", StringComparison.Ordinal))
				return on ? "on" : "off";
			if (js.Contains("s.setOn(true)", StringComparison.Ordinal))
			{
				if (on)
					return "same";
				on = true;
				toggles++;
				return "toggled";
			}
			if (js.Contains("s.setOn(false)", StringComparison.Ordinal))
			{
				if (!on)
					return "same";
				on = false;
				toggles++;
				return "toggled";
			}
			return null;
		};
		var flashlight = new SailfishFlashlight();

		Assert.True(await flashlight.IsSupportedAsync());
		await flashlight.TurnOnAsync();
		await flashlight.TurnOnAsync();   // already on: no second toggle
		await flashlight.TurnOffAsync();
		Assert.Equal(2, toggles);
		Assert.False(on);

		h.Shim.EvalHook = js => js.Contains("mauiService(", StringComparison.Ordinal) ? "ok" : js.Contains("s.", StringComparison.Ordinal) ? "unavailable" : null;
		Assert.False(await flashlight.IsSupportedAsync());
		await Assert.ThrowsAsync<FeatureNotSupportedException>(flashlight.TurnOnAsync);
	}

	[Fact]
	public async Task App_actions_become_cover_actions_and_raise_their_event()
	{
		var actions = new SailfishAppActions();
		AppAction? activated = null;
		actions.AppActionActivated += (_, e) => activated = e.AppAction;

		await actions.SetAsync(new[]
		{
			new AppAction("search", "Search", icon: "icon-cover-search"),
			new AppAction("new", "New note"),
			new AppAction("third", "Third"),
		});

		Assert.Equal(3, (await actions.GetAsync()).Count());
		var cover = SailfishCover.Actions;
		Assert.Equal(2, cover.Length);   // a Silica cover has two buttons
		Assert.Equal("image://theme/icon-cover-search", cover[0].Icon);
		Assert.Equal(SailfishAppActions.DefaultIcon, cover[1].Icon);
		cover[1].Triggered();
		Assert.Equal("new", activated?.Id);
	}

	[Fact]
	public async Task Web_authentication_completes_with_the_callback_the_browser_hands_back()
	{
		Uri? opened = null;
		var openBrowser = SailfishWebAuthenticator.OpenBrowser;
		SailfishWebAuthenticator.OpenBrowser = uri => { opened = uri; return Task.CompletedTask; };
		try
		{
			SailfishOpenUrl.Configure(null, null, null);
			var authenticator = new SailfishWebAuthenticator();
			var options = new WebAuthenticatorOptions
			{
				Url = new Uri("https://login.example.com/authorize?client=1"),
				CallbackUrl = new Uri("myapp://auth"),
			};
			// Without a declared URL scheme the browser could never come back.
			await Assert.ThrowsAsync<InvalidOperationException>(() => authenticator.AuthenticateAsync(options));

			SailfishOpenUrl.Configure("org.example.myapp", "/org/example/myapp", "org.example.myapp");
			var signIn = authenticator.AuthenticateAsync(options);
			Assert.Equal(options.Url, opened);
			Assert.False(signIn.IsCompleted);

			SailfishOpenUrl.Deliver(new Uri("otherscheme://auth?x=1"));   // not ours: the app gets it
			Assert.False(signIn.IsCompleted);
			SailfishOpenUrl.Deliver(new Uri("myapp://auth#access_token=abc&state=s1"));
			var result = await signIn.WaitAsync(TimeSpan.FromSeconds(5));
			Assert.Equal("abc", result.AccessToken);
			Assert.Equal("s1", result.Properties["state"]);
			Assert.Null(SailfishOpenUrl.Intercept);   // the next link goes to the app again

			using var cancel = new CancellationTokenSource();
			var abandoned = authenticator.AuthenticateAsync(options, cancel.Token);
			cancel.Cancel();
			await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned);
			Assert.Null(SailfishOpenUrl.Intercept);
		}
		finally
		{
			SailfishWebAuthenticator.OpenBrowser = openBrowser;
			SailfishOpenUrl.Configure(null, null, null);
			SailfishOpenUrl.Intercept = null;
		}
	}

	[Fact]
	public void Address_book_entries_map_to_contacts()
	{
		var contacts = SailfishContacts.Parse("""
			[{"id":"12","prefix":"Dr","first":"Ada","middle":"","last":"Lovelace","suffix":"","display":"Ada Lovelace",
			  "phones":["+44 20 7946 0000",""],"emails":["ada@example.org"]},
			 {"id":"13","first":"","last":"","display":"","phones":[],"emails":[]}]
			""").ToList();

		Assert.Equal(2, contacts.Count);
		var ada = contacts[0];
		Assert.Equal("12", ada.Id);
		Assert.Equal("Dr", ada.NamePrefix);
		Assert.Equal("Ada", ada.GivenName);
		Assert.Equal("Lovelace", ada.FamilyName);
		Assert.Equal("Ada Lovelace", ada.DisplayName);
		Assert.Equal("+44 20 7946 0000", Assert.Single(ada.Phones).PhoneNumber);
		Assert.Equal("ada@example.org", Assert.Single(ada.Emails).EmailAddress);
		Assert.Empty(contacts[1].Phones);
	}

	// Like READ_CONTACTS on Android: a sandboxed app reads contacts only after declaring the Contacts Sailjail permission.
	[Fact]
	public void Contacts_need_the_contacts_permission_in_a_sandbox()
	{
		var undeclared = (true, new HashSet<string> { "Internet" });
		var declared = (true, new HashSet<string> { "Contacts" });
		Assert.Equal(PermissionStatus.Denied, SailfishPermissions.StatusFor(typeof(Permissions.ContactsRead), undeclared));
		Assert.Equal(PermissionStatus.Granted, SailfishPermissions.StatusFor(typeof(Permissions.ContactsRead), declared));
		Assert.Contains("Contacts", SailfishPermissions.MissingMessage("Reading contacts", SailfishPermissions.SailjailFor(typeof(Permissions.ContactsRead))));
	}

	// Outside a sandbox there is nothing to declare: the call reaches the address book model, instead of a
	// PermissionException.
	[Fact]
	public async Task Contacts_outside_a_sandbox_load_the_address_book_model()
	{
		using var h = new RendererHarness(new ContentPage { Content = new Label { Text = "x" } });
		h.Shim.EvalHook = js =>
			js.Contains("mauiService(\"contacts\"", StringComparison.Ordinal) ? "ok"
			: js.Contains("s.all.populated", StringComparison.Ordinal) ? "true"
			: js.Contains("s.dump()", StringComparison.Ordinal) ? "[]"
			: null;
		Assert.Empty(await new SailfishContacts().GetAllAsync());
	}

	// MAUI's plain-net FileBase cannot resolve a MIME type (PlatformGetContentType throws); the pickers name it.
	[Theory]
	[InlineData("/home/defaultuser/Pictures/a.JPG", "image/jpeg")]
	[InlineData("/home/defaultuser/Videos/b.mp4", "video/mp4")]
	[InlineData("/home/defaultuser/Documents/c.pdf", "application/pdf")]
	[InlineData("/home/defaultuser/Documents/d.unknown", "application/octet-stream")]
	public void Picked_files_carry_their_content_type(string path, string contentType)
	{
		var file = SailfishPickers.ToFileResult(path);
		Assert.Equal(contentType, file.ContentType);
		Assert.Equal(Path.GetFileName(path), file.FileName);
	}
}

/// <summary>SailfishPreferences on a scratch file (never the user's ~/.config).</summary>
public class PreferencesStoreTests : IDisposable
{
	private readonly string _dir = Path.Combine(Path.GetTempPath(), "sf-prefs-" + Guid.NewGuid().ToString("N"));
	private string File => Path.Combine(_dir, "preferences.json");

	public void Dispose()
	{
		if (Directory.Exists(_dir))
			Directory.Delete(_dir, recursive: true);
	}

	[Fact]
	public void Clearing_a_shared_container_keeps_the_others()
	{
		var prefs = new Microsoft.Maui.SailfishOS.Platform.SailfishPreferences(File);
		prefs.Set("a", 1);
		prefs.Set("b", 2, "group");
		prefs.Set("c", 3, "other");

		prefs.Clear("group");
		Assert.Equal((1, 0, 3), (prefs.Get("a", 0), prefs.Get("b", 0, "group"), prefs.Get("c", 0, "other")));

		prefs.Clear();   // the default container only
		Assert.Equal((0, 3), (prefs.Get("a", 0), prefs.Get("c", 0, "other")));
	}

	[Fact]
	public void A_DateTimeOffset_round_trips_through_the_file()
	{
		var when = new DateTimeOffset(2026, 10, 3, 9, 30, 0, TimeSpan.FromHours(2));
		new Microsoft.Maui.SailfishOS.Platform.SailfishPreferences(File).Set("when", when);
		Assert.Equal(when, new Microsoft.Maui.SailfishOS.Platform.SailfishPreferences(File).Get("when", DateTimeOffset.MinValue));
		Assert.False(System.IO.File.Exists(File + ".tmp"));   // written through a temp file, renamed over the store
	}

	[Fact]
	public void Concurrent_writers_lose_no_key()
	{
		var prefs = new Microsoft.Maui.SailfishOS.Platform.SailfishPreferences(File);
		Parallel.For(0, 64, i => prefs.Set($"k{i}", i));
		var reread = new Microsoft.Maui.SailfishOS.Platform.SailfishPreferences(File);
		Assert.All(Enumerable.Range(0, 64), i => Assert.Equal(i, reread.Get($"k{i}", -1)));
	}
}

/// <summary>SailfishEssentialsRegistry: one row per Essentials service drives DI, the facades and the overlay.</summary>
public class EssentialsRegistryTests
{

	[Fact]
	public void Every_row_names_an_installer_the_facade_has()
	{
		foreach (var entry in Microsoft.Maui.SailfishOS.Platform.SailfishEssentialsRegistry.Entries)
		{
			var hook = entry.Facade.GetMethod(entry.Hook, System.Reflection.BindingFlags.Static |
				System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
			Assert.True(hook is not null, $"{entry.Facade.Name}.{entry.Hook} not found");
			Assert.IsAssignableFrom(entry.Service, Microsoft.Maui.SailfishOS.Platform.SailfishEssentialsRegistry.DefaultFor(entry));
		}
	}

	// The facade installed before CreateMauiApp, the container's singleton and the overlay's fallback are one object:
	// two Preferences instances kept two caches that overwrote each other's file. (No MauiApp is built here: that
	// remaps MAUI's official mappers process-wide and races HandlerParityTests.)
	[Fact]
	public void Container_overlay_and_registry_hand_out_one_instance()
	{
		var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
		SailfishEssentials.AddSailfishEssentials(services);
		var container = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
		var overlay = new SailfishServiceOverlay(container);
		var plainOverlay = new SailfishServiceOverlay(
			Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(
				new Microsoft.Extensions.DependencyInjection.ServiceCollection()));
		var registry = SailfishEssentialsRegistry.DefaultFor(SailfishEssentialsRegistry.Find(typeof(IPreferences))!);

		Assert.Same(registry, container.GetService(typeof(IPreferences)));
		Assert.Same(registry, overlay.GetService(typeof(IPreferences)));
		Assert.Same(registry, plainOverlay.GetService(typeof(IPreferences)));
		Assert.Same(container.GetService(typeof(IMediaPicker)), container.GetService(typeof(IFilePicker)));   // one pickers instance
	}
}
