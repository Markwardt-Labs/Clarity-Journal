namespace Markwardt.ClarityJournal.Tests.Integration;

[Collection("Headless")]
public sealed class ScreenshotTests(HeadlessFixture fixture)
{
    private readonly DateTimeOffset now = new(2026, 10, 16, 21, 0, 0, TimeSpan.Zero);

    private async Task<bool> Until(Func<bool> done)
    {
        for (int attempt = 0; attempt < 200 && !done(); attempt++)
        {
            await Task.Delay(25);
        }

        return done();
    }

    private (byte Red, byte Green, byte Blue) Pixel(Avalonia.Media.Imaging.WriteableBitmap frame, int x, int y)
    {
        using Avalonia.Platform.ILockedFramebuffer buffer = frame.Lock();
        byte[] bytes = new byte[4];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address + (y * buffer.RowBytes) + (x * 4), bytes, 0, 4);
        return buffer.Format == Avalonia.Platform.PixelFormat.Bgra8888 ? (bytes[2], bytes[1], bytes[0]) : (bytes[0], bytes[1], bytes[2]);
    }

    private Avalonia.Media.Imaging.WriteableBitmap Capture(Window window, string name)
    {
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Avalonia.Media.Imaging.WriteableBitmap frame = window.CaptureRenderedFrame()!;

        if (Environment.GetEnvironmentVariable("CLARITY_SCREENSHOTS") is { Length: > 0 } folder)
        {
            Directory.CreateDirectory(folder);
            frame.Save(Path.Combine(folder, $"{name}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        }

        return frame;
    }

    private double IconOffset(Avalonia.Media.Imaging.WriteableBitmap frame, Window window, Control button, Control area, Func<(byte Red, byte Green, byte Blue), bool> isIcon)
    {
        Point buttonOrigin = button.TranslatePoint(new Point(0, 0), window)!.Value;
        Point areaOrigin = area.TranslatePoint(new Point(0, 0), window)!.Value;
        int left = (int)Math.Floor(areaOrigin.X) - 3;
        int top = (int)Math.Floor(areaOrigin.Y) - 3;
        int right = (int)Math.Ceiling(areaOrigin.X + area.Bounds.Width) + 3;
        int bottom = (int)Math.Ceiling(areaOrigin.Y + area.Bounds.Height) + 3;
        int min = int.MaxValue;
        int max = int.MinValue;
        using Avalonia.Platform.ILockedFramebuffer buffer = frame.Lock();
        byte[] bytes = new byte[4];

        for (int y = top; y < bottom; y++)
        {
            for (int x = left; x < right; x++)
            {
                System.Runtime.InteropServices.Marshal.Copy(buffer.Address + (y * buffer.RowBytes) + (x * 4), bytes, 0, 4);
                (byte, byte, byte) pixel = buffer.Format == Avalonia.Platform.PixelFormat.Bgra8888 ? (bytes[2], bytes[1], bytes[0]) : (bytes[0], bytes[1], bytes[2]);

                if (isIcon(pixel))
                {
                    min = Math.Min(min, x);
                    max = Math.Max(max, x);
                }
            }
        }

        return min > max ? double.NaN : ((min + max + 1) / 2.0) - (buttonOrigin.X + (button.Bounds.Width / 2));
    }

    private bool Near((byte Red, byte Green, byte Blue) actual, string hex, int tolerance = 24)
    {
        int expected = Convert.ToInt32(hex, 16);
        return Math.Abs(actual.Red - ((expected >> 16) & 255)) <= tolerance && Math.Abs(actual.Green - ((expected >> 8) & 255)) <= tolerance && Math.Abs(actual.Blue - (expected & 255)) <= tolerance;
    }

    [Fact]
    public async Task ConnectScreen_UsesTheVioletToPinkWelcomeGradient()
    {
        using TempFolder temp = new();

        (int width, bool top, bool bottom) = await fixture.RunAsync(async () =>
        {
            ClientApp app = (ClientApp)Avalonia.Application.Current!;
            MainView view = app.CreateMainView(typeof(ScreenshotTests).Assembly, temp.Path);
            Window window = new() { Width = 390, Height = 800, Content = view };
            window.Show();
            await Until(() => ((MainViewModel)view.DataContext!).Current is ConnectViewModel);
            Avalonia.Media.Imaging.WriteableBitmap frame = Capture(window, "1-connect");
            (int, bool, bool) result = (frame.PixelSize.Width, Near(Pixel(frame, 4, 4), "9A95FC"), Near(Pixel(frame, 4, 795), "E98EB7"));
            window.Close();
            return result;
        });

        Assert.True(width >= 390);
        Assert.True(top);
        Assert.True(bottom);
    }

    [Fact]
    public async Task EveryScreen_RendersAgainstARealServerInThePastelTheme()
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.Now);
        FakeClaude claude = new()
        {
            Reply = request => request.Tools.Count == 0
                ? "## Highlights\n\nYou **started running** and felt *great* about it.\n\n- Ran 5k on Friday\n- Slept well all week\n\n> Small steps add up."
                : "You ran **5k** on [Friday, October 16](entries/2026/2026-10-16.md).\n\n- Felt strong\n- Slept well afterwards"
        };
        using TempFolder data = new();
        using TempFolder temp = new();
        DateTimeOffset start = DateTimeOffset.UtcNow;
        Mock<IVoiceRecorder> recorder = new();
        recorder.SetupGet(target => target.SampleRate).Returns(16000);
        recorder.Setup(target => target.Start(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        recorder.Setup(target => target.Stop()).ReturnsAsync(ReadOnlyMemory<byte>.Empty);

        await using (TestServer seeding = await TestServer.Start(claude, new FixedTimeProvider(start), data.Path))
        {
            IServerConnection setup = await new IServerConnection.Factory(new IMessageChannel.Factory(), Mock.Of<ILog>()).Connect(seeding.Settings(), CancellationToken.None);
            await setup.SaveEntry(today.AddDays(-3), "Started running again. My legs hurt but I felt alive.", CancellationToken.None);
            await setup.SaveEntry(today.AddDays(-1), "Ran 5k this morning without stopping. Slept well, grateful for good friends and a calm evening.", CancellationToken.None);
            await setup.DisposeAsync();
        }

        await using TestServer server = await TestServer.Start(claude, new FixedTimeProvider(start.AddDays(8)), data.Path);
        IServerConnection watching = await new IServerConnection.Factory(new IMessageChannel.Factory(), Mock.Of<ILog>()).Connect(server.Settings(), CancellationToken.None);

        for (int attempt = 0; attempt < 250 && (await watching.GetSummaries(SummaryKind.Lifetime, CancellationToken.None)).Count == 0; attempt++)
        {
            await Task.Delay(20);
        }

        await watching.DisposeAsync();
        await File.WriteAllTextAsync(temp.Combine("settings.json"), JsonSerializer.Serialize(new { host = "127.0.0.1", port = server.Port, accessKey = "secret", fingerprint = server.Fingerprint }));

        List<string> failures = await fixture.RunAsync(async () =>
        {
            List<string> problems = [];
            ClientApp app = (ClientApp)Avalonia.Application.Current!;
            MainView view = app.CreateMainView(typeof(ScreenshotTests).Assembly, temp.Path, recorder.Object);
            MainViewModel model = (MainViewModel)view.DataContext!;
            Window window = new() { Width = 390, Height = 800, Content = view };
            window.Show();

            if (!await Until(() => model.Current is SessionViewModel { Journal.Days.Count: 42 }))
            {
                return ["The session never opened: " + model.Current];
            }

            SessionViewModel session = (SessionViewModel)model.Current!;
            Avalonia.Media.Imaging.WriteableBitmap journal = Capture(window, "2-journal-today");
            Func<(byte Red, byte Green, byte Blue), bool> rose = pixel => pixel.Red > 200 && pixel.Green < 170 && pixel.Blue < 200;
            Button journalMic = window.GetVisualDescendants().OfType<Button>().First(button => button.Classes.Contains("mic"));

            if (Math.Abs(IconOffset(journal, window, journalMic, journalMic, rose)) > 1)
            {
                problems.Add("the journal's microphone icon is not horizontally centered in its button");
            }

            foreach (Button tab in window.GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains("tab")))
            {
                Avalonia.Controls.Shapes.Path icon = tab.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().First();

                if (Math.Abs(IconOffset(journal, window, tab, icon, pixel => pixel.Red < 245 || pixel.Green < 245 || pixel.Blue < 245)) > 1.5)
                {
                    problems.Add("a tab icon is not horizontally centered above its label");
                }
            }

            if (!Near(Pixel(journal, 4, 4), "F790A1"))
            {
                problems.Add("journal gradient top is not pink");
            }

            if (session.Journal.IsCalendarOpen || !session.Journal.CanEdit)
            {
                problems.Add("the journal should open on an editable empty entry for today with the calendar closed");
            }

            if (!session.Journal.Dictation.CanDictate)
            {
                problems.Add("the journal should offer voice input when the client can record");
            }

            session.Journal.Dictation.Command.Execute(null);
            Capture(window, "2b-journal-recording");

            if (!session.Journal.Dictation.IsRecording)
            {
                problems.Add("tapping the microphone should start recording");
            }

            session.Journal.Dictation.Cancel();
            session.Journal.EntryText = string.Join('\n', Enumerable.Range(1, 60).Select(line => $"Line {line} of today's long entry, which scrolls inside the panel."));
            Capture(window, "2c-journal-long-entry");
            session.Journal.EntryText = string.Empty;
            session.Journal.ToggleCalendarCommand.Execute(null);
            await Until(() => session.Journal.Days.Count(day => day.HasEntry) == 2);
            Capture(window, "3-journal-calendar");

            if (session.Journal.Days.Count(day => day.IsSelectable) != 3)
            {
                problems.Add("only today and the two days with entries should be selectable");
            }

            session.Journal.Days.Single(day => day.Date == today.AddDays(-1)).Select.Execute(null);
            await Until(() => session.Journal.IsReadOnly);
            Capture(window, "4-journal-past-entry");

            if (!session.Journal.IsReadOnly || session.Journal.IsCalendarOpen || session.Journal.EntryText.Length == 0)
            {
                problems.Add("selecting an earlier day should show its entry read-only and close the calendar");
            }

            session.SelectTabCommand.Execute(AppTab.Summaries);
            await Until(() => session.Summaries.Items.Count == 1);
            Capture(window, "5-summary-week");

            session.Summaries.Kinds[3].Select.Execute(null);
            await Until(() => session.Summaries.Items.Count == 1 && session.Summaries.Items[0].Title == "Life");
            Capture(window, "6-summary-life");

            if (session.Summaries.Items.Count != 1)
            {
                problems.Add("summaries did not load");
            }

            session.SelectTabCommand.Execute(AppTab.Chat);
            Avalonia.Media.Imaging.WriteableBitmap askFrame = Capture(window, "7-ask-empty");

            foreach (Button round in window.GetVisualDescendants().OfType<Button>().Where(button => button.IsVisible && (button.Classes.Contains("mic") || button.Classes.Contains("send")) && button.Bounds.Width > 0))
            {
                if (Math.Abs(IconOffset(askFrame, window, round, round, rose)) > 1)
                {
                    problems.Add($"the {(round.Classes.Contains("mic") ? "voice input" : "submit")} icon is not horizontally centered in its button");
                }
            }

            if (!session.Chat.Dictation.CanDictate)
            {
                problems.Add("the ask tab should offer voice input when the client can record");
            }

            session.Chat.Draft = "How far did I run this week?";
            session.Chat.SendCommand.Execute(null);
            await Until(() => session.Chat.Messages.Count == 2);
            Capture(window, "8-ask");

            if (session.Chat.Messages.Count != 2 || !session.Chat.Messages[1].IsAssistant)
            {
                problems.Add("chat did not get an answer");
            }

            session.Chat.AskClearCommand.Execute(null);
            Capture(window, "9-ask-clear-confirmation");

            if (!session.Chat.IsConfirmingClear)
            {
                problems.Add("clearing the chat should ask for confirmation");
            }

            session.Chat.CancelClearCommand.Execute(null);
            session.SelectTabCommand.Execute(AppTab.Settings);
            Capture(window, "10-options");
            window.Close();
            return problems;
        });

        Assert.Empty(failures);
    }
}
