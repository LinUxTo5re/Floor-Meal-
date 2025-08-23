using System;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;

namespace ClientLedgerApp;

public enum AlertType
{
    Success,
    Error,
    Warning,
    Info,
    Question
}

public interface ICustomAlertService
{
    Task ShowAlertAsync(string title, string message, AlertType type = AlertType.Info, string okText = "OK");
    Task<bool> ShowConfirmAsync(string title, string message, string yesText = "Yes", string noText = "No", AlertType type = AlertType.Question);
    Task ShowSuccessAsync(string message, string title = "Success");
    Task ShowErrorAsync(string message, string title = "Error");
    Task ShowWarningAsync(string message, string title = "Warning");
    Task ShowInfoAsync(string message, string title = "Information");
}

public class CustomAlertService : ICustomAlertService
{
    public async Task ShowAlertAsync(string title, string message, AlertType type = AlertType.Info, string okText = "OK")
    {
        var alertPage = new CustomAlertPage(title, message, type, okText, dismissible: true);
        await Application.Current?.MainPage?.Navigation.PushModalAsync(alertPage);
    }

    public async Task<bool> ShowConfirmAsync(string title, string message, string yesText = "Yes", string noText = "No", AlertType type = AlertType.Question)
    {
        var confirmPage = new CustomConfirmPage(title, message, type, yesText, noText);
        await Application.Current?.MainPage?.Navigation.PushModalAsync(confirmPage);
        return await confirmPage.GetResultAsync();
    }

    public async Task ShowSuccessAsync(string message, string title = "Success")
    {
        await ShowAlertAsync(title, message, AlertType.Success);
    }

    public async Task ShowErrorAsync(string message, string title = "Error")
    {
        await ShowAlertAsync(title, message, AlertType.Error);
    }

    public async Task ShowWarningAsync(string message, string title = "Warning")
    {
        await ShowAlertAsync(title, message, AlertType.Warning);
    }

    public async Task ShowInfoAsync(string message, string title = "Information")
    {
        await ShowAlertAsync(title, message, AlertType.Info);
    }
}