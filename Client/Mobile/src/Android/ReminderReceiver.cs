using Android.App;
using Android.Content;

namespace Markwardt.ClarityJournal.Client;

/// <summary>
/// Receives the alarm of a reminder: shows the notification and schedules the next reminder.
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = false)]
public sealed class ReminderReceiver : BroadcastReceiver
{
    /// <inheritdoc />
    public override void OnReceive(Context? context, Intent? intent)
    {
        new ReminderNotifier().Notify(intent?.GetStringExtra(Reminders.MessageKey) ?? "Take a moment to write your journal.");
        new Reminders(new IReminderSchedule.Factory().Create()).Schedule();
    }
}
