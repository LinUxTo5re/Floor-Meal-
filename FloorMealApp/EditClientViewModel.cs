using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;
using System.Text.RegularExpressions;

namespace ClientLedgerApp;

public partial class EditClientViewModel : ObservableObject
{
    private readonly IDatabaseService _db;
    private readonly ICustomAlertService _alertService;

    [ObservableProperty] private int clientId;
    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private string? contact;
    [ObservableProperty] private string? notes;

    [ObservableProperty] private bool nameErrorVisible;
    [ObservableProperty] private bool contactErrorVisible;
    [ObservableProperty] private bool isSaving;

    public EditClientViewModel(IDatabaseService db, ICustomAlertService alertService)
    {
        _db = db;
        _alertService = alertService;
    }

    public async Task LoadAsync(int id)
    {
        ClientId = id;
        await _db.InitializeAsync();
        var entity = await _db.GetClientByIdAsync(id);
        if (entity != null)
        {
            Name = entity.Name;
            Contact = entity.Contact;
            Notes = entity.Profile;
        }
    }

    private bool Validate()
    {
        NameErrorVisible = string.IsNullOrWhiteSpace(Name) || Name.Trim().Length > 25;
        ContactErrorVisible = !string.IsNullOrWhiteSpace(Contact) && !Regex.IsMatch(Contact.Trim(), "^\\d{10}$");
        return !NameErrorVisible && !ContactErrorVisible;
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        if (!Validate()) return;
        IsSaving = true;
        try
        {
            var entity = await _db.GetClientByIdAsync(ClientId);
            if (entity == null)
            {
                await _alertService.ShowErrorAsync("Client not found.");
                return;
            }

            string ToTitle(string s)
            {
                if (string.IsNullOrWhiteSpace(s)) return string.Empty;
                var ti = System.Globalization.CultureInfo.CurrentCulture.TextInfo;
                return ti.ToTitleCase(s.ToLower());
            }

            entity.Name = ToTitle(Name.Trim());
            entity.Contact = string.IsNullOrWhiteSpace(Contact) ? null : Contact.Trim();
            entity.Profile = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim();

            await _db.UpdateClientAsync(entity);

            // notify dashboard to refresh
            CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Send(new DataInvalidatedMessage("clients"), "clients");

            await _alertService.ShowSuccessAsync("Client updated successfully.", "Updated");
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            await _alertService.ShowErrorAsync($"Failed to update client: {ex.Message}", "Update Error");
        }
        finally
        {
            IsSaving = false;
        }
    }
}