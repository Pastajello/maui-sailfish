using Microsoft.Maui.Storage;
using Trailhead.Data;

namespace Trailhead.Features.Shared;

/// <summary>Shares a trip as a GPX file through the system share sheet (Sailfish.Share).</summary>
public static class TripSharing
{
	public static async Task ShareGpxAsync(Trip trip)
	{
		var name = string.Concat(trip.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == ' ' ? '-' : c));
		var file = Path.Combine(FileSystem.CacheDirectory, name + ".gpx");
		await File.WriteAllTextAsync(file, Gpx.Write(trip));
		await Share.Default.RequestAsync(new ShareFileRequest(trip.Name, new ShareFile(file, "application/gpx+xml")));
	}
}
