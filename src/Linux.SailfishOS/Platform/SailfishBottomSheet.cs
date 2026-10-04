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

	/// <summary>Dock edge; the bottom by default.</summary>
	public SailfishDockEdge Dock { get; set; } = SailfishDockEdge.Bottom;

	/// <summary>Panel extent in Qt scene units along the dock axis; 0 uses the adapter default.</summary>
	public double Size { get; set; }

	/// <summary>Open state, updated by native changes such as a user drag.</summary>
	public bool IsOpen { get; private set; }

	/// <summary>Raised when the native panel open state changes.</summary>
	public event EventHandler<bool>? OpenChanged;

	private QtHostPageRenderer? _listening;   // the renderer whose PanelOpenChanged this sheet listens to

	private static QtHostPageRenderer Renderer => SailfishRenderSession.OfApp?.Renderer
		?? throw new InvalidOperationException(
			"SailfishBottomSheet requires the Qt host (MAUI_SAILFISH_QT_HOST=1) with an active page renderer.");

	/// <summary>Creates or re-opens the panel with the current properties.</summary>
	public void Show() => QtThread.Run(() =>
	{
		var renderer = Renderer;
		if (_hostId is null)
		{
			renderer.PanelOpenChanged += OnNativePanelOpenChanged;
			_listening = renderer;
			_hostId = renderer.AddInteractionHost("docked-panel", Props(open: true));
		}
		else
		{
			renderer.UpdateInteractionHost(_hostId, Props(open: true));
		}
		IsOpen = true;
	});

	/// <summary>Slides the panel closed, keeping the host for a later <see cref="Show"/>.</summary>
	public void Hide() => QtThread.Run(() =>
	{
		if (_hostId is null)
			return;
		Renderer.UpdateInteractionHost(_hostId, Props(open: false));
		IsOpen = false;
	});

	/// <summary>Pushes changed properties to the live panel in place.</summary>
	public void Update() => QtThread.Run(() =>
	{
		if (_hostId is not null)
			Renderer.UpdateInteractionHost(_hostId, Props(open: IsOpen));
	});

	/// <summary>Releases the native host; the panel is destroyed on the next reconcile.</summary>
	public void Close() => QtThread.Run(() =>
	{
		if (_hostId is null)
			return;
		SailfishRenderSession.OfApp?.Renderer?.RemoveInteractionHost(_hostId);
		// The renderer it subscribed to, even when the session has no renderer any more (W1.10: the handler stayed).
		if (_listening is { } subscribed)
			subscribed.PanelOpenChanged -= OnNativePanelOpenChanged;
		_listening = null;
		_hostId = null;
		IsOpen = false;
	});

	private Dictionary<string, object?> Props(bool open) => new()
	{
		["mauiDock"] = Dock switch
		{
			SailfishDockEdge.Top => "top",
			SailfishDockEdge.Left => "left",
			SailfishDockEdge.Right => "right",
			_ => "bottom",
		},
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