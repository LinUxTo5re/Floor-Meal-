using Microsoft.Maui.Controls;
using System;
using System.Threading.Tasks;

namespace ClientLedgerApp;

public partial class DeveloperInfoWindow : ContentPage
{
    private readonly bool _showSupportMessage;

    public DeveloperInfoWindow(bool showSupportMessage = false)
    {
        InitializeComponent();
        _showSupportMessage = showSupportMessage;
        
        if (_showSupportMessage)
        {
            SupportFrame.IsVisible = true;
        }
        
        // Add entrance animation
        _ = AnimateEntrance();
    }
    
    private async Task AnimateEntrance()
    {
        // Start with info frame scaled down and transparent
        InfoFrame.Scale = 0.7;
        InfoFrame.Opacity = 0;
        
        // Animate to full size and opacity
        await Task.WhenAll(
            InfoFrame.ScaleTo(1.0, 400, Easing.CubicOut),
            InfoFrame.FadeTo(1.0, 400, Easing.CubicOut)
        );
    }
    
    private async Task AnimateExit()
    {
        // Animate out
        await Task.WhenAll(
            InfoFrame.ScaleTo(0.8, 250, Easing.CubicIn),
            InfoFrame.FadeTo(0, 250, Easing.CubicIn)
        );
    }
    
    private async void OnCloseClicked(object sender, EventArgs e)
    {
        await AnimateExit();
        await Navigation.PopModalAsync();
    }
    
    private async void OnGitHubTapped(object sender, TappedEventArgs e)
    {
        try
        {
            await Launcher.OpenAsync(new Uri("https://github.com/LinUxTo5re"));
        }
        catch (Exception ex)
        {
            // Fallback: copy to clipboard
            await Clipboard.SetTextAsync("https://github.com/LinUxTo5re");
            
            var alertService = ServiceHelper.GetService<ICustomAlertService>();
            await alertService.ShowInfoAsync("GitHub URL copied to clipboard!", "Link Copied");
        }
    }
    
    private async void OnEmailTapped(object sender, TappedEventArgs e)
    {
        try
        {
            // Try to open email client
            await Launcher.OpenAsync(new Uri("mailto:dev.linuxto5re@gmail.com?subject=FloorMeal%20App%20Support"));
        }
        catch (Exception ex)
        {
            // Fallback: copy to clipboard
            await Clipboard.SetTextAsync("dev.linuxto5re@gmail.com");
            
            var alertService = ServiceHelper.GetService<ICustomAlertService>();
            await alertService.ShowInfoAsync("Email address copied to clipboard!", "Email Copied");
        }
    }
    
    // Handle back button on Android
    protected override bool OnBackButtonPressed()
    {
        _ = Task.Run(async () =>
        {
            await AnimateExit();
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await Navigation.PopModalAsync();
            });
        });
        return true;
    }
}