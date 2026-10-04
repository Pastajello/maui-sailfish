using Microsoft.Maui.Accessibility;
using Microsoft.Maui.SailfishOS.Platform.QtHost;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// SemanticScreenReader.Announce: Sailfish OS has no screen reader, so the announcement goes nowhere, as on Android
/// with TalkBack off. The plain-net default throws instead, and an app announcing after a save (WhatToEat's New
/// Recipe) never got to its navigation.
/// </summary>
internal sealed class SailfishSemanticScreenReader : ISemanticScreenReader
{
	public void Announce(string text) =>
		QtHostDiag.Trace(QtHostDiagChannel.Lifecycle, $"screen reader announcement (no screen reader on Sailfish): '{text}'");
}
