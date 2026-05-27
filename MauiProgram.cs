using Microsoft.Extensions.Logging;

namespace Diarion;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("Montserrat-Regular.ttf", "OpenSansRegular"); // Alias kept for compatibility with Styles
				fonts.AddFont("Montserrat-SemiBold.ttf", "OpenSansSemibold");
				fonts.AddFont("Lora-Italic.ttf", "LoraItalic");
				fonts.AddFont("Parisienne-Regular.ttf", "Parisienne");
				fonts.AddFont("MarckScript-Regular.ttf", "MarckScript");
			});

#if DEBUG
		builder.Logging.AddDebug();
#endif

		// -- DEPENDENCY INJECTION --
		// Services (Infrastructure & Core Logic)
		builder.Services.AddSingleton<Diarion.Services.IDiaryService, Diarion.Services.DiaryService>();

		// ViewModels
		builder.Services.AddTransient<Diarion.ViewModels.MainViewModel>();
		builder.Services.AddTransient<Diarion.ViewModels.DiaryDetailViewModel>();
		builder.Services.AddTransient<Diarion.ViewModels.TodoDetailViewModel>();
		builder.Services.AddTransient<Diarion.ViewModels.ProfileViewModel>();

		// Views
		builder.Services.AddTransient<Diarion.Views.MainPage>();
		builder.Services.AddTransient<Diarion.Views.DiaryDetailPage>();
		builder.Services.AddTransient<Diarion.Views.TodoDetailPage>();
		builder.Services.AddTransient<Diarion.Views.ProfilePage>();

		return builder.Build();
	}
}
