using CommunityToolkit.Maui;
using Diarion.Extensions;
using Microsoft.Extensions.Logging;
using Plugin.LocalNotification;

namespace Diarion;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseMauiCommunityToolkit()
			.UseLocalNotification()
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
		builder.Services
			.AddCoreServices()
			.AddAppViewModels()
			.AddAppViews();

		return builder.Build();
	}
}
