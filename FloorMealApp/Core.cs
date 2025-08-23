using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SQLite;
using Microsoft.Maui.Controls;

namespace ClientLedgerApp;

// Data models (SQLite)
[Table("Client")]
public class Client
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed(Name = "IX_Client_Name", Order = 1)]
    public string Name { get; set; } = string.Empty;

    public string? Contact { get; set; }

    public string? Profile { get; set; }

    public string? PhotoPath { get; set; } // ImgBB URL for display

    public string? PhotoDeleteUrl { get; set; } // ImgBB delete URL

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

[Table("Order")]
public class Order
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int ClientId { get; set; }

    public string Item { get; set; } = string.Empty; // Wheat/Bajara/Jowar/Dal

    public double WeightKg { get; set; }

    public double RatePerKg { get; set; }

    public double Total { get; set; } // Derived: WeightKg * RatePerKg

    public DateTime Date { get; set; } = DateTime.UtcNow;
}

[Table("Payment")]
public class Payment
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int ClientId { get; set; }

    public double Amount { get; set; }

    public DateTime Date { get; set; } = DateTime.UtcNow;

    public string? Note { get; set; }
}

// Durable background job (Outbox) for non-blocking network work
[Table("OutboxJob")]
public class OutboxJob
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public string Type { get; set; } = string.Empty; // e.g., UploadClientPhoto | AwsPutClient | AwsPutOrder | AwsPutPayment

    public string PayloadJson { get; set; } = string.Empty; // serialized payload

    public int Attempts { get; set; } = 0;

    public int MaxAttempts { get; set; } = 5;

    [Indexed]
    public DateTime NextAttemptUtc { get; set; } = DateTime.UtcNow;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public string? LastError { get; set; }
}

[Table("AppSetting")]
public class AppSetting
{
    [PrimaryKey]
    public string Key { get; set; } = string.Empty;
    public string? Value { get; set; }
}

[Table("Credentials")]
public class Credentials
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    public string? Email { get; set; }
    public string? SmtpUser { get; set; }
    public string? SmtpAppPassword { get; set; }
    public string? ImgBBApiKey { get; set; }
    public int IsActive { get; set; } = 1; // 1=active, 0=inactive
}

public class ClientTotals
{
    public double Totals { get; set; }
    public double Received { get; set; }
    public double Pending => Math.Max(0, Totals - Received);
}

public class ClientSummary
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Contact { get; set; }
    public string? Profile { get; set; }
    public string? PhotoPath { get; set; }
    public double Pending { get; set; }
    public int OrderCount { get; set; }
}

public interface IDatabaseService
{
    Task InitializeAsync();
    Task<int> AddClientAsync(Client client);
    Task UpdateClientAsync(Client client);
    Task DeleteClientAsync(int clientId);
    Task<List<Client>> GetClientsAsync(string? search = null);

    Task<int> AddOrderAsync(Order order);
    Task UpdateOrderAsync(Order order);
    Task DeleteOrderAsync(int orderId);

    Task<int> AddPaymentAsync(Payment payment);
    Task UpdatePaymentAsync(Payment payment);
    Task DeletePaymentAsync(int paymentId);

    Task<List<Order>> GetOrdersForClientAsync(int clientId);
    Task<List<Payment>> GetPaymentsForClientAsync(int clientId);

    Task<ClientTotals> GetClientTotalsAsync(int clientId);
    Task<double> GetAllPendingAsync();
    Task<List<ClientSummary>> GetClientSummariesAsync(string? search = null);

    // App settings (key-value) persistence
    Task<string?> GetSettingAsync(string key);
    Task SetSettingAsync(string key, string? value);

    // Credentials helpers
    Task<Credentials?> GetCredentialsByEmailAsync(string email);
    Task UpsertCredentialsAsync(Credentials cred);

    // Upsert helpers for sync
    Task UpsertClientAsync(Client client);
    Task UpsertOrderAsync(Order order);
    Task UpsertPaymentAsync(Payment payment);

