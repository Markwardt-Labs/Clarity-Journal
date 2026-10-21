namespace Markwardt.ClarityJournal.Client;

/// <summary>
/// The Avalonia application shared by every client head: the theme plus the root view wired to the real services.
/// </summary>
public class ClientApp : Avalonia.Application
{
    private readonly TimeSpan awayLimit = TimeSpan.FromSeconds(3);

    /// <inheritdoc />
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Builds the root view wired to the real services, for heads that host the view themselves.
    /// </summary>
    /// <param name="head">The executable assembly of the head, whose version is shown.</param>
    /// <param name="dataFolder">The folder holding the saved settings, or <see langword="null" /> for the <c>ClarityJournal</c> folder of the user's application data.</param>
    /// <param name="recorder">Records speech for the voice input buttons, or <see langword="null" /> when this client cannot record, which hides them.</param>
    /// <returns>The root view, already starting up.</returns>
    public MainView CreateMainView(Assembly head, string? dataFolder = null, IVoiceRecorder? recorder = null)
    {
        string folder = dataFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClarityJournal");
        IAppInfo info = new IAppInfo.Factory().Create(head);
        MainViewModel model = new(info, new IServerConnection.Factory(new IMessageChannel.Factory(), new ConsoleLog()), new SettingsStore(Path.Combine(folder, "settings.json")), recorder);
        MainView view = new() { DataContext = model };
        _ = model.Start();

        if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime lifetime)
        {
            DateTime? left = null;
            lifetime.Deactivated += (_, _) =>
            {
                left = DateTime.UtcNow;
                _ = model.Flush();
            };
            lifetime.Activated += (_, _) =>
            {
                if (left is { } at && DateTime.UtcNow - at > awayLimit)
                {
                    _ = model.Resume();
                }

                left = null;
            };
        }

        return view;
    }
}
