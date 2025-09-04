using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Storage;
using System.Globalization;
using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using Microsoft.Maui.Controls;
using System.Diagnostics;

namespace ClientLedgerApp;

public partial class ItemPrice : ObservableObject
{
    [ObservableProperty] private string name = string.Empty;
    // Prices per unit
    [ObservableProperty] private double pricePerKg;
    [ObservableProperty] private double pricePerGram;
    [ObservableProperty] private double pricePerPyl; // 1 pyl = 5 kg
}

public class ItemPriceDto
{
    public string Name { get; set; } = string.Empty;
    public double PricePerKg { get; set; }
    public double PricePerGram { get; set; }
    public double PricePerPyl { get; set; }
}

public class SettingsData
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FloorMealName { get; set; } = string.Empty;
    public string ContactNumber { get; set; } = string.Empty;
    public string MailId { get; set; } = string.Empty; // read-only in UI
    public string? ProfileImagePath { get; set; }
    public string? ProfileImageDeleteUrl { get; set; } // ImgBB delete URL for profile image

    public string DefaultMeasurementUnit { get; set; } = "pyl"; // kg | gram | pyl
    public string DefaultItemName { get; set; } = "Wheat";
    public List<ItemPriceDto> Items { get; set; } = new();

    // AWS sync configuration (optional)
    public bool EnableAwsSync { get; set; } = true;
    public string? AwsRegion { get; set; } // e.g., us-east-1
    public string? CognitoIdentityPoolId { get; set; }
}

public partial class SettingsViewModel : ObservableObject
{
    private const string PrefKey = "app.settings.json";
    private readonly IDatabaseService _db;
    private readonly IAwsSyncService _aws;
    private readonly ICustomAlertService _alertService;

    [ObservableProperty] private string firstName = string.Empty;
    [ObservableProperty] private string lastName = string.Empty;
    [ObservableProperty] private string floorMealName = string.Empty;
    [ObservableProperty] private string contactNumber = string.Empty;
    [ObservableProperty] private string mailId = string.Empty; // read-only in UI

    [ObservableProperty] private string defaultMeasurementUnit = "pyl"; // kg | gram | pyl
    partial void OnDefaultMeasurementUnitChanged(string value)
    {
        PrefillFromUnit(value);
    }

    private void PrefillFromUnit(string unit)
    {
        foreach (var it in Items)
        {
            if (unit == "gram")
            {
                if (it.PricePerGram <= 0 && it.PricePerKg > 0)
                    it.PricePerGram = it.PricePerKg / 1000.0;
            }
            else if (unit == "pyl")
            {
                if (it.PricePerPyl <= 0 && it.PricePerKg > 0)
                    it.PricePerPyl = it.PricePerKg * 5.0;
            }
            else if (unit == "kg")
            {
                if (it.PricePerKg <= 0)
                {
                    if (it.PricePerPyl > 0)
                        it.PricePerKg = it.PricePerPyl / 5.0;
                    else if (it.PricePerGram > 0)
                        it.PricePerKg = it.PricePerGram * 1000.0;
                }
            }
        }
    }
    [ObservableProperty] private string defaultItemName = string.Empty;
    [ObservableProperty] private ItemPrice? defaultItem;

    public ObservableCollection<ItemPrice> Items { get; } = new();

