using Microsoft.Maui.SailfishOS.Platform.QtHost;
using Xunit;

namespace Linux.SailfishOS.Tests;

/// <summary>The diagnostics mirror lives in the phone's /tmp, which is RAM: it appended across every run until it
/// held 335 MB.</summary>
[Collection("console")]
public class DiagMirrorTests
{
	private static void WithMirror(long cap, Action<string> run)
	{
		var path = Path.Combine(Path.GetTempPath(), $"sf-diag-mirror-{Guid.NewGuid():N}.log");
		var original = Console.Error;
		Console.SetError(TextWriter.Null);
		try
		{
			File.WriteAllText(path, "left over from an earlier run\n");
			QtHostDiag.RestartMirror(path, cap);
			run(path);
		}
		finally
		{
			Console.SetError(original);
			QtHostDiag.RestartMirror("/tmp/kitchen-diag.log", 32L * 1024 * 1024);
			File.Delete(path);
		}
	}

	private static string Read(string path)
	{
		using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		return new StreamReader(stream).ReadToEnd();
	}

	[Fact]
	public void A_process_starts_the_mirror_afresh() =>
		WithMirror(1024 * 1024, path =>
		{
			QtHostDiag.Warn(QtHostDiagChannel.QtHost, "mirror-first");
			QtHostDiag.Warn(QtHostDiagChannel.QtHost, "mirror-second");

			var text = Read(path);
			Assert.DoesNotContain("left over", text);
			Assert.Contains("[Sailfish][QT_HOST][WARN] mirror-first", text);
			Assert.Contains("[Sailfish][QT_HOST][WARN] mirror-second", text);
		});

	[Fact]
	public void The_mirror_stops_at_its_cap() =>
		WithMirror(4096, path =>
		{
			for (var i = 0; i < 200; i++)
				QtHostDiag.Warn(QtHostDiagChannel.QtHost, $"mirror-line {i} {new string('x', 40)}");

			var text = Read(path);
			Assert.InRange(new FileInfo(path).Length, 1, 4096 + 200);
			Assert.Contains("mirror-line 0 ", text);
			Assert.DoesNotContain("mirror-line 199 ", text);
			Assert.Contains("diagnostics mirror stopped", text);
		});
}
