using Microsoft.Maui.SailfishOS.Platform;
using Xunit;

namespace Linux.SailfishOS.Tests;

/// <summary>Every MAUI_SAILFISH_* switch parses the same way: flags are exactly "1".</summary>
public class SailfishEnvTests
{
	private const string Name = "MAUI_SAILFISH_TEST_ENV_PROBE";

	[Theory]
	[InlineData("1", true)]
	[InlineData("0", false)]
	[InlineData("true", false)]
	[InlineData(" 1", false)]
	[InlineData(null, false)]
	public void Flag_is_exactly_one(string? value, bool expected)
	{
		Environment.SetEnvironmentVariable(Name, value);
		try { Assert.Equal(expected, SailfishEnv.Flag(Name)); }
		finally { Environment.SetEnvironmentVariable(Name, null); }
	}

	[Theory]
	[InlineData("250", 250)]
	[InlineData("-3", -3)]
	[InlineData("abc", null)]
	[InlineData(null, null)]
	public void Int_parses_or_returns_null(string? value, int? expected)
	{
		Environment.SetEnvironmentVariable(Name, value);
		try { Assert.Equal(expected, SailfishEnv.Int(Name)); }
		finally { Environment.SetEnvironmentVariable(Name, null); }
	}

	[Fact]
	public void IsSet_needs_a_non_empty_value()
	{
		Environment.SetEnvironmentVariable(Name, "x");
		try { Assert.True(SailfishEnv.IsSet(Name)); }
		finally { Environment.SetEnvironmentVariable(Name, null); }
		Assert.False(SailfishEnv.IsSet(Name));
	}
}

public class DevTapsTests
{
	[Fact]
	public void Tap_specs_parse_and_skip_malformed_entries()
	{
		var inputs = SailfishDevTaps.Parse("2000:455,1950; bad ;3500:10.5,20;4000:x,1;5000:900,400>100,400;6000:\"Rent; May\"");
		Assert.Equal(new[]
		{
			new SailfishDevTaps.DevInput(2000, SailfishDevTaps.Kind.Tap, 455, 1950),
			new SailfishDevTaps.DevInput(3500, SailfishDevTaps.Kind.Tap, 10.5, 20),
			new SailfishDevTaps.DevInput(5000, SailfishDevTaps.Kind.Drag, 900, 400, 100, 400),
		}, inputs.Take(3));
		Assert.Empty(SailfishDevTaps.Parse(null));
		// ';' separates entries, so typed text cannot contain one: the broken halves are skipped.
		Assert.Equal(3, inputs.Count);
		Assert.Equal("Bills", SailfishDevTaps.Parse("1:\"Bills\"").Single().Text);
		Assert.Equal(1500, SailfishDevTaps.Parse("1:516,600>516,1400@1500").Single().DurationMs);
	}
}
