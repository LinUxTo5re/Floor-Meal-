using System.Linq;
using Microsoft.Maui.Controls;

namespace ClientLedgerApp;

public partial class SettingsPage : ContentPage
{
    private readonly SettingsViewModel _vm;

    public SettingsPage()
    {
        InitializeComponent();
        _vm = ServiceHelper.GetService<SettingsViewModel>();
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