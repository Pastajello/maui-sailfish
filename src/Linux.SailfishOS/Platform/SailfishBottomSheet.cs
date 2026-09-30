using Microsoft.Maui.SailfishOS.Platform.QtHost;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// A bottom sheet over the Silica DockedPanel (MAUI has no cross-platform one). It lives outside the MAUI tree,
/// so it never affects layout; user drags write back to <see cref="IsOpen"/>.
/// <code>
/// var sheet = new SailfishBottomSheet { Text = "Details", Size = 320 };
/// sheet.Show();                          // native panel slides in
/// sheet.OpenChanged += (_, open) => …;   // user-drag write-back
/// sheet.Hide();                          // slides out (host stays alive)
/// sheet.Close();                         // releases the native host
/// </code>
/// </summary>
public sealed class SailfishBottomSheet : IDisposable
{
	private string? _hostId;

	/// <summary>Label text shown in the panel.</summary>
	public string Text { get; set; } = string.Empty;

	/// <summary>Dock edge: "bottom" (default), "top", "left" or "right".</summary>
	public string Dock { get; set; } = "bottom";

	/// <summary>Panel extent in Qt scene units along the dock axis; 0 uses the adapter default.</summary>
	public double Size { get; set; }

	/// <summary>Open state, updated by native changes such as a user drag.</summary>
	public bool IsOpen { get; private set; }

	/// <summary>Raised when the native panel open state changes.</summary>
	public event EventHandler<bool>? OpenChanged;

	private static QtHostPageRenderer Renderer => QtHostPageRenderer.Current
		?? throw new InvalidOperationException(
			"SailfishBottomSheet requires the Qt host (MAUI_SAILFISH_QT_HOST=1) with an active page renderer.");

	/// <summary>Creates or re-opens the panel with the current properties.</summary>
	public void Show()
	{
		var renderer = Renderer;
		if (_hostId is null)
		{
			renderer.PanelOpenChanged += OnNativePanelOpenChanged;
			_hostId = renderer.AddInteractionHost("docked-panel", Props(open: true));
		}
		else
		{
			renderer.UpdateInteractionHost(_hostId, Props(open: true));
		}
		IsOpen = true;
	}

	/// <summary>Slides the panel closed, keeping the host for a later <see cref="Show"/>.</summary>
	public void Hide()
	{
		if (_hostId is null)
			return;
		Renderer.UpdateInteractionHost(_hostId, Props(open: false));
		IsOpen = false;
	}

	/// <summary>Pushes changed properties to the live panel in place.</summary>
	public void Update()
	{
		if (_hostId is not null)
			Renderer.UpdateInteractionHost(_hostId, Props(open: IsOpen));
	}

	/// <summary>Releases the native host; the panel is destroyed on the next reconcile.</summary>
	public void Close()
	{
		if (_hostId is null)
			return;
		if (QtHostPageRenderer.Current is { } renderer)
		{
			renderer.RemoveInteractionHost(_hostId);
			renderer.PanelOpenChanged -= OnNativePanelOpenChanged;
		}
		_hostId = null;
		IsOpen = false;
	}

	private Dictionary<string, object?> Props(bool open) => new()
	{
		["mauiDock"] = Dock,
		["mauiOpen"] = open,
		["mauiSize"] = Size,
		["mauiText"] = Text,
	};

	private void OnNativePanelOpenChanged(string id, bool open)
	{
		if (id != _hostId)
			return;
		IsOpen = open;
		OpenChanged?.Invoke(this, open);
	}

	public void Dispose() => Close();
}