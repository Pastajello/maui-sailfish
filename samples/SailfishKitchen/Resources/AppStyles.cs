using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace SailfishKitchen.Resources;

/// <summary>
/// The app's styles, built in C# because the app has no App.xaml and a merged <c>ResourceDictionary Source=…</c>
/// relies on a XAML loader path this backend exercises less.
/// </summary>
public static class AppStyles
{
	public const string Card = "Card";
	public const string PrimaryButton = "PrimaryButton";
	public const string SecondaryButton = "SecondaryButton";
	public const string DangerButton = "DangerButton";
	public const string IconButton = "IconButton";
	public const string PageTitle = "PageTitle";
	public const string SectionHeader = "SectionHeader";
	public const string Body = "Body";
	public const string Caption = "Caption";
	public const string Muted = "Muted";
	public const string Chip = "Chip";
	public const string HeroImage = "HeroImage";
	public const string ThumbImage = "ThumbImage";

	/// <summary>
	/// Installs every style into <paramref name="target"/>. Implicit styles must go through <c>Add(Style)</c>;
	/// adding them as keyed entries would silently stop them applying.
	/// </summary>
	public static void Populate(ResourceDictionary target)
	{
		ArgumentNullException.ThrowIfNull(target);

		foreach (var style in CreateStyles())
			target.Add(style);

		foreach (var (key, value) in CreateKeyed())
			target.Add(key, value);
	}

	/// <summary>Implicit styles: applied to every instance of their target type.</summary>
	public static IEnumerable<Style> CreateStyles()
	{
		yield return new Style(typeof(Page))
		{
			ApplyToDerivedTypes = true,
			Setters = { new Setter { Property = Page.BackgroundColorProperty, Value = Palette.Background } },
		};

		yield return new Style(typeof(Label))
		{
			ApplyToDerivedTypes = true,
			Setters =
			{
				new Setter { Property = Label.TextColorProperty, Value = Palette.TextPrimary },
				new Setter { Property = Label.FontSizeProperty, Value = 17d },
			},
		};

		yield return ButtonBase;

		yield return new Style(typeof(Entry))
		{
			ApplyToDerivedTypes = true,
			Setters =
			{
				new Setter { Property = Entry.TextColorProperty, Value = Palette.TextPrimary },
				new Setter { Property = Entry.PlaceholderColorProperty, Value = Palette.TextMuted },
				new Setter { Property = Entry.BackgroundColorProperty, Value = Palette.Surface },
				new Setter { Property = Entry.FontSizeProperty, Value = 17d },
			},
		};

		yield return new Style(typeof(SearchBar))
		{
			ApplyToDerivedTypes = true,
			Setters =
			{
				new Setter { Property = SearchBar.TextColorProperty, Value = Palette.TextPrimary },
				new Setter { Property = SearchBar.PlaceholderColorProperty, Value = Palette.TextMuted },
				new Setter { Property = SearchBar.BackgroundColorProperty, Value = Palette.Surface },
				new Setter { Property = SearchBar.CancelButtonColorProperty, Value = Palette.Accent },
			},
		};

		yield return new Style(typeof(ActivityIndicator))
		{
			ApplyToDerivedTypes = true,
			Setters = { new Setter { Property = ActivityIndicator.ColorProperty, Value = Palette.Accent } },
		};

		yield return new Style(typeof(ProgressBar))
		{
			ApplyToDerivedTypes = true,
			Setters = { new Setter { Property = ProgressBar.ProgressColorProperty, Value = Palette.Accent } },
		};

		yield return new Style(typeof(Slider))
		{
			ApplyToDerivedTypes = true,
			Setters =
			{
				new Setter { Property = Slider.MinimumTrackColorProperty, Value = Palette.Accent },
				new Setter { Property = Slider.MaximumTrackColorProperty, Value = Palette.Border },
				new Setter { Property = Slider.ThumbColorProperty, Value = Palette.Accent },
			},
		};

		yield return new Style(typeof(Switch))
		{
			ApplyToDerivedTypes = true,
			Setters = { new Setter { Property = Switch.OnColorProperty, Value = Palette.AccentDim } },
		};

		yield return new Style(typeof(Picker))
		{
			ApplyToDerivedTypes = true,
			Setters =
			{
				new Setter { Property = Picker.TextColorProperty, Value = Palette.TextPrimary },
				new Setter { Property = Picker.BackgroundColorProperty, Value = Palette.Surface },
				new Setter { Property = Picker.TitleColorProperty, Value = Palette.TextMuted },
			},
		};
	}

