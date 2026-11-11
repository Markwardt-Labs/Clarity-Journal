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

    private string Flatten(InlineCollection inlines)
        => string.Concat(inlines.Select(inline => inline switch
        {
            Avalonia.Controls.Documents.Run run => run.Text,
            Avalonia.Controls.Documents.Span span => Flatten(span.Inlines),
            _ => string.Empty
        }));

    private Task<List<string>> Render(string? source)
        => fixture.Run(() =>
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
        connection.Setup(target => target.GetEntry(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ReturnsAsync((JournalEntry?)null);
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

    private IReadOnlyList<EntryDigest> ManyDigests(int count)
        => [.. Enumerable.Range(1, count).Select(index => new EntryDigest { Date = new DateOnly(2026, 10, 15).AddDays(-index), Title = $"Title {index}", Summary = $"Summary {index}", Excerpt = $"Excerpt {index}" })];

    private Mock<IServerConnection> ConnectionWith(IReadOnlyList<EntryDigest> digests)
    {
        Mock<IServerConnection> connection = new();
        connection.Setup(target => target.GetEntry(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ReturnsAsync((JournalEntry?)null);
        connection.Setup(target => target.ListDigests(It.IsAny<DateOnly?>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync((DateOnly? before, int limit, CancellationToken _) => (IReadOnlyList<EntryDigest>)[.. digests.Where(digest => before is null || digest.Date < before).Take(limit)]);
        return connection;
    }

    [Fact]
    public async Task JournalList_ScrollingToTheBottomLoadsTheNextPage()
    {
        Mock<IServerConnection> connection = ConnectionWith(ManyDigests(120));

        (int first, int afterScroll, int rendered) = await fixture.RunAsync(async () =>
        {
            JournalViewModel model = new(connection.Object, () => new DateOnly(2026, 10, 16));
            await model.Load();
            JournalView view = new() { DataContext = model };
            Window window = new() { Width = 390, Height = 700, Content = view };
            window.Show();
            model.ToggleListCommand.Execute(null);

            for (int attempt = 0; attempt < 100 && model.Rows.Count < 51; attempt++)
            {
                await Task.Delay(10);
            }

            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            int loaded = model.Rows.Count;
            view.GetVisualDescendants().OfType<Button>().First(button => button.Classes.Contains("entryRow")).GetVisualAncestors().OfType<ScrollViewer>().First().ScrollToEnd();

            for (int attempt = 0; attempt < 100 && model.Rows.Count < 101; attempt++)
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                await Task.Delay(10);
            }

            int rows = view.GetVisualDescendants().OfType<Button>().Count(button => button.Classes.Contains("entryRow"));
            window.Close();
            return (loaded, model.Rows.Count, rows);
        });

        Assert.Equal(51, first);
        Assert.Equal(101, afterScroll);
        Assert.Equal(101, rendered);
    }

    [Fact]
    public async Task JournalList_KeepsThePanelTheSameSizeAsTheEntryAndScrollsInside()
    {
        Mock<IServerConnection> connection = ConnectionWith(ManyDigests(50));

        (Rect entry, Rect list, bool scrolls) = await fixture.RunAsync(async () =>
        {
            JournalViewModel model = new(connection.Object, () => new DateOnly(2026, 10, 16));
            await model.Load();
            JournalView view = new() { DataContext = model };
            Window window = new() { Width = 390, Height = 700, Content = view };
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Border card = view.GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("card"));
            Rect entryBounds = card.Bounds;
            model.ToggleListCommand.Execute(null);

            for (int attempt = 0; attempt < 100 && model.Rows.Count < 50; attempt++)
            {
                await Task.Delay(10);
            }

            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            ScrollViewer scroller = view.GetVisualDescendants().OfType<Button>().First(button => button.Classes.Contains("entryRow")).GetVisualAncestors().OfType<ScrollViewer>().First();
            Rect listBounds = card.Bounds;
            bool scrolling = scroller.Extent.Height > scroller.Viewport.Height;
            window.Close();
            return (entryBounds, listBounds, scrolling);
        });

        Assert.Equal(entry, list);
        Assert.True(scrolls);
    }

    [Theory]
    [InlineData(390, "2026-09-30")]
    [InlineData(360, "2026-09-30")]
    [InlineData(360, "2026-11-10")]
    [InlineData(320, "2026-09-30")]
    [InlineData(412, "2026-09-30")]
    [InlineData(390, "2026-11-10")]
    [InlineData(390, "2026-10-14")]
    public async Task JournalHeader_ShowsTheWholeDateEvenWhenItIsLongAndThePhoneIsNarrow(int width, string day)
    {
        Mock<IServerConnection> connection = new();
        connection.Setup(target => target.GetEntry(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ReturnsAsync((JournalEntry?)null);
        DateOnly today = DateOnly.Parse(day, CultureInfo.InvariantCulture);

        (string text, double needed, double available, double center, int lines, string firstLine) = await fixture.RunAsync(async () =>
        {
            JournalViewModel model = new(connection.Object, () => today, Mock.Of<IVoiceRecorder>());
            await model.Load();
            JournalView view = new() { DataContext = model };
            Window window = new() { Width = width, Height = 700, Content = view };
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            TextBlock title = view.GetVisualDescendants().OfType<TextBlock>().First(block => block.Text == model.SelectedTitle);
            Border card = view.GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("card"));
            double middle = title.TranslatePoint(new Point(title.Bounds.Width / 2, 0), view)!.Value.X;
            double result = Math.Ceiling(title.TextLayout.WidthIncludingTrailingWhitespace);
            double shown = title.Bounds.Width;
            int count = title.TextLayout.TextLines.Count;
            string first = model.SelectedTitle[..Math.Min(title.TextLayout.TextLines[0].Length, model.SelectedTitle.Length)].TrimEnd();
            window.Close();
            return (model.SelectedTitle, result, shown, Math.Abs(middle - (card.Bounds.X + (card.Bounds.Width / 2))), count, first);
        });

        Assert.True(needed <= available + 1, $"'{text}' needs {needed}px but only {available}px is shown at width {width}");
        Assert.True(center <= 2, $"'{text}' is {center}px off the center of the panel");
        Assert.True(lines <= 2, $"'{text}' needs {lines} lines");
        Assert.True(lines == 1 || firstLine.EndsWith(",", StringComparison.Ordinal), $"'{text}' wraps in the middle of its month and day: first line '{firstLine}'");
    }
}
