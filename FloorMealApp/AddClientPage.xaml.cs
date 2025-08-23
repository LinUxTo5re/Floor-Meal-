using System.Text.RegularExpressions;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;

namespace ClientLedgerApp;

public partial class AddClientPage : ContentPage
{
    private readonly AddClientViewModel _vm;
    private readonly IPermissionService _permissionService;
    private readonly ICustomAlertService _alertService;

    public AddClientPage(AddClientViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        _permissionService = ServiceHelper.GetService<IPermissionService>();
        _alertService = ServiceHelper.GetService<ICustomAlertService>();
        BindingContext = _vm;
    }

    private async void PickPhoto_Clicked(object sender, EventArgs e)
    {
        try
        {
            // Check storage/media permission first
            var hasStoragePermission = await _permissionService.CheckAndRequestStoragePermissionAsync();
            if (!hasStoragePermission)
            {
                await _alertService.ShowWarningAsync(
                    "Storage permission is required to pick photos from your device. Please enable it in app settings.",
                    "Permission Required");
                return;
            }

#if WINDOWS
            var fileTypes = new List<string> { ".jpg", ".jpeg", ".png" };
            var result = await FilePicker.PickAsync(new PickOptions
            {
                PickerTitle = "Pick profile photo",
                FileTypes = FilePickerFileType.Images
            });
#else
            var result = await FilePicker.PickAsync(PickOptions.Images);
#endif

            if (result != null)
            {
                _vm.PhotoPath = result.FullPath;
                PhotoPreview.Source = ImageSource.FromFile(result.FullPath);
                await _alertService.ShowSuccessAsync("Photo selected successfully!");
            }
        }
        catch (Exception ex)
        {
            await _alertService.ShowErrorAsync($"Failed to pick photo: {ex.Message}");
        }
    }

    private void Contact_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is Entry entry)
        {
            var digits = new string((e.NewTextValue ?? string.Empty).Where(char.IsDigit).ToArray());
            if (digits.Length > 10)
                digits = digits.Substring(0, 10);
            if (entry.Text != digits)
                entry.Text = digits;
        }
    }

    private async void CapturePhoto_Clicked(object sender, EventArgs e)
    {
        try
        {
            // Check if camera is available
            if (!MediaPicker.Default.IsCaptureSupported)
            {
                await _alertService.ShowWarningAsync(
                    "Camera capture is not supported on this device.",
                    "Not Supported");
                return;
            }

            // Check camera permission
            var hasCameraPermission = await _permissionService.CheckAndRequestCameraPermissionAsync();
            if (!hasCameraPermission)
            {
                var cameraStatus = await _permissionService.GetCameraPermissionStatusAsync();
                var message = cameraStatus switch
                {
                    PermissionStatus.Denied => "Camera permission was denied. Please enable camera access in your device settings to take photos.",
                    PermissionStatus.Disabled => "Camera is disabled on this device.",
                    PermissionStatus.Restricted => "Camera access is restricted on this device.",
                    _ => "Camera permission is required to take photos. Please enable it in app settings."
                };

                var shouldOpenSettings = await _alertService.ShowConfirmAsync(
                    "Camera Permission Required",
                    message + "\n\nWould you like to open settings?",
                    "Open Settings", "Cancel",
                    AlertType.Warning);

                if (shouldOpenSettings && cameraStatus == PermissionStatus.Denied)
                {
                    try
                    {
                        await Launcher.OpenAsync(new Uri("app-settings:"));
                    }
                    catch
                    {
                        await _alertService.ShowInfoAsync(
                            "Please manually open Settings > Apps > FloorMeal > Permissions and enable Camera access.",
                            "Manual Setup Required");
                    }
                }
                return;
            }

            // Check storage permission for saving the photo
            var hasStoragePermission = await _permissionService.CheckAndRequestStoragePermissionAsync();
            if (!hasStoragePermission)
            {
                await _alertService.ShowWarningAsync(
                    "Storage permission is required to save photos. Please enable it in app settings.",
                    "Storage Permission Required");
                return;
            }

            // Capture photo
            var capture = await MediaPicker.CapturePhotoAsync(new MediaPickerOptions
            {
                Title = $"client-photo-{DateTime.Now:yyyyMMdd-HHmmss}.jpg"
            });

            if (capture == null) 
            {
                // User cancelled
                return;
            }

            // Save to local app data directory
            var newFile = System.IO.Path.Combine(FileSystem.AppDataDirectory, capture.FileName);
            using (var stream = await capture.OpenReadAsync())
            using (var file = System.IO.File.OpenWrite(newFile))
            {
                await stream.CopyToAsync(file);
            }

            _vm.PhotoPath = newFile;
            PhotoPreview.Source = ImageSource.FromFile(newFile);
            
            await _alertService.ShowSuccessAsync("Photo captured successfully!");
        }
        catch (FeatureNotSupportedException)
        {
            await _alertService.ShowWarningAsync(
                "Camera capture is not supported on this device.",
                "Not Supported");
        }
        catch (PermissionException)
        {
            await _alertService.ShowErrorAsync(
                "Camera permission was denied. Please enable camera access in your device settings.",
                "Permission Denied");
        }
        catch (Exception ex)
        {
            await _alertService.ShowErrorAsync(
                $"Failed to capture photo: {ex.Message}",
                "Camera Error");
        }
    }
}