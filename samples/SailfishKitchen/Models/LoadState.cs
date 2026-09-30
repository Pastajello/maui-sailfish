namespace SailfishKitchen.Models;

/// <summary>Mutually exclusive load states rendered by <see cref="SailfishKitchen.Controls.StateSlot"/>.</summary>
public enum LoadState
{
	/// <summary>Data is on screen; the slot contributes nothing.</summary>
	Content,

	/// <summary>First load in flight with nothing to show yet.</summary>
	Loading,

	/// <summary>The load succeeded and the result set is empty.</summary>
	Empty,

	/// <summary>The load failed; the slot shows the message and retry.</summary>
	Error,
}
