using Android.App;
using Android.Content;

namespace Markwardt.ClarityJournal.Client;

/// <summary>
/// Schedules the reminders again when the system forgot them: after the device boots, after the app is updated and after the clock or time zone changes.
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = true)]
[IntentFilter([Intent.ActionBootCompleted, Intent.ActionMyPackageReplaced, Intent.ActionTimezoneChanged, Intent.ActionTimeChanged])]
public sealed class ReminderRescheduler : BroadcastReceiver
{
    /// <inheritdoc />
    public override void OnReceive(Context? context, Intent? intent) => new Reminders(new IReminderSchedule.Factory().Create()).Schedule();
}
