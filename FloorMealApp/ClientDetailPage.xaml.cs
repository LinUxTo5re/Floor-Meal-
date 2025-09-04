using Microsoft.Maui.Controls;

namespace ClientLedgerApp;

public partial class ClientDetailPage : ContentPage, IQueryAttributable
{
    private readonly ClientDetailViewModel _vm;
    public ClientDetailPage(ClientDetailViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = _vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // Reload settings in case they were changed (e.g., new item added)
        try { await _vm.RefreshSettingsAsync(); } catch { }
    }

    public async void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("clientId", out var idObj) && idObj is string s && int.TryParse(s, out var id))
        {
            await _vm.Initialize(id);
        }
    }
}