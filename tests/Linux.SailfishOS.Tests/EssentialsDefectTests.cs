using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.Communication;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Media;
using Microsoft.Maui.SailfishOS.Platform;
using Microsoft.Maui.Storage;
using Xunit;

namespace Linux.SailfishOS.Tests;

/// <summary>Tracker S10 (plan M14 step 3): Essentials members that dropped what the app asked for.</summary>
public class EssentialsDefectTests
{
	[Fact]
	public void Sharing_text_with_a_link_keeps_both()
	{
		var (_, mime, resources) = SailfishShare.Describe(new ShareTextRequest("Look at this", "T") { Uri = "https://example.org/x" });
		Assert.Equal("text/plain", mime);
		var (_, content, _) = ((string, string, string))Assert.Single(resources);
		Assert.Contains("Look at this", content);
		Assert.Contains("https://example.org/x", content);
	}

	[Fact]
	public void Sharing_only_a_link_stays_a_link()
	{
		var (_, mime, _) = SailfishShare.Describe(new ShareTextRequest { Uri = "https://example.org" });
		Assert.Equal("text/x-url", mime);
	}

	[Fact]
	public Task An_email_with_attachments_is_refused_instead_of_sent_without_them()
	{
		var message = new EmailMessage("s", "b", "a@example.com");
		message.Attachments!.Add(new EmailAttachment(Path.GetTempFileName()));
		return Assert.ThrowsAsync<FeatureNotSupportedException>(() => new SailfishCommunication().ComposeAsync(message));
	}

	[Fact]
	public void ShowSettingsUI_reports_an_unsupported_feature() =>
		Assert.Throws<FeatureNotSupportedException>(() => new SailfishAppInfo(SailfishAppMeta.Empty).ShowSettingsUI());

	[Theory]
	[InlineData(0, 5, 5)]   // no limit
	[InlineData(3, 5, 3)]
	[InlineData(1, 5, 1)]   // MAUI's default limit
	[InlineData(10, 5, 5)]
	public void The_selection_limit_keeps_the_first_picked(int limit, int picked, int kept)
	{
		var files = Enumerable.Range(0, picked).Select(i => new FileResult($"/tmp/p{i}.jpg")).ToList();
		var result = SailfishPickers.Limit(files, new MediaPickerOptions { SelectionLimit = limit });
		Assert.Equal(kept, result.Count);
		Assert.Equal("/tmp/p0.jpg", result[0].FullPath);
	}

	[Fact]
	public void No_options_means_no_limit() =>
		Assert.Equal(4, SailfishPickers.Limit(Enumerable.Range(0, 4).Select(i => new FileResult($"/tmp/{i}")).ToList(), null).Count);

	[Fact]
	public void Fixes_closer_than_MinimumDistance_are_not_reported()
	{
		var start = new Location(52.2297, 21.0122);
		var nearby = new Location(52.22975, 21.0122);   // about 5.6 m north
		var far = new Location(52.2307, 21.0122);       // about 111 m north
		Assert.True(SailfishGeolocation.PassesMinimumDistance(null, start, 50));
		Assert.False(SailfishGeolocation.PassesMinimumDistance(start, nearby, 50));
		Assert.True(SailfishGeolocation.PassesMinimumDistance(start, far, 50));
		Assert.True(SailfishGeolocation.PassesMinimumDistance(start, nearby, 0));
	}

	[Fact]
	public void Low_accuracy_prefers_network_positioning()
	{
		Assert.Contains("NonSatellite", SailfishGeolocation.MethodsJs(GeolocationAccuracy.Low));
		Assert.Contains("AllPositioningMethods", SailfishGeolocation.MethodsJs(GeolocationAccuracy.Best));
		Assert.Contains("AllPositioningMethods", SailfishGeolocation.MethodsJs(GeolocationAccuracy.Default));
	}

	// Tracker S12: the Sailjail mapping checked against /etc/sailjail/permissions on SFOS 5.2.
	[Fact]
	public void Sensors_need_their_Sailjail_permission_and_the_flashlight_is_denied_in_a_sandbox()
	{
		var none = (Sandboxed: true, Declared: new HashSet<string>());
		var sensors = (Sandboxed: true, Declared: new HashSet<string> { "Sensors" });
		Assert.Equal(PermissionStatus.Denied, SailfishPermissions.StatusFor(typeof(Permissions.Sensors), none));
		Assert.Equal(PermissionStatus.Granted, SailfishPermissions.StatusFor(typeof(Permissions.Sensors), sensors));
		Assert.Equal(PermissionStatus.Denied, SailfishPermissions.StatusFor(typeof(Permissions.Flashlight), sensors));
		Assert.Equal(PermissionStatus.Granted, SailfishPermissions.StatusFor(typeof(Permissions.Flashlight), (false, new HashSet<string>())));
		Assert.Equal(new[] { "AppLaunch" }, SailfishPermissions.SailjailFor(typeof(Permissions.LaunchApp)));
	}

	[Fact]
	public void Pickers_name_the_folders_a_sandbox_must_declare()
	{
		Assert.Contains("Pictures", SailfishPickers.PickerPermissions("images"));
		Assert.Contains("Videos", SailfishPickers.PickerPermissions("video"));
		Assert.Contains("Documents", SailfishPickers.PickerPermissions("file"));
	}

	[Fact]
	public void SecureStorage_DeviceDisplay_and_the_screen_reader_come_from_the_registry()
	{
		var early = SailfishEssentialsRegistry.Entries.Where(e => e.Early).Select(e => e.Service).ToList();
		Assert.Contains(typeof(ISecureStorage), early);
		Assert.Contains(typeof(Microsoft.Maui.Devices.IDeviceDisplay), early);
		var reader = SailfishEssentialsRegistry.Find(typeof(Microsoft.Maui.Accessibility.ISemanticScreenReader));
		Assert.NotNull(reader);
		Assert.IsType<SailfishSemanticScreenReader>(SailfishEssentialsRegistry.DefaultFor(reader!));
	}
}
