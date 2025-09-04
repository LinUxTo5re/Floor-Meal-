using Microsoft.Maui.Controls;
using System.Text.Json;

namespace ClientLedgerApp;

public partial class MainPage : ContentPage
{
    private readonly MainViewModel _vm;
    private readonly ICustomAlertService _alertService;
        private readonly IDeveloperInfoService _developerInfoService;
    private bool _isLoadingDashboard;
    private bool _hasLoadedOnce;

    public MainPage()
    {
        InitializeComponent();
        _vm = ServiceHelper.GetService<MainViewModel>();
        _alertService = ServiceHelper.GetService<ICustomAlertService>();
                _developerInfoService = ServiceHelper.GetService<IDeveloperInfoService>();
        BindingContext = _vm;
        // refresh dashboard on data changes (e.g., edit client)
        CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Register<MainPage, DataInvalidatedMessage, string>(this, "clients", async (r, m) =>
        {
            await _vm.LoadAsync();
        });
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Check login status first, outside of any loader
        try
        {
            var db = ServiceHelper.GetService<IDatabaseService>();
            await db.InitializeAsync();
            var json = await db.GetSettingAsync("app.settings.json") ?? string.Empty;
            var data = string.IsNullOrWhiteSpace(json) ? new SettingsData() : (JsonSerializer.Deserialize<SettingsData>(json) ?? new SettingsData());
            var mail = data.MailId?.Trim();
            if (string.IsNullOrWhiteSpace(mail))
            {
                try { await Shell.Current.GoToAsync("login", false); }
                catch { try { await Shell.Current.Navigation.PushModalAsync(new LoginPage()); } catch { } }
                return;
            }
        }
        catch { }

        if (_hasLoadedOnce || _isLoadingDashboard) return;
        _isLoadingDashboard = true;
        _hasLoadedOnce = true; // Mark before to avoid re-entrancy on modal changes
        try
        {
            await _vm.LoadAsync();
        }
        finally
        {
            _isLoadingDashboard = false;
        }
    }

    private async void SearchBar_TextChanged(object sender, TextChangedEventArgs e)
    {
        await _vm.SearchChangedAsync(e.NewTextValue);
    }

    private async void CollectionView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = e.CurrentSelection?.FirstOrDefault() as ClientSummary;
        if (selected != null)
        {
            await _vm.SelectClientAsync(selected);
            ((CollectionView)sender).SelectedItem = null;
        }
    }

    private async void OnSettingsClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("settings", false);
    }

    private async void OnSyncNowClicked(object sender, EventArgs e)
    {
        {
            var db = ServiceHelper.GetService<IDatabaseService>();
            await db.InitializeAsync();
            var json = await db.GetSettingAsync("app.settings.json") ?? string.Empty;
            var data = string.IsNullOrWhiteSpace(json) ? new SettingsData() : (JsonSerializer.Deserialize<SettingsData>(json) ?? new SettingsData());
            var mail = data.MailId?.Trim();
            if (string.IsNullOrWhiteSpace(mail))
            {
                await _alertService.ShowWarningAsync("Please login first.", "Login Required");
                return;
            }
            
            var scheduler = ServiceHelper.GetService<SyncScheduler>();
            await scheduler.SyncNowAsync();
            
            // Refresh the dashboard after sync
            await _vm.LoadAsync();

            await _alertService.ShowSuccessAsync("Data synchronized successfully!", "Sync Complete");
        }
    }

    private async void OnDeveloperInfoTapped(object sender, TappedEventArgs e)
    {
        await _developerInfoService.ShowDeveloperInfoAsync();
    }
}