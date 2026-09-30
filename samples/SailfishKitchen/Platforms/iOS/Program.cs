using UIKit;

namespace SailfishKitchen;

/// <summary>
/// Native entry point for the iOS leg; the root <c>Program.cs</c> (Sailfish) is excluded from this TFM in the csproj.
/// </summary>
public static class Program
{
	private static void Main(string[] args) => UIApplication.Main(args, null, typeof(AppDelegate));
}
