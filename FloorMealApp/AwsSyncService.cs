using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Amazon;
using Amazon.CognitoIdentity;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.DynamoDBv2.Model;
using Microsoft.Maui.Storage;

namespace ClientLedgerApp;

public interface IAwsSyncService
{
    Task<Credentials?> GetRemoteCredentialsAsync(string email);
    Task<(int isActive, string? devMessage)> GetUserFlagsAsync(string email);
    Task SyncAllForUserAsync(string email);
    Task EnsureGlobalCredentialsAsync();
    Task PutAppSettingAsync(string mailId, string json);
    Task PutClientAsync(string mailId, Client client);
    Task PutOrderAsync(string mailId, Order order);
    Task PutPaymentAsync(string mailId, Payment payment);
}

public class AwsSyncService : IAwsSyncService
{
    private readonly IDatabaseService _db;

    // Table names
    private const string TBL_CREDENTIALS = "ClientLedger_Credentials";
    private const string TBL_CLIENT = "ClientLedger_Client";
    private const string TBL_ORDER = "ClientLedger_Order";
    private const string TBL_PAYMENT = "ClientLedger_Payment";
    private const string TBL_APPSETTING = "ClientLedger_AppSetting";

    public AwsSyncService(IDatabaseService db)
    {
        _db = db;
    }

    private async Task<SettingsData> LoadSettingsAsync()
    {
        await _db.InitializeAsync();
        var json = await _db.GetSettingAsync("app.settings.json") ?? string.Empty;
        var data = string.IsNullOrWhiteSpace(json) ? new SettingsData() : (JsonSerializer.Deserialize<SettingsData>(json) ?? new SettingsData());
        try
        {
            // Load defaults from packaged raw resource if present
            using var stream = await FileSystem.OpenAppPackageFileAsync("awsconfig.json");
            using var reader = new StreamReader(stream);
            var raw = await reader.ReadToEndAsync();
            var cfg = JsonSerializer.Deserialize<SettingsData>(raw) ?? new SettingsData();
            if (string.IsNullOrWhiteSpace(data.AwsRegion) && !string.IsNullOrWhiteSpace(cfg.AwsRegion)) data.AwsRegion = cfg.AwsRegion;
            if (string.IsNullOrWhiteSpace(data.CognitoIdentityPoolId) && !string.IsNullOrWhiteSpace(cfg.CognitoIdentityPoolId)) data.CognitoIdentityPoolId = cfg.CognitoIdentityPoolId;
            if (!data.EnableAwsSync) data.EnableAwsSync = cfg.EnableAwsSync;
        }
        catch { /* ignore if missing */ }
        return data;
    }

    private bool IsConfigured(SettingsData s)
        => s.EnableAwsSync && !string.IsNullOrWhiteSpace(s.AwsRegion) && !string.IsNullOrWhiteSpace(s.CognitoIdentityPoolId);

