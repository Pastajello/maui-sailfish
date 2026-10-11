using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.SailfishOS.Platform;
using Microsoft.Maui.Storage;
using Trailhead.Data;
using Trailhead.Features.Recording;
using Lab = Trailhead.Features.Lab;
using Trailhead.Features.Shared;

namespace Trailhead.Features.Trips;

public partial class TripsViewModel : ObservableObject
{
	/// <summary>Rows added per page: the first screenful and a margin, then more as the list nears its end.</summary>
	public const int PageSize = 40;

	private readonly TripStore _store;
	private readonly RecordingService _recording;
	private int _shown;

	public TripsViewModel(TripStore store, RecordingService recording)
	{
		_store = store;
		_recording = recording;
		_recording.PropertyChanged += (_, _) => UpdateRecordingBanner();
		_store.Changed += (_, _) => Rebuild();
		_store.Generating += (_, progress) => EmptyText = $"{L.T("Preparing the demo trips…")} {progress:P0}";
		Rebuild();
	}

	public ObservableCollection<TripGroup> Groups { get; } = new();

	[ObservableProperty]
	public partial string EmptyText { get; set; } = L.T("Preparing the demo trips…");

	[ObservableProperty]
	public partial bool IsBusy { get; set; }

	/// <summary>The list header while a recording runs: "● Recording · 2.4 km · 0:31:10" (tap: back to it).</summary>
	[ObservableProperty]
	public partial string RecordingBanner { get; set; } = "";

	[ObservableProperty]
	public partial bool IsRecording { get; set; }

	private void UpdateRecordingBanner()
	{
		IsRecording = _recording.IsActive;
		RecordingBanner = _recording.State switch
		{
			RecordingState.Recording => $"● {L.T("Recording")} · {_recording.Summary()}",
			RecordingState.Paused => $"Ⅱ {L.T("Paused")} · {_recording.Summary()}",
			_ => "",
		};
	}

	/// <summary>A draft left by a killed app: offer to carry on with it (first appearance of the list).</summary>
	public async Task OfferDraftAsync()
	{
		if (_recording.IsActive || !_recording.HasDraft)
			return;
		if (await Shell.Current.DisplayAlertAsync(L.T("Unfinished recording"), L.T("Trailhead was closed while recording. Continue it?"),
			    L.T("Continue"), L.T("Discard")))
		{
			await _recording.ResumeDraftAsync();
			await Shell.Current.GoToAsync(AppShell.RecordingRoute);
		}
		else
			_recording.Discard();
	}

	[RelayCommand]
	private Task OpenRecordingAsync() => Shell.Current.GoToAsync(AppShell.RecordingRoute);

	public void UpdateCover()
	{
		if (_recording.IsActive)
			return;   // the recording owns the cover
		var year = _store.Summaries.Where(s => s.Start.Year == DateTimeOffset.Now.Year).Sum(s => s.DistanceM);
		SailfishCover.SetContent(AppFlavor.Title, L.F("{0} trips", _store.Summaries.Count), L.F("{0} this year", Format.Distance(year)));
		SailfishCover.SetActions(new SailfishCoverAction("image://theme/icon-cover-new", () => _ = NewTripAsync()));
	}

	/// <summary>Regroups the summaries, keeping at least as many rows as were shown.</summary>
	private void Rebuild()
	{
		var count = Math.Max(PageSize, _shown);
		Groups.Clear();
		_shown = 0;
		Append(count);
		if (_store.Ready.IsCompleted)
			EmptyText = L.T("No trips yet. Pull down to start one.");
		UpdateCover();
	}

	[RelayCommand]
	private void LoadMore() => Append(PageSize);

	private void Append(int count)
	{
		foreach (var summary in _store.Summaries.Skip(_shown).Take(count))
		{
			var last = Groups.LastOrDefault();
			if (last is null || last.Month.Year != summary.Start.Year || last.Month.Month != summary.Start.Month)
				Groups.Add(last = new TripGroup(summary.Start));
			var thumb = summary.CoverPhoto is { } cover
				? _store.PhotoPath(summary.Id, Path.GetFileNameWithoutExtension(cover) + "_t" + Path.GetExtension(cover))
				: null;
			last.Add(new TripItem(summary, thumb));
			_shown++;
		}
	}

	public Task OpenAsync(TripItem item) => Shell.Current.GoToAsync($"{AppShell.TripRoute}?id={item.Id}");

	public Task EditAsync(TripItem item) => Shell.Current.GoToAsync($"{AppShell.TripEditRoute}?id={item.Id}");

	public Task DeleteAsync(TripItem item) => _store.DeleteAsync(item.Id);

	public async Task ShareAsync(TripItem item)
	{
		if (await _store.LoadAsync(item.Id) is { } trip)
			await TripSharing.ShareGpxAsync(trip);
	}

	[RelayCommand]
	private Task OpenDiagnosticsAsync() => Shell.Current.GoToAsync(AppShell.DiagnosticsRoute);

	[RelayCommand]
	private async Task NewTripAsync()
	{
		if (!_recording.IsActive)
		{
			var names = Enum.GetValues<Activity>().Select(Format.ActivityName).ToArray();
			var picked = await Shell.Current.DisplayActionSheetAsync(L.T("New trip"), L.T("Cancel"), null, names);
			var index = Array.IndexOf(names, picked);
			if (index < 0)
				return;
			await _recording.StartAsync((Activity)index);
		}
		await Shell.Current.GoToAsync(AppShell.RecordingRoute);
	}

	[RelayCommand]
	private async Task ImportGpxAsync()
	{
		var picked = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = L.T("Import a GPX track") });
		if (picked is null)
			return;
		try
		{
			IsBusy = true;
			await using var stream = await picked.OpenReadAsync();
			using var reader = new StreamReader(stream);
			var trip = Gpx.Read(await reader.ReadToEndAsync(), _store.NewId());
			await _store.SaveAsync(trip);
			await Shell.Current.GoToAsync($"{AppShell.TripRoute}?id={trip.Id}");
		}
		catch (Exception ex) when (ex is FormatException or System.Xml.XmlException)
		{
			await Shell.Current.DisplayAlertAsync(L.T("Not a GPX track"), $"{picked.FileName}: {ex.Message}", "OK");
		}
		finally
		{
			IsBusy = false;
		}
	}

	[RelayCommand]
	private Task OpenLabAsync() => Shell.Current.GoToAsync(Lab.LabPage.Route);

	[RelayCommand]
	private Task OpenSettingsAsync() => Shell.Current.GoToAsync(Trailhead.Features.Settings.SettingsPage.Route);

	[RelayCommand]
	private Task OpenStatisticsAsync() => Shell.Current.GoToAsync(AppShell.StatisticsRoute);

}
