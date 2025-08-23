using Microsoft.Maui.Controls;

namespace ClientLedgerApp;

public partial class PaymentsPage : ContentPage, IQueryAttributable
{
    private readonly PaymentsViewModel _vm;
    public PaymentsPage(PaymentsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = _vm;
    }

    public async void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("clientId", out var idObj) && idObj is string s && int.TryParse(s, out var id))
        {
            await _vm.Initialize(id);
        }
    }
}
