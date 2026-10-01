using System;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Internals;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// Named font sizes (FontSize="Large" in XAML, Device.GetNamedSize) in dp. MAUI resolves them through DependencyService
/// while a page's InitializeComponent runs, before Qt is up, so the values are fixed: Android's table, keeping layouts
/// written for iOS/Android the same size here. None of them is SailfishFontManager.DefaultSize, which reads as unset.
/// </summary>
#pragma warning disable CS0612, CS0618 // NamedSize is obsolete, but XAML written for older MAUI still resolves it
internal sealed class SailfishFontNamedSizeService : IFontNamedSizeService
{
	public double GetNamedSize(NamedSize size, Type targetElementType, bool useOldSizes) => size switch
	{
		NamedSize.Micro => 10,
		NamedSize.Small => 14,
		NamedSize.Medium => 17,
		NamedSize.Large => 22,
		NamedSize.Body => 16,
		NamedSize.Caption => 12,
		NamedSize.Subtitle => 16,
		NamedSize.Title => 24,
		NamedSize.Header => 96,
		_ => 14,
	};
}
#pragma warning restore CS0612, CS0618
