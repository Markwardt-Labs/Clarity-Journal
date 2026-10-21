namespace Markwardt.ClarityJournal.Tests.Unit;

public sealed class RequestHandlerTests
{
    private readonly Mock<IDatabase> database = new();

    private readonly Mock<IJournalExport> export = new();

    private readonly Mock<IJournalService> journal = new();

    private readonly Mock<ISummaryStore> summaries = new();

    private readonly Mock<IChatService> chat = new();

    private readonly Mock<ISpeechService> speech = new();

    private readonly ServerConfig config = new() { Key = "k", DataFolder = "/data" };

    private readonly RequestHandler handler;

    private readonly JournalEntry entry = new() { Date = new DateOnly(2026, 10, 16), Text = "x", UpdatedAt = DateTimeOffset.UnixEpoch };

    public RequestHandlerTests() => handler = new RequestHandler(database.Object, export.Object, journal.Object, summaries.Object, chat.Object, speech.Object);

    private async Task<RemoteResponse> Handle(RemoteRequest request)
    {
        await handler.Open(config, CancellationToken.None);
        return await handler.Handle(request, CancellationToken.None);
    }

    [Fact]
    public async Task Open_PreparesTheDatabaseThenTheExportAndTheSpeechService()
    {
        await handler.Open(config, CancellationToken.None);

        speech.Verify(target => target.Configure(config), Times.Once);
        database.Verify(target => target.Initialize("/data", It.IsAny<CancellationToken>()), Times.Once);
        export.Verify(target => target.Open("/data", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_BeforeOpen_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(new RemoteRequest { Id = 1, Command = RemoteCommand.GetChat }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_ListEntries_ReturnsTheEntriesOfTheRange()
    {
        journal.Setup(target => target.List(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), It.IsAny<CancellationToken>())).ReturnsAsync([entry]);

        RemoteResponse response = await Handle(new RemoteRequest { Id = 7, Command = RemoteCommand.ListEntries, From = new DateOnly(2026, 10, 1), To = new DateOnly(2026, 10, 31) });

        Assert.Equal(7, response.Id);
        Assert.Equal([entry], response.Entries);
    }

    [Fact]
    public async Task Handle_GetEntry_ReturnsTheEntryOrNothing()
    {
        journal.Setup(target => target.Get(entry.Date, It.IsAny<CancellationToken>())).ReturnsAsync(entry);

        Assert.Same(entry, (await Handle(new RemoteRequest { Id = 1, Command = RemoteCommand.GetEntry, Date = entry.Date })).Entry);
        Assert.Null((await Handle(new RemoteRequest { Id = 2, Command = RemoteCommand.GetEntry, Date = new DateOnly(2020, 1, 1) })).Entry);
    }

    [Fact]
    public async Task Handle_SaveAndDeleteEntry_UseTheDateAndText()
    {
        journal.Setup(target => target.Save(entry.Date, "x", It.IsAny<CancellationToken>())).ReturnsAsync(entry);

        Assert.Same(entry, (await Handle(new RemoteRequest { Id = 1, Command = RemoteCommand.SaveEntry, Date = entry.Date, Text = "x" })).Entry);
        Assert.Null((await Handle(new RemoteRequest { Id = 2, Command = RemoteCommand.DeleteEntry, Date = entry.Date })).Error);
        journal.Verify(target => target.Delete(entry.Date, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(RemoteCommand.GetEntry, "date")]
    [InlineData(RemoteCommand.SaveEntry, "date")]
    [InlineData(RemoteCommand.DeleteEntry, "date")]
    [InlineData(RemoteCommand.GetSummaries, "kind")]
    [InlineData(RemoteCommand.SendChat, "text")]
    [InlineData(RemoteCommand.Transcribe, "data")]
    public async Task Handle_MissingArgument_ReportsItInTheResponse(RemoteCommand command, string missing)
    {
        RemoteResponse response = await Handle(new RemoteRequest { Id = 3, Command = command });

        Assert.Equal(3, response.Id);
        Assert.Equal($"The request needs a {missing}.", response.Error);
    }

    [Fact]
    public async Task Handle_ServiceRefuses_ReportsTheMessageInTheResponse()
    {
        journal.Setup(target => target.Save(It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new RequestException("The entry is empty."));

        RemoteResponse response = await Handle(new RemoteRequest { Id = 4, Command = RemoteCommand.SaveEntry, Date = entry.Date, Text = " " });

        Assert.Equal("The entry is empty.", response.Error);
        Assert.Null(response.Entry);
    }

    [Fact]
    public async Task Handle_GetSummaries_ReadsTheKindFromTheStore()
    {
        Summary summary = new() { Kind = SummaryKind.Week, Period = "2026-10-12", Start = new DateOnly(2026, 10, 12), End = new DateOnly(2026, 10, 18), Text = "t", EntryCount = 1, UpdatedAt = DateTimeOffset.UnixEpoch };
        summaries.Setup(target => target.List(SummaryKind.Week, It.IsAny<CancellationToken>())).ReturnsAsync([summary]);

        Assert.Equal([summary], (await Handle(new RemoteRequest { Id = 1, Command = RemoteCommand.GetSummaries, Kind = SummaryKind.Week })).Summaries);
    }

    [Fact]
    public async Task Handle_Transcribe_ReturnsTheTextTheSpeechServiceHeard()
    {
        speech.Setup(target => target.Transcribe("QUJD", It.IsAny<CancellationToken>())).ReturnsAsync("hello there");

        RemoteResponse response = await Handle(new RemoteRequest { Id = 5, Command = RemoteCommand.Transcribe, Data = "QUJD" });

        Assert.Equal("hello there", response.Transcript);
        Assert.Equal(5, response.Id);
    }

    [Fact]
    public async Task Handle_Transcribe_ReportsWhyItFailed()
    {
        speech.Setup(target => target.Transcribe(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new RequestException("Voice input is turned off on the server."));

        RemoteResponse response = await Handle(new RemoteRequest { Id = 5, Command = RemoteCommand.Transcribe, Data = "QUJD" });

        Assert.Equal("Voice input is turned off on the server.", response.Error);
    }

    [Fact]
    public async Task Handle_ChatCommands_UseTheChatService()
    {
        ChatHistory history = new() { Messages = [], Working = true };
        chat.Setup(target => target.Get(It.IsAny<CancellationToken>())).ReturnsAsync(history);

        Assert.Same(history, (await Handle(new RemoteRequest { Id = 1, Command = RemoteCommand.GetChat })).Chat);
        await Handle(new RemoteRequest { Id = 2, Command = RemoteCommand.SendChat, Text = "hi" });
        await Handle(new RemoteRequest { Id = 3, Command = RemoteCommand.ClearChat });

        chat.Verify(target => target.Send(config, "hi", It.IsAny<CancellationToken>()), Times.Once);
        chat.Verify(target => target.Clear(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_UnknownCommand_ReportsIt()
    {
        RemoteResponse response = await Handle(new RemoteRequest { Id = 1, Command = (RemoteCommand)999 });

        Assert.Equal("The server does not know that command.", response.Error);
    }
}
