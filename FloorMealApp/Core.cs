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

    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
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
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
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
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
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

// Deletion log for reflecting deletes to cloud
[Table("DeletionLog")]
public class DeletionLog
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    public string EntityType { get; set; } = string.Empty; // Client | Order | Payment
    public int EntityId { get; set; }
    [Indexed]
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
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

    // Deletion log helpers
    Task LogDeletionAsync(string entityType, int entityId);
    Task<List<DeletionLog>> GetDeletionLogsAsync(int max = 200);
    Task DeleteDeletionLogAsync(int id);
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
            await _db.CreateTableAsync<DeletionLog>();
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
            await _db!.ExecuteAsync("ALTER TABLE Credentials ADD COLUMN ImgBBApiKey TEXT");
        }
        catch { /* ignore if already exists */ }

        // Add ModifiedAt columns lazily
        try { await _db!.ExecuteAsync("ALTER TABLE Client ADD COLUMN ModifiedAt TEXT"); } catch { }
        try { await _db!.ExecuteAsync("ALTER TABLE [Order] ADD COLUMN ModifiedAt TEXT"); } catch { }
        try { await _db!.ExecuteAsync("ALTER TABLE Payment ADD COLUMN ModifiedAt TEXT"); } catch { }

        // Performance indexes (idempotent)
        try { await _db!.ExecuteAsync("CREATE INDEX IF NOT EXISTS IX_Client_Contact ON Client(Contact)"); } catch { }
        try { await _db!.ExecuteAsync("CREATE INDEX IF NOT EXISTS IX_Order_ClientId ON [Order](ClientId)"); } catch { }
        try { await _db!.ExecuteAsync("CREATE INDEX IF NOT EXISTS IX_Payment_ClientId ON Payment(ClientId)"); } catch { }
    }

    public async Task<int> AddClientAsync(Client client)
    {
        await EnsureInitializedAsync();
        client.ModifiedAt = DateTime.UtcNow;
        return await _db!.InsertAsync(client);
    }

    public async Task UpdateClientAsync(Client client)
    {
        await EnsureInitializedAsync();
        client.ModifiedAt = DateTime.UtcNow;
        await _db!.UpdateAsync(client);
    }

    public async Task DeleteClientAsync(int clientId)
    {
        await EnsureInitializedAsync();
        // cascade: delete orders & payments for client (log deletions first)
        var orders = await _db!.Table<Order>().Where(o => o.ClientId == clientId).ToListAsync();
        var payments = await _db.Table<Payment>().Where(p => p.ClientId == clientId).ToListAsync();
        foreach (var o in orders)
        {
            await LogDeletionAsync("Order", o.Id);
            await _db.DeleteAsync(o);
        }
        foreach (var p in payments)
        {
            await LogDeletionAsync("Payment", p.Id);
            await _db.DeleteAsync(p);
        }
        await LogDeletionAsync("Client", clientId);
        await _db.DeleteAsync<Client>(clientId);
    }

    public async Task<List<Client>> GetClientsAsync(string? search = null)
    {
        await EnsureInitializedAsync();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = "%" + search.Trim().Replace("%", "[%]").Replace("_", "[_]") + "%";
            // Use SQL LIKE for server-side filtering (NOCASE for case-insensitive)
            var sql = "SELECT * FROM Client WHERE Name LIKE ? ESCAPE '[' COLLATE NOCASE OR Contact LIKE ? ESCAPE '[' COLLATE NOCASE ORDER BY Name";
            return await _db!.QueryAsync<Client>(sql, pattern, pattern);
        }
        // No filter
        return await _db!.Table<Client>().OrderBy(c => c.Name).ToListAsync();
    }

    public async Task<int> AddOrderAsync(Order order)
    {
        await EnsureInitializedAsync();
        // ensure total is consistent
        order.Total = order.WeightKg * order.RatePerKg;
        order.ModifiedAt = DateTime.UtcNow;
        return await _db!.InsertAsync(order);
    }

    public async Task UpdateOrderAsync(Order order)
    {
        await EnsureInitializedAsync();
        order.Total = order.WeightKg * order.RatePerKg;
        order.ModifiedAt = DateTime.UtcNow;
        await _db!.UpdateAsync(order);
    }

    public async Task DeleteOrderAsync(int orderId)
    {
        await EnsureInitializedAsync();
        await LogDeletionAsync("Order", orderId);
        await _db!.DeleteAsync<Order>(orderId);
    }

    public async Task<int> AddPaymentAsync(Payment payment)
    {
        await EnsureInitializedAsync();
        payment.ModifiedAt = DateTime.UtcNow;
        return await _db!.InsertAsync(payment);
    }

    public async Task UpdatePaymentAsync(Payment payment)
    {
        await EnsureInitializedAsync();
        payment.ModifiedAt = DateTime.UtcNow;
        await _db!.UpdateAsync(payment);
    }

    public async Task DeletePaymentAsync(int paymentId)
    {
        await EnsureInitializedAsync();
        await LogDeletionAsync("Payment", paymentId);
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
        if (clients.Count == 0) return new List<ClientSummary>();
        var clientIds = clients.Select(c => c.Id).ToList();
        var placeholders = string.Join(",", Enumerable.Repeat("?", clientIds.Count));
        var args = clientIds.Cast<object>().ToArray();

        // Batch load orders and payments for all clients in one query each
        var orders = await _db!.QueryAsync<Order>($"SELECT * FROM [Order] WHERE ClientId IN ({placeholders})", args);
        var payments = await _db!.QueryAsync<Payment>($"SELECT * FROM Payment WHERE ClientId IN ({placeholders})", args);

        var totalsByClient = orders.GroupBy(o => o.ClientId).ToDictionary(g => g.Key, g => g.Sum(o => o.Total));
        var countsByClient = orders.GroupBy(o => o.ClientId).ToDictionary(g => g.Key, g => g.Count());
        var receivedByClient = payments.GroupBy(p => p.ClientId).ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));

        var list = new List<ClientSummary>(clients.Count);
        foreach (var c in clients)
        {
            totalsByClient.TryGetValue(c.Id, out var totals);
            receivedByClient.TryGetValue(c.Id, out var received);
            countsByClient.TryGetValue(c.Id, out var orderCount);
            list.Add(new ClientSummary
            {
                Id = c.Id,
                Name = c.Name,
                Contact = c.Contact,
                Profile = c.Profile,
                Pending = Math.Max(0, (totals) - (received)),
                OrderCount = orderCount
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
        client.ModifiedAt = DateTime.UtcNow;
        await _db!.InsertOrReplaceAsync(client);
    }

    public async Task UpsertOrderAsync(Order order)
    {
        await EnsureInitializedAsync();
        // Ensure total is consistent when upserting
        order.Total = order.WeightKg * order.RatePerKg;
        order.ModifiedAt = DateTime.UtcNow;
        await _db!.InsertOrReplaceAsync(order);
    }

    public async Task UpsertPaymentAsync(Payment payment)
    {
        await EnsureInitializedAsync();
        payment.ModifiedAt = DateTime.UtcNow;
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
// Deletion log helpers
    public async Task LogDeletionAsync(string entityType, int entityId)
    {
        await EnsureInitializedAsync();
        await _db!.InsertAsync(new DeletionLog { EntityType = entityType, EntityId = entityId, TimestampUtc = DateTime.UtcNow });
    }

    public async Task<List<DeletionLog>> GetDeletionLogsAsync(int max = 200)
    {
        await EnsureInitializedAsync();
        return await _db!.Table<DeletionLog>().OrderBy(d => d.TimestampUtc).Take(max).ToListAsync();
    }

    public async Task DeleteDeletionLogAsync(int id)
    {
        await EnsureInitializedAsync();
        await _db!.DeleteAsync<DeletionLog>(id);
    }
}

// Main (Home) ViewModel
public partial class MainViewModel : ObservableObject
{
    private readonly IDatabaseService _db;
    private readonly ICustomAlertService _alert;
    private const string SettingsPrefKey = "app.settings.json";

    public ObservableCollection<ClientSummary> Clients { get; } = new();

    [ObservableProperty]
    private string? searchText;

    [ObservableProperty]
    private double totalPending;

    [ObservableProperty]
    private string pageTitle = "Home";

    public MainViewModel(IDatabaseService db, ICustomAlertService alert)
    {
        _db = db;
        _alert = alert;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        await _db.InitializeAsync();
        var items = await _db.GetClientSummariesAsync(SearchText);
        Clients.Clear();
        double totalPendingLocal = 0;
        foreach (var i in items)
        {
            Clients.Add(i);
            totalPendingLocal += i.Pending;
        }
        TotalPending = totalPendingLocal;

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

    [RelayCommand]
    public async Task DeleteClientAsync(ClientSummary? client)
    {
        if (client == null) return;
        var confirmed = await _alert.ShowConfirmAsync(
            "Delete Client",
            $"Are you sure you want to delete '{client.Name}'? This will also remove their orders and payments.",
            "Delete", "Cancel", AlertType.Warning);
        if (!confirmed) return;
        await _db.DeleteClientAsync(client.Id);
        await LoadAsync();
        await _alert.ShowSuccessAsync($"Client '{client.Name}' deleted.", "Deleted");
    }

    [RelayCommand]
    public async Task EditClientAsync(ClientSummary? client)
    {
        if (client == null) return;
        await Shell.Current.GoToAsync($"editclient?clientId={client.Id}");
    }
}
