using Microsoft.Maui.Controls;
using Microsoft.Maui.SailfishOS.Platform.QtHost;

namespace Microsoft.Maui.SailfishOS.Handlers;

/// <summary>Picker handler; items must precede the index so the QML index sync sees the full list.</summary>
public class SailfishPickerHandler : SailfishSnapshotHandler<IPicker>
{
	private static readonly string[] Keys =
	[
		nameof(Picker.SelectedIndex), nameof(Picker.ItemsSource), nameof(IPicker.Items), nameof(Picker.Title),
		nameof(Picker.TextColor), nameof(Picker.TitleColor), nameof(ITextStyle.CharacterSpacing),
		.. FontKeys,
		nameof(Picker.IsOpen),
		nameof(ITextAlignment.HorizontalTextAlignment), nameof(ITextAlignment.VerticalTextAlignment),
	];

	public static readonly PropertyMapper<IPicker, SailfishPickerHandler> Mapper = SnapshotMapper<SailfishPickerHandler>(Keys);

	public static readonly CommandMapper<IPicker, SailfishPickerHandler> CommandMapper = new(SailfishViewMapper.CommandMapper);

	public SailfishPickerHandler() : this(null)
	{
	}

	public SailfishPickerHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
		: base(mapper ?? Mapper, commandMapper ?? CommandMapper, SailfishMeasure.ValueBox)
	{
	}

	protected override string? AdapterUri => SailfishKeys.Adapter.Picker;

	protected override Dictionary<string, object?>? Snapshot(IPicker view) =>
		view is Picker picker ? AdapterSnapshots.PickerProps(picker) : null;
}

/// <summary>DatePicker handler (dates cross as local-midnight epoch ms).</summary>
public class SailfishDatePickerHandler : SailfishSnapshotHandler<IDatePicker>
{
	private static readonly string[] Keys =
	[
		nameof(DatePicker.Date), nameof(DatePicker.MinimumDate), nameof(DatePicker.MaximumDate),
		nameof(DatePicker.Format), nameof(DatePicker.TextColor), nameof(ITextStyle.CharacterSpacing),
		.. FontKeys,
		nameof(DatePicker.IsOpen),
	];

	public static readonly PropertyMapper<IDatePicker, SailfishDatePickerHandler> Mapper = SnapshotMapper<SailfishDatePickerHandler>(Keys);

	public static readonly CommandMapper<IDatePicker, SailfishDatePickerHandler> CommandMapper = new(SailfishViewMapper.CommandMapper);

	public SailfishDatePickerHandler() : this(null)
	{
	}

	public SailfishDatePickerHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
		: base(mapper ?? Mapper, commandMapper ?? CommandMapper, SailfishMeasure.ValueBox)
	{
	}

	protected override string? AdapterUri => SailfishKeys.Adapter.DatePicker;

	protected override Dictionary<string, object?>? Snapshot(IDatePicker view) =>
		view is DatePicker picker ? AdapterSnapshots.DatePickerProps(picker) : null;
}

/// <summary>TimePicker handler; hour and minute travel in one snapshot so no half-updated time is applied.</summary>
public class SailfishTimePickerHandler : SailfishSnapshotHandler<ITimePicker>
{
	private static readonly string[] Keys =
	[
		nameof(TimePicker.Time),
		nameof(TimePicker.Format), nameof(TimePicker.TextColor), nameof(ITextStyle.CharacterSpacing),
		.. FontKeys,
		nameof(TimePicker.IsOpen),
	];

	public static readonly PropertyMapper<ITimePicker, SailfishTimePickerHandler> Mapper = SnapshotMapper<SailfishTimePickerHandler>(Keys);

	public static readonly CommandMapper<ITimePicker, SailfishTimePickerHandler> CommandMapper = new(SailfishViewMapper.CommandMapper);

	public SailfishTimePickerHandler() : this(null)
	{
	}

	public SailfishTimePickerHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
		: base(mapper ?? Mapper, commandMapper ?? CommandMapper, SailfishMeasure.ValueBox)
	{
	}

	protected override string? AdapterUri => SailfishKeys.Adapter.TimePicker;

	protected override Dictionary<string, object?>? Snapshot(ITimePicker view) =>
		view is TimePicker picked ? AdapterSnapshots.TimePickerProps(picked) : null;
}
