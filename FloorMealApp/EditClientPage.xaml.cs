using Microsoft.Maui.Controls;

namespace ClientLedgerApp;

public partial class EditClientPage : ContentPage, IQueryAttributable
{
    private readonly EditClientViewModel _vm;

    public EditClientPage(EditClientViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = _vm;
    }

    public async void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("clientId", out var idObj) && idObj is string s && int.TryParse(s, out var id))
        {
            await _vm.LoadAsync(id);
        }
    }

    private void Contact_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is Entry entry)
        {
            var digits = new string((e.NewTextValue ?? string.Empty).Where(char.IsDigit).ToArray());
            if (digits.Length > 10)
                digits = digits.Substring(0, 10);
            if (entry.Text != digits)
                entry.Text = digits;
        }
    }
}