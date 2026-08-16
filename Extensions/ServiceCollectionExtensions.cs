using Microsoft.Extensions.DependencyInjection;
using Diarion.Services;
using Diarion.Services.Database;
using Diarion.Core.Services;
using Diarion.Services.Ai;
using Diarion.Services.Ai.Reports;
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
        // Lifts the note's formatting bar over the keyboard. Only the phones have one to lift, and
        // only iOS covers the window with it — Android moves the window instead and answers zero,
        // which is measured rather than assumed. Singleton: it holds a subscription to the platform
        // for the life of the app, and every screen that ever asks is asking the same question.
#if ANDROID
        services.AddSingleton<IKeyboardInsetService, AndroidKeyboardInsetService>();
#elif IOS
        services.AddSingleton<IKeyboardInsetService, IosKeyboardInsetService>();
#else
        services.AddSingleton<IKeyboardInsetService, NoKeyboardInsetService>();
#endif

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
        //
        // No overall timeout, deliberately. HttpClient.Timeout covers the whole response body, so
        // any finite value is a bet on how fast the user's connection is: 30 minutes killed a
        // healthy 1.1 GB download on a slow line, and shorter would be worse. ModelDownloadService
        // times the parts that can actually hang — the response headers, and the gap between two
        // reads — which is the question worth asking.
        services.AddSingleton(_ => new HttpClient { Timeout = Timeout.InfiniteTimeSpan });

        services.AddSingleton<IAiModelPathProvider, AppDataModelPaths>();
        // Answers "Wi-Fi or mobile data" for the one setting that asks. Singleton because it holds
        // a subscription to the platform's connectivity event for the life of the app.
        services.AddSingleton<INetworkStatusService, MauiNetworkStatusService>();
        // Asks the platform to let a download outlive the visible app, and fetches the bytes. Only
        // the phones need either: Windows and Mac do not suspend a windowed process for being
        // minimized, so there the no-op host is the whole truth and the ordinary HTTP loop keeps
        // working whatever the user does with the window.
        //
        // The two phones need opposite things. Android will keep the process running if asked, so
        // the host does the asking and the transport stays as it is. iOS will not — it stops the
        // process and the socket with it — so there is nothing to ask for, and instead the transfer
        // itself is handed to the system.
#if ANDROID
        services.AddSingleton<IModelTransferHost, AndroidModelTransferHost>();
        services.AddSingleton<IModelFileTransfer>(sp =>
            new HttpModelFileTransfer(sp.GetRequiredService<HttpClient>()));
#elif IOS
        services.AddSingleton<IModelTransferHost, SystemSessionTransferHost>();
        // The base directory is passed in rather than captured once: a destination has to be
        // remembered relative to it, because the container path carries a UUID that iOS is free to
        // change between launches.
        services.AddSingleton<IModelFileTransfer>(_ =>
            new BackgroundSessionTransfer(FileSystem.AppDataDirectory));
#else
        services.AddSingleton<IModelTransferHost, NullModelTransferHost>();
        services.AddSingleton<IModelFileTransfer>(sp =>
            new HttpModelFileTransfer(sp.GetRequiredService<HttpClient>()));
#endif
        services.AddSingleton<IModelDownloadService, ModelDownloadService>();
        services.AddSingleton<IEmbeddingModelLocator, InstalledEmbeddingModelLocator>();
        services.AddSingleton<IDeviceCapabilityProbe, MauiDeviceCapabilityProbe>();
        services.AddSingleton<ITextEmbedder, OnnxTextEmbedder>();
        services.AddSingleton<IVectorStore, LiteDbVectorStore>();
        // The single answer to "may the AI read this diary". Every consumer asks it, so consent
        // cannot be honoured in one place and forgotten in the next four.
        //
        // Which of the three answers the app gives is decided here and nowhere else: everything off,
        // embeddings only (where it stands — the generative model is retired), or the full stack.
        // AiAvailability itself is untouched and still tested, so the flags — not this line — are the
        // switch.
        services.AddSingleton<IAiAvailability>(sp =>
        {
            if (!OnDeviceAi.IsOffered)
            {
                return new DisabledAiAvailability();
            }

            var full = ActivatorUtilities.CreateInstance<AiAvailability>(sp);
            return OnDeviceAi.GenerationOffered ? full : new EmbeddingsOnlyAiAvailability(full);
        });
        services.AddSingleton<IEmbeddingIndexService, EmbeddingIndexService>();
        services.AddSingleton<ISemanticSearchService, SemanticSearchService>();
        services.AddSingleton<IDigestService, DigestService>();
        services.AddSingleton<IThemeClusterService, ThemeClusterService>();
        services.AddSingleton<IGenerativeModelLocator, InstalledGenerativeModelLocator>();
        // Resolves to the real generator, which reports IsAvailable=false until a generative model
        // is installed — so chat hides itself rather than the container deciding whether it exists.
        services.AddSingleton<ITextGenerator, OnnxGenAiTextGenerator>();
        services.AddSingleton<IDiaryChatService, DiaryChatService>();

        // Periodic reports over the API (spec 14). Nothing here touches the network or the local
        // models: the builder only gathers what the app already knows about a period, so it stays
        // registered and useful while the on-device stack is off.
        services.AddSingleton<ISnapshotBuilder, SnapshotBuilder>();

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
        services.AddTransient<SnapshotPreviewViewModel>();

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
        services.AddTransient<SnapshotPreviewPage>();

        return services;
    }
}