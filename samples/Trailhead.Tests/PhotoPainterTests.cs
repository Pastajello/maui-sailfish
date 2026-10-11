using Trailhead.Data;
using Xunit;

namespace Trailhead.Tests;

public class PhotoPainterTests
{
	[Fact]
	public void Paints_a_photo_and_its_thumbnail()
	{
		// TRAILHEAD_PHOTO_OUT keeps the images for a look; otherwise a temp dir.
		var dir = Environment.GetEnvironmentVariable("TRAILHEAD_PHOTO_OUT") ?? Path.Combine(Path.GetTempPath(), "trailhead-photos");
		foreach (var route in new[] { DemoRoutes.All[1], DemoRoutes.All[4], DemoRoutes.All[10], DemoRoutes.All[11] })
		{
			var trip = TripGenerator.Generate(route);
			for (var i = 0; i < Math.Min(2, trip.Photos.Count); i++)
			{
				var photo = trip.Photos[i];
				var path = Path.Combine(dir, $"{trip.Id}-{photo.File}");
				var thumb = Path.Combine(dir, $"{trip.Id}-{photo.ThumbnailFile}");
				PhotoPainter.Paint(trip, photo, i, path, thumb);
				Assert.True(new FileInfo(path).Length > 10_000);
				Assert.True(new FileInfo(thumb).Length > 2_000);
			}
		}
	}
}
