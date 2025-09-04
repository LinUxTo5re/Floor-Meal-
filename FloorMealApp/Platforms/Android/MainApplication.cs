using Android.App;
using Android.Runtime;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;

namespace ClientLedgerApp;

[Application]
public class MainApplication : MauiApplication
{
	public MainApplication(IntPtr handle, JniHandleOwnership ownership)
		: base(handle, ownership)
	{
	}

	public override void OnCreate()
	{
		base.OnCreate();
		// Schedule periodic background sync job
		SyncJobService.Schedule(this);
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
