namespace Markwardt.ClarityJournal.Client;

/// <summary>
/// The Android head of the client application; hosts the root view as the single view.
/// </summary>
public sealed class App : ClientApp
{
    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            IReleaseFeed feed = new IReleaseFeed.Factory().Create();
            singleView.MainView = CreateMainView(typeof(App).Assembly, null, new AndroidVoiceRecorder(), feed, new AndroidUpdater(feed));
        }

        base.OnFrameworkInitializationCompleted();
    }
}
