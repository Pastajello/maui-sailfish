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
}
