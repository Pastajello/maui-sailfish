namespace MauiSailfishApp;

public static class Program
{
	// Sailfish OS entry point, the counterpart of Platforms/iOS/Program.cs: runs SailfishApplication on the
	// Qt/Silica host.
	private static void Main(string[] args) => new SailfishApplication().Run(args);
}
