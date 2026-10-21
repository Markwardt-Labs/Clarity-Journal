namespace Markwardt.ClarityJournal.Tests.Integration;

public sealed class EndToEndTests : IAsyncLifetime
{
    private readonly FakeClaude claude = new();

    private readonly FixedTimeProvider time = new(new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero));

    private readonly IServerConnection.IFactory factory = new IServerConnection.Factory(new IMessageChannel.Factory(), Mock.Of<ILog>());

    private readonly List<IServerConnection> opened = [];

    private TestServer server = null!;

    private IServerConnection connection = null!;

    private EventLog events = null!;

    public async Task InitializeAsync()
    {
        server = await TestServer.Start(claude, time);
        connection = await Connect();
        events = new EventLog(connection);
    }

    public async Task DisposeAsync()
    {
        foreach (IServerConnection open in opened)
        {
            await open.DisposeAsync();
        }

        await server.DisposeAsync();
    }

    private async Task<IServerConnection> Connect(string? key = null, string? fingerprint = null)
    {
        IServerConnection open = await factory.Connect(server.Settings(key ?? "secret", fingerprint), CancellationToken.None);
        opened.Add(open);
        return open;
    }

    private DateOnly Day(string date) => DateOnly.Parse(date, CultureInfo.InvariantCulture);

    [Fact]
    public async Task Entries_RoundTripAndAreOnePerDay()
    {
        Assert.Null(await connection.GetEntry(Day("2026-10-16"), CancellationToken.None));

        JournalEntry saved = await connection.SaveEntry(Day("2026-10-16"), "  First words.  ", CancellationToken.None);
        await connection.SaveEntry(Day("2026-10-16"), "Replaced words.", CancellationToken.None);
        await connection.SaveEntry(Day("2026-10-14"), "Wednesday.", CancellationToken.None);

        Assert.Equal("First words.", saved.Text);
        Assert.Equal("Replaced words.", (await connection.GetEntry(Day("2026-10-16"), CancellationToken.None))!.Text);
        Assert.Equal([("2026-10-14", "Wednesday."), ("2026-10-16", "Replaced words.")], (await connection.ListEntries(Day("2026-10-01"), Day("2026-10-31"), CancellationToken.None)).Select(entry => (entry.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), entry.Text)));
        Assert.Single(await connection.ListEntries(Day("2026-10-15"), null, CancellationToken.None));

        await connection.DeleteEntry(Day("2026-10-14"), CancellationToken.None);
        Assert.Null(await connection.GetEntry(Day("2026-10-14"), CancellationToken.None));
    }

    [Fact]
    public async Task Entries_AreMirroredAsMarkdownFilesForClaudeToRead()
    {
        await connection.SaveEntry(Day("2026-10-16"), "Ran in the park.", CancellationToken.None);

        string path = Path.Combine(server.Config.DataFolder, "export", "entries", "2026", "2026-10-16.md");
        Assert.Equal("# Friday, October 16, 2026\n\nRan in the park.\n", await File.ReadAllTextAsync(path));

        await connection.DeleteEntry(Day("2026-10-16"), CancellationToken.None);
        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData("   ", "The entry is empty.")]
    [InlineData("future", "An entry cannot be written for a day that has not started.")]
    public async Task Save_RefusedEntries_ComeBackAsRemoteExceptions(string text, string message)
    {
        DateOnly date = text == "future" ? Day("2026-12-01") : Day("2026-10-16");

        RemoteException exception = await Assert.ThrowsAsync<RemoteException>(() => connection.SaveEntry(date, text, CancellationToken.None));

        Assert.Equal(message, exception.Message);
        await connection.SaveEntry(Day("2026-10-16"), "still works", CancellationToken.None);
    }

    [Fact]
    public async Task Delete_DayWithoutEntry_IsRefused()
    {
        await Assert.ThrowsAsync<RemoteException>(() => connection.DeleteEntry(Day("2026-10-16"), CancellationToken.None));
    }

    [Fact]
    public async Task AnotherDevice_IsToldWhenAnEntryChanges()
    {
        IServerConnection other = await Connect();
        EventLog otherEvents = new(other);

        await connection.SaveEntry(Day("2026-10-16"), "from the first phone", CancellationToken.None);

        RemoteEvent changed = await otherEvents.Next(remoteEvent => remoteEvent.Kind == RemoteEventKind.EntryChanged);
        Assert.Equal(Day("2026-10-16"), changed.Date);
        Assert.Equal("from the first phone", (await other.GetEntry(Day("2026-10-16"), CancellationToken.None))!.Text);
    }

    [Fact]
    public async Task WrongAccessKey_IsRejected()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => factory.Connect(server.Settings("wrong"), CancellationToken.None));

        Assert.NotNull(await connection.GetChat(CancellationToken.None));
    }

    [Fact]
    public async Task Certificate_IsPinnedOnceSeen()
    {
        Assert.Equal(server.Fingerprint, connection.Fingerprint);

        IServerConnection pinned = await Connect(fingerprint: server.Fingerprint.ToLowerInvariant());
        Assert.NotNull(await pinned.GetChat(CancellationToken.None));

        await Assert.ThrowsAsync<CertificateChangedException>(() => factory.Connect(server.Settings(fingerprint: new string('0', 64)), CancellationToken.None));
    }

    private async Task<IServerConnection> Open(TestServer started)
    {
        IServerConnection open = await factory.Connect(started.Settings(), CancellationToken.None);
        opened.Add(open);
        return open;
    }

    private async Task Until(Func<Task<bool>> done)
    {
        for (int attempt = 0; attempt < 250; attempt++)
        {
            if (await done())
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.Fail("Timed out waiting.");
    }

    private FixedTimeProvider At(string moment) => new(DateTimeOffset.Parse(moment, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal));

    [Fact]
    public async Task Summaries_AreWrittenWhenTheServerStartsAfterAFridayEveningAndBuildOnThePreviousOnes()
    {
        using TempFolder folder = new();

        await using (TestServer first = await TestServer.Start(claude, At("2026-10-21T12:00:00Z"), folder.Path))
        {
            IServerConnection one = await Open(first);
            await one.SaveEntry(Day("2026-10-14"), "Started running.", CancellationToken.None);
            await one.SaveEntry(Day("2026-10-16"), "Ran 5k.", CancellationToken.None);
            Assert.Empty(await one.GetSummaries(SummaryKind.Week, CancellationToken.None));
        }

        Assert.Empty(claude.Requests);

        await using TestServer second = await TestServer.Start(claude, At("2026-10-24T12:00:00Z"), folder.Path);
        IServerConnection two = await Open(second);
        await Until(async () => (await two.GetSummaries(SummaryKind.Lifetime, CancellationToken.None)).Count == 1);

        Summary week = Assert.Single(await two.GetSummaries(SummaryKind.Week, CancellationToken.None));
        Assert.Equal((Day("2026-10-12"), Day("2026-10-18"), 2), (week.Start, week.End, week.EntryCount));
        Assert.Equal(new DateOnly(2026, 10, 14), Assert.Single(await two.GetSummaries(SummaryKind.Lifetime, CancellationToken.None)).Start);
        Assert.Single(await two.GetSummaries(SummaryKind.Month, CancellationToken.None));
        Assert.Single(await two.GetSummaries(SummaryKind.Year, CancellationToken.None));
        Assert.Equal(4, claude.Requests.Count);
        Assert.EndsWith(week.Text + "\n", await File.ReadAllTextAsync(Path.Combine(folder.Path, "export", "summaries", "weeks", "2026-10-12.md")), StringComparison.Ordinal);
        string lifetimeBefore = Assert.Single(await two.GetSummaries(SummaryKind.Lifetime, CancellationToken.None)).Text;
        await two.SaveEntry(Day("2026-10-25"), "Rested.", CancellationToken.None);
        await second.DisposeAsync();

        await using TestServer third = await TestServer.Start(claude, At("2026-10-31T12:00:00Z"), folder.Path, null);
        IServerConnection three = await Open(third);
        await Until(async () => (await three.GetSummaries(SummaryKind.Lifetime, CancellationToken.None)).Single().Text != lifetimeBefore);

        List<ClaudeRequest> later = [.. claude.Requests.Skip(4)];
        Assert.Equal(4, later.Count);
        ClaudeRequest month = later.Single(request => request.Prompt.Contains("the month 2026-10", StringComparison.Ordinal));
        Assert.Contains("Rested.", month.Prompt);
        Assert.DoesNotContain("Started running.", month.Prompt);
        Assert.DoesNotContain("Ran 5k.", month.Prompt);
        Assert.Contains(lifetimeBefore, later.Single(request => request.Prompt.Contains("whole lifetime", StringComparison.Ordinal)).Prompt);
        Assert.DoesNotContain(later, request => request.Prompt.Contains("the week of 2026-10-12", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Summaries_ServerStartingAgainWithNothingNew_AsksClaudeNothing()
    {
        using TempFolder folder = new();

        await using (TestServer first = await TestServer.Start(claude, At("2026-10-21T12:00:00Z"), folder.Path))
        {
            await (await Open(first)).SaveEntry(Day("2026-10-16"), "Ran 5k.", CancellationToken.None);
        }

        await using (TestServer second = await TestServer.Start(claude, At("2026-10-24T12:00:00Z"), folder.Path))
        {
            IServerConnection two = await Open(second);
            await Until(async () => (await two.GetSummaries(SummaryKind.Lifetime, CancellationToken.None)).Count == 1);
        }

        int calls = claude.Requests.Count;

        await using TestServer third = await TestServer.Start(claude, At("2026-11-07T12:00:00Z"), folder.Path);
        await Task.Delay(300);

        Assert.Equal(4, calls);
        Assert.Equal(calls, claude.Requests.Count);
    }

    [Fact]
    public async Task Summaries_ServerStartingBeforeTheNextFridayEvening_WaitsInsteadOfSummarizing()
    {
        using TempFolder folder = new();

        await using (TestServer first = await TestServer.Start(claude, At("2026-10-24T12:00:00Z"), folder.Path))
        {
            await (await Open(first)).SaveEntry(Day("2026-10-24"), "Saturday.", CancellationToken.None);
        }

        await using TestServer second = await TestServer.Start(claude, At("2026-10-28T12:00:00Z"), folder.Path);
        await Task.Delay(300);

        Assert.Empty(claude.Requests);
    }

    [Fact]
    public async Task Summaries_ClaudeFailing_IsReportedAndTheNextStartRetries()
    {
        using TempFolder folder = new();
        await using (TestServer first = await TestServer.Start(claude, At("2026-10-21T12:00:00Z"), folder.Path))
        {
            await (await Open(first)).SaveEntry(Day("2026-10-16"), "Ran 5k.", CancellationToken.None);
        }

        claude.Failure = new ClaudeException("not logged in");
        await using (TestServer failing = await TestServer.Start(claude, At("2026-10-24T12:00:00Z"), folder.Path))
        {
            IServerConnection watching = await Open(failing);
            await Until(() => Task.FromResult(claude.Requests.Count > 0));
            await Task.Delay(100);
            Assert.Empty(await watching.GetSummaries(SummaryKind.Week, CancellationToken.None));
        }

        claude.Failure = null;
        await using TestServer retrying = await TestServer.Start(claude, At("2026-10-24T13:00:00Z"), folder.Path);
        IServerConnection retry = await Open(retrying);
        await Until(async () => (await retry.GetSummaries(SummaryKind.Lifetime, CancellationToken.None)).Count == 1);

        Assert.Single(await retry.GetSummaries(SummaryKind.Week, CancellationToken.None));
    }

    [Fact]
    public async Task Chat_QuestionsAreAnsweredByClaudeReadingTheJournalFolder()
    {
        await connection.SaveEntry(Day("2026-10-16"), "Ran 5k.", CancellationToken.None);

        await connection.SendChat("How far did I run?", CancellationToken.None);
        RemoteEvent answer = await events.Next(remoteEvent => remoteEvent is { Kind: RemoteEventKind.ChatMessageAdded, Message.Role: ChatRole.Assistant });
        await events.Next(remoteEvent => remoteEvent is { Kind: RemoteEventKind.ChatWorkingChanged, Working: false });

        Assert.Equal("You asked: How far did I run?", answer.Message!.Text);
        ChatHistory history = await connection.GetChat(CancellationToken.None);
        Assert.Equal([(ChatRole.User, "How far did I run?"), (ChatRole.Assistant, "You asked: How far did I run?")], history.Messages.Select(message => (message.Role, message.Text)));
        Assert.False(history.Working);

        ClaudeRequest request = Assert.Single(claude.Requests);
        Assert.Equal(["Read", "Grep", "Glob"], request.Tools);
        Assert.True(File.Exists(Path.Combine(request.Folder, "entries", "2026", "2026-10-16.md")));
    }

    [Fact]
    public async Task Chat_FollowUpsResumeTheSameClaudeSessionUntilTheChatIsCleared()
    {
        await connection.SendChat("one", CancellationToken.None);
        await events.Next(remoteEvent => remoteEvent is { Kind: RemoteEventKind.ChatMessageAdded, Message.Role: ChatRole.Assistant });
        await connection.SendChat("two", CancellationToken.None);
        await events.Next(remoteEvent => remoteEvent is { Kind: RemoteEventKind.ChatMessageAdded, Message.Text: "You asked: two" });

        Assert.Equal([false, true], claude.Requests.Select(request => request.Resume));
        Assert.Single(claude.Requests.Select(request => request.Session).Distinct());

        await connection.ClearChat(CancellationToken.None);
        await events.Next(remoteEvent => remoteEvent.Kind == RemoteEventKind.ChatCleared);
        Assert.Empty((await connection.GetChat(CancellationToken.None)).Messages);

        await connection.SendChat("three", CancellationToken.None);
        await events.Next(remoteEvent => remoteEvent is { Kind: RemoteEventKind.ChatMessageAdded, Message.Text: "You asked: three" });
        Assert.False(claude.Requests[2].Resume);
        Assert.NotEqual(claude.Requests[0].Session, claude.Requests[2].Session);
    }

    [Fact]
    public async Task Chat_MessagesOlderThanTwoDaysAreDeletedWhenTheServerStarts()
    {
        using TempFolder folder = new();

        await using (TestServer first = await TestServer.Start(claude, At("2026-10-21T12:00:00Z"), folder.Path))
        {
            IServerConnection one = await Open(first);
            EventLog log = new(one);
            await one.SendChat("an old question", CancellationToken.None);
            await log.Next(remoteEvent => remoteEvent is { Kind: RemoteEventKind.ChatMessageAdded, Message.Role: ChatRole.Assistant });
        }

        await using (TestServer sameDay = await TestServer.Start(claude, At("2026-10-23T11:00:00Z"), folder.Path))
        {
            await Task.Delay(200);
            Assert.Equal(2, (await (await Open(sameDay)).GetChat(CancellationToken.None)).Messages.Count);
        }

        await using TestServer later = await TestServer.Start(claude, At("2026-10-23T13:00:00Z"), folder.Path);
        IServerConnection two = await Open(later);
        await Until(async () => (await two.GetChat(CancellationToken.None)).Messages.Count == 0);

        await two.SendChat("a fresh question", CancellationToken.None);
        await Until(async () => (await two.GetChat(CancellationToken.None)).Messages.Count == 2);
        Assert.False(claude.Requests[^1].Resume);
    }

    [Fact]
    public async Task Chat_ClaudeFailing_AnswersWithTheReason()
    {
        claude.Failure = new ClaudeException("Claude Code could not be started ('claude')");

        await connection.SendChat("hello", CancellationToken.None);
        RemoteEvent answer = await events.Next(remoteEvent => remoteEvent is { Kind: RemoteEventKind.ChatMessageAdded, Message.Role: ChatRole.Assistant });

        Assert.Equal("I could not answer that: Claude Code could not be started ('claude')", answer.Message!.Text);
    }

    [Fact]
    public async Task Transcribe_WithVoiceInputTurnedOff_IsRefused()
    {
        RemoteException exception = await Assert.ThrowsAsync<RemoteException>(() => connection.Transcribe(new byte[] { 1, 2, 3 }, CancellationToken.None));

        Assert.Contains("Voice input is turned off", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Transcribe_WithASpeechCommand_ReturnsWhatTheCommandPrinted()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using TempFolder folder = new();
        string script = folder.Combine("speech.sh");
        await File.WriteAllTextAsync(script, "#!/bin/sh\nprintf 'heard %s' \"$(head -c 4 \"$1\")\"\n");
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        await using TestServer speaking = await TestServer.Start(claude, time, null, config => config with { SpeechCommand = script, SpeechArguments = ["{file}"] });
        IServerConnection open = await Open(speaking);

        string text = await open.Transcribe("RIFF-and-some-more-bytes"u8.ToArray(), CancellationToken.None);

        Assert.Equal("heard RIFF", text);
    }

    [Fact]
    public async Task Reconnect_OpensANewConnectionWithTheSameData()
    {
        await connection.SaveEntry(Day("2026-10-16"), "before", CancellationToken.None);
        TaskCompletionSource reconnected = new();
        connection.Reconnected += reconnected.SetResult;

        await connection.Reconnect(CancellationToken.None);
        await reconnected.Task;

        Assert.Equal("before", (await connection.GetEntry(Day("2026-10-16"), CancellationToken.None))!.Text);
    }

    [Fact]
    public async Task Journal_SurvivesARestartOfTheServer()
    {
        using TempFolder folder = new();
        await using (TestServer first = await TestServer.Start(claude, time, folder.Path))
        {
            IServerConnection one = await factory.Connect(first.Settings(), CancellationToken.None);
            await one.SaveEntry(Day("2026-10-16"), "still here", CancellationToken.None);
            await one.DisposeAsync();
        }

        await using TestServer second = await TestServer.Start(claude, time, folder.Path);
        IServerConnection two = await factory.Connect(second.Settings(), CancellationToken.None);
        await two.SendChat("hi", CancellationToken.None);

        Assert.Equal("still here", (await two.GetEntry(Day("2026-10-16"), CancellationToken.None))!.Text);
        Assert.True(File.Exists(Path.Combine(folder.Path, "export", "entries", "2026", "2026-10-16.md")));
        await two.DisposeAsync();
    }
}
