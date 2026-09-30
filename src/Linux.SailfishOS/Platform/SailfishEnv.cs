namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// Reads the MAUI_SAILFISH_* switches from the environment, so every flag parses the same way.
/// </summary>
internal static class SailfishEnv
{
	public static string? Get(string name) => Environment.GetEnvironmentVariable(name);

	/// <summary>True when the variable is exactly "1".</summary>
	public static bool Flag(string name) => string.Equals(Get(name), "1", StringComparison.Ordinal);

	/// <summary>True when the variable is set to any non-empty value.</summary>
	public static bool IsSet(string name) => !string.IsNullOrEmpty(Get(name));

	/// <summary>The variable as an integer; null when unset or not a number.</summary>
	public static int? Int(string name) => int.TryParse(Get(name), out var value) ? value : null;
}
