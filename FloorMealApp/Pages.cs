using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;
using System.Collections.Generic;
using System.Text.Json;

namespace ClientLedgerApp;

// ViewModels only (pages are defined in their .xaml.cs files)
public partial class ClientDetailViewModel : ObservableObject
{
    public class HistoryItem
    {
        public string Type { get; set; } = "Order"; // Order | Payment
        public string? Item { get; set; }
        public double? WeightKg { get; set; }
        public double? RatePerKg { get; set; }
        public double? Total { get; set; }
        public double? Amount { get; set; }
        public DateTime Timestamp { get; set; }
    }

    private readonly IDatabaseService _db;
    private readonly IAwsSyncService _aws;
    private readonly ICustomAlertService _alertService;

    [ObservableProperty] private int clientId;
    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private string? contact;

    [ObservableProperty] private double total;
    [ObservableProperty] private double received;
    public double Pending => Math.Max(0, Total - Received);

    public ObservableCollection<Order> Orders { get; } = new();
    public ObservableCollection<Payment> Payments { get; } = new();
    public ObservableCollection<HistoryItem> HistoryItems { get; } = new();
    public ObservableCollection<HistoryItem> HistoryVisibleItems { get; } = new();
    private List<HistoryItem> _historyBuffer = new();
    private const int HistoryPageSize = 25;

    [ObservableProperty] private int historyPageIndex;
    [ObservableProperty] private int historyTotalPages;
    public bool HasPrevPage => HistoryPageIndex > 0;
    public bool HasNextPage => HistoryPageIndex < Math.Max(HistoryTotalPages - 1, 0);
    public string HistoryPageDisplay => HistoryTotalPages == 0 ? "0 of 0" : $"Page {HistoryPageIndex + 1} of {HistoryTotalPages}";

    [ObservableProperty] private string newItem = "Wheat";
    [ObservableProperty] private double newWeightKg;
    [ObservableProperty] private string newWeightUnit = "kg";
    [ObservableProperty] private double newRatePerKg;
    public double NewTotal => (NewWeightUnit?.Equals("gram", StringComparison.OrdinalIgnoreCase) == true ? (NewWeightKg / 1000.0) : NewWeightUnit?.Equals("pyl", StringComparison.OrdinalIgnoreCase) == true ? (NewWeightKg * 5.0) : NewWeightKg) * NewRatePerKg;

    partial void OnNewWeightKgChanged(double value) => OnPropertyChanged(nameof(NewTotal));
    partial void OnNewRatePerKgChanged(double value) => OnPropertyChanged(nameof(NewTotal));

    partial void OnNewItemChanged(string value)
    {
        UpdateRateFromSettings();
    }

    partial void OnNewWeightUnitChanged(string value)
    {
        UpdateRateFromSettings();
        OnPropertyChanged(nameof(NewTotal));
    }

    public ObservableCollection<string> AvailableItems { get; } = new();
    [ObservableProperty] private string selectedUnitPriceDisplay = string.Empty;
    [ObservableProperty] private double selectedUnitPrice;
    private List<ItemPriceDto> _settingsItems = new();
    private const string SettingsPrefKey = "app.settings.json";

    private void UpdateRateFromSettings()
    {
        double unitPrice = 0d;
        double ratePerKg = 0d;
        var item = _settingsItems.FirstOrDefault(i => string.Equals(i.Name, NewItem, StringComparison.OrdinalIgnoreCase));
        if (item != null)
        {
            if (NewWeightUnit?.Equals("gram", StringComparison.OrdinalIgnoreCase) == true)
            {
                unitPrice = item.PricePerGram;
                if (unitPrice > 0) ratePerKg = unitPrice * 1000.0; // derive kg rate strictly from gram price
            }
            else if (NewWeightUnit?.Equals("pyl", StringComparison.OrdinalIgnoreCase) == true)
            {
                unitPrice = item.PricePerPyl;
                if (unitPrice > 0) ratePerKg = unitPrice / 5.0; // derive kg rate strictly from pyl price
            }
            else // kg
            {
                unitPrice = item.PricePerKg;
                if (unitPrice > 0) ratePerKg = unitPrice;
            }
        }
        SelectedUnitPrice = unitPrice;
        NewRatePerKg = ratePerKg; // 0 if no price set for the selected unit
        var unitLabel = (NewWeightUnit?.Equals("gram", StringComparison.OrdinalIgnoreCase) == true) ? "gram"
                       : (NewWeightUnit?.Equals("pyl", StringComparison.OrdinalIgnoreCase) == true) ? "pyl (5 kg)"
                       : "kg";
        SelectedUnitPriceDisplay = $"Rate: ₹/{unitLabel} {unitPrice:N2}"; // always show numeric 0.00 if missing
        OnPropertyChanged(nameof(NewTotal));
    }

