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

    /// <summary>
    /// Superseded by <see cref="BirthDate"/> on 2026-09-20 and no longer shown: a typed age goes stale
    /// every birthday, and nothing ever read it. Kept so old profiles still deserialize untouched.
    /// </summary>
    [ObservableProperty] private int? _age;

    /// <summary>
    /// Optional, and asked for exactly one reason: the intimate-life row and the cycle calendar are
    /// shown by age (<see cref="Services.AgeGate"/>). Stays on the device like everything else; it is
    /// not in any export or report. Null is "not told", which every gate reads as "not old enough".
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBirthDateKnown))]
    [NotifyPropertyChangedFor(nameof(IsIntimateLifeAvailable))]
    [NotifyPropertyChangedFor(nameof(ShowsIntimateLifeAgeHint))]
    [NotifyPropertyChangedFor(nameof(IsCycleFeatureAvailable))]
    [NotifyPropertyChangedFor(nameof(IsCycleTrackingActive))]
    [NotifyPropertyChangedFor(nameof(NeedsBirthDateForCycle))]
    private DateTime? _birthDate;
    [ObservableProperty] private double? _weight;
    [ObservableProperty] private int? _height;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCycleFeatureAvailable))]
    [NotifyPropertyChangedFor(nameof(IsCycleTrackingActive))]
    [NotifyPropertyChangedFor(nameof(NeedsBirthDateForCycle))]
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

    // ШІ на пристрої. Вимкнено за замовчуванням: фіча коштує сотні мегабайтів завантаження, тож
    // вмикати її має користувач, а не оновлення застосунку. Наявні профілі читають false без міграції.
    [ObservableProperty] private bool _isAiEnabled;

    /// <summary>
    /// Whether model downloads wait for Wi-Fi. On by default because the encoder alone is over
    /// 100 MB, and spending someone's mobile data without asking is not a recoverable mistake.
    /// </summary>
    [ObservableProperty] private bool _isWifiOnlyModelDownload = true;

    // Сортування меню
    [ObservableProperty] private System.Collections.Generic.List<string>? _quickMenuOrder;

    /// <summary>
    /// Which version of the home-screen guide this profile has already been walked through. Zero is
    /// "none", which is also what LiteDB returns for every profile written before the field existed —
    /// so an update offers the guide once and needs no migration. The rule lives in
    /// <see cref="Services.TourService"/>; nothing outside it should compare this number.
    /// </summary>
    [ObservableProperty] private int _tourVersionSeen;

    /// <summary>The same marker for the statistics guide. See <see cref="TourVersionSeen"/>.</summary>
    [ObservableProperty] private int _statsTourVersionSeen;

    /// <summary>The same marker for the settings guide. See <see cref="TourVersionSeen"/>.</summary>
    [ObservableProperty] private int _settingsTourVersionSeen;

    public bool IsBirthDateKnown => BirthDate.HasValue;

    /// <summary>The intimate-life row of the health block: 18 and over, and only once the date is told.</summary>
    public bool IsIntimateLifeAvailable
        => Services.AgeGate.IsAtLeast(BirthDate, Services.AgeGate.IntimateLifeMinimumAge, DateTime.Today);

    /// <summary>
    /// The one-line hint that stands in for the intimate-life row while the birth date is unknown.
    /// Deliberately not shown when the date is known and too young: a row that says "not for you
    /// yet" is an invitation to lie about the date, and there is nothing to gain by advertising it.
    /// </summary>
    public bool ShowsIntimateLifeAgeHint => !IsBirthDateKnown;

    /// <summary>Whether the cycle feature is offered at all. Hidden for men rather than merely switched
    /// off, so it never has to be dismissed; the stored flag and logged days survive untouched, and a
    /// mis-set gender is fully reversible. Since 2026-09-20 also hidden under 14 — and until the birth
    /// date is told at all — by the same reversible rule: the flag and the logged days stay put.</summary>
    public bool IsCycleFeatureAvailable
        => Gender != GenderType.Male
           && Services.AgeGate.IsAtLeast(BirthDate, Services.AgeGate.CycleMinimumAge, DateTime.Today);

    /// <summary>The settings card that explains why the cycle card is missing: not a man, and no date yet.</summary>
    public bool NeedsBirthDateForCycle => Gender != GenderType.Male && !IsBirthDateKnown;

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