using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Diarion.Models;

public enum GenderType { NotSpecified, Female, Male, Other }

public partial class UserProfile : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private int? _age;
    [ObservableProperty] private double? _weight;
    [ObservableProperty] private int? _height;
    [ObservableProperty] private GenderType _gender = GenderType.NotSpecified;

    // Менструальний календар
    [ObservableProperty] private bool _isMenstrualTrackingEnabled;
    [ObservableProperty] private int _cycleLength = 28;
    [ObservableProperty] private int _periodLength = 5;
    [ObservableProperty] private DateTime? _lastPeriodStartDate;
}