    private async Task LoadSettingsAsync()
    {
        try
        {
            await _db.InitializeAsync();
            var json = await _db.GetSettingAsync(SettingsPrefKey) ?? string.Empty;
            var data = string.IsNullOrWhiteSpace(json)
                ? new SettingsData { DefaultMeasurementUnit = "pyl", DefaultItemName = "Wheat", Items = new List<ItemPriceDto>() }
                : (JsonSerializer.Deserialize<SettingsData>(json) ?? new SettingsData());
            _settingsItems = data.Items ?? new List<ItemPriceDto>();
            AvailableItems.Clear();
            foreach (var it in _settingsItems)
                if (!string.IsNullOrWhiteSpace(it.Name))
                    AvailableItems.Add(it.Name);
            if (AvailableItems.Count == 0)
            {
                AvailableItems.Add("Wheat");
                AvailableItems.Add("Bajara");
                AvailableItems.Add("Jowar");
                AvailableItems.Add("Dal");
            }
            NewItem = string.IsNullOrWhiteSpace(data.DefaultItemName) ? (AvailableItems.FirstOrDefault() ?? "Wheat") : data.DefaultItemName;
            NewWeightUnit = string.IsNullOrWhiteSpace(data.DefaultMeasurementUnit) ? "pyl" : data.DefaultMeasurementUnit;
            UpdateRateFromSettings();
        }
        catch { }
    }

    public async Task RefreshSettingsAsync() => await LoadSettingsAsync();

    [ObservableProperty] private string historyFilter = "All"; // All | Order | Payment
    [ObservableProperty] private string historySort = "Timestamp (Newest)"; // Backwards-compat; will be removed from UI
    [ObservableProperty] private string historySortColumn = "Timestamp"; // Item | Weight | Total | Timestamp | Mode
    [ObservableProperty] private bool historySortAscending = false; // default: Timestamp newest first

    partial void OnHistoryFilterChanged(string value)
    {
        HistoryPageIndex = 0;
        RebuildHistory();
    }
    partial void OnHistorySortChanged(string value)
    {
        switch (value)
        {
            case "Timestamp (Oldest)":
                HistorySortColumn = "Timestamp";
                HistorySortAscending = true;
                break;
            case "Item (A-Z)":
                HistorySortColumn = "Item";
                HistorySortAscending = true;
                break;
            case "Total (High-Low)":
                HistorySortColumn = "Total";
                HistorySortAscending = false;
                break;
            case "Total (Low-High)":
                HistorySortColumn = "Total";
                HistorySortAscending = true;
                break;
            default:
                HistorySortColumn = "Timestamp";
                HistorySortAscending = false;
                break;
        }
        HistoryPageIndex = 0;
        RebuildHistory();
    }

    [ObservableProperty] private double receiveAmount;
    [ObservableProperty] private DateTime receiveDate = DateTime.Today;

    public ClientDetailViewModel(IDatabaseService db, IAwsSyncService aws, ICustomAlertService alertService)
    {
        _db = db;
        _aws = aws;
        _alertService = alertService;
    }

