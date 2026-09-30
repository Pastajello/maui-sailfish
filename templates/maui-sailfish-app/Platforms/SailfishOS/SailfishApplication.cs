using Microsoft.Maui.SailfishOS.Platform;

namespace MauiSailfishApp;

// The Sailfish OS counterpart of AppDelegate (iOS) and MainActivity (Android): builds the shared app and receives
// the native Sailfish/Qt events. Shared code can subscribe too, through
// builder.ConfigureLifecycleEvents(events => events.AddSailfish(sf => sf.OnQuitting(app => ...))).
// MAUI's Window.Activated/Deactivated/Resumed/Stopped fire as on the other platforms.
public class SailfishApplication : SailfishMauiApplication
{
	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	// protected override void OnLaunched(string[] arguments) { }                       // window exists, loop starts next
	// protected override void OnApplicationStateChanged(SailfishApplicationState state) { } // Active / Inactive (cover) / …
	// protected override void OnOrientationChanged(SailfishOrientation orientation) { }
	// protected override void OnCoverStatusChanged(SailfishCoverStatus status) { }     // the home-screen cover
	// protected override void OnCoverActionTriggered(int index) { }                    // SailfishCover.SetActions
	// protected override void OnColorSchemeChanged(SailfishColorScheme scheme) { }     // light/dark ambience
	// protected override void OnInputMethodChanged(bool visible, Rect keyboard) { }    // virtual keyboard
	// protected override void OnQuitting() { }                                         // save state: the app quits next
}
