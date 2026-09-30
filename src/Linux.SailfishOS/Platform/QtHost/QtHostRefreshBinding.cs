using System.ComponentModel;
using Microsoft.Maui.Controls;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>
/// The RefreshView armed on one scroll surface (page flickable, scroll-view host or list): owns the
/// PropertyChanged subscription and forwards refresh-surface changes on the Qt thread.
/// </summary>
internal sealed class QtHostRefreshBinding
{
	private PropertyChangedEventHandler? _handler;

	public RefreshView? View { get; private set; }

	/// <summary>Arms <paramref name="refresh"/> (null disarms); false when it was already armed.</summary>
	/// <param name="onChange">Runs on the Qt thread after IsRefreshing/IsRefreshEnabled/RefreshColor change.</param>
	/// <param name="suppressed">True while a native write-back is running, so its echo is not pushed back.</param>
	public bool Arm(RefreshView? refresh, Action<RefreshView> onChange, Func<bool>? suppressed = null)
	{
		if (ReferenceEquals(View, refresh))
			return false;
		Disarm();
		View = refresh;
		if (refresh is null)
			return true;
		_handler = (_, e) =>
		{
			if (!QtHostPageRenderer.IsRefreshSurfaceProperty(e.PropertyName) || suppressed?.Invoke() == true)
				return;
			QtHostRuntime.RunOnQtThread(() => onChange(refresh));
		};
		refresh.PropertyChanged += _handler;
		return true;
	}

	public void Disarm()
	{
		if (View is not null && _handler is not null)
			View.PropertyChanged -= _handler;
		_handler = null;
		View = null;
	}
}
