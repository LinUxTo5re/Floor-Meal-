using Microsoft.Maui.Controls;

namespace ClientLedgerApp;

public partial class CustomConfirmPage : ContentPage
{
    private TaskCompletionSource<bool> _taskCompletionSource;
    
    public CustomConfirmPage(string title, string message, AlertType type, string yesText = "Yes", string noText = "No")
    {
        InitializeComponent();
        
        _taskCompletionSource = new TaskCompletionSource<bool>();
        
        TitleLabel.Text = title;
        MessageLabel.Text = message;
        YesButton.Text = yesText;
        NoButton.Text = noText;
        
        SetConfirmStyle(type);
        
        // Add entrance animation
        _ = AnimateEntrance();
    }
    
    private void SetConfirmStyle(AlertType type)
    {
        switch (type)
        {
            case AlertType.Success:
                HeaderGrid.BackgroundColor = Color.FromArgb("#4CAF50"); // Green
                IconLabel.Text = "✅";
                YesButton.BackgroundColor = Color.FromArgb("#4CAF50");
                break;
                
            case AlertType.Error:
                HeaderGrid.BackgroundColor = Color.FromArgb("#F44336"); // Red
                IconLabel.Text = "❌";
                YesButton.BackgroundColor = Color.FromArgb("#F44336");
                break;
                
            case AlertType.Warning:
                HeaderGrid.BackgroundColor = Color.FromArgb("#FF9800"); // Orange
                IconLabel.Text = "⚠️";
                YesButton.BackgroundColor = Color.FromArgb("#FF9800");
                break;
                
            case AlertType.Question:
            default:
                HeaderGrid.BackgroundColor = Color.FromArgb("#FF9800"); // Orange
                IconLabel.Text = "❓";
                YesButton.BackgroundColor = Color.FromArgb("#FF9800");
                break;
        }
    }
    
    private async Task AnimateEntrance()
    {
        // Start with confirm scaled down and transparent
        ConfirmFrame.Scale = 0.7;
        ConfirmFrame.Opacity = 0;
        
        // Animate to full size and opacity
        await Task.WhenAll(
            ConfirmFrame.ScaleTo(1.0, 300, Easing.CubicOut),
            ConfirmFrame.FadeTo(1.0, 300, Easing.CubicOut)
        );
    }
    
    private async Task AnimateExit()
    {
        // Animate out
        await Task.WhenAll(
            ConfirmFrame.ScaleTo(0.8, 200, Easing.CubicIn),
            ConfirmFrame.FadeTo(0, 200, Easing.CubicIn)
        );
    }
    
    private async void OnYesClicked(object sender, EventArgs e)
    {
        await AnimateExit();
        await Navigation.PopModalAsync();
        _taskCompletionSource.SetResult(true);
    }
    
    private async void OnNoClicked(object sender, EventArgs e)
    {
        await AnimateExit();
        await Navigation.PopModalAsync();
        _taskCompletionSource.SetResult(false);
    }
    
    public Task<bool> GetResultAsync()
    {
        return _taskCompletionSource.Task;
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
                _taskCompletionSource.SetResult(false);
            });
        });
        return true;
    }
}