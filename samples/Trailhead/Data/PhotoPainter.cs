using SkiaSharp;

namespace Trailhead.Data;

/// <summary>
/// Paints the demo photos: a landscape whose ridgelines come from the trip's own elevation profile around the spot,
/// lit for the time of day (night, dawn, day, dusk) and the season (snow in winter, on ski trips and on high ground),
/// with water where the caption names a lake or the sea. No two photos are the same and nothing is downloaded.
/// </summary>
public static class PhotoPainter
{
	public const int Width = 960, Height = 720, ThumbWidth = 320, ThumbHeight = 240;

	private static readonly string[] WaterWords = { "Oko", "Staw", "pier", "Orłowo", "lake", "Mustalampi", "Kattila", "Kilpisjärvi", "Brzeźno", "Vistula" };

	public static void Paint(Trip trip, TripPhoto photo, int index, string path, string thumbPath)
	{
		var rng = new Random(HashCode.Combine(trip.Id.GetHashCode(StringComparison.Ordinal), index));
		var info = new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul);
		using var surface = SKSurface.Create(info);
		var canvas = surface.Canvas;

		var hour = photo.Time.Hour + photo.Time.Minute / 60.0;
		var winter = trip.Activity == Activity.Ski || photo.Time.Month is 12 or 1 or 2 or 3;
		var (skyTop, skyBottom, light) = Sky(hour);

