using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace ClientLedgerApp;

public interface IPermissionService
{
    Task<bool> CheckAndRequestCameraPermissionAsync();
    Task<bool> CheckAndRequestStoragePermissionAsync();
    Task<bool> CheckAndRequestMediaPermissionAsync();
    Task<PermissionStatus> GetCameraPermissionStatusAsync();
    Task<PermissionStatus> GetStoragePermissionStatusAsync();
}

public class PermissionService : IPermissionService
{
    public async Task<bool> CheckAndRequestCameraPermissionAsync()
    {
        try
        {
            var status = await Permissions.CheckStatusAsync<Permissions.Camera>();

            if (status == PermissionStatus.Granted)
            {
                return true;
            }

            if (status == PermissionStatus.Denied && DeviceInfo.Platform == DevicePlatform.iOS)
            {
                return false;
            }

            status = await Permissions.RequestAsync<Permissions.Camera>();
            return status == PermissionStatus.Granted;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"PermissionService: Error checking camera permission: {ex}");
            return false;
        }
    }

    public async Task<bool> CheckAndRequestStoragePermissionAsync()
    {
        try
        {
            // For Android 13+ (API 33+), we need READ_MEDIA_IMAGES instead of READ_EXTERNAL_STORAGE
            if (DeviceInfo.Platform == DevicePlatform.Android)
            {
                return await CheckAndRequestMediaPermissionAsync();
            }

            var status = await Permissions.CheckStatusAsync<Permissions.StorageRead>();

            if (status == PermissionStatus.Granted)
            {
                return true;
            }

            if (status == PermissionStatus.Denied && DeviceInfo.Platform == DevicePlatform.iOS)
            {
                return false;
            }

            status = await Permissions.RequestAsync<Permissions.StorageRead>();
            return status == PermissionStatus.Granted;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"PermissionService: Error checking storage permission: {ex}");
            return false;
        }
    }

    public async Task<bool> CheckAndRequestMediaPermissionAsync()
    {
        try
        {
            var status = await Permissions.CheckStatusAsync<Permissions.Media>();

            if (status == PermissionStatus.Granted)
            {
                return true;
            }

            if (status == PermissionStatus.Denied && DeviceInfo.Platform == DevicePlatform.iOS)
            {
                return false;
            }

            status = await Permissions.RequestAsync<Permissions.Media>();
            return status == PermissionStatus.Granted;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"PermissionService: Error checking media permission: {ex}");
            return false;
        }
    }

    public async Task<PermissionStatus> GetCameraPermissionStatusAsync()
    {
        try
        {
            return await Permissions.CheckStatusAsync<Permissions.Camera>();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"PermissionService: Error getting camera permission status: {ex}");
            return PermissionStatus.Unknown;
        }
    }

    public async Task<PermissionStatus> GetStoragePermissionStatusAsync()
    {
        try
        {
            if (DeviceInfo.Platform == DevicePlatform.Android)
            {
                return await Permissions.CheckStatusAsync<Permissions.Media>();
            }
            return await Permissions.CheckStatusAsync<Permissions.StorageRead>();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"PermissionService: Error getting storage permission status: {ex}");
            return PermissionStatus.Unknown;
        }
    }
}

/// <summary>
/// Extension methods for better permission handling
/// </summary>
public static class PermissionExtensions
{
    /// <summary>
    /// Shows a user-friendly message based on permission status
    /// </summary>
    public static string GetUserFriendlyMessage(this PermissionStatus status, string permissionType)
    {
        return status switch
        {
            PermissionStatus.Granted => $"{permissionType} permission granted.",
            PermissionStatus.Denied => $"{permissionType} permission denied. Please enable it in app settings.",
            PermissionStatus.Disabled => $"{permissionType} is disabled on this device.",
            PermissionStatus.Limited => $"{permissionType} permission is limited.",
            PermissionStatus.Restricted => $"{permissionType} permission is restricted.",
            _ => $"{permissionType} permission status unknown."
        };
    }

    /// <summary>
    /// Checks if permission can be requested again
    /// </summary>
    public static bool CanRequestAgain(this PermissionStatus status)
    {
        return status == PermissionStatus.Unknown || status == PermissionStatus.Denied;
    }
}