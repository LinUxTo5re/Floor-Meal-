using System.Threading.Tasks;

namespace ClientLedgerApp;

/// <summary>
/// Extension methods to easily show developer info from anywhere in the app
/// </summary>
public static class DeveloperInfoExtensions
{
    /// <summary>
    /// Shows developer info window
    /// </summary>
    public static async Task ShowDeveloperInfoAsync()
    {
        var developerInfoService = ServiceHelper.GetService<IDeveloperInfoService>();
        await developerInfoService.ShowDeveloperInfoAsync();
    }

    /// <summary>
    /// Shows developer info window with support message
    /// </summary>
    public static async Task ShowSupportInfoAsync()
    {
        var developerInfoService = ServiceHelper.GetService<IDeveloperInfoService>();
        await developerInfoService.ShowSupportInfoAsync();
    }
}