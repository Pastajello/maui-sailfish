namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>Qt::ApplicationState, as Sailfish reports it.</summary>
public enum SailfishApplicationState
{
	/// <summary>Not running any more, e.g. about to be killed.</summary>
	Suspended = 0,
	/// <summary>Not visible at all.</summary>
	Hidden = 1,
	/// <summary>Visible but not in front: minimized to a cover, or behind a system dialog.</summary>
	Inactive = 2,
	/// <summary>In front and receiving input.</summary>
	Active = 4,
}

/// <summary>Silica's Orientation flags for the current window orientation.</summary>
public enum SailfishOrientation
{
	Portrait = 1,
	Landscape = 2,
	PortraitInverted = 4,
	LandscapeInverted = 8,
}

/// <summary>Silica CoverBackground.status: the app's cover on the home screen.</summary>
public enum SailfishCoverStatus
{
	Inactive,
	Activating,
	Active,
	Deactivating,
}

/// <summary>Silica Theme.colorScheme: the ambience's text on background.</summary>
public enum SailfishColorScheme
{
	/// <summary>Light text on a dark ambience.</summary>
	LightOnDark,
	/// <summary>Dark text on a light ambience.</summary>
	DarkOnLight,
}

/// <summary>MCE display state (Nemo.Mce MceDisplay).</summary>
public enum SailfishDisplayState
{
	Off = 0,
	Dim = 1,
	On = 2,
}

/// <summary>MCE memory pressure level (sig_memory_level_ind), as Android's OnTrimMemory / iOS's memory warning.</summary>
public enum SailfishMemoryLevel
{
	Unknown,
	Normal,
	/// <summary>Free caches and anything you can rebuild.</summary>
	Warning,
	/// <summary>The system is about to kill background apps.</summary>
	Critical,
}