    public async Task Initialize(int id)
    {
        ClientId = id;
        var client = (await _db.GetClientsAsync()).First(c => c.Id == id);
        Name = client.Name;
        Contact = client.Contact;
        await LoadSettingsAsync();
        await RefreshAsync();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        // Fetch in parallel to reduce latency
        var totalsTask = _db.GetClientTotalsAsync(ClientId);
        var ordersTask = _db.GetOrdersForClientAsync(ClientId);
        var paymentsTask = _db.GetPaymentsForClientAsync(ClientId);
        await Task.WhenAll(totalsTask, ordersTask, paymentsTask);

        var totals = totalsTask.Result;
        Total = totals.Totals;
        Received = totals.Received;

        Orders.Clear();
        foreach (var o in ordersTask.Result) Orders.Add(o);
        Payments.Clear();
        foreach (var p in paymentsTask.Result) Payments.Add(p);
        OnPropertyChanged(nameof(Pending));
        OnPropertyChanged(nameof(NewTotal));
        RebuildHistory();
    }

    [RelayCommand]
    public async Task AddOrderAsync()
    {
        // Block adding order if selected unit price is zero/missing
        if (SelectedUnitPrice <= 0)
        {
            await _alertService.ShowWarningAsync($"Please set price for '{NewItem}' in '{NewWeightUnit}' on the Settings page.", "Price Not Set");
            return;
        }

        var weightKg = NewWeightUnit?.Equals("gram", StringComparison.OrdinalIgnoreCase) == true ? (NewWeightKg / 1000.0)
                    : NewWeightUnit?.Equals("pyl", StringComparison.OrdinalIgnoreCase) == true ? (NewWeightKg * 5.0)
                    : NewWeightKg;
        var order = new Order
        {
            ClientId = ClientId,
            Item = NewItem,
            WeightKg = weightKg,
            RatePerKg = NewRatePerKg,
            Date = DateTime.Now
        };
        await _db.AddOrderAsync(order);
        // Reset only the weight; keep unit/item and re-derive rate from settings to avoid zero totals next time
        NewWeightKg = 0;
        UpdateRateFromSettings();
        await RefreshAsync();
        
        await _alertService.ShowSuccessAsync($"Order added: {NewItem} - ₹{order.Total:N2}", "Order Added");
        
        // Enqueue AWS push job (durable)
        try
        {
            var sjson = await _db.GetSettingAsync(SettingsPrefKey) ?? string.Empty;
            var sdata = string.IsNullOrWhiteSpace(sjson) ? new SettingsData() : (JsonSerializer.Deserialize<SettingsData>(sjson) ?? new SettingsData());
            var mail = sdata.MailId?.Trim();
            if (!string.IsNullOrWhiteSpace(mail) && sdata.EnableAwsSync)
            {
                var payload = new { OrderId = order.Id };
                await _db.EnqueueOutboxJobAsync(new OutboxJob
                {
                    Type = "AwsPutOrder",
                    PayloadJson = System.Text.Json.JsonSerializer.Serialize(payload),
                    Attempts = 0,
                    MaxAttempts = 5,
                    NextAttemptUtc = DateTime.UtcNow
                });
            }
        }
        catch { }

        // Hint scheduler to run soon
        try { var scheduler = ServiceHelper.GetService<SyncScheduler>(); _ = scheduler.SyncNowAsync(); } catch { }
    }

    [RelayCommand]
    public async Task DeleteOrderAsync(Order? order)
    {
        if (order == null) return;
        
        var confirmed = await _alertService.ShowConfirmAsync(
            "Delete Order", 
            $"Are you sure you want to delete this order?\n\n{order.Item} - {order.WeightKg:N2} kg - ₹{order.Total:N2}",
            "Delete", "Cancel", 
            AlertType.Warning);
            
        if (!confirmed) return;
        
        await _db.DeleteOrderAsync(order.Id);
        await RefreshAsync();
        await _alertService.ShowSuccessAsync("Order deleted successfully!", "Order Deleted");
    }

