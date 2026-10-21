namespace Markwardt.ClarityJournal.Client;

/// <summary>
/// The desktop head of the client application, a development and preview host that shows the root view in a window the size of a phone.
/// </summary>
public sealed class App : ClientApp
{
    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new Window
            {
                Title = "Clarity Journal",
                Width = 390,
                Height = 800,
                CanResize = true,
                Content = CreateMainView(typeof(App).Assembly, DataFolder(desktop.Args), new DesktopVoiceRecorder()),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private string? DataFolder(string[]? args)
    {
        int index = Array.IndexOf(args ?? [], "--data-folder");
        return index >= 0 && index + 1 < args!.Length ? Path.GetFullPath(args[index + 1]) : null;
    }
}
