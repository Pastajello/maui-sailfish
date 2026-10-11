namespace Trailhead;

/// <summary>
/// The build flavor: Full (the default) or Harbour (-p:SailfishHarbour=true, the Jolla Store build). They differ in the
/// Sailjail permissions only (the Store allows a shorter list, see Trailhead.csproj); the map and the camera are on the
/// Store's allowed lists and ship in both.
/// </summary>
public static class AppFlavor
{
#if HARBOUR
	public const bool IsHarbour = true;
	public const string Name = "Harbour";
#else
	public const bool IsHarbour = false;
	public const string Name = "Full";
#endif

	public const string Title = "Trailhead";
}
