using Android.App;
using Android.App.Job;
using Android.Content;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ClientLedgerApp;

[Service(Name = "com.linuxto5re.fmc.SyncJobService", Permission = "android.permission.BIND_JOB_SERVICE", Exported = true)]
public class SyncJobService : JobService
{
    public override bool OnStartJob(JobParameters @params)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                // Minimal dependencies to run push-only sync
                var db = new DatabaseService();
                await db.InitializeAsync();

                var settingsJson = await db.GetSettingAsync("app.settings.json") ?? string.Empty;
                var data = string.IsNullOrWhiteSpace(settingsJson) ? new SettingsData() : (System.Text.Json.JsonSerializer.Deserialize<SettingsData>(settingsJson) ?? new SettingsData());
                var mail = data.MailId?.Trim();
                if (!string.IsNullOrWhiteSpace(mail))
                {
                    var aws = new AwsSyncService(db);
                    await aws.EnsureGlobalCredentialsAsync();
                    await PushNewChangesAsync(mail, db, aws);
                }
            }
            catch { /* ignore to keep job healthy */ }
            finally
            {
                try { JobFinished(@params, false); } catch { }
            }
        });

        // Work is running async
        return true;
    }

    public override bool OnStopJob(JobParameters @params)
    {
        // If the job is stopped prematurely, request reschedule
        return true;
    }

    private static async Task PushNewChangesAsync(string mailId, IDatabaseService db, IAwsSyncService aws)
    {
        const string Key = "sync.lastPushTicks";
        try
        {
            var lastTicksStr = await db.GetSettingAsync(Key);
            long lastTicks;
            if (!long.TryParse(lastTicksStr, out lastTicks))
            {
                // Initialize on first run so we don't push historical data
                var nowTicks = DateTime.UtcNow.Ticks;
                await db.SetSettingAsync(Key, nowTicks.ToString());
                return;
            }
            var lastPushUtc = new DateTime(lastTicks, DateTimeKind.Utc);

            // Clients
            var clients = await db.GetClientsAsync();
            foreach (var c in clients)
            {
                var createdUtc = DateTime.SpecifyKind(c.CreatedAt, DateTimeKind.Utc);
                if (createdUtc > lastPushUtc)
                {
                    await aws.PutClientAsync(mailId, c);
                }
            }

            // Orders and payments per client
            foreach (var c in clients)
            {
                var orders = await db.GetOrdersForClientAsync(c.Id);
                foreach (var o in orders)
                {
                    var od = DateTime.SpecifyKind(o.Date, DateTimeKind.Utc);
                    if (od > lastPushUtc)
                    {
                        await aws.PutOrderAsync(mailId, o);
                    }
                }
                var payments = await db.GetPaymentsForClientAsync(c.Id);
                foreach (var p in payments)
                {
                    var pd = DateTime.SpecifyKind(p.Date, DateTimeKind.Utc);
                    if (pd > lastPushUtc)
                    {
                        await aws.PutPaymentAsync(mailId, p);
                    }
                }
            }

            var newTicks = DateTime.UtcNow.Ticks;
            await db.SetSettingAsync(Key, newTicks.ToString());
        }
        catch { /* ignore to keep background robust */ }
    }

    public static void Schedule(Context context)
    {
        try
        {
            var component = new Android.Content.ComponentName(context, Java.Lang.Class.FromType(typeof(SyncJobService)).Name);
            var builder = new JobInfo.Builder(1001, component)
                .SetRequiredNetworkType(NetworkType.Any)
                .SetPersisted(true)
                .SetPeriodic(15 * 60 * 1000L); // 15 minutes minimum

            var job = builder.Build();
            var scheduler = (JobScheduler)context.GetSystemService(JobSchedulerService);

            // Avoid duplicate schedules
            var existing = scheduler.AllPendingJobs?.FirstOrDefault(j => j.Id == 1001);
            if (existing != null)
            {
                scheduler.Cancel(1001);
            }
            scheduler.Schedule(job);
        }
        catch { /* ignore */ }
    }
}