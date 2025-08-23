using System.Linq;
using Microsoft.Maui.Controls;

namespace ClientLedgerApp;

public partial class SettingsPage : ContentPage
{
    private readonly SettingsViewModel _vm;
    private readonly ICustomAlertService _alertService;

    public SettingsPage()
    {
        InitializeComponent();
        _vm = ServiceHelper.GetService<SettingsViewModel>();
        _alertService = ServiceHelper.GetService<ICustomAlertService>();
        BindingContext = _vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        
        // Directly load settings without loader
        await _vm.LoadAsync();
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        // Directly save settings without loader
        await _vm.SaveAsync();
    }

    private void OnAddItemClicked(object sender, EventArgs e)
    {
        _vm.AddItem();
    }

    // Handle both TapGestureRecognizer and Button.Clicked events
    private async void OnAvatarTapped(object sender, EventArgs e)
    {
        try
        {
            await _vm.PickProfileImageAsync();
        }
        catch (Exception ex)
        {
            await _alertService.ShowErrorAsync($"Error picking profile image: {ex.Message}", "Avatar Error");
        }
    }

    private void OnContactTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is Entry entry)
        {
            var d = new string((e.NewTextValue ?? string.Empty).Where(char.IsDigit).ToArray());
            if (d.Length > 10) d = d.Substring(0, 10);
            if (entry.Text != d) entry.Text = d;
        }
    }
}