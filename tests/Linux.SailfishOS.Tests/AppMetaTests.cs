using Microsoft.Maui.SailfishOS.Platform;
using Xunit;

namespace Linux.SailfishOS.Tests;

/// <summary>qml/maui-appmeta.json, read once into SailfishAppMeta (shell, AppInfo and app paths share it).</summary>
public class AppMetaTests
{
	[Fact]
	public void The_baked_meta_parses_into_one_record()
	{
		var meta = SailfishAppMeta.Parse("""
			{ "orientation": "Landscape", "cover": true, "title": "Kitchen", "coverQml": "cover/Cover.qml",
			  "application": "harbour-kitchen", "organization": "org.example", "sandboxed": true,
			  "dbusName": "org.example.kitchen", "dbusPath": "/", "dbusIface": "org.example.kitchen" }
			""");
		Assert.Equal(("Landscape", true, "Kitchen", "cover/Cover.qml"), (meta.Orientation, meta.Cover, meta.Title, meta.CoverQml));
		Assert.Equal(("harbour-kitchen", "org.example", true), (meta.Application, meta.Organization, meta.Sandboxed));
		Assert.Equal(("org.example.kitchen", "/", "org.example.kitchen"), (meta.DbusName, meta.DbusPath, meta.DbusIface));
	}

	[Fact]
	public void Missing_fields_read_as_the_defaults()
	{
		var meta = SailfishAppMeta.Parse("""{ "application": "" }""");
		Assert.Equal(SailfishAppMeta.Empty, meta);
		Assert.Equal((1, 2, 15, 15), (SailfishAppMeta.OrientationMask("Portrait"), SailfishAppMeta.OrientationMask("Landscape"),
			SailfishAppMeta.OrientationMask("Any"), SailfishAppMeta.OrientationMask(null)));
	}

	// Tracker S09: AppInfo reports what the other heads report (ApplicationId, ApplicationDisplayVersion,
	// ApplicationVersion), not the entry assembly's name and version.
	[Fact]
	public void AppInfo_reads_the_app_id_and_versions_from_the_meta()
	{
		var meta = SailfishAppMeta.Parse("""
			{ "title": "Kitchen", "application": "harbour-kitchen", "appId": "org.example.kitchen", "version": "1.4.2", "build": "17" }
			""");
		var info = new SailfishAppInfo(meta);
		Assert.Equal(("org.example.kitchen", "Kitchen", "1.4.2", "17"), (info.PackageName, info.Name, info.VersionString, info.BuildString));
		Assert.Equal(new Version(1, 4, 2), info.Version);
		Assert.Equal(Microsoft.Maui.ApplicationModel.AppPackagingModel.Packaged, info.PackagingModel);
	}

	[Theory]
	[InlineData("1.2-beta", "1.2")]
	[InlineData("3", "3.0")]
	[InlineData("2.0.1.4", "2.0.1.4")]
	[InlineData("beta", "0.0")]
	public void A_display_version_that_is_not_numeric_reads_its_numeric_part(string text, string expected) =>
		Assert.Equal(Version.Parse(expected), SailfishAppInfo.ParseVersion(text));

	// VersionTracking (MAUI's own implementation over the Sailfish Preferences and AppInfo) sees an RPM version bump.
	[Fact]
	public void VersionTracking_sees_a_new_display_version()
	{
		var file = Path.Combine(Path.GetTempPath(), $"sf-versiontracking-{Guid.NewGuid():N}.json");
		try
		{
			var impl = typeof(Microsoft.Maui.ApplicationModel.VersionTracking).Assembly
				.GetType("Microsoft.Maui.ApplicationModel.VersionTrackingImplementation", throwOnError: true)!;
			Microsoft.Maui.ApplicationModel.IVersionTracking Track(string version, string build) =>
				(Microsoft.Maui.ApplicationModel.IVersionTracking)Activator.CreateInstance(impl,
					new SailfishPreferences(file),
					new SailfishAppInfo(SailfishAppMeta.Empty with { AppId = "org.example.app", Version = version, Build = build }))!;

			var first = Track("1.0", "1");
			Assert.True(first.IsFirstLaunchEver);
			var second = Track("1.1", "2");
			Assert.False(second.IsFirstLaunchEver);
			Assert.True(second.IsFirstLaunchForCurrentVersion);
			Assert.Equal(("1.0", "1.1"), (second.PreviousVersion, second.CurrentVersion));
		}
		finally
		{
			File.Delete(file);
		}
	}
}
