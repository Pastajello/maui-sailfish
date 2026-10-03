using Microsoft.Maui.SailfishOS.Platform;
using Xunit;

namespace Linux.SailfishOS.Tests;

/// <summary>
/// The public types of the core package are its API: apps, libraries (the SkiaSharp package) and the generated entry
/// point build on them, everything else is internal (Diagnostics and these tests see it through InternalsVisibleTo).
/// A type that turns public, or stops being public, fails here until <c>PublicSurface.txt</c> is updated on purpose:
/// regenerate it with <c>SF_WRITE_PUBLIC_SURFACE=1 dotnet test --filter PublicSurfaceTests</c>.
/// </summary>
public class PublicSurfaceTests
{
	private static readonly string SnapshotPath = Path.Combine(Repo.Root, "tests", "Linux.SailfishOS.Tests", "PublicSurface.txt");

	[Fact]
	public void Public_types_match_the_snapshot()
	{
		var actual = typeof(SailfishMauiApplication).Assembly.GetExportedTypes()
			.Select(t => t.FullName!)
			.Order(StringComparer.Ordinal)
			.ToArray();
		if (Environment.GetEnvironmentVariable("SF_WRITE_PUBLIC_SURFACE") == "1")
			File.WriteAllLines(SnapshotPath, actual);
		var expected = File.ReadAllLines(SnapshotPath).Where(l => l.Length > 0).ToArray();

		Assert.Empty(actual.Except(expected));     // newly public: an API decision, update the snapshot
		Assert.Empty(expected.Except(actual));     // no longer public: breaking for apps and libraries
	}
}

public class SailfishPlatformTests
{
	[Fact]
	public void Device_info_reports_the_public_sailfish_platform()
	{
		Assert.Equal(SailfishPlatform.DevicePlatform, new SailfishDeviceInfo().Platform);
		Assert.Equal("SailfishOS", SailfishPlatform.DevicePlatform.ToString());
	}
}
