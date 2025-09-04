using System;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;

namespace ClientLedgerApp;

public interface IDeveloperInfoService
{
    Task ShowDeveloperInfoAsync(bool showSupportMessage = false);
    Task ShowSupportInfoAsync(); // Shortcut for support scenarios
}

public class DeveloperInfoService : IDeveloperInfoService
{
    private bool _isShowing;

    public async Task ShowDeveloperInfoAsync(bool showSupportMessage = false)
    {
        if (_isShowing) return;
        
        _isShowing = true;
        
        try
        {
            var developerInfoWindow = new DeveloperInfoWindow(showSupportMessage);
            
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await Application.Current?.MainPage?.Navigation.PushModalAsync(developerInfoWindow, false);
            });
        }
        finally
        {
            _isShowing = false;
        }
    }

    public async Task ShowSupportInfoAsync()
    {
        await ShowDeveloperInfoAsync(showSupportMessage: true);
    }
}