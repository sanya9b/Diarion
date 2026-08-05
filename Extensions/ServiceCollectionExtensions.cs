using Microsoft.Extensions.DependencyInjection;
using Diarion.Services;
using Diarion.Services.Database;
using Diarion.Core.Services;
using Diarion.Services.Ai;
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

        services.AddAiServices();

        return services;
    }

    /// <summary>
    /// On-device AI (specs/13). Singletons throughout: the ONNX session and the vector matrix are
    /// expensive enough that a second copy would be felt.
    /// </summary>
    private static IServiceCollection AddAiServices(this IServiceCollection services)
    {
        // The one HttpClient in the app. It only ever GETs pinned model URLs from HuggingFace —
        // no user data, no identifiers, no telemetry — which is the entire justification for the
        // INTERNET permission.
        services.AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromMinutes(30) });

        services.AddSingleton<IAiModelPathProvider, AppDataModelPaths>();
        services.AddSingleton<IModelDownloadService, ModelDownloadService>();
        services.AddSingleton<IEmbeddingModelLocator, InstalledEmbeddingModelLocator>();
        services.AddSingleton<IDeviceCapabilityProbe, MauiDeviceCapabilityProbe>();
        services.AddSingleton<ITextEmbedder, OnnxTextEmbedder>();
        services.AddSingleton<IVectorStore, LiteDbVectorStore>();
        // The single answer to "may the AI read this diary". Every consumer asks it, so consent
        // cannot be honoured in one place and forgotten in the next four.
        services.AddSingleton<IAiAvailability, AiAvailability>();
        services.AddSingleton<IEmbeddingIndexService, EmbeddingIndexService>();
        services.AddSingleton<ISemanticSearchService, SemanticSearchService>();
        services.AddSingleton<IDigestService, DigestService>();
        services.AddSingleton<IThemeClusterService, ThemeClusterService>();
        services.AddSingleton<IGenerativeModelLocator, InstalledGenerativeModelLocator>();
        // Resolves to the real generator, which reports IsAvailable=false until a generative model
        // is installed — so chat hides itself rather than the container deciding whether it exists.
        services.AddSingleton<ITextGenerator, OnnxGenAiTextGenerator>();
        services.AddSingleton<IDiaryChatService, DiaryChatService>();

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
        services.AddTransient<AiSettingsViewModel>();
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
        services.AddTransient<CycleStatsViewModel>();
        services.AddTransient<StatisticsViewModel>();
        
        services.AddTransient<WishlistViewModel>();
        services.AddTransient<FinanceViewModel>();
        services.AddTransient<SearchViewModel>();
        services.AddTransient<AiChatViewModel>();
        services.AddTransient<NotesViewModel>();
        services.AddTransient<NoteDetailViewModel>();
        services.AddTransient<HabitEditorViewModel>();
        services.AddTransient<PromptLibraryViewModel>();
        services.AddTransient<PromptEditorViewModel>();
        services.AddTransient<PromptHistoryViewModel>();
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
        services.AddTransient<SearchPage>();
        services.AddTransient<AiChatPage>();
        services.AddTransient<NotesPage>();
        services.AddTransient<NoteDetailPage>();
        services.AddTransient<HabitEditorPage>();
        services.AddTransient<PromptLibraryPage>();
        services.AddTransient<PromptEditorPage>();
        services.AddTransient<PromptHistoryPage>();
        services.AddTransient<CyclePage>();

        return services;
    }
}