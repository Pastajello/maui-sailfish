using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Microsoft.Maui.SailfishOS.Build.Tasks;

/// <summary>
/// Writes the Harbour launcher's rpath into the placeholder the native build reserved ("/__SAILFISH_LAUNCHER_RPATH__",
/// tools/sf native-build), so /usr/bin/&lt;package&gt; finds the package's own libraries.
/// </summary>
public class SailfishPatchLauncherRpath : Task
{
	[Required] public string File { get; set; } = string.Empty;
	[Required] public string Rpath { get; set; } = string.Empty;

	public override bool Execute()
	{
		var bytes = System.IO.File.ReadAllBytes(File);
		var marker = System.Text.Encoding.ASCII.GetBytes("/__SAILFISH_LAUNCHER_RPATH__");
		var at = -1;
		for (var i = 0; at < 0 && i + marker.Length <= bytes.Length; i++)
		{
		    var hit = true;
		    for (var j = 0; j < marker.Length; j++)
		        if (bytes[i + j] != marker[j]) { hit = false; break; }
		    if (hit) at = i;
		}
		if (at < 0) { Log.LogError("sailfish-launcher has no rpath placeholder (rebuild it with tools/sf native-build)"); return false; }
		var end = at;
		while (end < bytes.Length && bytes[end] != 0) end++;
		var value = System.Text.Encoding.ASCII.GetBytes(Rpath);
		if (value.Length > end - at) { Log.LogError("rpath '" + Rpath + "' longer than the launcher placeholder"); return false; }
		for (var i = at; i < end; i++) bytes[i] = 0;
		System.Array.Copy(value, 0, bytes, at, value.Length);
		System.IO.File.WriteAllBytes(File, bytes);
		return true;
	}
}
