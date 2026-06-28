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

		// Remove native borders and underlines for all Entries and Editors globally
		Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("Borderless", (handler, view) =>
		{
#if ANDROID
			if (handler?.PlatformView != null)
			{
				handler.PlatformView.SetBackgroundColor(Android.Graphics.Color.Transparent);
				handler.PlatformView.SetPadding(0, 0, 0, 0);
			}
#elif IOS || MACCATALYST
			handler.PlatformView.BackgroundColor = UIKit.UIColor.Clear;
			handler.PlatformView.BorderStyle = UIKit.UITextBorderStyle.None;
#elif WINDOWS
			handler.PlatformView.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
#endif
		});

		Microsoft.Maui.Handlers.EditorHandler.Mapper.AppendToMapping("Borderless", (handler, view) =>
		{
#if ANDROID
			if (handler?.PlatformView != null)
			{
				handler.PlatformView.SetBackgroundColor(Android.Graphics.Color.Transparent);
				handler.PlatformView.SetPadding(0, 0, 0, 0);
			}
#elif IOS || MACCATALYST
			handler.PlatformView.BackgroundColor = UIKit.UIColor.Clear;
#elif WINDOWS
			handler.PlatformView.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
#endif
		});

		Microsoft.Maui.Handlers.TimePickerHandler.Mapper.AppendToMapping("Borderless", (handler, view) =>
		{
#if ANDROID
			if (handler?.PlatformView != null)
			{
				handler.PlatformView.SetBackgroundColor(Android.Graphics.Color.Transparent);
				handler.PlatformView.SetPadding(0, 0, 0, 0);
			}
#elif IOS || MACCATALYST
			handler.PlatformView.BackgroundColor = UIKit.UIColor.Clear;
			handler.PlatformView.BorderStyle = UIKit.UITextBorderStyle.None;
#elif WINDOWS
			handler.PlatformView.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
#endif
		});

		Microsoft.Maui.Handlers.DatePickerHandler.Mapper.AppendToMapping("Borderless", (handler, view) =>
		{
#if ANDROID
			if (handler?.PlatformView != null)
			{
				handler.PlatformView.SetBackgroundColor(Android.Graphics.Color.Transparent);
				handler.PlatformView.SetPadding(0, 0, 0, 0);
			}
#elif IOS || MACCATALYST
			handler.PlatformView.BackgroundColor = UIKit.UIColor.Clear;
			handler.PlatformView.BorderStyle = UIKit.UITextBorderStyle.None;
#elif WINDOWS
			handler.PlatformView.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
#endif
		});

		return builder.Build();
	}
}
