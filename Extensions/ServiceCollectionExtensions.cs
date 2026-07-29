using Microsoft.Extensions.DependencyInjection;
using Diarion.Services;
using Diarion.Services.Database;
using Diarion.Core.Services;
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
        services.AddSingleton<IDatabaseContext, DatabaseContext>();
        services.AddSingleton<INotificationService, LocalNotificationService>();
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
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<IExportService, ExportService>();
        services.AddSingleton<INavigationService, MauiNavigationService>();
        services.AddSingleton<IDialogService, MauiDialogService>();
        services.AddSingleton<IHealthDataService, HealthDataService>();
        services.AddSingleton<IFileSystemService, MauiFileSystemService>();
        services.AddSingleton<IShareService, MauiShareService>();
        services.AddSingleton<IFilePickerService, MauiFilePickerService>();
        services.AddSingleton<IDispatcherService, MauiDispatcherService>();
        
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
        services.AddTransient<DiaryDetailViewModel>();
        services.AddTransient<TodoDetailViewModel>();
        services.AddTransient<ProfileViewModel>();
        services.AddTransient<HabitTrackerViewModel>();
        services.AddTransient<GoodDeedsViewModel>();
        services.AddTransient<ReadingTrackerViewModel>();
        services.AddTransient<HappyMomentsViewModel>();
        
        services.AddTransient<MoodStatsViewModel>();
        services.AddTransient<SleepStatsViewModel>();
        services.AddTransient<ProductivityStatsViewModel>();
        services.AddTransient<FinanceStatsViewModel>();
        services.AddTransient<HabitStatsViewModel>();
        services.AddTransient<StatisticsViewModel>();
        
        services.AddTransient<WishlistViewModel>();
        services.AddTransient<FinanceViewModel>();
        services.AddTransient<NotesViewModel>();
        services.AddTransient<NoteDetailViewModel>();
        services.AddTransient<HabitEditorViewModel>();
        services.AddTransient<PromptLibraryViewModel>();
        services.AddTransient<PromptEditorViewModel>();
        services.AddTransient<CycleViewModel>();

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
        services.AddTransient<NotesPage>();
        services.AddTransient<NoteDetailPage>();
        services.AddTransient<HabitEditorPage>();
        services.AddTransient<PromptLibraryPage>();
        services.AddTransient<PromptEditorPage>();
        services.AddTransient<CyclePage>();

        return services;
    }
}