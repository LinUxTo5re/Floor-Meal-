using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Text.RegularExpressions;
using Microsoft.Maui.Controls;
using System.Text.Json;
using System.Diagnostics;

namespace ClientLedgerApp;

public partial class AddClientViewModel : ObservableObject
{
    private readonly IDatabaseService _db;
    private readonly IAwsSyncService _aws;
    private readonly ICustomAlertService _alertService;
    
    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private string? contact;
    [ObservableProperty] private string? notes;

    [ObservableProperty] private bool nameErrorVisible;
    [ObservableProperty] private bool contactErrorVisible;
    [ObservableProperty] private bool isSaving;

    public AddClientViewModel(IDatabaseService db, IAwsSyncService aws, ICustomAlertService alertService)
    {
        _db = db;
        _aws = aws;
        _alertService = alertService;
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
            // Normalize to Title Case for client name
            string ToTitle(string s)
            {
                if (string.IsNullOrWhiteSpace(s)) return string.Empty;
                var ti = System.Globalization.CultureInfo.CurrentCulture.TextInfo;
                return ti.ToTitleCase(s.ToLower());
            }

            var client = new Client
            {
                Name = ToTitle(Name.Trim()),
                Contact = string.IsNullOrWhiteSpace(Contact) ? null : Contact.Trim(),
                Profile = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim()
            };

            await _db.AddClientAsync(client);

            // Enqueue durable outbox jobs for background processing
                        // Only enqueue AWS push if sync is enabled and MailId present
            try
            {
                var sjson = await _db.GetSettingAsync("app.settings.json") ?? string.Empty;
                var sdata = string.IsNullOrWhiteSpace(sjson) ? new SettingsData() : (JsonSerializer.Deserialize<SettingsData>(sjson) ?? new SettingsData());
                var mail = sdata.MailId?.Trim();
                if (false && !string.IsNullOrWhiteSpace(mail) && sdata.EnableAwsSync)
                {
                // Immediate AWS enqueue disabled; periodic background sync will push changes.
                var awsPayload = new { ClientId = client.Id };
                await _db.EnqueueOutboxJobAsync(new OutboxJob
                {
                Type = "AwsPutClient",
                PayloadJson = JsonSerializer.Serialize(awsPayload),
                Attempts = 0,
                MaxAttempts = 5,
                NextAttemptUtc = DateTime.UtcNow
                });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Enqueue AWS client job failed: {ex.Message}");
            }

            // Immediate sync disabled to keep add local-only
            // try { var scheduler = ServiceHelper.GetService<SyncScheduler>(); _ = scheduler.SyncNowAsync(); } catch { }

            // notify dashboard to refresh, navigate back immediately, then show success
            CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Send(new DataInvalidatedMessage("clients"), "clients");
            await Shell.Current.GoToAsync("..");
            await _alertService.ShowSuccessAsync($"Client '{client.Name}' has been added successfully!");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Save failed: {ex}");
            // Show error to user with beautiful alert
            await _alertService.ShowErrorAsync($"Failed to save client: {ex.Message}", "Save Error");
        }
        finally
        {
            IsSaving = false;
        }
    }
}