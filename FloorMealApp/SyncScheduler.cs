using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Networking;

namespace ClientLedgerApp;

public class SyncScheduler
{
    private readonly IAwsSyncService _aws;
    private readonly IDatabaseService _db;
    private readonly ICustomAlertService _alertService;
    private readonly IImgBBService _imgBB;
    private bool _started;
    private bool _notifiedInactive;
    private bool _syncInProgress;
    private DateTime _lastConnectivitySync = DateTime.MinValue;

    public SyncScheduler(IAwsSyncService aws, IDatabaseService db, ICustomAlertService alertService)
    {
        _aws = aws;
        _db = db;
        _alertService = alertService;
        _imgBB = ServiceHelper.GetService<IImgBBService>();
    }

    public void Start(TimeSpan interval)
    {
        if (_started) return;
        _started = true;
        Device.StartTimer(interval, () =>
        {
            _ = TickAsync();
            return true; // keep running
        });

        try
        {
            Connectivity.ConnectivityChanged -= OnConnectivityChanged;
            Connectivity.ConnectivityChanged += OnConnectivityChanged;
        }
        catch { }
    }

    public async Task SyncNowAsync()
    {
        await TickAsync();
    }

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        if (e.NetworkAccess != NetworkAccess.Internet) return;
        if (DateTime.UtcNow - _lastConnectivitySync < TimeSpan.FromSeconds(10)) return;
        _lastConnectivitySync = DateTime.UtcNow;
        _ = SyncNowAsync();
    }

    private async Task TickAsync()
    {
        if (_syncInProgress) return;
        _syncInProgress = true;
        try
        {
            await _db.InitializeAsync();
            // Load current user
            var json = await _db.GetSettingAsync("app.settings.json") ?? string.Empty;
            var data = string.IsNullOrWhiteSpace(json) ? new SettingsData() : (JsonSerializer.Deserialize<SettingsData>(json) ?? new SettingsData());
            var mail = data.MailId?.Trim();
            if (string.IsNullOrWhiteSpace(mail))
            {
                // Still try to process non-AWS jobs (e.g., photo uploads) without MailId
                await ProcessOutboxAsync(mailId: null);
                return;
            }

            // Ensure we have global SMTP cached locally
            await _aws.EnsureGlobalCredentialsAsync();
            // Sync all entities
            await _aws.SyncAllForUserAsync(mail);
            // Refresh dashboard after sync completes
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    var mainVm = ServiceHelper.GetService<MainViewModel>();
                    await mainVm.LoadAsync();
                }
                catch { }
            });

            // Check user active flag from local credentials
            var cred = await _db.GetCredentialsByEmailAsync(mail);
            if (cred != null && cred.IsActive == 0)
            {
                if (_notifiedInactive) return;
                _notifiedInactive = true;
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    try
                    {
                        await _alertService.ShowErrorAsync("Your account has been deactivated. Please contact support.", "Access Blocked");
                    }
                    catch { }
                    // Clear login and route to login
                    try
                    {
                        data.MailId = string.Empty;
                        var newJson = JsonSerializer.Serialize(data);
                        await _db.SetSettingAsync("app.settings.json", newJson);
                        await Shell.Current.GoToAsync("login");
                    }
                    catch { }
                });
            }
            else
            {
                _notifiedInactive = false; // reset if reactivated
            }

            // Process durable outbox jobs (uploads and AWS pushes) with retries
            await ProcessOutboxAsync(mail);
        }
        catch
        {
            // swallow to keep timer alive
        }
        finally
        {
            _syncInProgress = false;
        }
    }

    private async Task ProcessOutboxAsync(string? mailId)
    {
        var now = DateTime.UtcNow;
        var jobs = await _db.GetDueOutboxJobsAsync(now, max: 20);
        foreach (var job in jobs)
        {
            bool success = false;
            string? error = null;
            try
            {
                switch (job.Type)
                {
                    case "UploadClientPhoto":
                    {
                        var payload = JsonSerializer.Deserialize<UploadClientPhotoPayload>(job.PayloadJson);
                        if (payload == null) { success = true; break; }
                        var client = await _db.GetClientByIdAsync(payload.ClientId);
                        if (client == null) { success = true; break; }
                        if (string.IsNullOrWhiteSpace(payload.LocalPhotoPath) || !System.IO.File.Exists(payload.LocalPhotoPath))
                        { success = true; break; }
                        var isConfigured = await _imgBB.IsConfiguredAsync();
                        if (!isConfigured) { success = false; error = "ImgBB not configured"; break; }
                        var clientName = client.Name.Trim().Replace(" ", "_");
                        var resp = await _imgBB.UploadImageAsync(payload.LocalPhotoPath, $"client_{clientName}_{DateTime.Now:yyyyMMdd_HHmmss}");
                        if (resp?.success == true && resp.data != null)
                        {
                            client.PhotoPath = resp.data.display_url;
                            client.PhotoDeleteUrl = resp.data.delete_url;
                            await _db.UpdateClientAsync(client);
                            success = true;
                        }
                        else
                        {
                            error = "ImgBB upload failed";
                        }
                        break;
                    }
                    case "AwsPutClient":
                    {
                        if (string.IsNullOrWhiteSpace(mailId)) { error = "No MailId"; break; }
                        var payload = JsonSerializer.Deserialize<AwsPutClientPayload>(job.PayloadJson);
                        if (payload == null) { success = true; break; }
                        var client = await _db.GetClientByIdAsync(payload.ClientId);
                        if (client == null) { success = true; break; }
                        await _aws.PutClientAsync(mailId, client);
                        success = true;
                        break;
                    }
                    case "AwsPutOrder":
                    {
                        if (string.IsNullOrWhiteSpace(mailId)) { error = "No MailId"; break; }
                        var payload = JsonSerializer.Deserialize<AwsPutOrderPayload>(job.PayloadJson);
                        if (payload == null) { success = true; break; }
                        var order = await _db.GetOrderByIdAsync(payload.OrderId);
                        if (order == null) { success = true; break; }
                        await _aws.PutOrderAsync(mailId, order);
                        success = true;
                        break;
                    }
                    case "AwsPutPayment":
                    {
                        if (string.IsNullOrWhiteSpace(mailId)) { error = "No MailId"; break; }
                        var payload = JsonSerializer.Deserialize<AwsPutPaymentPayload>(job.PayloadJson);
                        if (payload == null) { success = true; break; }
                        var payment = await _db.GetPaymentByIdAsync(payload.PaymentId);
                        if (payment == null) { success = true; break; }
                        await _aws.PutPaymentAsync(mailId, payment);
                        success = true;
                        break;
                    }
                    default:
                        success = true; // unknown job, drop
                        break;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            if (success)
            {
                await _db.DeleteOutboxJobAsync(job.Id);
            }
            else
            {
                job.Attempts++;
                job.LastError = error;
                var delayMinutes = Math.Min(60, (int)Math.Pow(2, Math.Max(1, job.Attempts))); // 2,4,8,16,32,60
                job.NextAttemptUtc = DateTime.UtcNow.AddMinutes(delayMinutes);
                await _db.UpdateOutboxJobAsync(job);
            }
        }
    }

    private record UploadClientPhotoPayload(int ClientId, string LocalPhotoPath);
    private record AwsPutClientPayload(int ClientId);
    private record AwsPutOrderPayload(int OrderId);
    private record AwsPutPaymentPayload(int PaymentId);
}