    // Fetch by id helpers
    Task<Client?> GetClientByIdAsync(int id);
    Task<Order?> GetOrderByIdAsync(int id);
    Task<Payment?> GetPaymentByIdAsync(int id);

    // Outbox job queue
    Task EnqueueOutboxJobAsync(OutboxJob job);
    Task<List<OutboxJob>> GetDueOutboxJobsAsync(DateTime utcNow, int max = 20);
    Task UpdateOutboxJobAsync(OutboxJob job);
    Task DeleteOutboxJobAsync(int id);
}

public class DatabaseService : IDatabaseService
{
    private SQLiteAsyncConnection? _db;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private async Task EnsureInitializedAsync()
    {
        if (_db != null) return;
        await _initLock.WaitAsync();
        try
        {
            if (_db != null) return;
            var dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "clientledger.db3");
            _db = new SQLiteAsyncConnection(dbPath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);
            await _db.CreateTableAsync<Client>();
            await _db.CreateTableAsync<Order>();
            await _db.CreateTableAsync<Payment>();
            await _db.CreateTableAsync<AppSetting>();
            await _db.CreateTableAsync<Credentials>();
            await _db.CreateTableAsync<OutboxJob>();
            await EnsureMigrationsAsync();
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task InitializeAsync() => await EnsureInitializedAsync();

    private async Task EnsureMigrationsAsync()
    {
        try
        {
            await _db!.ExecuteAsync("ALTER TABLE Client ADD COLUMN PhotoPath TEXT");
        }
        catch { /* ignore if already exists */ }
        
        try
        {
            await _db!.ExecuteAsync("ALTER TABLE Client ADD COLUMN PhotoDeleteUrl TEXT");
        }
        catch { /* ignore if already exists */ }
        
        try
        {
            await _db!.ExecuteAsync("ALTER TABLE Credentials ADD COLUMN ImgBBApiKey TEXT");
        }
        catch { /* ignore if already exists */ }
    }

    public async Task<int> AddClientAsync(Client client)
    {
        await EnsureInitializedAsync();
        return await _db!.InsertAsync(client);
    }

    public async Task UpdateClientAsync(Client client)
    {
        await EnsureInitializedAsync();
        await _db!.UpdateAsync(client);
    }

    public async Task DeleteClientAsync(int clientId)
    {
        await EnsureInitializedAsync();
        // cascade: delete orders & payments for client
        var orders = await _db!.Table<Order>().Where(o => o.ClientId == clientId).ToListAsync();
        var payments = await _db.Table<Payment>().Where(p => p.ClientId == clientId).ToListAsync();
        foreach (var o in orders) await _db.DeleteAsync(o);
        foreach (var p in payments) await _db.DeleteAsync(p);
        await _db.DeleteAsync<Client>(clientId);
    }

    public async Task<List<Client>> GetClientsAsync(string? search = null)
    {
        await EnsureInitializedAsync();
        var q = _db!.Table<Client>();
        if (!string.IsNullOrWhiteSpace(search))
        {
            // sqlite-net async does not support Contains on server; fetch and filter locally for simplicity
            var all = await q.ToListAsync();
            search = search.Trim();
            return all.Where(c => (c.Name?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                               || (c.Contact?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))
                      .OrderBy(c => c.Name)
                      .ToList();
        }
        return await q.OrderBy(c => c.Name).ToListAsync();
    }

    public async Task<int> AddOrderAsync(Order order)
    {
        await EnsureInitializedAsync();
        // ensure total is consistent
        order.Total = order.WeightKg * order.RatePerKg;
        return await _db!.InsertAsync(order);
    }

    public async Task UpdateOrderAsync(Order order)
    {
        await EnsureInitializedAsync();
        order.Total = order.WeightKg * order.RatePerKg;
        await _db!.UpdateAsync(order);
    }

    public async Task DeleteOrderAsync(int orderId)
    {
        await EnsureInitializedAsync();
        await _db!.DeleteAsync<Order>(orderId);
    }

    public async Task<int> AddPaymentAsync(Payment payment)
    {
        await EnsureInitializedAsync();
        return await _db!.InsertAsync(payment);
    }

    public async Task UpdatePaymentAsync(Payment payment)
    {
        await EnsureInitializedAsync();
        await _db!.UpdateAsync(payment);
    }

    public async Task DeletePaymentAsync(int paymentId)
    {
        await EnsureInitializedAsync();
        await _db!.DeleteAsync<Payment>(paymentId);
    }

    public async Task<List<Order>> GetOrdersForClientAsync(int clientId)
    {
        await EnsureInitializedAsync();
        return await _db!.Table<Order>().Where(o => o.ClientId == clientId).OrderByDescending(o => o.Date).ToListAsync();
    }

    public async Task<List<Payment>> GetPaymentsForClientAsync(int clientId)
    {
        await EnsureInitializedAsync();
        return await _db!.Table<Payment>().Where(p => p.ClientId == clientId).OrderByDescending(p => p.Date).ToListAsync();
    }

    public async Task<ClientTotals> GetClientTotalsAsync(int clientId)
    {
        await EnsureInitializedAsync();
        var orders = await _db!.Table<Order>().Where(o => o.ClientId == clientId).ToListAsync();
        var payments = await _db.Table<Payment>().Where(p => p.ClientId == clientId).ToListAsync();
        var totals = orders.Sum(o => o.Total);
        var received = payments.Sum(p => p.Amount);
        return new ClientTotals { Totals = totals, Received = received };
    }

    public async Task<double> GetAllPendingAsync()
    {
        await EnsureInitializedAsync();
        var clients = await _db!.Table<Client>().ToListAsync();
        double sum = 0;
        foreach (var c in clients)
        {
            var t = await GetClientTotalsAsync(c.Id);
            sum += t.Pending;
        }
        return sum;
    }

    public async Task<List<ClientSummary>> GetClientSummariesAsync(string? search = null)
    {
        var clients = await GetClientsAsync(search);
        var list = new List<ClientSummary>(clients.Count);
        foreach (var c in clients)
        {
            var orders = await GetOrdersForClientAsync(c.Id);
            var totals = orders.Sum(o => o.Total);
            var payments = await GetPaymentsForClientAsync(c.Id);
            var received = payments.Sum(p => p.Amount);
            list.Add(new ClientSummary
            {
                Id = c.Id,
                Name = c.Name,
                Contact = c.Contact,
                Profile = c.Profile,
                PhotoPath = c.PhotoPath,
                Pending = Math.Max(0, totals - received),
                OrderCount = orders.Count
            });
        }
        return list.OrderBy(cs => cs.Name).ToList();
    }

    public async Task<string?> GetSettingAsync(string key)
    {
        await EnsureInitializedAsync();
        var s = await _db!.Table<AppSetting>().Where(x => x.Key == key).FirstOrDefaultAsync();
        return s?.Value;
    }

    public async Task SetSettingAsync(string key, string? value)
    {
        await EnsureInitializedAsync();
        if (value == null)
        {
            await _db!.DeleteAsync<AppSetting>(key);
            return;
        }
        var s = new AppSetting { Key = key, Value = value };
        await _db!.InsertOrReplaceAsync(s);
    }

    public async Task<Credentials?> GetCredentialsByEmailAsync(string email)
    {
        await EnsureInitializedAsync();
        var list = await _db!.Table<Credentials>().Where(c => c.Email == email).ToListAsync();
        return list.FirstOrDefault();
    }

    public async Task UpsertCredentialsAsync(Credentials cred)
    {
        await EnsureInitializedAsync();
        if (cred.Id == 0)
        {
            var existing = string.IsNullOrWhiteSpace(cred.Email) ? null : await GetCredentialsByEmailAsync(cred.Email);
            if (existing != null)
            {
                cred.Id = existing.Id;
            }
        }
        if (cred.Id == 0) await _db!.InsertAsync(cred);
        else await _db!.UpdateAsync(cred);
    }

    public async Task UpsertClientAsync(Client client)
    {
        await EnsureInitializedAsync();
        await _db!.InsertOrReplaceAsync(client);
    }

    public async Task UpsertOrderAsync(Order order)
    {
        await EnsureInitializedAsync();
        // Ensure total is consistent when upserting
        order.Total = order.WeightKg * order.RatePerKg;
        await _db!.InsertOrReplaceAsync(order);
    }

    public async Task UpsertPaymentAsync(Payment payment)
    {
        await EnsureInitializedAsync();
        await _db!.InsertOrReplaceAsync(payment);
    }

    // Fetch by id helpers
    public async Task<Client?> GetClientByIdAsync(int id)
    {
        await EnsureInitializedAsync();
        return await _db!.FindAsync<Client>(id);
    }

    public async Task<Order?> GetOrderByIdAsync(int id)
    {
        await EnsureInitializedAsync();
        return await _db!.FindAsync<Order>(id);
    }

    public async Task<Payment?> GetPaymentByIdAsync(int id)
    {
        await EnsureInitializedAsync();
        return await _db!.FindAsync<Payment>(id);
    }

    // Outbox job queue implementations
    public async Task EnqueueOutboxJobAsync(OutboxJob job)
    {
        await EnsureInitializedAsync();
        await _db!.InsertAsync(job);
    }

    public async Task<List<OutboxJob>> GetDueOutboxJobsAsync(DateTime utcNow, int max = 20)
    {
        await EnsureInitializedAsync();
        return await _db!.Table<OutboxJob>()
            .Where(j => j.NextAttemptUtc <= utcNow)
            .OrderBy(j => j.NextAttemptUtc)
            .Take(max)
            .ToListAsync();
    }

    public async Task UpdateOutboxJobAsync(OutboxJob job)
    {
        await EnsureInitializedAsync();
        await _db!.UpdateAsync(job);
    }

    public async Task DeleteOutboxJobAsync(int id)
    {
        await EnsureInitializedAsync();
        await _db!.DeleteAsync<OutboxJob>(id);
    }
}

// Main (Home) ViewModel
public partial class MainViewModel : ObservableObject
{
    private readonly IDatabaseService _db;
    private const string SettingsPrefKey = "app.settings.json";

    public ObservableCollection<ClientSummary> Clients { get; } = new();

    [ObservableProperty]
    private string? searchText;

    [ObservableProperty]
    private double totalPending;

    [ObservableProperty]
    private string pageTitle = "Home";

    public MainViewModel(IDatabaseService db)
    {
        _db = db;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        await _db.InitializeAsync();
        var items = await _db.GetClientSummariesAsync(SearchText);
        Clients.Clear();
        foreach (var i in items)
            Clients.Add(i);
        TotalPending = await _db.GetAllPendingAsync();

        // Update page title from settings if FloorMealName exists
        try
        {
            var json = await _db.GetSettingAsync(SettingsPrefKey) ?? string.Empty;
            var data = string.IsNullOrWhiteSpace(json) ? new SettingsData() : (System.Text.Json.JsonSerializer.Deserialize<SettingsData>(json) ?? new SettingsData());
            if (!string.IsNullOrWhiteSpace(data.FloorMealName))
                PageTitle = data.FloorMealName.Trim();
            else
                PageTitle = "Home";
        }
        catch { PageTitle = "Home"; }
    }

    [RelayCommand]
    public async Task SearchChangedAsync(string? text)
    {
        SearchText = text;
        await LoadAsync();
    }

    [RelayCommand]
    public async Task AddClientAsync()
    {
        await Shell.Current.GoToAsync("addclient");
    }

    [RelayCommand]
    public async Task SelectClientAsync(ClientSummary? client)
    {
        if (client == null) return;
        await Shell.Current.GoToAsync($"clientdetail?clientId={client.Id}");
    }
}
