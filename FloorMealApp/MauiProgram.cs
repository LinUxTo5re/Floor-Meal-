using Microsoft.Extensions.Logging;
using CommunityToolkit.Maui;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace ClientLedgerApp;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseMauiCommunityToolkit()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			})
			.ConfigureMauiHandlers(handlers =>
			{
#if ANDROID
handlers.AddHandler(typeof(Entry), typeof(Microsoft.Maui.Handlers.EntryHandler));
#endif
#if WINDOWS
Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("CustomBorder", (handler, view) =>
{
if (handler.PlatformView is Microsoft.UI.Xaml.Controls.TextBox tb)
{
// Dark black border on focus
var brush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.Black);
tb.BorderBrush = brush;
}
});
#endif
			});

		// DI registrations
		builder.Services.AddSingleton<IDatabaseService>(provider => new DatabaseService());
		builder.Services.AddSingleton<MainViewModel>();
		builder.Services.AddTransient<ClientDetailViewModel>();
		builder.Services.AddTransient<ClientDetailPage>();
		builder.Services.AddTransient<PaymentsViewModel>();
		builder.Services.AddTransient<PaymentsPage>();
		builder.Services.AddTransient<AddClientViewModel>();
		builder.Services.AddTransient<AddClientPage>();
		builder.Services.AddSingleton<SettingsViewModel>();
		builder.Services.AddTransient<SettingsPage>();
		builder.Services.AddSingleton<LoginViewModel>();
		builder.Services.AddTransient<LoginPage>();
		// Use Gmail SMTP for OTP sending
		builder.Services.AddSingleton<IEmailSender, GmailSmtpEmailSender>();
		// AWS sync service
		builder.Services.AddSingleton<IAwsSyncService, AwsSyncService>();
		builder.Services.AddSingleton<SyncScheduler>();
		// ImgBB image upload service
		builder.Services.AddSingleton<IImgBBService, ImgBBService>();
		// Permission service for camera and storage
		builder.Services.AddSingleton<IPermissionService, PermissionService>();
		// Custom alert service for beautiful dialogs
		builder.Services.AddSingleton<ICustomAlertService, CustomAlertService>();
				// Developer info service for support and about info
		builder.Services.AddSingleton<IDeveloperInfoService, DeveloperInfoService>();
		// Note: ResendEmailSender is removed from DI and not used.

#if DEBUG
		builder.Logging.AddDebug();
#endif

		var app = builder.Build();
		ServiceHelper.Initialize(app.Services);
		return app;
	}
}