    private async Task<(AmazonDynamoDBClient ddb, string mailId)?> CreateClientAsync()
    {
        try
        {
            var s = await LoadSettingsAsync();
            if (!IsConfigured(s)) return null;
            var region = RegionEndpoint.GetBySystemName(s.AwsRegion!.Trim());
            var creds = new CognitoAWSCredentials(s.CognitoIdentityPoolId!.Trim(), region);
            var ddb = new AmazonDynamoDBClient(creds, region);
            var mailJson = await _db.GetSettingAsync("app.settings.json") ?? string.Empty;
            var app = string.IsNullOrWhiteSpace(mailJson) ? new SettingsData() : (JsonSerializer.Deserialize<SettingsData>(mailJson) ?? new SettingsData());
            var mailId = app.MailId?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(mailId)) return null;
            return (ddb, mailId);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CreateClientAsync error: {ex}");
            return null;
        }
    }

    private async Task<AmazonDynamoDBClient?> CreateDdbClientAsync()
    {
        try
        {
            var s = await LoadSettingsAsync();
            // For fetching global credentials, do not require EnableAwsSync; just need Region and Identity Pool Id
            if (string.IsNullOrWhiteSpace(s.AwsRegion) || string.IsNullOrWhiteSpace(s.CognitoIdentityPoolId)) return null;
            var region = RegionEndpoint.GetBySystemName(s.AwsRegion!.Trim());
            var creds = new CognitoAWSCredentials(s.CognitoIdentityPoolId!.Trim(), region);
            return new AmazonDynamoDBClient(creds, region);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CreateDdbClientAsync error: {ex}");
            return null;
        }
    }

    public async Task EnsureGlobalCredentialsAsync()
    {
        try
        {
            var ddb = await CreateDdbClientAsync();
            if (ddb == null) return;
            var resp = await ddb.GetItemAsync(new GetItemRequest
            {
                TableName = TBL_CREDENTIALS,
                Key = new System.Collections.Generic.Dictionary<string, AttributeValue>
                {
                    { "MailId", new AttributeValue { S = "GLOBAL" } }
                }
            });
            if (resp.Item == null || resp.Item.Count == 0) return;
            resp.Item.TryGetValue("SmtpUser", out var smtpUserAttr);
            resp.Item.TryGetValue("SmtpAppPassword", out var smtpPassAttr);
            var cred = new Credentials
            {
                Email = "GLOBAL",
                SmtpUser = smtpUserAttr?.S,
                SmtpAppPassword = smtpPassAttr?.S,
                IsActive = 1
            };
            await _db.UpsertCredentialsAsync(cred);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"EnsureGlobalCredentialsAsync error: {ex}");
        }
    }

    public async Task<(int isActive, string? devMessage)> GetUserFlagsAsync(string email)
    {
        try
        {
            var ddb = await CreateDdbClientAsync();
            if (ddb == null) return (1, null);
            var appTable = Table.LoadTable(ddb, TBL_APPSETTING);
            var udoc = await appTable.GetItemAsync(email);
            int isActive = 1;
            string? devMessage = null;
            if (udoc != null)
            {
                if (udoc.TryGetValue("IsActive", out var iaVal))
                    isActive = (int)iaVal.AsInt();
                if (udoc.TryGetValue("DevMessage", out var dmVal))
                    devMessage = dmVal.AsString();
            }
            return (isActive, devMessage);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GetUserFlagsAsync error: {ex}");
            return (1, null);
        }
    }

    public async Task<Credentials?> GetRemoteCredentialsAsync(string email)
    {
        try
        {
            var ddb = await CreateDdbClientAsync();
            if (ddb == null) return null;
            // Read global SMTP from credentials table (MailId = "GLOBAL")
            var credTable = Table.LoadTable(ddb, TBL_CREDENTIALS);
            var gdoc = await credTable.GetItemAsync("GLOBAL");
            // Read user IsActive from app settings table
            var appTable = Table.LoadTable(ddb, TBL_APPSETTING);
            var udoc = await appTable.GetItemAsync(email);
            int isActive = 1;
            string? devMessage = null;
            if (udoc != null)
            {
                if (udoc.TryGetValue("IsActive", out var iaVal))
                    isActive = (int)iaVal.AsInt();
                if (udoc.TryGetValue("DevMessage", out var dmVal))
                    devMessage = dmVal.AsString();
            }
            return new Credentials
            {
                Email = email,
                SmtpUser = gdoc != null && gdoc.TryGetValue("SmtpUser", out var su) ? su.AsString() : null,
                SmtpAppPassword = gdoc != null && gdoc.TryGetValue("SmtpAppPassword", out var sp) ? sp.AsString() : null,
                IsActive = isActive
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GetRemoteCredentialsAsync error: {ex}");
            return null;
        }
    }

    public async Task SyncAllForUserAsync(string email)
    {
        try
        {
            Debug.WriteLine($"SyncAllForUserAsync: Starting sync for email: {email}");
            
            var ddb = await CreateDdbClientAsync();
            if (ddb == null) 
            {
                Debug.WriteLine("SyncAllForUserAsync: DDB client is null - missing region or identity pool configuration");
                return;
            }
            
            var mailId = email?.Trim();
            if (string.IsNullOrWhiteSpace(mailId)) 
            {
                Debug.WriteLine("SyncAllForUserAsync: MailId is null or empty");
                return;
            }

            Debug.WriteLine($"SyncAllForUserAsync: Using mailId: {mailId}");

            // Pull from remote to local first
            Debug.WriteLine("SyncAllForUserAsync: Starting pull operations...");
            await PullCredentialsAsync(ddb, mailId);
            await PullAppSettingAsync(ddb, mailId);
            await PullClientsAsync(ddb, mailId);
            await PullOrdersAsync(ddb, mailId);
            await PullPaymentsAsync(ddb, mailId);

            // Push local to remote (optional for full sync)
            Debug.WriteLine("SyncAllForUserAsync: Starting push operations...");
            await PushAppSettingAsync(ddb, mailId);
            await PushClientsAsync(ddb, mailId);
            await PushOrdersAsync(ddb, mailId);
            await PushPaymentsAsync(ddb, mailId);
            
            Debug.WriteLine("SyncAllForUserAsync: Sync completed successfully");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SyncAllForUserAsync error: {ex}");
        }
    }

    public async Task PutAppSettingAsync(string mailId, string json)
    {
        try
        {
            var ddb = await CreateDdbClientAsync();
            if (ddb == null) return;
            var req = new PutItemRequest
            {
                TableName = TBL_APPSETTING,
                Item = new System.Collections.Generic.Dictionary<string, AttributeValue>
                {
                    { "MailId", new AttributeValue { S = mailId } },
                    { "Json", new AttributeValue { S = json } }
                }
            };
            await ddb.PutItemAsync(req);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"PutAppSettingAsync error: {ex}");
        }
    }

    public async Task PutClientAsync(string mailId, Client client)
    {
        try
        {
            var ddb = await CreateDdbClientAsync();
            if (ddb == null) return;
            var item = new System.Collections.Generic.Dictionary<string, AttributeValue>
            {
                { "MailId", new AttributeValue { S = mailId } },
                // ClientId is a STRING (S) key in DynamoDB
                { "ClientId", new AttributeValue { S = client.Id.ToString() } },
                { "Name", new AttributeValue { S = client.Name ?? string.Empty } },
            };
            if (!string.IsNullOrWhiteSpace(client.Contact)) item["Contact"] = new AttributeValue { S = client.Contact };
            if (!string.IsNullOrWhiteSpace(client.Profile)) item["Profile"] = new AttributeValue { S = client.Profile };
            item["CreatedAtTicks"] = new AttributeValue { N = client.CreatedAt.Ticks.ToString() };
            await ddb.PutItemAsync(new PutItemRequest { TableName = TBL_CLIENT, Item = item });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"PutClientAsync error: {ex}");
        }
    }

    public async Task PutOrderAsync(string mailId, Order order)
    {
        try
        {
            var ddb = await CreateDdbClientAsync();
            if (ddb == null) return;
            var item = new System.Collections.Generic.Dictionary<string, AttributeValue>
            {
                { "MailId", new AttributeValue { S = mailId } },
                { "OrderId", new AttributeValue { S = order.Id.ToString() } },
                { "ClientId", new AttributeValue { N = order.ClientId.ToString() } },
                { "Item", new AttributeValue { S = order.Item ?? string.Empty } },
                { "WeightKg", new AttributeValue { N = order.WeightKg.ToString() } },
                { "RatePerKg", new AttributeValue { N = order.RatePerKg.ToString() } },
                { "Total", new AttributeValue { N = order.Total.ToString() } },
                { "DateTicks", new AttributeValue { N = order.Date.Ticks.ToString() } }
            };
            await ddb.PutItemAsync(new PutItemRequest { TableName = TBL_ORDER, Item = item });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"PutOrderAsync error: {ex}");
        }
    }

    public async Task PutPaymentAsync(string mailId, Payment payment)
    {
        try
        {
            var ddb = await CreateDdbClientAsync();
            if (ddb == null) return;
            var item = new System.Collections.Generic.Dictionary<string, AttributeValue>
            {
                { "MailId", new AttributeValue { S = mailId } },
                { "PaymentId", new AttributeValue { S = payment.Id.ToString() } },
                { "ClientId", new AttributeValue { N = payment.ClientId.ToString() } },
                { "Amount", new AttributeValue { N = payment.Amount.ToString() } },
                { "DateTicks", new AttributeValue { N = payment.Date.Ticks.ToString() } }
            };
            if (!string.IsNullOrWhiteSpace(payment.Note)) item["Note"] = new AttributeValue { S = payment.Note };
            await ddb.PutItemAsync(new PutItemRequest { TableName = TBL_PAYMENT, Item = item });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"PutPaymentAsync error: {ex}");
        }
    }

    private static Document NewDoc(string pkName, string pkValue)
    {
        var d = new Document();
        d[pkName] = pkValue;
        return d;
    }

    private async Task PullCredentialsAsync(AmazonDynamoDBClient ddb, string mailId)
    {
        try
        {
            // Pull global SMTP credentials only (MailId = "GLOBAL") and store locally under Email="GLOBAL"
            var table = Table.LoadTable(ddb, TBL_CREDENTIALS);
            var doc = await table.GetItemAsync("GLOBAL");
            if (doc == null) return;
            var cred = new Credentials
            {
                Email = "GLOBAL",
                SmtpUser = doc.TryGetValue("SmtpUser", out var su) ? su.AsString() : null,
                SmtpAppPassword = doc.TryGetValue("SmtpAppPassword", out var sp) ? sp.AsString() : null,
                 ImgBBApiKey = doc.TryGetValue("ImgBBApiKey", out var imgbb) ? imgbb.AsString() : null,
                IsActive = 1 // do not use global IsActive for gating
            };
            await _db.UpsertCredentialsAsync(cred);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"PullCredentialsAsync error: {ex}");
        }
    }

    private async Task PushCredentialsAsync(AmazonDynamoDBClient ddb, string mailId)
    {
        // Do not push credentials per-user; credentials are managed centrally as GLOBAL.
        await Task.CompletedTask;
    }

    private async Task PullAppSettingAsync(AmazonDynamoDBClient ddb, string mailId)
    {
        try
        {
            var table = Table.LoadTable(ddb, TBL_APPSETTING);
            var doc = await table.GetItemAsync(mailId);
            if (doc != null)
            {
                var json = doc.TryGetValue("Json", out var j) ? j.AsString() : null;
                if (!string.IsNullOrWhiteSpace(json))
                {
                    await _db.SetSettingAsync("app.settings.json", json);
                }
                // Also pull per-user IsActive and DevMessage from AppSetting and persist locally for gating/UX
                var localUserCred = await _db.GetCredentialsByEmailAsync(mailId) ?? new Credentials { Email = mailId };
                if (doc.TryGetValue("IsActive", out var ia))
                {
                    localUserCred.IsActive = (int)ia.AsInt();
                }
                await _db.UpsertCredentialsAsync(localUserCred);
            }
            // Ensure a local credentials row exists for this user (default active) even if no IsActive provided in cloud
            var ensureCred = await _db.GetCredentialsByEmailAsync(mailId);
            if (ensureCred == null)
            {
                await _db.UpsertCredentialsAsync(new Credentials { Email = mailId, IsActive = 1 });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"PullAppSettingAsync error: {ex}");
        }
    }

    private async Task PushAppSettingAsync(AmazonDynamoDBClient ddb, string mailId)
    {
        var json = await _db.GetSettingAsync("app.settings.json");
        if (string.IsNullOrWhiteSpace(json)) return;
        var table = Table.LoadTable(ddb, TBL_APPSETTING);
        var doc = NewDoc("MailId", mailId);
        doc["Json"] = json;
        await table.PutItemAsync(doc);
    }

    private async Task PullClientsAsync(AmazonDynamoDBClient ddb, string mailId)
    {
        try
        {
            var table = Table.LoadTable(ddb, TBL_CLIENT);
            var filterClients = new QueryFilter("MailId", QueryOperator.Equal, mailId);
            var search = table.Query(filterClients);
            List<Document> all = new();
            
            do
            {
                var page = await search.GetNextSetAsync();
                if (page == null || page.Count == 0) break;
                all.AddRange(page);
            } while (!search.IsDone);
            
            Debug.WriteLine($"PullClientsAsync: Found {all.Count} clients for mailId: {mailId}");
            
            foreach (var doc in all)
            {
                try
                {
                    int id = 0;
                    if (doc.TryGetValue("ClientId", out var sk))
                    {
                        // ClientId is stored as string in DynamoDB, parse it
                        var clientIdStr = sk.AsString();
                        if (!int.TryParse(clientIdStr, out id))
                        {
                            Debug.WriteLine($"PullClientsAsync: Failed to parse ClientId: {clientIdStr}");
                            continue;
                        }
                    }
                    
                    if (id <= 0)
                    {
                        Debug.WriteLine($"PullClientsAsync: Invalid ClientId: {id}");
                        continue;
                    }
                    
                    var client = new Client
                    {
                        Id = id,
                        Name = doc.TryGetValue("Name", out var nm) ? (nm.AsString() ?? string.Empty) : string.Empty,
                        Contact = doc.TryGetValue("Contact", out var ct) ? ct.AsString() : null,
                        Profile = doc.TryGetValue("Profile", out var pf) ? pf.AsString() : null,
                        CreatedAt = doc.TryGetValue("CreatedAtTicks", out var ca) ? new DateTime(ca.AsLong()) : DateTime.UtcNow
                    };
                    
                    Debug.WriteLine($"PullClientsAsync: Processing client - Id: {client.Id}, Name: {client.Name}");
                    
                    // Use UpsertClientAsync instead of manual try-catch
                    await _db.UpsertClientAsync(client);
                    
                    Debug.WriteLine($"PullClientsAsync: Successfully upserted client: {client.Name}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"PullClientsAsync: Error processing individual client: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"PullClientsAsync error: {ex}");
        }
    }

    private async Task PushClientsAsync(AmazonDynamoDBClient ddb, string mailId)
    {
        var clients = await _db.GetClientsAsync();
        var table = Table.LoadTable(ddb, TBL_CLIENT);
        foreach (var c in clients)
        {
            var doc = NewDoc("MailId", mailId);
            // ClientId stored as string in DynamoDB
            doc["ClientId"] = c.Id.ToString();
            doc["Name"] = c.Name ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(c.Contact)) doc["Contact"] = c.Contact;
            if (!string.IsNullOrWhiteSpace(c.Profile)) doc["Profile"] = c.Profile;
            doc["CreatedAtTicks"] = c.CreatedAt.Ticks;
            await table.PutItemAsync(doc);
        }
    }

    private async Task PullOrdersAsync(AmazonDynamoDBClient ddb, string mailId)
    {
        try
        {
            var table = Table.LoadTable(ddb, TBL_ORDER);
            var filterOrders = new QueryFilter("MailId", QueryOperator.Equal, mailId);
            var search = table.Query(filterOrders);
            List<Document> all = new();
            
            do
            {
                var page = await search.GetNextSetAsync();
                if (page == null || page.Count == 0) break;
                all.AddRange(page);
            } while (!search.IsDone);
            
            Debug.WriteLine($"PullOrdersAsync: Found {all.Count} orders for mailId: {mailId}");
            
            foreach (var doc in all)
            {
                try
                {
                    int id = 0;
                    if (doc.TryGetValue("OrderId", out var sk))
                    {
                        // OrderId is stored as string in DynamoDB, parse it
                        var orderIdStr = sk.AsString();
                        if (!int.TryParse(orderIdStr, out id))
                        {
                            Debug.WriteLine($"PullOrdersAsync: Failed to parse OrderId: {orderIdStr}");
                            continue;
                        }
                    }
                    
                    if (id <= 0)
                    {
                        Debug.WriteLine($"PullOrdersAsync: Invalid OrderId: {id}");
                        continue;
                    }
                    
                    var order = new Order
                    {
                        Id = id,
                        ClientId = doc.TryGetValue("ClientId", out var cid) ? cid.AsInt() : 0,
                        Item = doc.TryGetValue("Item", out var it) ? (it.AsString() ?? string.Empty) : string.Empty,
                        WeightKg = doc.TryGetValue("WeightKg", out var wg) ? wg.AsDouble() : 0d,
                        RatePerKg = doc.TryGetValue("RatePerKg", out var rk) ? rk.AsDouble() : 0d,
                        Total = doc.TryGetValue("Total", out var tt) ? tt.AsDouble() : 0d,
                        Date = doc.TryGetValue("DateTicks", out var dt) ? new DateTime(dt.AsLong()) : DateTime.UtcNow
                    };
                    
                    Debug.WriteLine($"PullOrdersAsync: Processing order - Id: {order.Id}, ClientId: {order.ClientId}, Item: {order.Item}");
                    
                    // Use UpsertOrderAsync instead of manual try-catch
                    await _db.UpsertOrderAsync(order);
                    
                    Debug.WriteLine($"PullOrdersAsync: Successfully upserted order: {order.Id}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"PullOrdersAsync: Error processing individual order: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"PullOrdersAsync error: {ex}");
        }
    }

    private async Task PushOrdersAsync(AmazonDynamoDBClient ddb, string mailId)
    {
        try
        {
            var clients = await _db.GetClientsAsync();
            var table = Table.LoadTable(ddb, TBL_ORDER);
            Debug.WriteLine($"PushOrdersAsync: Starting push for {clients.Count} clients");
            
            foreach (var c in clients)
            {
                var orders = await _db.GetOrdersForClientAsync(c.Id);
                Debug.WriteLine($"PushOrdersAsync: Pushing {orders.Count} orders for client {c.Name}");
                
                foreach (var o in orders)
                {
                    try
                    {
                        var doc = NewDoc("MailId", mailId);
                        doc["OrderId"] = o.Id.ToString();
                        doc["ClientId"] = o.ClientId;
                        doc["Item"] = o.Item ?? string.Empty;
                        doc["WeightKg"] = o.WeightKg;
                        doc["RatePerKg"] = o.RatePerKg;
                        doc["Total"] = o.Total;
                        doc["DateTicks"] = o.Date.Ticks;
                        await table.PutItemAsync(doc);
                        Debug.WriteLine($"PushOrdersAsync: Successfully pushed order {o.Id}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"PushOrdersAsync: Error pushing order {o.Id}: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"PushOrdersAsync error: {ex}");
        }
    }

    private async Task PullPaymentsAsync(AmazonDynamoDBClient ddb, string mailId)
    {
        try
        {
            var table = Table.LoadTable(ddb, TBL_PAYMENT);
            var filterPayments = new QueryFilter("MailId", QueryOperator.Equal, mailId);
            var search = table.Query(filterPayments);
            List<Document> all = new();
            
            do
            {
                var page = await search.GetNextSetAsync();
                if (page == null || page.Count == 0) break;
                all.AddRange(page);
            } while (!search.IsDone);
            
            Debug.WriteLine($"PullPaymentsAsync: Found {all.Count} payments for mailId: {mailId}");
            
            foreach (var doc in all)
            {
                try
                {
                    int id = 0;
                    if (doc.TryGetValue("PaymentId", out var sk))
                    {
                        // PaymentId is stored as string in DynamoDB, parse it
                        var paymentIdStr = sk.AsString();
                        if (!int.TryParse(paymentIdStr, out id))
                        {
                            Debug.WriteLine($"PullPaymentsAsync: Failed to parse PaymentId: {paymentIdStr}");
                            continue;
                        }
                    }
                    
                    if (id <= 0)
                    {
                        Debug.WriteLine($"PullPaymentsAsync: Invalid PaymentId: {id}");
                        continue;
                    }
                    
                    var payment = new Payment
                    {
                        Id = id,
                        ClientId = doc.TryGetValue("ClientId", out var cid) ? cid.AsInt() : 0,
                        Amount = doc.TryGetValue("Amount", out var am) ? am.AsDouble() : 0d,
                        Date = doc.TryGetValue("DateTicks", out var dt) ? new DateTime(dt.AsLong()) : DateTime.UtcNow,
                        Note = doc.TryGetValue("Note", out var nt) ? nt.AsString() : null
                    };
                    
                    Debug.WriteLine($"PullPaymentsAsync: Processing payment - Id: {payment.Id}, ClientId: {payment.ClientId}, Amount: {payment.Amount}");
                    
                    // Use UpsertPaymentAsync instead of manual try-catch
                    await _db.UpsertPaymentAsync(payment);
                    
                    Debug.WriteLine($"PullPaymentsAsync: Successfully upserted payment: {payment.Id}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"PullPaymentsAsync: Error processing individual payment: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"PullPaymentsAsync error: {ex}");
        }
    }

    private async Task PushPaymentsAsync(AmazonDynamoDBClient ddb, string mailId)
    {
        try
        {
            var clients = await _db.GetClientsAsync();
            var table = Table.LoadTable(ddb, TBL_PAYMENT);
            Debug.WriteLine($"PushPaymentsAsync: Starting push for {clients.Count} clients");
            
            foreach (var c in clients)
            {
                var payments = await _db.GetPaymentsForClientAsync(c.Id);
                Debug.WriteLine($"PushPaymentsAsync: Pushing {payments.Count} payments for client {c.Name}");
                
                foreach (var p in payments)
                {
                    try
                    {
                        var doc = NewDoc("MailId", mailId);
                        doc["PaymentId"] = p.Id.ToString();
                        doc["ClientId"] = p.ClientId;
                        doc["Amount"] = p.Amount;
                        doc["DateTicks"] = p.Date.Ticks;
                        if (!string.IsNullOrWhiteSpace(p.Note)) doc["Note"] = p.Note;
                        await table.PutItemAsync(doc);
                        Debug.WriteLine($"PushPaymentsAsync: Successfully pushed payment {p.Id}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"PushPaymentsAsync: Error pushing payment {p.Id}: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"PushPaymentsAsync error: {ex}");
        }
    }
}
