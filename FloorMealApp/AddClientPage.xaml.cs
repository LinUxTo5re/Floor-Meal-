using System.Text.RegularExpressions;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;

namespace ClientLedgerApp;

public partial class AddClientPage : ContentPage
{
    private readonly AddClientViewModel _vm;
    private readonly ICustomAlertService _alertService;

    public AddClientPage(AddClientViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        _alertService = ServiceHelper.GetService<ICustomAlertService>();
        BindingContext = _vm;
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