	/// <summary>The base every keyed button variant inherits from.</summary>
	private static readonly Style ButtonBase = new(typeof(Button))
	{
		ApplyToDerivedTypes = true,
		Setters =
		{
			new Setter { Property = Button.BackgroundColorProperty, Value = Palette.SurfaceRaised },
			new Setter { Property = Button.TextColorProperty, Value = Palette.TextPrimary },
			new Setter { Property = Button.BorderColorProperty, Value = Palette.Border },
			new Setter { Property = Button.BorderWidthProperty, Value = 1d },
			new Setter { Property = Button.CornerRadiusProperty, Value = 10 },
			new Setter { Property = Button.FontSizeProperty, Value = 16d },
			new Setter { Property = Button.PaddingProperty, Value = new Thickness(16, 10) },
		},
	};

	/// <summary>Named styles, referenced from XAML with <c>StaticResource</c>.</summary>
	public static IEnumerable<(string Key, object Value)> CreateKeyed()
	{
		// CTK converters need no handler, so they work unchanged on this host.
		yield return ("Invert", new CommunityToolkit.Maui.Converters.InvertedBoolConverter());
		yield return ("IsNotNull", new CommunityToolkit.Maui.Converters.IsNotNullConverter());
		yield return ("HasText", new CommunityToolkit.Maui.Converters.IsStringNotNullOrWhiteSpaceConverter());
		yield return ("ToTextCase", new CommunityToolkit.Maui.Converters.TextCaseConverter
		{
			Type = CommunityToolkit.Maui.Converters.TextCaseType.Upper,
		});

		// The rounded, bordered surface every tile, row and detail block reuses.
		yield return (Card, new Style(typeof(Border))
		{
			Setters =
			{
				new Setter { Property = Border.BackgroundColorProperty, Value = Palette.Surface },
				new Setter { Property = Border.StrokeProperty, Value = Palette.Border },
				new Setter { Property = Border.StrokeThicknessProperty, Value = 1d },
				new Setter { Property = Border.StrokeShapeProperty, Value = new RoundRectangle { CornerRadius = new CornerRadius(14) } },
				new Setter { Property = Border.PaddingProperty, Value = new Thickness(14) },
			},
		});

		yield return (PrimaryButton, Based(ButtonBase, typeof(Button),
			new Setter { Property = Button.BackgroundColorProperty, Value = Palette.Accent },
			new Setter { Property = Button.TextColorProperty, Value = Color.FromArgb("#1A1206") },
			new Setter { Property = Button.BorderColorProperty, Value = Palette.Accent },
			new Setter { Property = Button.FontAttributesProperty, Value = FontAttributes.Bold }));

		yield return (SecondaryButton, Based(ButtonBase, typeof(Button),
			new Setter { Property = Button.BackgroundColorProperty, Value = Palette.SurfaceRaised },
			new Setter { Property = Button.BorderColorProperty, Value = Palette.Border }));

		yield return (DangerButton, Based(ButtonBase, typeof(Button),
			new Setter { Property = Button.BackgroundColorProperty, Value = Color.FromArgb("#3A1F1C") },
			new Setter { Property = Button.TextColorProperty, Value = Palette.Error },
			new Setter { Property = Button.BorderColorProperty, Value = Color.FromArgb("#5C2E29") }));

		// Compact square button used for the favourite star and the layout toggle.
		yield return (IconButton, Based(ButtonBase, typeof(Button),
			new Setter { Property = Button.BackgroundColorProperty, Value = Colors.Transparent },
			new Setter { Property = Button.BorderColorProperty, Value = Colors.Transparent },
			new Setter { Property = Button.PaddingProperty, Value = new Thickness(6) },
			new Setter { Property = Button.FontSizeProperty, Value = 20d },
			new Setter { Property = Button.MinimumWidthRequestProperty, Value = 40d },
			new Setter { Property = Button.MinimumHeightRequestProperty, Value = 40d }));

		yield return (PageTitle, new Style(typeof(Label))
		{
			Setters =
			{
				new Setter { Property = Label.FontSizeProperty, Value = 28d },
				new Setter { Property = Label.FontAttributesProperty, Value = FontAttributes.Bold },
				new Setter { Property = Label.TextColorProperty, Value = Palette.TextPrimary },
			},
		});

		yield return (SectionHeader, new Style(typeof(Label))
		{
			Setters =
			{
				new Setter { Property = Label.FontSizeProperty, Value = 15d },
				new Setter { Property = Label.FontAttributesProperty, Value = FontAttributes.Bold },
				new Setter { Property = Label.TextColorProperty, Value = Palette.Accent },
				new Setter { Property = Label.MarginProperty, Value = new Thickness(0, 14, 0, 6) },
			},
		});

		yield return (Body, new Style(typeof(Label))
		{
			Setters =
			{
				new Setter { Property = Label.FontSizeProperty, Value = 17d },
				new Setter { Property = Label.TextColorProperty, Value = Palette.TextPrimary },
				new Setter { Property = Label.LineHeightProperty, Value = 1.25d },
			},
		});

		yield return (Caption, new Style(typeof(Label))
		{
			Setters =
			{
				new Setter { Property = Label.FontSizeProperty, Value = 14d },
				new Setter { Property = Label.TextColorProperty, Value = Palette.TextSecondary },
			},
		});

		yield return (Muted, new Style(typeof(Label))
		{
			Setters =
			{
				new Setter { Property = Label.FontSizeProperty, Value = 13d },
				new Setter { Property = Label.TextColorProperty, Value = Palette.TextMuted },
			},
		});

		yield return (Chip, new Style(typeof(Border))
		{
			Setters =
			{
				new Setter { Property = Border.BackgroundColorProperty, Value = Palette.SurfaceRaised },
				new Setter { Property = Border.StrokeProperty, Value = Palette.Border },
				new Setter { Property = Border.StrokeThicknessProperty, Value = 1d },
				new Setter { Property = Border.StrokeShapeProperty, Value = new RoundRectangle { CornerRadius = new CornerRadius(12) } },
				new Setter { Property = Border.PaddingProperty, Value = new Thickness(12, 6) },
			},
		});

		yield return (HeroImage, new Style(typeof(Image))
		{
			Setters =
			{
				new Setter { Property = Image.AspectProperty, Value = Aspect.AspectFill },
				new Setter { Property = Image.BackgroundColorProperty, Value = Palette.Skeleton },
			},
		});

		yield return (ThumbImage, new Style(typeof(Image))
		{
			Setters =
			{
				new Setter { Property = Image.AspectProperty, Value = Aspect.AspectFill },
				new Setter { Property = Image.BackgroundColorProperty, Value = Palette.Skeleton },
			},
		});
	}

	public static ResourceDictionary Create()
	{
		var styles = new ResourceDictionary();
		Populate(styles);
		return styles;
	}

	/// <summary>A keyed variant that inherits every setter from an implicit style.</summary>
	private static Style Based(Style baseStyle, Type targetType, params Setter[] extra)
	{
		var style = new Style(targetType) { BasedOn = baseStyle };
		foreach (var setter in extra)
			style.Setters.Add(setter);

		return style;
	}
}
