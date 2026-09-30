using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.Communication;
using Microsoft.Maui.SailfishOS.Platform;
using Xunit;

namespace Linux.SailfishOS.Tests;

/// <summary>Build-host checks of the Essentials pieces that need no device.</summary>
public class EssentialsTests
{
	[Fact]
	public void Communication_uris_follow_the_schemes()
	{
		Assert.Equal("tel:%2B48%20600%20100%20200", SailfishCommunication.DialUri(" +48 600 100 200 "));
		Assert.Equal("mailto:a@example.com,b@example.com?bcc=d%40example.com&subject=Hi%20there&body=Line%201",
			SailfishCommunication.MailUri(new EmailMessage("Hi there", "Line 1", "a@example.com", "b@example.com") { Bcc = new List<string> { "d@example.com" } }));
		Assert.Equal("mailto:", SailfishCommunication.MailUri(null));
		Assert.Equal("sms:+1555,+1666?body=hello%20world", SailfishCommunication.SmsUri(new SmsMessage("hello world", new[] { "+1555", "+1666" })));
		Assert.Equal("geo:-33.8688,151.2093", SailfishCommunication.GeoUri(-33.8688, 151.2093, null));
		Assert.Equal("geo:1.5,2.25?q=1.5,2.25(Caf%C3%A9)", SailfishCommunication.GeoUri(1.5, 2.25, "Café"));
	}

	[Fact]
	public void Permissions_map_to_sailjail_names()
	{
		Assert.Equal(new[] { "Camera" }, SailfishPermissions.SailjailFor(typeof(Permissions.Camera)));
		Assert.Equal(new[] { "Location" }, SailfishPermissions.SailjailFor(typeof(Permissions.LocationWhenInUse)));
		Assert.Equal(new[] { "Location" }, SailfishPermissions.SailjailFor(typeof(Permissions.LocationAlways)));
		Assert.Equal(new[] { "Contacts" }, SailfishPermissions.SailjailFor(typeof(Permissions.ContactsRead)));
		Assert.Equal(new[] { "Messages" }, SailfishPermissions.SailjailFor(typeof(Permissions.Sms)));
		Assert.Contains("Pictures", SailfishPermissions.SailjailFor(typeof(Permissions.Photos)));
		// no Sailjail counterpart → nothing to declare
		Assert.Empty(SailfishPermissions.SailjailFor(typeof(Permissions.Vibrate)));
		Assert.Empty(SailfishPermissions.SailjailFor(typeof(Permissions.NetworkState)));
	}

