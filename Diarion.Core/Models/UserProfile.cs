using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Diarion.Models;

public enum GenderType { NotSpecified, Female, Male, Other }

public partial class UserProfile : ObservableObject
{
    public const int DefaultCycleLength = 28;
    public const int DefaultPeriodLength = 5;

    public Guid Id { get; set; } = Guid.NewGuid();

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private int? _age;
    [ObservableProperty] private double? _weight;
    [ObservableProperty] private int? _height;
    [ObservableProperty] private GenderType _gender = GenderType.NotSpecified;

    // Налаштування завдань
    [ObservableProperty] private bool _autoMigrateUncompletedTasksEnabled = true;

    // Менструальний календар
    [ObservableProperty] private bool _isMenstrualTrackingEnabled;
    [ObservableProperty] private int _cycleLength = DefaultCycleLength;
    [ObservableProperty] private int _periodLength = DefaultPeriodLength;
    [ObservableProperty] private DateTime? _lastPeriodStartDate;

    public int GetNormalizedCycleLength()
    {
        return CycleLength > 0 ? CycleLength : DefaultCycleLength;
    }

    public int GetNormalizedPeriodLength()
    {
        var cycleLength = GetNormalizedCycleLength();
        var periodLength = PeriodLength > 0 ? PeriodLength : DefaultPeriodLength;
        return Math.Min(periodLength, cycleLength);
    }

    public bool NormalizeCycleSettings()
    {
        var normalizedCycleLength = GetNormalizedCycleLength();
        var normalizedPeriodLength = GetNormalizedPeriodLength();
        var changed = CycleLength != normalizedCycleLength || PeriodLength != normalizedPeriodLength;

        CycleLength = normalizedCycleLength;
        PeriodLength = normalizedPeriodLength;

        return changed;
    }
}