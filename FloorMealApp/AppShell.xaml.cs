using Microsoft.Maui.Controls;

namespace ClientLedgerApp;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();
		Routing.RegisterRoute("clientdetail", typeof(ClientDetailPage));
		Routing.RegisterRoute("payments", typeof(PaymentsPage));
		Routing.RegisterRoute("addclient", typeof(AddClientPage));
		Routing.RegisterRoute("settings", typeof(SettingsPage));
		Routing.RegisterRoute("login", typeof(LoginPage));
	}
}
