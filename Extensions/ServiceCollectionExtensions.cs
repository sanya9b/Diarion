using Microsoft.Extensions.DependencyInjection;
using Diarion.Services;
using Diarion.Services.Database;
using Diarion.ViewModels;
using Diarion.ViewModels.Statistics;
using Diarion.Views;

namespace Diarion.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCoreServices(this IServiceCollection services)
    {
        services.AddSingleton<IDatabaseSeeder, DatabaseSeeder>();
        services.AddSingleton<IDatabaseContext, DatabaseContext>();
        services.AddSingleton<INotificationService, LocalNotificationService>();
        services.AddSingleton<ITodoService, TodoService>();
        services.AddSingleton<IHabitService, HabitService>();
        services.AddSingleton<IFinanceService, FinanceService>();
        services.AddSingleton<IWishlistService, WishlistService>();
        services.AddSingleton<IProfileService, ProfileService>();
        services.AddSingleton<IAuxiliaryService, AuxiliaryService>();
        services.AddSingleton<IDiaryService, DiaryService>();
        services.AddSingleton<IMenstrualCycleService, MenstrualCycleService>();
        services.AddSingleton<ICalendarService, CalendarService>();
        services.AddSingleton<IStatisticsService, StatisticsService>();
        services.AddSingleton<IMenuConfigurationService, MenuConfigurationService>();
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<INavigationService, MauiNavigationService>();
        services.AddSingleton<IDialogService, MauiDialogService>();
        
        return services;
    }

    public static IServiceCollection AddAppViewModels(this IServiceCollection services)
    {
        services.AddTransient<CalendarSectionViewModel>();
        services.AddTransient<PlannerSectionViewModel>();
        services.AddTransient<QuickMenuViewModel>();
        services.AddTransient<HabitsSectionViewModel>();
        services.AddTransient<MainViewModel>();
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
        services.AddTransient<StatisticsViewModel>();
        
        services.AddTransient<WishlistViewModel>();
        services.AddTransient<FinanceViewModel>();
        
        return services;
    }

    public static IServiceCollection AddAppViews(this IServiceCollection services)
    {
        services.AddTransient<MainPage>();
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
        
        return services;
    }
}