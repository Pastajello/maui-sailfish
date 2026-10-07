using Microsoft.Maui.SailfishOS.Platform.QtHost;
using Xunit;

namespace Linux.SailfishOS.Tests;

/// <summary>
/// Tracker S03 (plan M1): the window lifecycle as Android raises it. Going to the cover (Qt.application.state Inactive)
/// is Deactivated then Stopped (OnSleep), coming back is Resumed (OnResume) then Activated, startup raises no Resumed,
/// and an unknown state (-1) changes nothing.
/// </summary>
public sealed class ActivationGateTests
{
	// Qt.ApplicationState values.
	private const int Suspended = 0, Hidden = 1, Inactive = 2, Active = 4;

	private sealed class Recorder
	{
		public readonly List<string> Events = new();
		public readonly ActivationGate Gate;

		public Recorder() => Gate = new ActivationGate((_, _, kind) => Events.Add(kind switch
		{
			1 => "Activated",
			2 => "Deactivated",
			3 => "Resumed",
			4 => "Stopped",
			_ => "?" + kind,
		}));
	}

	[Fact]
	public void Startup_raises_Activated_only()
	{
		var r = new Recorder();
		r.Gate.Observe(active: true, Active);
		Assert.Equal(new[] { "Activated" }, r.Events);
	}

	[Fact]
	public void A_cover_round_trip_raises_sleep_and_resume_in_Android_order()
	{
		var r = new Recorder();
		r.Gate.Observe(true, Active);
		r.Events.Clear();

		r.Gate.Observe(false, Inactive);   // minimized to the cover
		Assert.Equal(new[] { "Deactivated", "Stopped" }, r.Events);
		r.Events.Clear();

		r.Gate.Observe(true, Active);      // back from the cover
		Assert.Equal(new[] { "Resumed", "Activated" }, r.Events);
	}

	[Fact]
	public void Going_deeper_while_stopped_raises_no_second_Stopped()
	{
		var r = new Recorder();
		r.Gate.Observe(true, Active);
		r.Gate.Observe(false, Inactive);
		r.Gate.Observe(false, Hidden);
		r.Gate.Observe(false, Suspended);
		Assert.Equal(1, r.Events.Count(e => e == "Stopped"));
		r.Gate.Observe(true, Active);
		Assert.Equal(1, r.Events.Count(e => e == "Resumed"));
	}

	[Fact]
	public void Suspended_straight_from_Active_stops_once()
	{
		var r = new Recorder();
		r.Gate.Observe(true, Active);
		r.Events.Clear();
		r.Gate.Observe(true, Suspended);
		Assert.Equal(new[] { "Stopped" }, r.Events);
	}

	[Fact]
	public void An_unknown_state_neither_stops_nor_hides_the_next_transition()
	{
		var r = new Recorder();
		r.Gate.Observe(true, -1);          // Qt.application not readable in the first snapshot
		Assert.Equal(new[] { "Activated" }, r.Events);
		r.Gate.Observe(true, Active);
		r.Events.Clear();
		r.Gate.Observe(true, -1);
		Assert.Empty(r.Events);
		r.Gate.Observe(false, Inactive);
		Assert.Equal(new[] { "Deactivated", "Stopped" }, r.Events);
		Assert.Equal(Inactive, r.Gate.LastAppState);
	}

	[Fact]
	public void An_app_that_starts_inactive_is_not_stopped_before_it_ever_ran()
	{
		var r = new Recorder();
		r.Gate.Observe(false, Inactive);   // a system dialog in front at launch
		Assert.Empty(r.Events);
		r.Gate.Observe(true, Active);
		Assert.Equal(new[] { "Activated" }, r.Events);
	}

	// Tracker S04: quitting sends what Android sends before onDestroy, once each and only what is still owed.
	[Fact]
	public void Quitting_an_active_app_deactivates_and_stops_it_once()
	{
		var r = new Recorder();
		r.Gate.Observe(true, Active);
		r.Events.Clear();
		r.Gate.Quit();
		r.Gate.Quit();
		Assert.Equal(new[] { "Deactivated", "Stopped" }, r.Events);
	}

	[Fact]
	public void Quitting_from_the_cover_owes_nothing_more()
	{
		var r = new Recorder();
		r.Gate.Observe(true, Active);
		r.Gate.Observe(false, Inactive);
		r.Events.Clear();
		r.Gate.Quit();
		Assert.Empty(r.Events);
	}
}
