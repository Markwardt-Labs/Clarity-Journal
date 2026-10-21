using Android.App;
using Android.Content;

namespace Markwardt.ClarityJournal.Client;

/// <summary>
/// Shows the notification that reminds the user to write the journal.
/// </summary>
internal interface IReminderNotifier
{
    /// <summary>
    /// Shows the notification; where the user has not allowed notifications the system drops it.
    /// </summary>
    /// <param name="message">The text of the notification.</param>
    void Notify(string message);
}

/// <inheritdoc />
internal sealed class ReminderNotifier : IReminderNotifier
{
    private readonly string channelId = "journal-reminders";

    private readonly int notificationId = 1;

    private readonly Context context = global::Android.App.Application.Context;

    /// <inheritdoc />
    public void Notify(string message)
    {
        if (context.GetSystemService(Context.NotificationService) is not NotificationManager manager)
        {
            return;
        }

        Notification.Builder builder;

        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            manager.CreateNotificationChannel(new NotificationChannel(channelId, "Journal reminders", NotificationImportance.Default) { Description = "Reminds you at noon and in the evening to write your journal" });
            builder = new Notification.Builder(context, channelId);
        }
        else
        {
#pragma warning disable CS0618
            builder = new Notification.Builder(context);
#pragma warning restore CS0618
        }

        Intent launch = new Intent(context, typeof(MainActivity)).AddFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop | ActivityFlags.ClearTop)!;
        PendingIntent? open = PendingIntent.GetActivity(context, 0, launch, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        Notification notification = builder
            .SetSmallIcon(context.ApplicationInfo?.Icon ?? 0)!
            .SetContentTitle("Clarity Journal")!
            .SetContentText(message)!
            .SetStyle(new Notification.BigTextStyle().BigText(message))!
            .SetAutoCancel(true)!
            .SetContentIntent(open)!
            .Build()!;
        manager.Notify(notificationId, notification);
    }
}
