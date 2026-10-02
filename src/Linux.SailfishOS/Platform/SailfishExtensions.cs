using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// Sailfish extensions of libraries: a package that adds Sailfish support for a library (handlers, image sources)
/// names its registrar in the app assembly with
/// <c>[assembly: AssemblyMetadata("Microsoft.Maui.SailfishOS.Extension", "Namespace.Type, Assembly")]</c>, written by
/// its buildTransitive targets. The backend calls the type's <c>public static void Register()</c> once, before
/// <c>CreateMauiApp</c>, so the app needs no code. The package's targets also root its assembly for trimming.
/// </summary>
public static class SailfishExtensions
{
	/// <summary>The metadata key of an extension registrar.</summary>
	public const string MetadataKey = "Microsoft.Maui.SailfishOS.Extension";

	private static readonly HashSet<string> Loaded = new(StringComparer.Ordinal);

	/// <summary>Registrars that ran, by type name (diagnostics).</summary>
	public static IReadOnlyCollection<string> Registered => Loaded;

	[UnconditionalSuppressMessage("Trimming", "IL2057", Justification = "Extension packages root their assembly.")]
	[UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Extension packages root their assembly.")]
	internal static void Load(Assembly appAssembly)
	{
		foreach (var assembly in new[] { appAssembly, Assembly.GetEntryAssembly() })
		{
			if (assembly is null)
				continue;
			foreach (var metadata in assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
			{
				if (metadata.Key != MetadataKey || string.IsNullOrWhiteSpace(metadata.Value) || !Loaded.Add(metadata.Value))
					continue;
				try
				{
					var type = Type.GetType(metadata.Value, throwOnError: true)!;
					var register = type.GetMethod("Register", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes)
						?? throw new MissingMethodException(type.FullName, "Register");
					register.Invoke(null, null);
					Console.Error.WriteLine($"[Sailfish] extension registered: {metadata.Value}");
				}
				catch (Exception ex)
				{
					Console.Error.WriteLine($"[Sailfish][WARN] extension '{metadata.Value}' could not be registered: {(ex as TargetInvocationException)?.InnerException?.Message ?? ex.Message}");
				}
			}
		}
	}
}
