using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;

namespace ClientLedgerApp;

// ImgBB API Response Models
public class ImgBBResponse
{
    public ImgBBData? data { get; set; }
    public bool success { get; set; }
    public int status { get; set; }
}

public class ImgBBData
{
    public string? id { get; set; }
    public string? title { get; set; }
    public string? url_viewer { get; set; }
    public string? url { get; set; }
    public string? display_url { get; set; }
    public int width { get; set; }
    public int height { get; set; }
    public long size { get; set; }
    public long time { get; set; }
    public int expiration { get; set; }
    public ImgBBImageInfo? image { get; set; }
    public ImgBBImageInfo? thumb { get; set; }
    public ImgBBImageInfo? medium { get; set; }
    public string? delete_url { get; set; }
}

public class ImgBBImageInfo
{
    public string? filename { get; set; }
    public string? name { get; set; }
    public string? mime { get; set; }
    public string? extension { get; set; }
    public string? url { get; set; }
}

// Image Cache Entry
public class ImageCacheEntry
{
    public string LocalPath { get; set; } = string.Empty;
    public string ImgBBUrl { get; set; } = string.Empty;
    public string DeleteUrl { get; set; } = string.Empty;
    public DateTime CachedAt { get; set; } = DateTime.UtcNow;
    public long FileSizeBytes { get; set; }
}

public interface IImgBBService
{
    Task<ImgBBResponse?> UploadImageAsync(string imagePath, string? name = null);
    Task<ImgBBResponse?> UploadImageFromBase64Async(string base64Data, string? name = null);
    Task<string?> GetCachedImageAsync(string imgBBUrl);
    Task CacheImageAsync(string imgBBUrl, string localPath);
    Task<bool> DeleteImageAsync(string deleteUrl);
    Task PreloadAllProfileImagesAsync();
    Task ClearCacheAsync();
    Task<long> GetCacheSizeAsync();
    Task<bool> IsConfiguredAsync();
}

public class ImgBBService : IImgBBService
{
    private readonly HttpClient _httpClient;
    private readonly IDatabaseService _db;
    private readonly string _cacheDirectory;
    private readonly ConcurrentDictionary<string, ImageCacheEntry> _imageCache = new();
    private const string IMGBB_API_URL = "https://api.imgbb.com/1/upload";
    private const long MAX_CACHE_SIZE_BYTES = 100 * 1024 * 1024; // 100MB
    private const int CACHE_EXPIRY_DAYS = 30;

    public ImgBBService(IDatabaseService db)
    {
        _httpClient = new HttpClient();
        _db = db;
        _cacheDirectory = Path.Combine(FileSystem.CacheDirectory, "profile_images");
        Directory.CreateDirectory(_cacheDirectory);
        _ = Task.Run(LoadCacheIndexAsync);
    }

    private async Task<string?> GetApiKeyAsync()
    {
        try
        {
            // Get ImgBB API key from GLOBAL credentials
            var globalCred = await _db.GetCredentialsByEmailAsync("GLOBAL");
            if (!string.IsNullOrWhiteSpace(globalCred?.ImgBBApiKey))
            {
                return globalCred.ImgBBApiKey;
            }

            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GetApiKeyAsync error: {ex}");
            return null;
        }
    }

    public async Task<bool> IsConfiguredAsync()
    {
        var apiKey = await GetApiKeyAsync();
        return !string.IsNullOrWhiteSpace(apiKey);
    }

    public async Task<ImgBBResponse?> UploadImageAsync(string imagePath, string? name = null)
    {
        try
        {
            if (!File.Exists(imagePath))
            {
                return null;
            }

            var imageBytes = await File.ReadAllBytesAsync(imagePath);
            var base64String = Convert.ToBase64String(imageBytes);
            
            if (string.IsNullOrWhiteSpace(name))
            {
                name = Path.GetFileNameWithoutExtension(imagePath);
            }

            return await UploadImageFromBase64Async(base64String, name);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ImgBB UploadImageAsync error: {ex}");
            return null;
        }
    }

