using Microsoft.Extensions.Logging;

namespace MobileSmsSync;

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
			});

		builder.Services.AddMauiBlazorWebView();
		builder.Services.AddSingleton<MobileSmsSync.Services.ISmsSyncPlatformService, MobileSmsSync.Services.SmsSyncPlatformService>();
		builder.Services.AddSingleton<MobileSmsSync.ViewModels.MainViewModel>();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
