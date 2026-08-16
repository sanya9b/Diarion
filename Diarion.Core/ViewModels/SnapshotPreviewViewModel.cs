using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Diarion.Helpers;
using Diarion.Models;
using Diarion.Models.Ai.Reports;
using Diarion.Resources.Localization;
using Diarion.Services;
using Diarion.Services.Ai.Reports;

namespace Diarion.ViewModels;

/// <summary>
/// Shows the user the exact request body before anyone is asked to approve sending it.
/// </summary>
/// <remarks>
/// <para>
/// The screen exists before the network does, and that ordering is the point. A promise that "only
/// this leaves your phone" is worth what it can be checked against, so the check ships first — and
/// what it displays is the serializer's own output, not a friendly summary of it. A summary would be
/// a second description of the payload, free to drift from the payload.
/// </para>
/// <para>
/// Nothing here sends anything. The two switches govern the two parts of a diary a person can be hurt
/// by in ways the rest of it cannot hurt them, so they start off and are flipped here, on the screen
/// that shows exactly what flipping them adds.
/// </para>
/// </remarks>
public partial class SnapshotPreviewViewModel : BaseViewModel
{
    private readonly ISnapshotBuilder _builder;
    private readonly IProfileService _profile;

    // Flipping both switches is two rebuilds of the same diary a second apart. The window is short
    // enough that a single deliberate tap still feels immediate.
    private readonly AsyncDebouncer _debouncer = new(TimeSpan.FromMilliseconds(150));

    /// <summary>True while the switches are being set from a load, so they do not trigger a rebuild.</summary>
    private bool _settingUp;

    public SnapshotPreviewViewModel(ISnapshotBuilder builder, IProfileService profile)
    {
        _builder = builder;
        _profile = profile;
        Title = AppResources.SnapshotPreviewTitle;
    }

    /// <summary>Which cadence the snapshot claims to be. Set before <see cref="LoadAsync"/>.</summary>
    public PeriodKind Kind { get; set; } = PeriodKind.Week;

    /// <summary>
    /// The window to show. Defaults to the last week that has actually finished — an unfinished period
    /// would preview one payload and send a different one tomorrow.
    /// </summary>
    public StatsRange Range { get; set; } = PeriodBoundaries.LastClosed(PeriodKind.Week, DateTime.Today);

    /// <summary>The request body, verbatim.</summary>
    [ObservableProperty]
    public partial string Json { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PeriodText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SizeText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IncludeIntimateLife { get; set; }

    [ObservableProperty]
    public partial bool IncludeCycle { get; set; }

    /// <summary>Hides the cycle switch entirely for a profile the feature is not offered to.</summary>
    [ObservableProperty]
    public partial bool IsCycleAvailable { get; set; }

    /// <summary>
    /// True when not one day of the period carries written text. Worth saying out loud: the report
    /// would then be a paid restatement of the numbers already on the statistics screen.
    /// </summary>
    [ObservableProperty]
    public partial bool IsWordless { get; set; }

    /// <summary>The options the payload on screen was built with — what a caller would go on to send.</summary>
    public SnapshotOptions Options => new()
    {
        IncludeIntimateLife = IncludeIntimateLife,
        IncludeCycle = IncludeCycle && IsCycleAvailable
    };

    public async Task LoadAsync()
    {
        var profile = await _profile.GetUserProfileAsync();

        _settingUp = true;
        IsCycleAvailable = profile.IsCycleTrackingActive;
        if (!IsCycleAvailable) IncludeCycle = false;
        _settingUp = false;

        PeriodText = string.Concat(
            Range.Start.ToString("d", CultureInfo.CurrentCulture),
            " — ",
            Range.End.ToString("d", CultureInfo.CurrentCulture));

        await RebuildAsync();
    }

    /// <summary>Runs a rebuild a switch has queued, without waiting out the debounce window.</summary>
    public Task FlushAsync() => _debouncer.FlushAsync();

    [RelayCommand]
    private Task RefreshAsync() => RebuildAsync();

    partial void OnIncludeIntimateLifeChanged(bool value) => QueueRebuild();

    partial void OnIncludeCycleChanged(bool value) => QueueRebuild();

    private void QueueRebuild()
    {
        if (_settingUp) return;

        _debouncer.Debounce(RebuildAsync);
    }

    private async Task RebuildAsync()
    {
        var options = Options;

        IsBusy = true;
        try
        {
            var snapshot = await _builder.BuildAsync(Kind, Range, options);

            Json = SnapshotSerializer.ToJson(snapshot);
            IsWordless = snapshot.Days.All(IsBlank);

            SizeText = string.Format(
                CultureInfo.CurrentCulture,
                AppResources.SnapshotPreviewSizeFormat,
                snapshot.DayCount,
                Json.Length);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static bool IsBlank(SnapshotDay day)
        => day.Text is null && day.Title is null && day.Gratitude is null && day.SoulFood is null
           && day.Triggers is null && day.SupportForOthers is null && day.PromptAnswer is null
           && day.SleepNotes is null && day.IntimateLife is null;
}
