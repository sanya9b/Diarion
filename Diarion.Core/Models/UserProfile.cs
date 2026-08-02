using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Diarion.Models;

public enum GenderType { NotSpecified, Female, Male, Other }

public partial class UserProfile : ObservableObject
{
    public const int DefaultCycleLength = 28;
    public const int DefaultPeriodLength = 5;
    public const int DefaultStreakGraceDays = 1;
    public const int MaxStreakGraceDays = 3;

    public Guid Id { get; set; } = Guid.NewGuid();

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private int? _age;
    [ObservableProperty] private double? _weight;
    [ObservableProperty] private int? _height;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCycleFeatureAvailable))]
    [NotifyPropertyChangedFor(nameof(IsCycleTrackingActive))]
    private GenderType _gender = GenderType.NotSpecified;

    // Налаштування завдань
    [ObservableProperty] private bool _autoMigrateUncompletedTasksEnabled = true;

    // Видимість блоків на головному екрані (за замовчуванням усі показані).
    // Значення true в ініціалізаторі гарантує, що наявні профілі без цих полів
    // (після оновлення застосунку) також відображатимуть усі блоки.
    [ObservableProperty] private bool _isMoodBlockVisible = true;
    [ObservableProperty] private bool _isSleepBlockVisible = true;
    [ObservableProperty] private bool _isHealthBlockVisible = true;
    [ObservableProperty] private bool _isFoodBlockVisible = true;
    [ObservableProperty] private bool _isHabitsBlockVisible = true;
    [ObservableProperty] private bool _isGuidedPromptBlockVisible = true;
    [ObservableProperty] private bool _isReflectionBlockVisible = true;

    // Фінанси: показувати бюджети (за замовчуванням увімкнено)
    [ObservableProperty] private bool _isBudgetsEnabled = true;

    /// <summary>
    /// ISO code of the single currency every amount is shown in. Empty means the user has not chosen,
    /// and the device region answers instead — so existing profiles, which have no such field, pick up
    /// something sensible without a migration.
    /// </summary>
    [ObservableProperty] private string _currencyCode = string.Empty;

    // Фінанси: планові транзакції. Увімкнено, бо без створеного правила фіча не робить нічого;
    // тумблер потрібен, щоб її можна було зупинити, не видаляючи правил.
    [ObservableProperty] private bool _isPlannedTransactionsEnabled = true;

    // Менструальний календар
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCycleTrackingActive))]
    private bool _isMenstrualTrackingEnabled;
    [ObservableProperty] private int _cycleLength = DefaultCycleLength;
    [ObservableProperty] private int _periodLength = DefaultPeriodLength;
    [ObservableProperty] private DateTime? _lastPeriodStartDate;

    // Безпека
    [ObservableProperty] private bool _isBiometricAuthEnabled;

    // Нагадування вести щоденник
    [ObservableProperty] private bool _isDailyReminderEnabled;
    [ObservableProperty] private TimeSpan _dailyReminderTime = new(20, 0, 0);

    // Прощаючі серії: скільки пропущених днів витримує серія, перш ніж обнулиться.
    // Увімкнено за замовчуванням — вимкнена фіча нікому не трапиться на очі, а число серії
    // від поблажок може лише зрости, тож наявним профілям це не шкодить.
    [ObservableProperty] private bool _isForgivingStreaksEnabled = true;
    [ObservableProperty] private int _streakGraceDays = DefaultStreakGraceDays;

    // Сортування меню
    [ObservableProperty] private System.Collections.Generic.List<string>? _quickMenuOrder;

    /// <summary>Whether the cycle feature is offered at all. Hidden for men rather than merely switched
    /// off, so it never has to be dismissed; the stored flag and logged days survive untouched, and a
    /// mis-set gender is fully reversible.</summary>
    public bool IsCycleFeatureAvailable => Gender != GenderType.Male;

    /// <summary>The one flag every consumer should read — availability and the user's choice together.</summary>
    public bool IsCycleTrackingActive => IsCycleFeatureAvailable && IsMenstrualTrackingEnabled;

    /// <summary>The currency to display in: the chosen one, or what the device suggests.</summary>
    public string GetEffectiveCurrencyCode()
        => string.IsNullOrWhiteSpace(CurrencyCode)
            ? Services.MoneyFormatter.DeviceDefaultCode()
            : CurrencyCode;

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

    /// <summary>Missed days a streak may forgive; zero when the feature is off, so callers need no branch.</summary>
    public int GetEffectiveStreakGrace()
        => IsForgivingStreaksEnabled ? Math.Clamp(StreakGraceDays, 0, MaxStreakGraceDays) : 0;

    public bool NormalizeStreakSettings()
    {
        var normalized = Math.Clamp(StreakGraceDays, 0, MaxStreakGraceDays);
        var changed = StreakGraceDays != normalized;

        StreakGraceDays = normalized;

        return changed;
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