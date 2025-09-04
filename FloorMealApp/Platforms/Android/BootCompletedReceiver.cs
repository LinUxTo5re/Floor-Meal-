using Android.App;
using Android.Content;

namespace ClientLedgerApp;

[BroadcastReceiver(Enabled = true, Exported = true)]
[IntentFilter(new[] { Intent.ActionBootCompleted })]
public class BootCompletedReceiver : BroadcastReceiver
{
    public override void OnReceive(Context context, Intent intent)
    {
        try
        {
            if (Intent.ActionBootCompleted.Equals(intent.Action))
            {
                SyncJobService.Schedule(context);
            }
        }
        catch { }
    }
}