using System.Text.Json;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;

namespace ClientLedgerApp;

public partial class LoginViewModel : ObservableObject
{
    private readonly IDatabaseService _db;
    private readonly IEmailSender _emailSender;
    private readonly IAwsSyncService _aws;
    private readonly ICustomAlertService _alertService;
    private const string SettingsPrefKey = "app.settings.json";

    [ObservableProperty]
    private string email = string.Empty;

    [ObservableProperty]
    private string otp = string.Empty;

    [ObservableProperty]
    private bool sent;

    private string? _serverOtp;
    private DateTime _otpExpires;

    public LoginViewModel(IDatabaseService db, IEmailSender emailSender, IAwsSyncService aws, ICustomAlertService alertService)
    {
        _db = db;
        _emailSender = emailSender;
        _aws = aws;
        _alertService = alertService;
    }

    private static bool IsGmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        email = email.Trim();
        // basic email pattern, and must end with @gmail.com
        var ok = Regex.IsMatch(email, @"^[^@\s]+@gmail\.com$", RegexOptions.IgnoreCase);
        return ok;
    }

    [RelayCommand]
    public async Task SendOtpAsync()
    {
        await _db.InitializeAsync();
        if (!IsGmail(Email))
        {
            await _alertService.ShowWarningAsync("Please enter a valid Gmail address (example@gmail.com).", "Invalid Email");
            return;
        }
        var rnd = new Random();
        _serverOtp = rnd.Next(100000, 999999).ToString();
        _otpExpires = DateTime.UtcNow.AddMinutes(10);
        try
        {
            await _emailSender.SendOtpAsync(Email.Trim(), _serverOtp);
            Sent = true;
            await _alertService.ShowSuccessAsync($"Verification code sent to {Email.Trim()}", "Code Sent");
        }
        catch (Exception ex)
        {
            await _alertService.ShowErrorAsync($"Failed to send code: {ex.Message}", "Send Failed");
        }
    }

    [RelayCommand]
    public async Task VerifyAsync()
    {
        await _db.InitializeAsync();
        if (string.IsNullOrWhiteSpace(_serverOtp) || DateTime.UtcNow > _otpExpires)
        {
            await _alertService.ShowWarningAsync("Please request a new code.", "Code Expired");
            return;
        }
        if (!string.Equals(Otp?.Trim(), _serverOtp, StringComparison.Ordinal))
        {
            await _alertService.ShowErrorAsync("The code you entered is incorrect.", "Invalid Code");
            return;
        }
        // Check remote user flags (IsActive, DevMessage) and sync data before persisting email
        var (isActive, devMessage) = await _aws.GetUserFlagsAsync(Email.Trim());
        if (isActive == 0)
        {
            // Show non-dismissible popup if inactive
            var alertPage = new CustomAlertPage("Access Blocked", string.IsNullOrWhiteSpace(devMessage) ? "Your account is inactive. Please contact support." : devMessage, AlertType.Error, okText: "OK", dismissible: false);
            await Application.Current?.MainPage?.Navigation.PushModalAsync(alertPage);
            return;
        }
        var remoteCred = await _aws.GetRemoteCredentialsAsync(Email.Trim());
        if (remoteCred != null)
            await _db.UpsertCredentialsAsync(remoteCred);

        await _aws.SyncAllForUserAsync(Email.Trim());

        // Persist email after successful verification
        var json = await _db.GetSettingAsync(SettingsPrefKey) ?? string.Empty;
        SettingsData data;
        var hadMail = false;
        if (!string.IsNullOrWhiteSpace(json))
        {
            data = JsonSerializer.Deserialize<SettingsData>(json) ?? new SettingsData();
            hadMail = !string.IsNullOrWhiteSpace(data.MailId);
        }
        else
        {
            data = new SettingsData();
        }
        data.MailId = Email.Trim();
        // Force AWS sync to be enabled in persisted settings
        data.EnableAwsSync = true;
        var newJson = JsonSerializer.Serialize(data);
        await _db.SetSettingAsync(SettingsPrefKey, newJson);

        var isNewUser = !hadMail;
        if (isNewUser)
        {
            await _alertService.ShowInfoAsync("Please update your Settings (Floor Meal Name, item prices, etc.).", "Welcome!");
            try { await Shell.Current.GoToAsync("settings"); } catch { }
        }
        else
        {
            await _alertService.ShowSuccessAsync("Login successful!", "Welcome Back");
        }

        // Navigate away from login (use Shell route only, avoid modal pop to prevent loader conflicts)
        try { await Shell.Current.GoToAsync("//MainPage"); } catch { }
    }
}