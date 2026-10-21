using Android;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Avalonia.Android;

namespace Markwardt.ClarityJournal.Client;

/// <summary>
/// The Android activity that hosts the Avalonia application.
/// </summary>
[Activity(Label = "Clarity Journal", Icon = "@mipmap/appicon", Theme = "@style/Theme.AppCompat.NoActionBar", MainLauncher = true, ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode, LaunchMode = LaunchMode.SingleTop)]
public sealed class MainActivity : AvaloniaMainActivity
{
    private static MainActivity? current;

    private static TaskCompletionSource<bool>? microphone;

    private static readonly int microphoneRequest = 2;

    private static readonly int notificationsRequest = 1;

    /// <summary>
    /// Makes sure the app may use the microphone, asking the user if it has not been decided yet.
    /// </summary>
    /// <returns>Whether the microphone may be used.</returns>
    public static Task<bool> AskForMicrophone()
    {
        if (current is not { } activity)
        {
            return Task.FromResult(false);
        }

        if (activity.CheckSelfPermission(Manifest.Permission.RecordAudio) == Permission.Granted)
        {
            return Task.FromResult(true);
        }

        microphone?.TrySetResult(false);
        TaskCompletionSource<bool> asking = new(TaskCreationOptions.RunContinuationsAsynchronously);
        microphone = asking;
        activity.RunOnUiThread(() => activity.RequestPermissions([Manifest.Permission.RecordAudio], microphoneRequest));
        return asking.Task;
    }

    /// <inheritdoc />
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        current = this;

        if (OperatingSystem.IsAndroidVersionAtLeast(33) && CheckSelfPermission(Manifest.Permission.PostNotifications) != Permission.Granted)
        {
            RequestPermissions([Manifest.Permission.PostNotifications], notificationsRequest);
        }

        new Reminders(new IReminderSchedule.Factory().Create()).Schedule();
    }

    /// <inheritdoc />
    protected override void OnDestroy()
    {
        if (current == this)
        {
            current = null;
        }

        base.OnDestroy();
    }

    /// <inheritdoc />
    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);

        if (requestCode == microphoneRequest)
        {
            microphone?.TrySetResult(grantResults.Length > 0 && grantResults[0] == Permission.Granted);
        }
    }
}
