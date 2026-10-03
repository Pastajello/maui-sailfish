using System.Reflection;
using Microsoft.Maui;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// IFontRegistrar: GetFont returns the file behind an alias for Qt's font database. File fonts resolve to fonts/ next
/// to the binary; embedded fonts are extracted once to the user cache.
/// </summary>
internal sealed class SailfishFontRegistrar : IFontRegistrar
{
	private readonly Dictionary<string, string> _fonts = new(StringComparer.Ordinal);

	public void Register(string filename, string? alias)
	{
		_fonts[filename] = filename;
		if (!string.IsNullOrEmpty(alias))
			_fonts[alias] = filename;
	}

	public void Register(string filename, string? alias, Assembly assembly)
	{
		var path = Extract(filename, assembly) ?? filename;
		_fonts[filename] = path;
		if (!string.IsNullOrEmpty(alias))
			_fonts[alias] = path;
	}

	public string? GetFont(string font) => _fonts.TryGetValue(font, out var file) ? file : null;

	internal static string? Extract(string filename, Assembly assembly)
	{
		try
		{
			var resource = assembly.GetManifestResourceNames()
				.FirstOrDefault(n => n.EndsWith(filename, StringComparison.OrdinalIgnoreCase));
			if (resource is null)
				return null;
			var dir = SailfishAppPaths.Cache("fonts");
			var path = Path.Combine(dir, filename);
			if (!File.Exists(path))
			{
				using var stream = assembly.GetManifestResourceStream(resource);
				if (stream is null)
					return null;
				using var file = File.Create(path);
				stream.CopyTo(file);
			}
			return path;
		}
		catch
		{
			return null;
		}
	}
}
