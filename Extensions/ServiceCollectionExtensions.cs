using Microsoft.Extensions.DependencyInjection;
using Diarion.Services;
using Diarion.Services.Database;
using Diarion.Core.Services;
using Diarion.Services.Ai.Reports;
using Diarion.Services.Backup;
using Diarion.Services.Store;
using Diarion.ViewModels;
using Diarion.ViewModels.Statistics;
using Diarion.Views;

namespace Diarion.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCoreServices(this IServiceCollection services)
    {
        services.AddSingleton<IDatabaseSeeder, DatabaseSeeder>();
        services.AddSingleton<IEncryptionKeyProvider, SecureStorageKeyProvider>();
        services.AddSingleton<IAppLockService, AppLockService>();
        services.AddSingleton<IBiometricService, BiometricService>();
        // Subscription state (spec 15). iOS talks to StoreKit 2 through the Swift facade; wherever
        // there is no store yet (Android before Play Billing, Windows, Mac Catalyst) the state
        // grants writing.
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IEntitlementStore, SecureStorageEntitlementStore>();
        services.AddSingleton<IInstallEvidence, MauiInstallEvidence>();
#if IOS
        services.AddSingleton<IStoreClient, StoreKitClient>();
#else
        services.AddSingleton<IStoreClient, UnavailableStoreClient>();
#endif
#if DEBUG
        // DIARION_FAKE_STORE=new|legacy: a pretend store for working on the paywall where no store
        // exists (Windows). Debug builds only; Release cannot even see the type.
        DebugStoreOverrides.Apply(services);
#endif
        services.AddSingleton<IExternalLinkService, MauiExternalLinkService>();
        services.AddSingleton<PaywallPresenter>();
        services.AddSingleton<ReminderAccessCoordinator>();
        services.AddSingleton<ILegacyAccessDetector, LegacyAccessDetector>();
        services.AddSingleton<IEntitlementService, EntitlementService>();
        services.AddSingleton<IAccessGuard, AccessGuard>();
        services.AddSingleton<IDatabaseContext, DatabaseContext>();
        // The platform service behind a read-only gate: no daily or habit reminder is scheduled
        // while the diary is read-only; the coordinator puts them back when access returns.
        services.AddSingleton<LocalNotificationService>();
        services.AddSingleton<INotificationService>(sp => new AccessAwareNotificationService(
            sp.GetRequiredService<LocalNotificationService>(), sp.GetRequiredService<IAccessGuard>()));
        services.AddSingleton<IMotionSettings, MotionSettings>();
        services.AddSingleton<ITodoService, TodoService>();
        services.AddSingleton<IHabitService, HabitService>();
        services.AddSingleton<IGuidedPromptService, GuidedPromptService>();
        services.AddSingleton<IDiaryHabitSyncService, DiaryHabitSyncService>();
        services.AddSingleton<IFinanceService, FinanceService>();
        services.AddSingleton<IWishlistService, WishlistService>();
        services.AddSingleton<IProfileService, ProfileService>();
        services.AddSingleton<IAuxiliaryService, AuxiliaryService>();
        services.AddSingleton<IDiaryService, DiaryService>();
        services.AddSingleton<INoteService, NoteService>();
        services.AddSingleton<ICycleLogService, CycleLogService>();
        services.AddSingleton<ICalendarService, CalendarService>();
        services.AddSingleton<IStatisticsService, StatisticsService>();
        services.AddSingleton<ICorrelationService, CorrelationService>();
        services.AddSingleton<IMenuConfigurationService, MenuConfigurationService>();
        services.AddSingleton<IOnboardingModuleService, OnboardingModuleService>();
        services.AddSingleton<ITourService, TourService>();
        services.AddSingleton<IBackupService, BackupService>();
        // Scheduled copies to a folder of the user's choosing (roadmap 0.1). The folder provider is
        // the platform's own — SAF on Android, security-scoped bookmarks on iOS, the FutureAccessList
        // on Windows; the schedule, the envelope and the pruning are Core's and tested there. Where
        // there is no provider the card says so instead of offering a switch.
#if ANDROID
        services.AddSingleton<IBackupFolderService, AndroidBackupFolderService>();
#elif IOS
        services.AddSingleton<IBackupFolderService, IosBackupFolderService>();
#elif WINDOWS
        services.AddSingleton<IBackupFolderService, WindowsBackupFolderService>();
#else
        services.AddSingleton<IBackupFolderService, NullBackupFolderService>();
#endif
        services.AddSingleton<IScheduledBackupStore, PreferencesScheduledBackupStore>();
        services.AddSingleton<IScheduledBackupService, ScheduledBackupService>();
        services.AddSingleton<IExportService, ExportService>();
        services.AddSingleton<INavigationService, MauiNavigationService>();
        services.AddSingleton<IDialogService, MauiDialogService>();
        services.AddSingleton<IHealthDataService, HealthDataService>();
        services.AddSingleton<IFileSystemService, MauiFileSystemService>();
        services.AddSingleton<IShareService, MauiShareService>();
        services.AddSingleton<IFilePickerService, MauiFilePickerService>();
        services.AddSingleton<IDispatcherService, MauiDispatcherService>();
        // How much of the window the keyboard covers, for a formatting bar laid out under the note.
        // Android moves the window instead and answers zero, which is measured rather than assumed.
        // iOS is the platform that slides the keyboard over the page — and there the bar is not in
        // the page at all: it rides on the keyboard as its accessory view (RichTextBlockHandler.Apple),
        // so the system lifts it and nothing here has to know how tall the keyboard is. Singleton: it
        // holds a subscription to the platform for the life of the app.
#if ANDROID
        services.AddSingleton<IKeyboardInsetService, AndroidKeyboardInsetService>();
#else
        services.AddSingleton<IKeyboardInsetService, NoKeyboardInsetService>();
#endif

        // Word search over the diary and notes (the search screen; its tile is retired for now).
        services.AddSingleton<Diarion.Services.Search.ISearchService, Diarion.Services.Search.SearchService>();

        // Periodic reports over the API (spec 14). The builder only gathers what the app already knows
        // about a period; nothing here touches the network. The on-device AI that used to be registered
        // beside it — encoder, index, downloads, chat — was removed on 2026-10-21.
        services.AddSingleton<ISnapshotBuilder, SnapshotBuilder>();
        // The local review tab: the same snapshot, twice, compared on the device. No network, no model.
        services.AddSingleton<Diarion.Services.Reports.IPeriodReviewService, Diarion.Services.Reports.PeriodReviewService>();

        return services;
    }

    public static IServiceCollection AddAppViewModels(this IServiceCollection services)
    {
        services.AddTransient<CalendarSectionViewModel>();
        services.AddTransient<PlannerSectionViewModel>();
        services.AddTransient<QuickMenuViewModel>();
        services.AddTransient<HabitsSectionViewModel>();
        services.AddTransient<CycleStatusViewModel>();
        services.AddTransient<MainViewModel>();
        services.AddTransient<LockViewModel>();
        services.AddTransient<PinSetupViewModel>();
        services.AddTransient<OnboardingViewModel>();
        services.AddTransient<TourViewModel>();
        services.AddTransient<DiaryDetailViewModel>();
        services.AddTransient<TodoDetailViewModel>();
        services.AddTransient<ProfileViewModel>();
        services.AddTransient<PaywallViewModel>();
        services.AddTransient<SubscriptionSectionViewModel>();
        services.AddTransient<AccessBannerViewModel>();
        services.AddTransient<HabitTrackerViewModel>();
        services.AddTransient<GoodDeedsViewModel>();
        services.AddTransient<ReadingTrackerViewModel>();
        services.AddTransient<HappyMomentsViewModel>();
        
        services.AddTransient<MoodStatsViewModel>();
        services.AddTransient<SleepStatsViewModel>();
        services.AddTransient<ProductivityStatsViewModel>();
        services.AddTransient<FinanceStatsViewModel>();
        services.AddTransient<HabitStatsViewModel>();
        services.AddTransient<CycleStatsViewModel>();
        services.AddTransient<PeriodReviewViewModel>();
        services.AddTransient<StatisticsViewModel>();
        
        services.AddTransient<WishlistViewModel>();
        services.AddTransient<FinanceViewModel>();
        services.AddTransient<SearchViewModel>();
        services.AddTransient<NotesViewModel>();
        services.AddTransient<NoteDetailViewModel>();
        services.AddTransient<HabitEditorViewModel>();
        services.AddTransient<PromptLibraryViewModel>();
        services.AddTransient<PromptEditorViewModel>();
        services.AddTransient<PromptHistoryViewModel>();
        services.AddTransient<CycleViewModel>();
        services.AddTransient<CycleRecordViewModel>();
        services.AddTransient<SnapshotPreviewViewModel>();
        services.AddTransient<QuickCaptureViewModel>();
        services.AddTransient<ScheduledBackupViewModel>();

        return services;
    }

    public static IServiceCollection AddAppViews(this IServiceCollection services)
    {
        services.AddTransient<MainPage>();
        services.AddTransient<LockPage>();
        services.AddTransient<PinSetupPage>();
        services.AddTransient<OnboardingPage>();
        services.AddTransient<DiaryDetailPage>();
        services.AddTransient<TodoDetailPage>();
        services.AddTransient<ProfilePage>();
        services.AddTransient<HabitTrackerPage>();
        services.AddTransient<GoodDeedsPage>();
        services.AddTransient<ReadingTrackerPage>();
        services.AddTransient<HappyMomentsPage>();
        services.AddTransient<StatisticsPage>();
        services.AddTransient<WishlistPage>();
        services.AddTransient<FinancePage>();
        services.AddTransient<SearchPage>();
        services.AddTransient<NotesPage>();
        services.AddTransient<NoteDetailPage>();
        services.AddTransient<HabitEditorPage>();
        services.AddTransient<PromptLibraryPage>();
        services.AddTransient<PromptEditorPage>();
        services.AddTransient<PromptHistoryPage>();
        services.AddTransient<CyclePage>();
        services.AddTransient<SnapshotPreviewPage>();
        services.AddTransient<PaywallPage>();
        services.AddTransient<QuickCapturePage>();

        return services;
    }
}