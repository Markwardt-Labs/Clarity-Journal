namespace Markwardt.ClarityJournal.Tests.Unit;

public sealed class ChatServiceTests
{
    private readonly Mock<IChatStore> chats = new();

    private readonly Mock<IMetaStore> meta = new();

    private readonly Mock<IClaudeRunner> claude = new();

    private readonly Mock<IJournalExport> export = new();

    private readonly EventHub events = new();

    private readonly List<RemoteEvent> published = [];

    private readonly ServerConfig config = new() { Key = "k" };

    private readonly List<ClaudeRequest> requests = [];

    private readonly TaskCompletionSource answered = new();

    private readonly ChatService service;

    private long nextId;

    private string? session;

    public ChatServiceTests()
    {
        events.Subscribe(remoteEvent =>
        {
            lock (published)
            {
                published.Add(remoteEvent);
            }

            if (remoteEvent is { Kind: RemoteEventKind.ChatWorkingChanged, Working: false })
            {
                answered.TrySetResult();
            }
        });
        export.SetupGet(target => target.Folder).Returns("/export");
        chats.Setup(store => store.Add(It.IsAny<ChatRole>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync((ChatRole role, string text, DateTimeOffset at, CancellationToken _) => new ChatMessage { Id = ++nextId, Role = role, Text = text, At = at });
        meta.Setup(store => store.Get("chat_session", It.IsAny<CancellationToken>())).ReturnsAsync(() => session);
        meta.Setup(store => store.Set("chat_session", It.IsAny<string>(), It.IsAny<CancellationToken>())).Callback<string, string, CancellationToken>((_, value, _) => session = value).Returns(Task.CompletedTask);
        meta.Setup(store => store.Remove("chat_session", It.IsAny<CancellationToken>())).Callback(() => session = null).Returns(Task.CompletedTask);
        service = new ChatService(chats.Object, meta.Object, claude.Object, new Prompts(), export.Object, events, new FixedTimeProvider(DateTimeOffset.UnixEpoch), Mock.Of<ILog>());
    }

    private void Reply(string text) => claude.Setup(runner => runner.Run(config, It.IsAny<ClaudeRequest>(), It.IsAny<CancellationToken>())).Callback<ServerConfig, ClaudeRequest, CancellationToken>((_, request, _) => requests.Add(request)).ReturnsAsync(text);

    private List<RemoteEvent> Published()
    {
        lock (published)
        {
            return [.. published];
        }
    }

    [Fact]
    public async Task Send_StoresTheQuestionThenAnswersItInTheBackground()
    {
        Reply("You were grateful for friends.");

        await service.Send(config, "  What was I grateful for? ", CancellationToken.None);
        await answered.Task;

        List<RemoteEvent> events = Published();
        Assert.Equal([RemoteEventKind.ChatMessageAdded, RemoteEventKind.ChatWorkingChanged, RemoteEventKind.ChatMessageAdded, RemoteEventKind.ChatWorkingChanged], events.Select(remoteEvent => remoteEvent.Kind));
        Assert.Equal("What was I grateful for?", events[0].Message!.Text);
        Assert.Equal(ChatRole.User, events[0].Message!.Role);
        Assert.Equal(ChatRole.Assistant, events[2].Message!.Role);
        Assert.Equal("You were grateful for friends.", events[2].Message!.Text);
        Assert.Equal([true, false], events.Where(remoteEvent => remoteEvent.Kind == RemoteEventKind.ChatWorkingChanged).Select(remoteEvent => remoteEvent.Working == true));
    }

    [Fact]
    public async Task Send_RunsClaudeWithOnlyReadToolsInTheExportFolder()
    {
        Reply("ok");

        await service.Send(config, "hello", CancellationToken.None);
        await answered.Task;

        ClaudeRequest request = Assert.Single(requests);
        Assert.Equal("hello", request.Prompt);
        Assert.Equal("/export", request.Folder);
        Assert.Equal(["Read", "Grep", "Glob"], request.Tools);
        Assert.False(request.ReplacesSystemPrompt);
        Assert.Equal(new Prompts().ChatSystem, request.SystemPrompt);
    }

    [Fact]
    public async Task Send_FirstQuestionStartsASessionAndLaterOnesResumeIt()
    {
        Reply("ok");

        await service.Send(config, "one", CancellationToken.None);
        await answered.Task;
        TaskCompletionSource second = new();
        events.Subscribe(remoteEvent => { if (remoteEvent is { Kind: RemoteEventKind.ChatMessageAdded, Message.Role: ChatRole.Assistant, Message.Id: > 2 }) { second.TrySetResult(); } });
        await service.Send(config, "two", CancellationToken.None);
        await second.Task;

        Assert.False(requests[0].Resume);
        Assert.NotNull(requests[0].Session);
        Assert.True(requests[1].Resume);
        Assert.Equal(requests[0].Session, requests[1].Session);
    }

    [Fact]
    public async Task Send_ClaudeFails_AnswersWithTheFailureAndDoesNotKeepTheSession()
    {
        claude.Setup(runner => runner.Run(config, It.IsAny<ClaudeRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new ClaudeException("not logged in"));

        await service.Send(config, "hello", CancellationToken.None);
        await answered.Task;

        Assert.Equal("I could not answer that: not logged in", Published().Last(remoteEvent => remoteEvent.Kind == RemoteEventKind.ChatMessageAdded).Message!.Text);
        Assert.Null(session);
    }

    [Fact]
    public async Task Send_ResumedSessionIsGone_ForgetsItSoTheNextQuestionStartsFresh()
    {
        session = Guid.NewGuid().ToString();
        claude.Setup(runner => runner.Run(config, It.IsAny<ClaudeRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new ClaudeException("No conversation found with session ID: x"));

        await service.Send(config, "hello", CancellationToken.None);
        await answered.Task;

        Assert.Null(session);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Send_BlankQuestion_IsRefused(string text)
    {
        await Assert.ThrowsAsync<RequestException>(() => service.Send(config, text, CancellationToken.None));

        Assert.Empty(Published());
    }

    [Fact]
    public async Task Clear_DeletesMessagesAndSessionAndAnnouncesIt()
    {
        session = Guid.NewGuid().ToString();

        await service.Clear(CancellationToken.None);

        chats.Verify(store => store.Clear(It.IsAny<CancellationToken>()), Times.Once);
        Assert.Null(session);
        Assert.Equal(RemoteEventKind.ChatCleared, Assert.Single(Published()).Kind);
    }

    [Fact]
    public async Task Trim_DeletesOldMessagesAnnouncesItAndKeepsTheSessionWhileMessagesRemain()
    {
        session = Guid.NewGuid().ToString();
        DateTimeOffset cutoff = new(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);
        chats.Setup(store => store.DeleteBefore(cutoff, It.IsAny<CancellationToken>())).ReturnsAsync(3);
        chats.Setup(store => store.List(It.IsAny<CancellationToken>())).ReturnsAsync([new ChatMessage { Id = 9, Role = ChatRole.User, Text = "recent", At = cutoff.AddHours(1) }]);

        int removed = await service.Trim(cutoff, CancellationToken.None);

        Assert.Equal(3, removed);
        Assert.NotNull(session);
        Assert.Equal(RemoteEventKind.ChatTrimmed, Assert.Single(Published()).Kind);
    }

    [Fact]
    public async Task Trim_WhenNoMessagesRemain_AlsoForgetsTheClaudeSession()
    {
        session = Guid.NewGuid().ToString();
        chats.Setup(store => store.DeleteBefore(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(2);
        chats.Setup(store => store.List(It.IsAny<CancellationToken>())).ReturnsAsync([]);

        await service.Trim(DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Null(session);
    }

    [Fact]
    public async Task Trim_NothingOld_ChangesAndAnnouncesNothing()
    {
        session = Guid.NewGuid().ToString();
        chats.Setup(store => store.DeleteBefore(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);

        Assert.Equal(0, await service.Trim(DateTimeOffset.UtcNow, CancellationToken.None));

        Assert.NotNull(session);
        Assert.Empty(Published());
        meta.Verify(store => store.Remove(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Trim_WaitsForAnAnswerBeingWrittenInsteadOfDeletingUnderIt()
    {
        TaskCompletionSource release = new();
        claude.Setup(runner => runner.Run(config, It.IsAny<ClaudeRequest>(), It.IsAny<CancellationToken>())).Returns(async () =>
        {
            await release.Task;
            return "done";
        });
        await service.Send(config, "hi", CancellationToken.None);

        Task<int> trimming = service.Trim(DateTimeOffset.UtcNow, CancellationToken.None);
        await Task.Delay(100);
        Assert.False(trimming.IsCompleted);

        release.SetResult();
        await trimming;
        chats.Verify(store => store.DeleteBefore(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Get_ReportsTheMessagesAndWhetherAnAnswerIsBeingWritten()
    {
        ChatMessage message = new() { Id = 1, Role = ChatRole.User, Text = "hi", At = DateTimeOffset.UnixEpoch };
        chats.Setup(store => store.List(It.IsAny<CancellationToken>())).ReturnsAsync([message]);
        TaskCompletionSource release = new();
        claude.Setup(runner => runner.Run(config, It.IsAny<ClaudeRequest>(), It.IsAny<CancellationToken>())).Returns(async () =>
        {
            await release.Task;
            return "done";
        });

        await service.Send(config, "hi", CancellationToken.None);
        ChatHistory working = await service.Get(CancellationToken.None);
        release.SetResult();
        await answered.Task;
        ChatHistory idle = await service.Get(CancellationToken.None);

        Assert.True(working.Working);
        Assert.Equal([message], working.Messages);
        Assert.False(idle.Working);
    }
}
