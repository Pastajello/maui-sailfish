using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Maui;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>
/// Resolves a MAUI ConfigureFonts alias to its font file, registers it with Qt once and returns
/// the Qt family name; other names pass through. Qt thread only (QFontDatabase); off-thread callers
/// get the name unchanged.
/// </summary>
internal static class QtHostFonts
{
	private static readonly Dictionary<string, string> Families = new(StringComparer.Ordinal);

	/// <summary>The Qt family for a MAUI FontFamily value (null/empty stays empty).</summary>
	public static string Resolve(string? family)
	{
		if (string.IsNullOrEmpty(family))
			return string.Empty;
		if (Families.TryGetValue(family, out var known))
			return known;
		if (!QtHostRuntime.IsRunning || !QtHostRuntime.IsQtThread)
			return family;
		var resolved = family;
		var file = FontFile(family);
		if (file is not null && QtHostRuntime.RegisterFont(file) is { Length: > 0 } registered)
		{
			resolved = registered;
			QtHostTextMetrics.ClearCache();   // a family measured with Qt's fallback may now render with this font
		}
		Families[family] = resolved;
		QtHostDiag.Trace(QtHostDiagChannel.QmlProperty, $"font '{family}' → Qt family '{resolved}'{(file is null ? " (not an app font)" : $" ({file})")}");
		return resolved;
	}

	/// <summary>The font file behind an alias (or a file name given directly),
	/// or null when it is not an app font.</summary>
	private static string? FontFile(string family)
	{
		string? path = null;
		try
		{
			var registrar = QtHostPageRenderer.Current?.MauiContext.Services.GetService<IFontRegistrar>();
			path = registrar?.GetFont(family) ?? MauiRegistrarFile(registrar, family);
		}
		catch (Exception ex)
		{
			QtHostDiag.Warn(QtHostDiagChannel.QmlProperty, $"font registrar lookup for '{family}' failed: {ex.Message}");
		}
		// A "file.ttf#Family" style name, or the alias being the file itself.
		path ??= family.Contains('.') ? family.Split('#')[0] : null;
		if (path is null)
			return null;
		if (Path.IsPathRooted(path) && File.Exists(path))
			return path;
		var name = Path.GetFileName(path);
		foreach (var candidate in new[]
		{
			Path.Combine(AppContext.BaseDirectory, "fonts", name),
			Path.Combine(AppContext.BaseDirectory, name),
			Path.Combine(AppContext.BaseDirectory, "Resources", "Fonts", name),
		})
		{
			if (File.Exists(candidate))
				return candidate;
		}
		return null;
	}

	/// <summary>
	/// MAUI's own <see cref="FontRegistrar"/> returns null from GetFont here (no native loader), so its
	/// private registrations are read by reflection; failing that, the alias is matched to fonts/ by
	/// letters and digits ("OpenSansRegular" ↔ OpenSans-Regular.ttf).
	/// </summary>
	[DynamicDependency(DynamicallyAccessedMemberTypes.NonPublicFields, typeof(FontRegistrar))]
	[UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "FontRegistrar's fields are rooted by the DynamicDependency above.")]
	private static string? MauiRegistrarFile(IFontRegistrar? registrar, string family)
	{
		if (registrar is FontRegistrar maui)
		{
			try
			{
				var type = typeof(FontRegistrar);
				if (type.GetField("_nativeFonts", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(maui)
				        is Dictionary<string, (string Filename, string? Alias)> native
				    && native.TryGetValue(family, out var file))
					return file.Filename;
				if (type.GetField("_embeddedFonts", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(maui)
				        is Dictionary<string, (string Filename, string? Alias, Assembly Assembly)> embedded
				    && embedded.TryGetValue(family, out var font))
					return SailfishFontRegistrar.Extract(font.Filename, font.Assembly) ?? font.Filename;
			}
			catch (Exception ex)
			{
				QtHostDiag.Warn(QtHostDiagChannel.QmlProperty, $"MAUI font registrar read for '{family}' failed: {ex.Message}");
			}
		}
		var dir = Path.Combine(AppContext.BaseDirectory, "fonts");
		if (!Directory.Exists(dir))
			return null;
		var key = LettersAndDigits(family);
		foreach (var candidate in Directory.EnumerateFiles(dir))
			if (LettersAndDigits(Path.GetFileNameWithoutExtension(candidate)) == key)
				return candidate;
		return null;
	}

	private static string LettersAndDigits(string s) =>
		new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
}
