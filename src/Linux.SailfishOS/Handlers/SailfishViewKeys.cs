namespace Microsoft.Maui.SailfishOS.Handlers;

/// <summary>View-level mapper keys the handlers treat specially. (The handler-parity tables live in the test project,
/// HandlerParityTests: W2.4 moved them out of the production assembly.)</summary>
internal static class SailfishViewKeys
{
	/// <summary>Text-input properties the handlers leave out of their snapshots, so native focus and caret survive the
	/// reconcile poll; their mappers push them on their own (QtHostPageRenderer.PushTransient).</summary>
	internal static readonly string[] TransientInput =
		{ nameof(Microsoft.Maui.Controls.VisualElement.IsFocused), nameof(Microsoft.Maui.Controls.InputView.CursorPosition),
		  nameof(Microsoft.Maui.Controls.InputView.SelectionLength) };
}