    [RelayCommand]
    public void SortBy(string? column)
    {
        if (string.IsNullOrWhiteSpace(column)) return;
        if (HistorySortColumn.Equals(column, StringComparison.OrdinalIgnoreCase))
        {
            HistorySortAscending = !HistorySortAscending;
        }
        else
        {
            HistorySortColumn = column;
            HistorySortAscending = column.Equals("Timestamp", StringComparison.OrdinalIgnoreCase) ? false : true;
        }
        HistoryPageIndex = 0;
        RebuildHistory();
    }

    private void RebuildHistory()
    {
        var merged = new List<HistoryItem>(Orders.Count + Payments.Count);
        foreach (var o in Orders)
        {
            merged.Add(new HistoryItem
            {
                Type = "Order",
                Item = o.Item,
                WeightKg = o.WeightKg,
                RatePerKg = o.RatePerKg,
                Total = o.Total,
                Timestamp = o.Date
            });
        }
        foreach (var p in Payments)
        {
            merged.Add(new HistoryItem
            {
                Type = "Payment",
                Amount = p.Amount,
                Timestamp = p.Date
            });
        }

        IEnumerable<HistoryItem> query = merged;
        // Filter
        switch (HistoryFilter)
        {
            case "Order":
                query = query.Where(h => h.Type == "Order");
                break;
            case "Payment":
                query = query.Where(h => h.Type == "Payment");
                break;
            default:
                break;
        }
        // Sort by selected column header
        query = HistorySortColumn switch
        {
            "Item" => HistorySortAscending
                ? query.OrderBy(h => h.Type == "Order" ? (h.Item ?? string.Empty) : "Paid").ThenByDescending(h => h.Timestamp)
                : query.OrderByDescending(h => h.Type == "Order" ? (h.Item ?? string.Empty) : "Paid").ThenByDescending(h => h.Timestamp),
            "Weight" => HistorySortAscending
                ? query.OrderBy(h => h.Type == "Order" ? (h.WeightKg ?? 0) : 0d).ThenByDescending(h => h.Timestamp)
                : query.OrderByDescending(h => h.Type == "Order" ? (h.WeightKg ?? 0) : 0d).ThenByDescending(h => h.Timestamp),
            "Total" => HistorySortAscending
                ? query.OrderBy(h => h.Type == "Order" ? (h.Total ?? 0) : (h.Amount ?? 0)).ThenByDescending(h => h.Timestamp)
                : query.OrderByDescending(h => h.Type == "Order" ? (h.Total ?? 0) : (h.Amount ?? 0)).ThenByDescending(h => h.Timestamp),
            "Mode" => HistorySortAscending
                ? query.OrderBy(h => h.Type).ThenByDescending(h => h.Timestamp)
                : query.OrderByDescending(h => h.Type).ThenByDescending(h => h.Timestamp),
            _ => HistorySortAscending
                ? query.OrderBy(h => h.Timestamp)
                : query.OrderByDescending(h => h.Timestamp),
        };

        _historyBuffer = query.ToList();
        HistoryTotalPages = _historyBuffer.Count == 0 ? 0 : (int)Math.Ceiling(_historyBuffer.Count / (double)HistoryPageSize);
        if (HistoryPageIndex >= HistoryTotalPages) HistoryPageIndex = Math.Max(HistoryTotalPages - 1, 0);
        RebuildHistoryPage();
    }

    private void RebuildHistoryPage()
    {
        HistoryVisibleItems.Clear();
        if (_historyBuffer.Count == 0)
        {
            OnPropertyChanged(nameof(HasPrevPage));
            OnPropertyChanged(nameof(HasNextPage));
            OnPropertyChanged(nameof(HistoryPageDisplay));
            return;
        }
        int skip = HistoryPageIndex * HistoryPageSize;
        foreach (var h in _historyBuffer.Skip(skip).Take(HistoryPageSize))
            HistoryVisibleItems.Add(h);
        OnPropertyChanged(nameof(HasPrevPage));
        OnPropertyChanged(nameof(HasNextPage));
        OnPropertyChanged(nameof(HistoryPageDisplay));
    }

    partial void OnHistoryPageIndexChanged(int value)
    {
        RebuildHistoryPage();
    }

