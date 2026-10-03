using Microsoft.Maui.Controls;
using Microsoft.Maui.SailfishOS.Platform;
using Microsoft.Maui.SailfishOS.Platform.QtHost;
using Xunit;

namespace Linux.SailfishOS.Tests.Renderer;

/// <summary>Platform services over QtHostServices (HOP: callable from any thread, one Ensure per service).</summary>
[Collection("renderer")]
public class PlatformServiceThreadingTests
{
	// Without Nemo.Mce the battery used to stay at -1 / Unknown and retry (and warn about) the QML service on every
	// read; it now answers from the kernel's power_supply and never asks MCE again.
	[Fact]
	public void A_battery_without_mce_falls_back_to_the_kernel_once()
	{
		using var h = new RendererHarness(new ContentPage { Content = new Label { Text = "x" } });
		var serviceLoads = 0;
		h.Shim.EvalHook = js =>
		{
			if (!js.Contains("mauiService(\"battery\"", StringComparison.Ordinal))
				return null;
			serviceLoads++;
			return "error: module \"Nemo.Mce\" is not installed";
		};
		var battery = new SailfishBattery();

		_ = battery.ChargeLevel;
		_ = battery.State;
		_ = battery.PowerSource;

		Assert.Equal(1, serviceLoads);
		Assert.True(QtHostServices.IsUnavailable("battery"));
	}

	[Fact]
	public void QtThread_runs_inline_where_it_can_and_surfaces_exceptions_as_faulted_tasks()
	{
		using var h = new RendererHarness(new ContentPage { Content = new Label { Text = "x" } });
		Assert.True(QtThread.IsCurrent);
		Assert.Equal(42, QtThread.Run(() => 42));
		var failed = QtThread.RunAsync<int>(() => throw new InvalidOperationException("boom"));
		Assert.True(failed.IsFaulted);
	}
}

[Collection("renderer")]
public class StrictShimThreadTests
{
	// An off-thread shim call used to be logged and counted only, and went on to corrupt the QV4 heap.
	[Fact]
	public async Task A_shim_call_off_the_qt_thread_throws()
	{
		var shim = Microsoft.Maui.SailfishOS.Platform.QtHost.QtHostRuntime.TestShim;
		var loop = typeof(Microsoft.Maui.SailfishOS.Platform.QtHost.QtHostRuntime).GetField("_loopThreadId",
			System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
		var previousLoop = (int)loop.GetValue(null)!;
		Microsoft.Maui.SailfishOS.Platform.QtHost.QtHostRuntime.TestShim = null;
		loop.SetValue(null, Environment.CurrentManagedThreadId);
		try
		{
			var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(() =>
				Microsoft.Maui.SailfishOS.Platform.QtHost.QtHostRuntime.InjectPointer(0, 1, 1)));
			Assert.Contains("inject_pointer", error.Message);
		}
		finally
		{
			loop.SetValue(null, previousLoop);
			Microsoft.Maui.SailfishOS.Platform.QtHost.QtHostRuntime.TestShim = shim;
		}
	}
}
