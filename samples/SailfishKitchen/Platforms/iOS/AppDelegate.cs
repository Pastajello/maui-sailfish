using Foundation;
using Microsoft.Maui.Hosting;

namespace SailfishKitchen;

/// <summary>
/// Hands the shared <see cref="MauiProgram.CreateMauiApp"/> to UIKit; only the hosting extension differs from the Sailfish leg.
/// </summary>
[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
