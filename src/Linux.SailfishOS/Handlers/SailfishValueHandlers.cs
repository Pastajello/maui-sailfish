using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.SailfishOS.Platform.QtHost;

namespace Microsoft.Maui.SailfishOS.Handlers;

/// <summary>Switch handler (Silica Switch adapter).</summary>
public class SailfishSwitchHandler : SailfishSnapshotHandler<ISwitch>
{
	private static readonly string[] Keys =
	{
		nameof(Switch.IsToggled), nameof(ISwitch.IsOn),
		nameof(ISwitch.ThumbColor), nameof(ISwitch.TrackColor), nameof(Switch.OnColor),
	};

	public static readonly PropertyMapper<ISwitch, SailfishSwitchHandler> Mapper = SnapshotMapper<SailfishSwitchHandler>(Keys);

	public static readonly CommandMapper<ISwitch, SailfishSwitchHandler> CommandMapper = new(SailfishViewMapper.CommandMapper);

	public SailfishSwitchHandler() : this(null)
	{
	}

	public SailfishSwitchHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
		: base(mapper ?? Mapper, commandMapper ?? CommandMapper, SailfishMeasure.Switch)
	{
	}

	protected override string? AdapterUri => SailfishKeys.Adapter.Switch;

	protected override Dictionary<string, object?>? Snapshot(ISwitch view) =>
		view is Switch sw ? AdapterSnapshots.SwitchProps(sw) : null;
}

/// <summary>CheckBox handler: a framed check box, not the Silica Switch.</summary>
public class SailfishCheckBoxHandler : SailfishSnapshotHandler<ICheckBox>
{
	private static readonly string[] Keys =
	{
		nameof(CheckBox.IsChecked), nameof(ICheckBox.Foreground), nameof(CheckBox.Color),
	};

	public static readonly PropertyMapper<ICheckBox, SailfishCheckBoxHandler> Mapper = SnapshotMapper<SailfishCheckBoxHandler>(Keys);

	public static readonly CommandMapper<ICheckBox, SailfishCheckBoxHandler> CommandMapper = new(SailfishViewMapper.CommandMapper);

	public SailfishCheckBoxHandler() : this(null)
	{
	}

	public SailfishCheckBoxHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
		: base(mapper ?? Mapper, commandMapper ?? CommandMapper, SailfishMeasure.Check)
	{
	}

	protected override string? AdapterUri => SailfishKeys.Adapter.CheckBox;

	protected override Dictionary<string, object?>? Snapshot(ICheckBox view) =>
		view is CheckBox cb ? AdapterSnapshots.CheckBoxProps(cb) : null;
}

/// <summary>Slider handler; bounds must precede the value because QML clamps on assignment.</summary>
public class SailfishSliderHandler : SailfishSnapshotHandler<ISlider>
{
	private static readonly string[] Keys =
	{
		nameof(Slider.Value), nameof(Slider.Minimum), nameof(Slider.Maximum),
		nameof(ISlider.MinimumTrackColor), nameof(ISlider.MaximumTrackColor), nameof(ISlider.ThumbColor),
		nameof(ISlider.ThumbImageSource),
	};

	public static readonly PropertyMapper<ISlider, SailfishSliderHandler> Mapper = SnapshotMapper<SailfishSliderHandler>(Keys);

	public static readonly CommandMapper<ISlider, SailfishSliderHandler> CommandMapper = new(SailfishViewMapper.CommandMapper);

	public SailfishSliderHandler() : this(null)
	{
	}

	public SailfishSliderHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
		: base(mapper ?? Mapper, commandMapper ?? CommandMapper, SailfishMeasure.Slider)
	{
	}

	protected override string? AdapterUri => SailfishKeys.Adapter.Slider;

	protected override Dictionary<string, object?>? Snapshot(ISlider view) =>
		view is Slider slider ? AdapterSnapshots.SliderProps(slider) : null;
}

/// <summary>ProgressBar handler.</summary>
public class SailfishProgressBarHandler : SailfishSnapshotHandler<IProgress>
{
	private static readonly string[] Keys = { nameof(ProgressBar.Progress), nameof(IProgress.ProgressColor) };

	public static readonly PropertyMapper<IProgress, SailfishProgressBarHandler> Mapper = SnapshotMapper<SailfishProgressBarHandler>(Keys);

	public static readonly CommandMapper<IProgress, SailfishProgressBarHandler> CommandMapper = new(SailfishViewMapper.CommandMapper);

	public SailfishProgressBarHandler() : this(null)
	{
	}

	public SailfishProgressBarHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
		: base(mapper ?? Mapper, commandMapper ?? CommandMapper, SailfishMeasure.Progress)
	{
	}

	protected override string? AdapterUri => SailfishKeys.Adapter.ProgressBar;

	protected override Dictionary<string, object?>? Snapshot(IProgress view) =>
		view is ProgressBar progress ? AdapterSnapshots.ProgressBarProps(progress) : null;
}

/// <summary>ActivityIndicator handler (Silica BusyIndicator).</summary>
public class SailfishActivityIndicatorHandler : SailfishSnapshotHandler<IActivityIndicator>
{
	private static readonly string[] Keys = { nameof(ActivityIndicator.IsRunning), nameof(IActivityIndicator.Color) };

	public static readonly PropertyMapper<IActivityIndicator, SailfishActivityIndicatorHandler> Mapper = SnapshotMapper<SailfishActivityIndicatorHandler>(Keys);

	public static readonly CommandMapper<IActivityIndicator, SailfishActivityIndicatorHandler> CommandMapper = new(SailfishViewMapper.CommandMapper);

