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
    private readonly IImgBBService _imgBBService;
    private readonly ICustomAlertService _alertService;
    
    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private string? contact;
    [ObservableProperty] private string? notes;
    [ObservableProperty] private string? photoPath;

    [ObservableProperty] private bool nameErrorVisible;
    [ObservableProperty] private bool contactErrorVisible;
    [ObservableProperty] private bool isSaving;

    public AddClientViewModel(IDatabaseService db, IAwsSyncService aws, IImgBBService imgBBService, ICustomAlertService alertService)
    {
        _db = db;
        _aws = aws;
        _imgBBService = imgBBService;
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

            // Do not block UI with uploads; keep local path for now, upload in background after save
            string? imgBBUrl = !string.IsNullOrWhiteSpace(PhotoPath) && System.IO.File.Exists(PhotoPath) ? PhotoPath : null;
            string? imgBBDeleteUrl = null;
            var localPhotoPath = imgBBUrl; // capture for background upload

            var client = new Client
            {
                Name = ToTitle(Name.Trim()),
                Contact = string.IsNullOrWhiteSpace(Contact) ? null : Contact.Trim(),
                Profile = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
                PhotoPath = imgBBUrl,
                PhotoDeleteUrl = imgBBDeleteUrl
            };

            await _db.AddClientAsync(client);

            // Enqueue durable outbox jobs for background processing
            if (!string.IsNullOrWhiteSpace(localPhotoPath) && System.IO.File.Exists(localPhotoPath))
            {
                var payload = new { ClientId = client.Id, LocalPhotoPath = localPhotoPath };
                await _db.EnqueueOutboxJobAsync(new OutboxJob
                {
                    Type = "UploadClientPhoto",
                    PayloadJson = JsonSerializer.Serialize(payload),
                    Attempts = 0,
                    MaxAttempts = 5,
                    NextAttemptUtc = DateTime.UtcNow
                });
            }
            // Only enqueue AWS push if sync is enabled and MailId present
            try
            {
                var sjson = await _db.GetSettingAsync("app.settings.json") ?? string.Empty;
                var sdata = string.IsNullOrWhiteSpace(sjson) ? new SettingsData() : (JsonSerializer.Deserialize<SettingsData>(sjson) ?? new SettingsData());
                var mail = sdata.MailId?.Trim();
                if (!string.IsNullOrWhiteSpace(mail) && sdata.EnableAwsSync)
                {
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

            // Kick the scheduler to process outbox soon (non-blocking)
            try { var scheduler = ServiceHelper.GetService<SyncScheduler>(); _ = scheduler.SyncNowAsync(); } catch { }

            // Show success message and navigate back immediately
            await _alertService.ShowSuccessAsync($"Client '{client.Name}' has been added successfully!");
            await Shell.Current.GoToAsync("..");
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