    public SettingsViewModel(IDatabaseService db, ICustomAlertService alertService)
    {
        _db = db;
        _aws = ServiceHelper.GetService<IAwsSyncService>();
        _alertService = alertService;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            await _db.InitializeAsync();
            var json = await _db.GetSettingAsync(PrefKey) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(json))
            {
                var data = System.Text.Json.JsonSerializer.Deserialize<SettingsData>(json) ?? new SettingsData();
                FirstName = data.FirstName;
                LastName = data.LastName;
                FloorMealName = data.FloorMealName;
                ContactNumber = data.ContactNumber;
                MailId = data.MailId;
                DefaultMeasurementUnit = string.IsNullOrWhiteSpace(data.DefaultMeasurementUnit) ? "pyl" : data.DefaultMeasurementUnit;
                DefaultItemName = string.IsNullOrWhiteSpace(data.DefaultItemName) ? "Wheat" : data.DefaultItemName;
                Items.Clear();
                foreach (var it in data.Items)
                    Items.Add(new ItemPrice { Name = it.Name, PricePerKg = it.PricePerKg, PricePerGram = it.PricePerGram, PricePerPyl = it.PricePerPyl });
                DefaultItem = Items.FirstOrDefault(i => string.Equals(i.Name, DefaultItemName, StringComparison.OrdinalIgnoreCase))
                               ?? Items.FirstOrDefault(i => string.Equals(i.Name, "Wheat", StringComparison.OrdinalIgnoreCase))
                               ?? Items.FirstOrDefault();
                if (DefaultItem != null) DefaultItemName = DefaultItem.Name;
                PrefillFromUnit(DefaultMeasurementUnit);
            }
            else
            {
                // initial defaults
                Items.Clear();
                Items.Add(new ItemPrice { Name = "Wheat", PricePerKg = 0, PricePerGram = 0, PricePerPyl = 0 });
                Items.Add(new ItemPrice { Name = "Bajara", PricePerKg = 0, PricePerGram = 0, PricePerPyl = 0 });
                Items.Add(new ItemPrice { Name = "Jowar", PricePerKg = 0, PricePerGram = 0, PricePerPyl = 0 });
                Items.Add(new ItemPrice { Name = "Dal", PricePerKg = 0, PricePerGram = 0, PricePerPyl = 0 });
                DefaultItem = Items.FirstOrDefault(i => string.Equals(i.Name, "Wheat", StringComparison.OrdinalIgnoreCase)) ?? Items.FirstOrDefault();
                DefaultItemName = DefaultItem?.Name ?? "Wheat";
                DefaultMeasurementUnit = "pyl"; // 1 payli = 5 kg
            }
        }
        catch
        {
            // ignore
        }
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        // Validation
        var fn = FirstName?.Trim() ?? string.Empty;
        var ln = LastName?.Trim() ?? string.Empty;
        var fm = FloorMealName?.Trim() ?? string.Empty;
        var digits = new string((ContactNumber ?? string.Empty).Where(char.IsDigit).ToArray());
        if (fn.Length > 15)
        {
            await _alertService.ShowWarningAsync("First name must be 15 characters or fewer.", "Validation Error");
            return;
        }
        if (ln.Length > 15)
        {
            await _alertService.ShowWarningAsync("Last name must be 15 characters or fewer.", "Validation Error");
            return;
        }
        if (fm.Length > 25)
        {
            await _alertService.ShowWarningAsync("Floor meal name must be 25 characters or fewer.", "Validation Error");
            return;
        }
        if (digits.Length > 10)
        {
            await _alertService.ShowWarningAsync("Contact number must contain at most 10 digits.", "Validation Error");
            return;
        }
        ContactNumber = digits;

        // Normalize casing to Title Case for names
        string ToTitle(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            var ti = CultureInfo.CurrentCulture.TextInfo;
            return ti.ToTitleCase(s.ToLower());
        }
        fn = ToTitle(fn);
        ln = ToTitle(ln);
        fm = ToTitle(fm);
        FirstName = fn;
        LastName = ln;
        FloorMealName = fm;

        
        // Merge with existing settings to preserve AWS config; force EnableAwsSync=true
        var existingJson = await _db.GetSettingAsync(PrefKey) ?? string.Empty;
        var merged = string.IsNullOrWhiteSpace(existingJson)
            ? new SettingsData()
            : (System.Text.Json.JsonSerializer.Deserialize<SettingsData>(existingJson) ?? new SettingsData());
        merged.FirstName = fn;
        merged.LastName = ln;
        merged.FloorMealName = fm;
        merged.ContactNumber = digits;
        merged.MailId = MailId?.Trim() ?? string.Empty;
                merged.DefaultMeasurementUnit = DefaultMeasurementUnit;
        merged.DefaultItemName = (DefaultItem?.Name ?? (string.IsNullOrWhiteSpace(DefaultItemName) ? "Wheat" : DefaultItemName));
        merged.Items = Items.Select(i => new ItemPriceDto { Name = i.Name?.Trim() ?? string.Empty, PricePerKg = i.PricePerKg, PricePerGram = i.PricePerGram, PricePerPyl = i.PricePerPyl }).ToList();
        merged.EnableAwsSync = true;
        var json = System.Text.Json.JsonSerializer.Serialize(merged);
        await _db.SetSettingAsync(PrefKey, json);
        
        // Write-through to AWS (if configured); ignore failures
        try
        {
            var mail = MailId?.Trim();
            if (!string.IsNullOrWhiteSpace(mail))
                await _aws.PutAppSettingAsync(mail!, json);
        }
        catch { }
        
        await _alertService.ShowSuccessAsync("Settings saved successfully.", "Settings Saved");
    }

    [RelayCommand]
    public void AddItem()
    {
        Items.Add(new ItemPrice { Name = "", PricePerKg = 0 });
    }

    [RelayCommand]
    public async Task RemoveItemAsync(ItemPrice? item)
    {
        if (item == null) return;
        
        var confirmed = await _alertService.ShowConfirmAsync(
            "Remove Item", 
            $"Are you sure you want to remove '{item.Name}' from the price list?",
            "Remove", "Cancel", 
            AlertType.Warning);
            
        if (confirmed)
        {
            Items.Remove(item);
            await _alertService.ShowSuccessAsync($"'{item.Name}' removed from price list.", "Item Removed");
        }
    }
}