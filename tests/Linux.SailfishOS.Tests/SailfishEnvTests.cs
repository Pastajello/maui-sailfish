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
