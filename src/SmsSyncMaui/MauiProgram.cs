using Microsoft.Extensions.Logging;
using SmsSyncMaui.ViewModels;
#if WINDOWS
using Microsoft.Maui.LifecycleEvents;
#endif

namespace SmsSyncMaui;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			})
			.ConfigureLifecycleEvents(events =>
			{
#if WINDOWS
				events.AddWindows(windows => windows.OnWindowCreated(window =>
				{
					SmsSyncMaui.WinUI.TrayService.Initialize(window);
				}));
#endif
			});

		builder.Services.AddMauiBlazorWebView();
		builder.Services.AddSingleton<MainViewModel>();
#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
