using System.Text;
using Microsoft.Maui.SailfishOS.Platform.QtHost;
using Xunit;

namespace Linux.SailfishOS.Tests.Renderer;

/// <summary>Large payloads (a 10,000-row list's rows JSON) cross to the shim as heap UTF-8: the runtime's string
/// marshaller copied them onto the 2 MB main-thread stack and overflowed it on the phone.</summary>
public class LargeNativeArgTests
{
	[Fact]
	public void Utf8z_is_the_utf8_text_with_a_terminator()
	{
		var bytes = QtHostNative.Utf8z("Kuźnice → Kasprowy");
		Assert.Equal(0, bytes[^1]);
		Assert.Equal("Kuźnice → Kasprowy", Encoding.UTF8.GetString(bytes, 0, bytes.Length - 1));
	}

	[Fact]
	public void Only_long_payloads_take_the_heap_path()
	{
		Assert.False(QtHostNative.IsLarge(null));
		Assert.False(QtHostNative.IsLarge(new string('x', QtHostNative.LargeArgChars)));
		Assert.True(QtHostNative.IsLarge(new string('x', QtHostNative.LargeArgChars + 1)));
	}
}
