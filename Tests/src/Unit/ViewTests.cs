namespace Markwardt.ClarityJournal.Tests.Unit;

[Collection("Headless")]
public sealed class ViewTests(HeadlessFixture fixture)
{
    private readonly string markdownSample = """
        # Title

        Some *italic*, **bold**, ~~gone~~, `code` and [a link](https://example.com).

        > quoted

        - one
        - two

        1. first
        2. second

        ```csharp
        int x = 1;
        ```

        | A | B |
        |---|---|
        | 1 | 2 |

        ---
        """;

    private string Flatten(InlineCollection inlines) => string.Concat(inlines.Select(inline => inline switch
    {
        Avalonia.Controls.Documents.Run run => run.Text,
        Avalonia.Controls.Documents.Span span => Flatten(span.Inlines),
        _ => string.Empty
    }));

    private Task<List<string>> Render(string? source) => fixture.Run(() =>
    {
        MarkdownView view = new() { Source = source };
        Window window = new() { Width = 390, Height = 800, Content = view };
        window.Show();
        List<string> texts = [.. view.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Inlines is { Count: > 0 } ? Flatten(block.Inlines) : block.Text ?? string.Empty)];
        window.Close();
        return texts;
    });

    [Fact]
    public async Task MarkdownView_RendersHeadingsInlinesListsQuotesCodeAndTables()
    {
        List<string> texts = await Render(markdownSample);

        Assert.Contains("Title", texts);
        Assert.Contains("Some italic, bold, gone, code and a link.", texts);
        Assert.Contains("quoted", texts);
        Assert.Contains("one", texts);
        Assert.Contains("second", texts);
        Assert.Contains("•", texts);
        Assert.Contains("2.", texts);
        Assert.Contains("int x = 1;", texts);
        Assert.Contains("A", texts);
        Assert.Contains("2", texts);
    }

    [Fact]
    public async Task MarkdownView_NoSource_RendersNothing()
    {
        Assert.Empty(await Render(null));
    }

    [Fact]
    public async Task MarkdownView_SourceChanges_ReplacesTheContent()
    {
        List<string> texts = await fixture.Run(() =>
        {
            MarkdownView view = new() { Source = "first" };
            view.Source = "second";
            Window window = new() { Content = view };
            window.Show();
            List<string> found = [.. view.GetVisualDescendants().OfType<TextBlock>().Select(block => Flatten(block.Inlines!))];
            window.Close();
            return found;
        });

        Assert.Equal(["second"], texts);
    }

    [Fact]
    public async Task ConnectView_ShowsTheFieldsAndTheConnectButton()
    {
        (List<string> texts, int boxes) = await fixture.Run(() =>
        {
            Mock<IServerConnection.IFactory> factory = new();
            ConnectView view = new() { DataContext = new ConnectViewModel(factory.Object, null, "Clarity Journal 1.0.0") };
            Window window = new() { Width = 390, Height = 800, Content = view };
            window.Show();
            List<string> found = [.. view.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text ?? string.Empty)];
            int fields = view.GetVisualDescendants().OfType<TextBox>().Count();
            window.Close();
            return (found, fields);
        });

        Assert.Contains("Clarity Journal", texts);
        Assert.Contains("Connect", texts);
        Assert.Contains("Access key", texts);
        Assert.Equal(3, boxes);
    }

    private async Task<(Rect Before, Rect After, Rect Screen, bool Scrolls, int OuterScrollers)> MeasureJournalPanel()
    {
        Mock<IServerConnection> connection = new();
        connection.Setup(target => target.ListEntries(It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        Mock<IVoiceRecorder> recorder = new();

        return await fixture.RunAsync(async () =>
        {
            JournalViewModel model = new(connection.Object, () => new DateOnly(2026, 10, 16), recorder.Object);
            await model.Load();
            JournalView view = new() { DataContext = model };
            Window window = new() { Width = 390, Height = 700, Content = view };
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Border card = view.GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("card"));
            Rect before = card.Bounds;

            model.EntryText = string.Join('\n', Enumerable.Range(1, 200).Select(line => $"Line {line} of a very long journal entry that keeps going."));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            TextBox box = view.GetVisualDescendants().OfType<TextBox>().First();
            ScrollViewer inner = box.GetVisualDescendants().OfType<ScrollViewer>().First();
            Rect after = card.Bounds;
            int outer = box.GetVisualAncestors().OfType<ScrollViewer>().Count();
            bool scrolls = inner.Extent.Height > inner.Viewport.Height;
            Rect screen = view.Bounds;
            window.Close();
            return (before, after, screen, scrolls, outer);
        });
    }

    [Fact]
    public async Task JournalPanel_FillsTheScreenWithTheSameMarginOnEverySide()
    {
        (Rect before, _, Rect screen, _, _) = await MeasureJournalPanel();

        Assert.Equal(20, before.X, 0.5);
        Assert.Equal(20, before.Y, 0.5);
        Assert.Equal(20, screen.Width - before.Right, 0.5);
        Assert.Equal(20, screen.Height - before.Bottom, 0.5);
    }

    [Fact]
    public async Task JournalPanel_KeepsItsSizeWhenTheTextOverflowsAndScrollsInsideTheTextBox()
    {
        (Rect before, Rect after, _, bool scrolls, int outerScrollers) = await MeasureJournalPanel();

        Assert.Equal(before, after);
        Assert.True(scrolls);
        Assert.Equal(0, outerScrollers);
    }
}
