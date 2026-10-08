using Microsoft.Maui.Controls;

namespace Microsoft.Maui.Controls.PlatformConfiguration;

/// <summary>
/// The Sailfish OS platform for MAUI's platform-specific API, as <c>Android</c>, <c>iOS</c> and <c>Windows</c>:
/// <c>page.On&lt;SailfishOS&gt;().SetAllowedOrientations(…)</c> (decision D13, tracker S47).
/// </summary>
public sealed class SailfishOS : IConfigPlatform
{
}