		// Sky and sun (or moon).
		using (var sky = new SKPaint { Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, Height * 0.7f),
			new[] { skyTop, skyBottom }, SKShaderTileMode.Clamp) })
			canvas.DrawRect(0, 0, Width, Height, sky);
		var sunX = (float)(Width * Math.Clamp((hour - 4) / 18, 0.05, 0.95));
		var sunY = (float)(Height * (0.62 - 0.45 * Math.Sin(Math.PI * Math.Clamp((hour - 4) / 18, 0, 1))));
		using (var sun = new SKPaint { IsAntialias = true, Color = light.WithAlpha(230) })
			canvas.DrawCircle(sunX, sunY, hour is < 5 or > 21 ? 22 : 38, sun);

		// Ridgelines, far to near: the far ones fade into the sky (atmospheric perspective). Mountains get sharp ridged
		// peaks, lowlands soft hills; both are shaped by the trip's own elevation profile around the spot.
		var profile = Profile(trip, photo, rng);
		var mountainous = trip.Track.Max(p => p.Ele) > 700;
		var water = WaterWords.Any(w => photo.Caption.Contains(w, StringComparison.OrdinalIgnoreCase));
		var layers = water ? 2 : 4;
		for (var layer = 0; layer < layers; layer++)
		{
			var depth = layer / 3f;   // 0 far … 1 near
			var baseY = Height * (water ? 0.55f + 0.05f * layer : 0.45f + 0.13f * layer);
			var amplitude = Height * (mountainous ? 0.30f - 0.05f * layer : 0.09f - 0.015f * layer);
			var snowy = winter || (mountainous && layer == 0 && trip.Track.Max(p => p.Ele) > 1500);
			var ground = snowy ? new SKColor(236, 241, 247) : Ground(trip.Activity, layer);
			using var paint = new SKPaint { IsAntialias = true, Color = Mix(skyBottom, ground, 0.3f + 0.7f * depth) };
			var (f1, f2, p1, p2) = (1.5 + rng.NextDouble() * 2.5, 5 + rng.NextDouble() * 6, rng.NextDouble() * 6, rng.NextDouble() * 6);
			using var ridge = new SKPath();
			ridge.MoveTo(0, Height);
			for (var x = 0; x <= Width; x += 6)
			{
				var u = (double)x / Width;
				var shape = mountainous
					? 0.65 * (1 - Math.Abs(Math.Sin(u * Math.PI * f1 + p1))) + 0.2 * (0.5 + 0.5 * Math.Sin(u * Math.PI * f2 + p2))
					: 0.5 + 0.35 * Math.Sin(u * Math.PI * f1 + p1) + 0.15 * Math.Sin(u * Math.PI * f2 + p2);
				var h = 0.6 * shape + 0.4 * ProfileAt(profile, (u + 0.37 * layer) % 1);
				ridge.LineTo(x, baseY - (float)(h * amplitude));
			}
			ridge.LineTo(Width, Height);
			ridge.Close();
			canvas.DrawPath(ridge, paint);
		}
		if (water)
		{
			DrawWater(canvas, Height * 0.66f, skyTop, skyBottom, light, sunX);
			// The near shore.
			using var shore = new SKPaint { IsAntialias = true, Color = Mix(Ground(trip.Activity, 3), new SKColor(214, 196, 150), 0.4f) };
			using var bank = new SKPath();
			bank.MoveTo(0, Height);
			bank.LineTo(0, Height * 0.9f);
			bank.QuadTo(Width * 0.5f, Height * 0.84f, Width, Height * 0.93f);
			bank.LineTo(Width, Height);
			bank.Close();
			canvas.DrawPath(bank, shore);
		}

		// Spruces in the foreground below the tree line.
		if (photo.Caption.Length % 3 != 0 && !winter)
			DrawTrees(canvas, rng, Mix(Ground(trip.Activity, 3), SKColors.Black, 0.35f));

		DrawCaption(canvas, $"{photo.Caption} · {photo.Time:d MMM yyyy, HH:mm}");

		using var image = surface.Snapshot();
		Save(image, path, 86);
		using var thumbSurface = SKSurface.Create(new SKImageInfo(ThumbWidth, ThumbHeight));
		thumbSurface.Canvas.DrawImage(image, new SKRect(0, 0, ThumbWidth, ThumbHeight),
			new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
		using var thumb = thumbSurface.Snapshot();
		Save(thumb, thumbPath, 80);
	}

	/// <summary>Elevations around the photo, scaled to 0…1, padded with noise when the track is short or flat.</summary>
	private static double[] Profile(Trip trip, TripPhoto photo, Random rng)
	{
		var at = trip.Track.FindIndex(p => p.Time >= photo.Time);
		at = at < 0 ? trip.Track.Count / 2 : at;
		var window = trip.Track.Skip(Math.Max(0, at - 150)).Take(300).Select(p => p.Ele).ToArray();
		if (window.Length < 8)
			window = Enumerable.Range(0, 64).Select(i => Math.Sin(i / 6.0) * 50).ToArray();
		double min = window.Min(), max = window.Max();
		var flat = max - min < 40;
		return window.Select((e, i) => flat
				? 0.3 + 0.25 * Math.Sin(i / 9.0 + rng.NextDouble() * 0.2) + 0.1 * Math.Sin(i / 3.0)
				: 0.15 + 0.85 * (e - min) / (max - min))
			.ToArray();
	}

	/// <summary>The profile at 0…1, mirrored (there and back) so the ends meet without a jump.</summary>
	private static double ProfileAt(double[] profile, double u)
	{
		var t = u * 2 * (profile.Length - 1);
		var i = (int)t % (2 * (profile.Length - 1));
		var j = i < profile.Length ? i : 2 * (profile.Length - 1) - i;
		return profile[Math.Clamp(j, 0, profile.Length - 1)];
	}

	private static (SKColor Top, SKColor Bottom, SKColor Light) Sky(double hour) => hour switch
	{
		< 5 or >= 21.5 => (new SKColor(10, 14, 38), new SKColor(36, 44, 86), new SKColor(235, 235, 210)),
		< 7.5 => (new SKColor(64, 72, 140), new SKColor(250, 170, 120), new SKColor(255, 220, 160)),
		< 17.5 => (new SKColor(58, 120, 200), new SKColor(176, 214, 240), new SKColor(255, 250, 225)),
		_ => (new SKColor(90, 60, 130), new SKColor(250, 140, 90), new SKColor(255, 200, 120)),
	};

	private static SKColor Ground(Activity activity, int layer) => (activity, layer) switch
	{
		(Activity.Bike, _) => new SKColor(96, 140, 80),
		(_, 0) => new SKColor(110, 120, 140),
		(_, 1) => new SKColor(80, 110, 90),
		(_, 2) => new SKColor(60, 100, 60),
		_ => new SKColor(46, 86, 46),
	};

	private static void DrawWater(SKCanvas canvas, float top, SKColor skyTop, SKColor skyBottom, SKColor light, float sunX)
	{
		using var water = new SKPaint
		{
			Shader = SKShader.CreateLinearGradient(new SKPoint(0, top), new SKPoint(0, Height),
				new[] { Mix(skyBottom, new SKColor(30, 70, 110), 0.5f), Mix(skyTop, new SKColor(10, 30, 60), 0.6f) }, SKShaderTileMode.Clamp),
		};
		canvas.DrawRect(0, top, Width, Height - top, water);
		using var glint = new SKPaint { Color = light.WithAlpha(90), StrokeWidth = 3, IsAntialias = true };
		for (var y = top + 10; y < Height; y += 14)
			canvas.DrawLine(sunX - 40 + (y % 30), y, sunX + 40 - (y % 20), y, glint);
	}

	private static void DrawTrees(SKCanvas canvas, Random rng, SKColor color)
	{
		using var paint = new SKPaint { IsAntialias = true, Color = color };
		for (var i = 0; i < 14; i++)
		{
			var x = (float)rng.NextDouble() * Width;
			var h = 70 + (float)rng.NextDouble() * 110;
			var baseY = Height - (float)rng.NextDouble() * 40;
			using var tree = new SKPath();
			tree.MoveTo(x, baseY - h);
			tree.LineTo(x + h * 0.22f, baseY);
			tree.LineTo(x - h * 0.22f, baseY);
			tree.Close();
			canvas.DrawPath(tree, paint);
		}
	}

	private static void DrawCaption(SKCanvas canvas, string text)
	{
		using var font = new SKFont(SKTypeface.Default, 26);
		using var shade = new SKPaint { Color = new SKColor(0, 0, 0, 110) };
		canvas.DrawRect(0, Height - 54, Width, 54, shade);
		using var ink = new SKPaint { IsAntialias = true, Color = SKColors.White };
		canvas.DrawText(text, 20, Height - 18, SKTextAlign.Left, font, ink);
	}

	private static SKColor Mix(SKColor a, SKColor b, float t) => new(
		(byte)(a.Red + (b.Red - a.Red) * t),
		(byte)(a.Green + (b.Green - a.Green) * t),
		(byte)(a.Blue + (b.Blue - a.Blue) * t));

	private static void Save(SKImage image, string path, int quality)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		using var data = image.Encode(SKEncodedImageFormat.Jpeg, quality);
		using var file = File.Create(path);
		data.SaveTo(file);
	}
}