    public async Task<ImgBBResponse?> UploadImageFromBase64Async(string base64Data, string? name = null)
    {
        try
        {
            var apiKey = await GetApiKeyAsync();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return null;
            }

            var formData = new MultipartFormDataContent();
            formData.Add(new StringContent(apiKey), "key");
            formData.Add(new StringContent(base64Data), "image");
            
            if (!string.IsNullOrWhiteSpace(name))
            {
                formData.Add(new StringContent(name), "name");
            }

            var response = await _httpClient.PostAsync(IMGBB_API_URL, formData);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                try
                {
                    var imgBBResponse = JsonSerializer.Deserialize<ImgBBResponse>(responseContent);
                    if (imgBBResponse?.success == true && imgBBResponse.data != null)
                    {
                        return imgBBResponse;
                    }
                }
                catch (JsonException ex)
                {
                    Debug.WriteLine($"ImgBB JSON deserialization error: {ex}");
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ImgBB UploadImageFromBase64Async error: {ex}");
            return null;
        }
    }

    public async Task<string?> GetCachedImageAsync(string imgBBUrl)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(imgBBUrl))
                return null;

            // Check memory cache first
            if (_imageCache.TryGetValue(imgBBUrl, out var cacheEntry))
            {
                if (File.Exists(cacheEntry.LocalPath))
                {
                    // Check if cache is still valid
                    if (DateTime.UtcNow - cacheEntry.CachedAt < TimeSpan.FromDays(CACHE_EXPIRY_DAYS))
                    {
                        return cacheEntry.LocalPath;
                    }
                    else
                    {
                        // Cache expired, remove entry
                        _imageCache.TryRemove(imgBBUrl, out _);
                        File.Delete(cacheEntry.LocalPath);
                    }
                }
                else
                {
                    // File doesn't exist, remove from cache
                    _imageCache.TryRemove(imgBBUrl, out _);
                }
            }

            // Download and cache the image
            var imageBytes = await _httpClient.GetByteArrayAsync(imgBBUrl);
            
            var fileName = $"{Guid.NewGuid()}.jpg";
            var localPath = Path.Combine(_cacheDirectory, fileName);
            
            await File.WriteAllBytesAsync(localPath, imageBytes);
            
            // Add to cache
            var newCacheEntry = new ImageCacheEntry
            {
                LocalPath = localPath,
                ImgBBUrl = imgBBUrl,
                CachedAt = DateTime.UtcNow,
                FileSizeBytes = imageBytes.Length
            };
            
            _imageCache[imgBBUrl] = newCacheEntry;
            await SaveCacheIndexAsync();
            
            // Clean up cache if it's getting too large
            _ = Task.Run(CleanupCacheAsync);
            
            return localPath;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ImgBB GetCachedImageAsync error: {ex}");
            return null;
        }
    }

    public async Task CacheImageAsync(string imgBBUrl, string localPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(imgBBUrl) || string.IsNullOrWhiteSpace(localPath))
                return;

            if (!File.Exists(localPath))
                return;

            var fileInfo = new FileInfo(localPath);
            var cacheEntry = new ImageCacheEntry
            {
                LocalPath = localPath,
                ImgBBUrl = imgBBUrl,
                CachedAt = DateTime.UtcNow,
                FileSizeBytes = fileInfo.Length
            };

            _imageCache[imgBBUrl] = cacheEntry;
            await SaveCacheIndexAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ImgBB CacheImageAsync error: {ex}");
        }
    }

    public async Task<bool> DeleteImageAsync(string deleteUrl)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(deleteUrl))
                return false;

            var response = await _httpClient.GetAsync(deleteUrl);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ImgBB DeleteImageAsync error: {ex}");
            return false;
        }
    }

    public async Task PreloadAllProfileImagesAsync()
    {
        try
        {
            // Only preload if ImgBB is configured
            if (!await IsConfiguredAsync())
            {
                return;
            }

            var clients = await _db.GetClientsAsync();
            var tasks = new List<Task>();
            
            foreach (var client in clients)
            {
                if (!string.IsNullOrWhiteSpace(client.PhotoPath) && client.PhotoPath.StartsWith("http"))
                {
                    tasks.Add(GetCachedImageAsync(client.PhotoPath));
                }
            }
            
            // Also preload user profile image from settings
            var settingsJson = await _db.GetSettingAsync("app.settings.json");
            if (!string.IsNullOrWhiteSpace(settingsJson))
            {
                try
                {
                    var settings = JsonSerializer.Deserialize<SettingsData>(settingsJson);
                    if (!string.IsNullOrWhiteSpace(settings?.ProfileImagePath) && settings.ProfileImagePath.StartsWith("http"))
                    {
                        tasks.Add(GetCachedImageAsync(settings.ProfileImagePath));
                    }
                }
                catch { /* ignore json errors */ }
            }
            
            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ImgBB PreloadAllProfileImagesAsync error: {ex}");
        }
    }

    public async Task ClearCacheAsync()
    {
        try
        {
            _imageCache.Clear();
            
            if (Directory.Exists(_cacheDirectory))
            {
                var files = Directory.GetFiles(_cacheDirectory);
                foreach (var file in files)
                {
                    File.Delete(file);
                }
            }
            
            await SaveCacheIndexAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ImgBB ClearCacheAsync error: {ex}");
        }
    }

    public async Task<long> GetCacheSizeAsync()
    {
        try
        {
            if (!Directory.Exists(_cacheDirectory))
                return 0;

            var files = Directory.GetFiles(_cacheDirectory);
            long totalSize = 0;
            
            foreach (var file in files)
            {
                var fileInfo = new FileInfo(file);
                totalSize += fileInfo.Length;
            }
            
            return totalSize;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ImgBB GetCacheSizeAsync error: {ex}");
            return 0;
        }
    }

    private async Task LoadCacheIndexAsync()
    {
        try
        {
            var indexPath = Path.Combine(_cacheDirectory, "cache_index.json");
            if (!File.Exists(indexPath))
                return;

            var json = await File.ReadAllTextAsync(indexPath);
            var entries = JsonSerializer.Deserialize<Dictionary<string, ImageCacheEntry>>(json);
            
            if (entries != null)
            {
                foreach (var kvp in entries)
                {
                    // Verify file still exists and is not expired
                    if (File.Exists(kvp.Value.LocalPath) && 
                        DateTime.UtcNow - kvp.Value.CachedAt < TimeSpan.FromDays(CACHE_EXPIRY_DAYS))
                    {
                        _imageCache[kvp.Key] = kvp.Value;
                    }
                    else if (File.Exists(kvp.Value.LocalPath))
                    {
                        // Delete expired file
                        File.Delete(kvp.Value.LocalPath);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ImgBB LoadCacheIndexAsync error: {ex}");
        }
    }

    private async Task SaveCacheIndexAsync()
    {
        try
        {
            var indexPath = Path.Combine(_cacheDirectory, "cache_index.json");
            var json = JsonSerializer.Serialize(_imageCache.ToDictionary(kvp => kvp.Key, kvp => kvp.Value));
            await File.WriteAllTextAsync(indexPath, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ImgBB SaveCacheIndexAsync error: {ex}");
        }
    }

    private async Task CleanupCacheAsync()
    {
        try
        {
            var currentSize = await GetCacheSizeAsync();
            if (currentSize <= MAX_CACHE_SIZE_BYTES)
                return;

            // Sort by oldest first
            var sortedEntries = _imageCache.Values
                .OrderBy(e => e.CachedAt)
                .ToList();
            
            long deletedSize = 0;
            int deletedCount = 0;
            
            foreach (var entry in sortedEntries)
            {
                if (currentSize - deletedSize <= MAX_CACHE_SIZE_BYTES * 0.8) // Keep 80% of max size
                    break;
                
                try
                {
                    if (File.Exists(entry.LocalPath))
                    {
                        File.Delete(entry.LocalPath);
                        deletedSize += entry.FileSizeBytes;
                        deletedCount++;
                    }
                    
                    // Remove from memory cache
                    var keyToRemove = _imageCache.FirstOrDefault(kvp => kvp.Value == entry).Key;
                    if (keyToRemove != null)
                    {
                        _imageCache.TryRemove(keyToRemove, out _);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"ImgBB: Error deleting cached file {entry.LocalPath}: {ex}");
                }
            }
            
            await SaveCacheIndexAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ImgBB CleanupCacheAsync error: {ex}");
        }
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}