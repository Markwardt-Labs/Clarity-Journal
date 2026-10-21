using Android.App;
using Android.Content;

namespace Markwardt.ClarityJournal.Client;

/// <summary>
/// Keeps the daily reminders to write the journal scheduled with the system.
/// </summary>
internal interface IReminders
{
    /// <summary>
    /// Schedules the next reminder, replacing the one that was scheduled before. Called when the app starts, when the device boots, when the clock or time zone changes and each time a reminder is shown, since the system keeps one alarm and not a series.
    /// </summary>
    void Schedule();
}

/// <inheritdoc />
internal sealed class Reminders(IReminderSchedule schedule) : IReminders
{
    private readonly Context context = global::Android.App.Application.Context;

    /// <summary>
    /// Gets the name of the intent extra that carries the text of the reminder.
    /// </summary>
    public static string MessageKey => "message";

    /// <inheritdoc />
    public void Schedule()
    {
        if (context.GetSystemService(Context.AlarmService) is not AlarmManager alarms)
        {
            return;
        }

        Reminder next = schedule.Next(DateTime.Now);
        Intent intent = new Intent(context, typeof(ReminderReceiver)).PutExtra(MessageKey, next.Message)!;
        PendingIntent? pending = PendingIntent.GetBroadcast(context, 0, intent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

        if (pending is not null)
        {
            alarms.SetAndAllowWhileIdle(AlarmType.RtcWakeup, new DateTimeOffset(next.At).ToUnixTimeMilliseconds(), pending);
        }
    }
}
