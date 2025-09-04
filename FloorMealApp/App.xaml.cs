using Microsoft.Maui.Controls;
using Microsoft.Maui;

namespace ClientLedgerApp;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell());
	}

	protected override async void OnStart()
	{
		base.OnStart();
		// Check login status; open login if missing
		try
		{
			var db = ServiceHelper.GetService<IDatabaseService>();
			await db.InitializeAsync();
			var json = await db.GetSettingAsync("app.settings.json") ?? string.Empty;
			var data = string.IsNullOrWhiteSpace(json) ? new SettingsData() : (System.Text.Json.JsonSerializer.Deserialize<SettingsData>(json) ?? new SettingsData());
			var mail = data.MailId?.Trim();
			if (string.IsNullOrWhiteSpace(mail))
			{
			// Defer navigation to LoginPage to MainPage.OnAppearing to avoid modal stack conflicts
			}

			// Start periodic sync + gating
			try
			{
				var aws = ServiceHelper.GetService<IAwsSyncService>();
				await aws.EnsureGlobalCredentialsAsync();
				var scheduler = ServiceHelper.GetService<SyncScheduler>();
				scheduler.Start(TimeSpan.FromMinutes(5));
			}
			catch { }

			// Preload profile images disabled (ImgBB disabled for performance)
			// try
			// {
			//     var imgBBService = ServiceHelper.GetService<IImgBBService>();
			//     _ = Task.Run(async () => await imgBBService.PreloadAllProfileImagesAsync());
			// }
			// catch { /* ignore */ }
		}
		catch { }
	}
}