    partial void OnHistoryTotalPagesChanged(int value)
    {
        OnPropertyChanged(nameof(HasPrevPage));
        OnPropertyChanged(nameof(HasNextPage));
        OnPropertyChanged(nameof(HistoryPageDisplay));
    }

    [RelayCommand]
    public void PrevPage()
    {
        if (HistoryPageIndex > 0) HistoryPageIndex--;
    }

    [RelayCommand]
    public void NextPage()
    {
        if (HistoryPageIndex < Math.Max(HistoryTotalPages - 1, 0)) HistoryPageIndex++;
    }

    [RelayCommand]
    public async Task ReceivePaymentAsync()
    {
        if (ReceiveAmount <= 0) return;
        var payment = new Payment
        {
            ClientId = ClientId,
            Amount = ReceiveAmount,
            Date = ReceiveDate.Date.Add(DateTime.Now.TimeOfDay)
        };
        await _db.AddPaymentAsync(payment);
        
        await _alertService.ShowSuccessAsync($"Payment received: ₹{ReceiveAmount:N2}", "Payment Added");
        
        ReceiveAmount = 0;
        ReceiveDate = DateTime.Today;
        await RefreshAsync();
        // Enqueue AWS push job (durable)
        try
        {
            var sjson = await _db.GetSettingAsync(SettingsPrefKey) ?? string.Empty;
            var sdata = string.IsNullOrWhiteSpace(sjson) ? new SettingsData() : (JsonSerializer.Deserialize<SettingsData>(sjson) ?? new SettingsData());
            var mail = sdata.MailId?.Trim();
            if (!string.IsNullOrWhiteSpace(mail) && sdata.EnableAwsSync)
            {
                var payload = new { PaymentId = payment.Id };
                await _db.EnqueueOutboxJobAsync(new OutboxJob
                {
                    Type = "AwsPutPayment",
                    PayloadJson = System.Text.Json.JsonSerializer.Serialize(payload),
                    Attempts = 0,
                    MaxAttempts = 5,
                    NextAttemptUtc = DateTime.UtcNow
                });
            }
        }
        catch { }

        // Hint scheduler to run soon
        try { var scheduler = ServiceHelper.GetService<SyncScheduler>(); _ = scheduler.SyncNowAsync(); } catch { }
    }

    [RelayCommand]
    public async Task OpenPaymentsAsync()
    {
        await Shell.Current.GoToAsync($"payments?clientId={ClientId}");
    }
}

public partial class PaymentsViewModel : ObservableObject
{
    private readonly IDatabaseService _db;
    private readonly ICustomAlertService _alertService;

    [ObservableProperty] private int clientId;
    public ObservableCollection<Payment> Payments { get; } = new();

    [ObservableProperty] private double summaryTotals;
    [ObservableProperty] private double summaryReceived;
    public double SummaryPending => Math.Max(0, SummaryTotals - SummaryReceived);

    public PaymentsViewModel(IDatabaseService db, ICustomAlertService alertService)
    {
        _db = db;
        _alertService = alertService;
    }

    public async Task Initialize(int id)
    {
        ClientId = id;
        await RefreshAsync();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        Payments.Clear();
        foreach (var p in await _db.GetPaymentsForClientAsync(ClientId)) Payments.Add(p);
        var t = await _db.GetClientTotalsAsync(ClientId);
        SummaryTotals = t.Totals;
        SummaryReceived = t.Received;
        OnPropertyChanged(nameof(SummaryPending));
    }

    [RelayCommand]
    public async Task DeletePaymentAsync(Payment? payment)
    {
        if (payment == null) return;
        
        var confirmed = await _alertService.ShowConfirmAsync(
            "Delete Payment", 
            $"Are you sure you want to delete this payment?\n\n₹{payment.Amount:N2} on {payment.Date:dd/MM/yyyy}",
            "Delete", "Cancel", 
            AlertType.Warning);
            
        if (!confirmed) return;
        
        await _db.DeletePaymentAsync(payment.Id);
        await RefreshAsync();
        await _alertService.ShowSuccessAsync("Payment deleted successfully!", "Payment Deleted");
    }
}