	public SailfishActivityIndicatorHandler() : this(null)
	{
	}

	public SailfishActivityIndicatorHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
		: base(mapper ?? Mapper, commandMapper ?? CommandMapper, SailfishMeasure.Activity)
	{
	}

	protected override string? AdapterUri => SailfishKeys.Adapter.ActivityIndicator;

	protected override Dictionary<string, object?>? Snapshot(IActivityIndicator view) =>
		view is ActivityIndicator indicator ? AdapterSnapshots.ActivityIndicatorProps(indicator) : null;
}

/// <summary>Stepper handler (minus/plus IconButton pair).</summary>
public class SailfishStepperHandler : SailfishSnapshotHandler<IStepper>
{
	private static readonly string[] Keys =
	{
		nameof(Stepper.Value), nameof(Stepper.Minimum), nameof(Stepper.Maximum),
		nameof(IStepper.Interval), nameof(Stepper.Increment),
	};

	public static readonly PropertyMapper<IStepper, SailfishStepperHandler> Mapper = SnapshotMapper<SailfishStepperHandler>(Keys);

	public static readonly CommandMapper<IStepper, SailfishStepperHandler> CommandMapper = new(SailfishViewMapper.CommandMapper);

	public SailfishStepperHandler() : this(null)
	{
	}

	public SailfishStepperHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
		: base(mapper ?? Mapper, commandMapper ?? CommandMapper, SailfishMeasure.Stepper)
	{
	}

	protected override string? AdapterUri => SailfishKeys.Adapter.Stepper;

	protected override Dictionary<string, object?>? Snapshot(IStepper view) =>
		view is Stepper stepper ? AdapterSnapshots.StepperProps(stepper) : null;
}

/// <summary>RadioButton handler; group exclusivity stays in MAUI.</summary>
public class SailfishRadioButtonHandler : SailfishSnapshotHandler<IRadioButton>
{
	private static readonly string[] Keys =
	[
		nameof(RadioButton.IsChecked), nameof(RadioButton.Content), nameof(TemplatedView.ControlTemplate),
		nameof(RadioButton.TextColor), nameof(ITextStyle.CharacterSpacing),
		.. FontKeys,
		nameof(IButtonStroke.StrokeColor), nameof(RadioButton.BorderColor),
		nameof(IButtonStroke.StrokeThickness), nameof(RadioButton.BorderWidth),
		nameof(IButtonStroke.CornerRadius),
	];

	public static readonly PropertyMapper<IRadioButton, SailfishRadioButtonHandler> Mapper = SnapshotMapper<SailfishRadioButtonHandler>(Keys);

	public static readonly CommandMapper<IRadioButton, SailfishRadioButtonHandler> CommandMapper = new(SailfishViewMapper.CommandMapper);

	public SailfishRadioButtonHandler() : this(null)
	{
	}

	public SailfishRadioButtonHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
		: base(mapper ?? Mapper, commandMapper ?? CommandMapper, SailfishMeasure.Radio)
	{
	}

	// A ControlTemplate (an implicit Style, or View content and MAUI's default template) makes the RadioButton a
	// templated control, as MAUI renders it everywhere: the template tree paints and MAUI's own tap gesture toggles
	// IsChecked. The Silica radio would only show the content's ToString().
	private bool Templated => ConnectedView is IContentView { PresentedContent: not null };

	protected override string? AdapterUri => Templated ? SailfishKeys.Adapter.ContentView : SailfishKeys.Adapter.RadioButton;

	protected override bool WalksChildren => Templated;

	protected override Dictionary<string, object?>? Snapshot(IRadioButton view) =>
		view is not RadioButton radio ? null
		: Templated ? AdapterSnapshots.ContainerProps(radio)
		: AdapterSnapshots.RadioButtonProps(radio);
}

/// <summary>IndicatorView dot strip; with an IndicatorTemplate MAUI lays out the template and no adapter is used.</summary>
public class SailfishIndicatorViewHandler : SailfishSnapshotHandler<IIndicatorView>
{
	private static readonly string[] Keys =
	{
		nameof(IndicatorView.Count), nameof(IndicatorView.Position),
		nameof(IndicatorView.IndicatorColor), nameof(IndicatorView.SelectedIndicatorColor),
		nameof(IndicatorView.IndicatorSize), nameof(IndicatorView.IndicatorsShape),
		nameof(IndicatorView.MaximumVisible), nameof(IndicatorView.HideSingle),
	};

	public static readonly PropertyMapper<IIndicatorView, SailfishIndicatorViewHandler> Mapper = SnapshotMapper<SailfishIndicatorViewHandler>(Keys);

	public static readonly CommandMapper<IIndicatorView, SailfishIndicatorViewHandler> CommandMapper = new(SailfishViewMapper.CommandMapper);

	public SailfishIndicatorViewHandler() : this(null)
	{
	}

	public SailfishIndicatorViewHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
		: base(mapper ?? Mapper, commandMapper ?? CommandMapper, SailfishMeasure.IndicatorView)
	{
	}

	protected override string? AdapterUri =>
		ConnectedView is IndicatorView { IndicatorTemplate: null } ? SailfishKeys.Adapter.IndicatorView : null;

	// The dot strip adapter is the default template's drawing, so template children are not walked.
	protected override bool WalksChildren => ConnectedView is not IndicatorView { IndicatorTemplate: null };

	protected override Dictionary<string, object?>? Snapshot(IIndicatorView view) =>
		view is IndicatorView { IndicatorTemplate: null } indicator ? AdapterSnapshots.IndicatorProps(indicator) : null;
}
