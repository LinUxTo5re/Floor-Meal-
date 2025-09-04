using Microsoft.Maui.Controls;

namespace ClientLedgerApp;

/// <summary>
/// Extension methods to make custom alerts easier to use throughout the app
/// </summary>
public static class AlertExtensions
{
    /// <summary>
    /// Shows a beautiful success alert
    /// </summary>
    public static async Task ShowSuccessAsync(this Page page, string message, string title = "Success")
    {
        var alertService = ServiceHelper.GetService<ICustomAlertService>();
        await alertService.ShowSuccessAsync(message, title);
    }

    /// <summary>
    /// Shows a beautiful error alert
    /// </summary>
    public static async Task ShowErrorAsync(this Page page, string message, string title = "Error")
    {
        var alertService = ServiceHelper.GetService<ICustomAlertService>();
        await alertService.ShowErrorAsync(message, title);
    }

    /// <summary>
    /// Shows a beautiful warning alert
    /// </summary>
    public static async Task ShowWarningAsync(this Page page, string message, string title = "Warning")
    {
        var alertService = ServiceHelper.GetService<ICustomAlertService>();
        await alertService.ShowWarningAsync(message, title);
    }

    /// <summary>
    /// Shows a beautiful info alert
    /// </summary>
    public static async Task ShowInfoAsync(this Page page, string message, string title = "Information")
    {
        var alertService = ServiceHelper.GetService<ICustomAlertService>();
        await alertService.ShowInfoAsync(message, title);
    }

    /// <summary>
    /// Shows a beautiful confirmation dialog
    /// </summary>
    public static async Task<bool> ShowConfirmAsync(this Page page, string title, string message, string yesText = "Yes", string noText = "No", AlertType type = AlertType.Question)
    {
        var alertService = ServiceHelper.GetService<ICustomAlertService>();
        return await alertService.ShowConfirmAsync(title, message, yesText, noText, type);
    }

    /// <summary>
    /// Shows a beautiful custom alert
    /// </summary>
    public static async Task ShowCustomAlertAsync(this Page page, string title, string message, AlertType type = AlertType.Info, string okText = "OK")
    {
        var alertService = ServiceHelper.GetService<ICustomAlertService>();
        await alertService.ShowAlertAsync(title, message, type, okText);
    }
}