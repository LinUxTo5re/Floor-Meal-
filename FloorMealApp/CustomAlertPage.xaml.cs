using Microsoft.Maui.Controls;

namespace ClientLedgerApp;

public partial class CustomAlertPage : ContentPage
{
    private readonly bool _dismissible;

    public CustomAlertPage(string title, string message, AlertType type, string okText = "OK", bool dismissible = true)
    {
        InitializeComponent();
        
        _dismissible = dismissible;
        TitleLabel.Text = title;
        MessageLabel.Text = message;
        OkButton.Text = okText;
        OkButton.IsVisible = _dismissible;
        
        SetAlertStyle(type);
        
        // Add entrance animation
        _ = AnimateEntrance();
    }
    
    private void SetAlertStyle(AlertType type)
    {
        switch (type)
        {
            case AlertType.Success:
                HeaderGrid.BackgroundColor = Color.FromArgb("#4CAF50"); // Green
                IconLabel.Text = "✅";
                OkButton.BackgroundColor = Color.FromArgb("#4CAF50");
                break;
                
            case AlertType.Error:
                HeaderGrid.BackgroundColor = Color.FromArgb("#F44336"); // Red
                IconLabel.Text = "❌";
                OkButton.BackgroundColor = Color.FromArgb("#F44336");
                break;
                
            case AlertType.Warning:
                HeaderGrid.BackgroundColor = Color.FromArgb("#FF9800"); // Orange
                IconLabel.Text = "⚠️";
                OkButton.BackgroundColor = Color.FromArgb("#FF9800");
                break;
                
            case AlertType.Info:
            default:
                HeaderGrid.BackgroundColor = Color.FromArgb("#2196F3"); // Blue
                IconLabel.Text = "ℹ️";
                OkButton.BackgroundColor = Color.FromArgb("#2196F3");
                break;
        }
    }
    
    private async Task AnimateEntrance()
    {
        // Start with alert scaled down and transparent
        AlertFrame.Scale = 0.7;
        AlertFrame.Opacity = 0;
        
        // Animate to full size and opacity
        await Task.WhenAll(
            AlertFrame.ScaleTo(1.0, 300, Easing.CubicOut),
            AlertFrame.FadeTo(1.0, 300, Easing.CubicOut)
        );
    }
    
    private async Task AnimateExit()
    {
        // Animate out
        await Task.WhenAll(
            AlertFrame.ScaleTo(0.8, 200, Easing.CubicIn),
            AlertFrame.FadeTo(0, 200, Easing.CubicIn)
        );
    }
    
    private async void OnOkClicked(object sender, EventArgs e)
    {
        if (!_dismissible) return;
        await AnimateExit();
        await SafePopModalAsync();
    }

    private async Task SafePopModalAsync()
    {
        try
        {
            if (Navigation?.ModalStack?.Count > 0)
            {
                await Navigation.PopModalAsync();
            }
        }
        catch { }
    }
    
    // Handle back button on Android
    protected override bool OnBackButtonPressed()
    {
        if (!_dismissible) return true; // block back button
        _ = Task.Run(async () =>
        {
            await AnimateExit();
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await SafePopModalAsync();
            });
        });
        return true;
    }
}