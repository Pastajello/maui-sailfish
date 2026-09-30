// Compiled into the app when SailfishGenerateMain=true: finds a public static CreateMauiApp() and runs it.
internal static class SailfishGeneratedEntryPoint
{
	private static void Main(string[] args)
	{
		new Microsoft.Maui.SailfishOS.Hosting.SailfishMauiApplicationHost(Discover()).Run(args);
	}

	private static System.Func<Microsoft.Maui.Hosting.MauiApp> Discover()
	{
		var asm = typeof(SailfishGeneratedEntryPoint).Assembly;
		foreach (var type in asm.GetTypes())
		{
			var method = type.GetMethod("CreateMauiApp",
				System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
				binder: null, types: System.Type.EmptyTypes, modifiers: null);
			if (method is not null && typeof(Microsoft.Maui.Hosting.MauiApp).IsAssignableFrom(method.ReturnType))
				return (System.Func<Microsoft.Maui.Hosting.MauiApp>)method.CreateDelegate(typeof(System.Func<Microsoft.Maui.Hosting.MauiApp>));
		}
		throw new System.InvalidOperationException(
			"SailfishGenerateMain: no public static CreateMauiApp() returning MauiApp found in " +
			asm.GetName().Name + ". Add Platforms/SailfishOS/Program.cs with your own Main (see dotnet new maui-sailfish-platform).");
	}
}