	[Fact]
	public void Release_files_parse_like_os_release()
	{
		var path = Path.GetTempFileName();
		try
		{
			File.WriteAllText(path, "# comment\nNAME=\"Jolla Jolla Phone\"\nID=jp2601\nMER_HA_VENDOR=jolla\n\nbroken line\nVERSION_ID=5.2.0.17\n");
			var map = SailfishDeviceInfo.ReadRelease(path);
			Assert.Equal("Jolla Jolla Phone", map["NAME"]);
			Assert.Equal("jolla", map["MER_HA_VENDOR"]);
			Assert.Equal("5.2.0.17", map["VERSION_ID"]);
			Assert.False(map.ContainsKey("broken line"));
			Assert.Empty(SailfishDeviceInfo.ReadRelease(path + ".missing"));
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Fact]
	public void Json_context_round_trips_the_stores_without_reflection()
	{
		var prefs = new Dictionary<string, JsonElement>
		{
			["n"] = JsonSerializer.SerializeToElement(42, SailfishJsonContext.Default.Int32),
			["s"] = JsonSerializer.SerializeToElement("x", SailfishJsonContext.Default.String),
			["b"] = JsonSerializer.SerializeToElement(true, SailfishJsonContext.Default.Boolean),
		};
		var json = JsonSerializer.Serialize(prefs, SailfishJsonContext.Default.DictionaryStringJsonElement);
		var back = JsonSerializer.Deserialize(json, SailfishJsonContext.Default.DictionaryStringJsonElement)!;
		Assert.Equal(42, back["n"].GetInt32());
		Assert.Equal("x", back["s"].GetString());
		Assert.True(back["b"].GetBoolean());
		var secure = JsonSerializer.Serialize(new Dictionary<string, string> { ["k"] = "v" }, SailfishJsonContext.Default.DictionaryStringString);
		Assert.Equal("v", JsonSerializer.Deserialize(secure, SailfishJsonContext.Default.DictionaryStringString)!["k"]);
	}

	[Fact]
	public void Secrets_collection_name_is_a_plain_identifier()
	{
		var name = SailfishSecureStorage.CollectionName;
		// the sqlcipher plugin rejects anything but alphanumeric Latin-1 < 32 chars
		Assert.StartsWith("maui", name);
		Assert.True(name.Length < 32, name);
		Assert.All(name, c => Assert.True(char.IsAsciiLetterOrDigit(c), $"'{c}' in {name}"));
	}

	[Fact]
	public void Secrets_bridge_is_optional()
	{
		// Without the bridge the loader reports why instead of throwing, and the store uses the file backend.
		if (OperatingSystem.IsLinux())
			return;
		Assert.False(SecretsNative.TryLoad(out var reason));
		Assert.False(string.IsNullOrEmpty(reason));
	}
}

/// <summary>With UseMauiAppSailfish the Essentials are DI services that MAUI's own bridge installs behind the statics
/// during Build (the facades are process-wide, hence the serialized collection).</summary>
[Collection("renderer")]
public class EssentialsBridgeTests
{
	private sealed class TestApp : Microsoft.Maui.Controls.Application
	{
	}

	private sealed class AppVibration : Microsoft.Maui.Devices.IVibration
	{
		public bool IsSupported => true;
		public void Vibrate() { }
		public void Vibrate(TimeSpan duration) { }
		public void Cancel() { }
	}

	[Fact]
	public void Build_installs_the_sailfish_services_and_app_registrations_win()
	{
		var builder = Microsoft.Maui.Hosting.MauiApp.CreateBuilder();
		Microsoft.Maui.SailfishOS.Hosting.AppHostBuilderExtensions.UseMauiAppSailfish<TestApp>(builder);
		builder.Services.AddSingleton<Microsoft.Maui.Devices.IVibration, AppVibration>();
		using var app = builder.Build();

		Assert.IsType<SailfishClipboard>(Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.Default);
		Assert.Same(app.Services.GetService(typeof(Microsoft.Maui.ApplicationModel.DataTransfer.IClipboard)),
			Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.Default);
		Assert.IsType<SailfishBattery>(Microsoft.Maui.Devices.Battery.Default);
		Assert.IsType<SailfishCommunication>(Email.Default);
		Assert.Same(Email.Default, Sms.Default);   // one instance behind several interfaces
		Assert.IsType<AppVibration>(Microsoft.Maui.Devices.Vibration.Default);
	}
}

/// <summary>UriImageSource caching as Android and iOS honour it: the policy rides a URL fragment to the shim's cache.</summary>
public class ImageCachePolicyTests
{
	[Fact]
	public void UriImageSource_caching_flags_reach_the_http_cache()
	{
		var uri = new Uri("https://example.com/thumb.jpg");
		Assert.Equal("https://example.com/thumb.jpg#maui-cache=86400",
			Microsoft.Maui.SailfishOS.Platform.QtHost.QtHostImages.Resolve(new Microsoft.Maui.Controls.UriImageSource { Uri = uri }));
		Assert.Equal("https://example.com/thumb.jpg#maui-cache=0",
			Microsoft.Maui.SailfishOS.Platform.QtHost.QtHostImages.Resolve(new Microsoft.Maui.Controls.UriImageSource { Uri = uri, CachingEnabled = false }));
		Assert.Equal("https://example.com/thumb.jpg#maui-cache=3600",
			Microsoft.Maui.SailfishOS.Platform.QtHost.QtHostImages.Resolve(new Microsoft.Maui.Controls.UriImageSource { Uri = uri, CacheValidity = TimeSpan.FromHours(1) }));
	}
}
