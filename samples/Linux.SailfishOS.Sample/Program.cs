using Microsoft.Maui.SailfishOS.Platform;

namespace Microsoft.Maui.SailfishOS.Sample;

public class Program : SailfishMauiApplication
{
	protected override Microsoft.Maui.Hosting.MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	public static void Main(string[] args)
	{
		System.IO.File.AppendAllText("/tmp/maui_trace.log", $"[Program.Main] entered {System.DateTime.Now:HH:mm:ss.fff}\n");
		try
		{
			var app = new Program();
			System.IO.File.AppendAllText("/tmp/maui_trace.log", "[Program.Main] calling app.Run\n");
			app.Run(args);
			System.IO.File.AppendAllText("/tmp/maui_trace.log", "[Program.Main] app.Run returned\n");
		}
		catch (System.Exception ex)
		{
			System.IO.File.AppendAllText("/tmp/maui_trace.log", $"[Program.Main] EXCEPTION: {ex}\n");
			throw;
		}
	}
}