namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>
/// Builds the JS that addresses a model page (MauiModelPage.qml) and calls its entry points.
/// Hosts must be created and destroyed on the page instance that owns them, so every eval goes through here.
/// </summary>
internal static class QmlPage
{
	/// <summary>The model page on top. While a Silica Dialog is on the pageStack, currentPage is the dialog,
	/// so the shell exposes the model page as mauiModelPage.</summary>
	public const string Model =
		"(typeof window!=='undefined'&&window.mauiModelPage?window.mauiModelPage:pageStack.currentPage)";

	/// <summary>A page instance by its shell registry id; null in JS once that page is gone.</summary>
	public static string ById(string pageId) => $"window.mauiPageById('{pageId}')";

	/// <summary>A page by id, or <paramref name="fallbackJs"/> when the registry no longer knows it.</summary>
	public static string ByIdOr(string pageId, string fallbackJs) => $"({ById(pageId)}||{fallbackJs})";

	/// <summary>Calls <c>page.fn(argJs)</c> when both the page and the function exist; a no-op otherwise.</summary>
	/// <param name="argJs">Raw JS argument text, e.g. a <see cref="BridgeValue.Quote"/>d JSON string.</param>
	public static string Call(string pageJs, string fn, string argJs = "") =>
		$"(function(){{var p={pageJs};if(p&&p.{fn})p.{fn}({argJs});}})()";
}
