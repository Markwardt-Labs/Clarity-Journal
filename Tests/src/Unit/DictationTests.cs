namespace Markwardt.ClarityJournal.Tests.Unit;

public sealed class DictationTests
{
    private readonly Mock<IServerConnection> connection = new();

    private readonly Mock<IVoiceRecorder> recorder = new();

    private readonly List<string> dictated = [];

    private readonly List<string> failures = [];

    private bool allowed = true;

    private readonly Dictation dictation;

    public DictationTests()
    {
        recorder.SetupGet(target => target.SampleRate).Returns(16000);
        recorder.Setup(target => target.Start(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        recorder.Setup(target => target.Stop()).ReturnsAsync(Speech(1));
        connection.Setup(target => target.Transcribe(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>())).ReturnsAsync("hello there");
        dictation = new Dictation(connection.Object, recorder.Object, () => allowed);
        dictation.Dictated += dictated.Add;
        dictation.Failed += failures.Add;
    }

    private ReadOnlyMemory<byte> Speech(double seconds) => new byte[(int)(16000 * 2 * seconds)];

    [Fact]
    public void CanDictate_IsOnlyTrueWhenTheClientCanRecord()
    {
        Assert.True(dictation.CanDictate);
        Assert.False(new Dictation(connection.Object, null, () => true).CanDictate);
    }

    [Fact]
    public void Command_RecordsThenTurnsTheSpeechIntoText()
    {
        ReadOnlyMemory<byte> sent = default;
        connection.Setup(target => target.Transcribe(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>())).Callback((ReadOnlyMemory<byte> audio, CancellationToken _) => sent = audio).ReturnsAsync(" hello there ");

        dictation.Command.Execute(null);
        Assert.True(dictation.IsRecording);
        Assert.Equal("Stop recording and add the text", dictation.Tip);

        dictation.Command.Execute(null);

        Assert.False(dictation.IsRecording);
        Assert.False(dictation.IsTranscribing);
        Assert.Equal(["hello there"], dictated);
        Assert.Empty(failures);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(sent.Span[..4]));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(sent.Span[8..12]));
        Assert.Equal(44 + 32000, sent.Length);
        Assert.Equal(16000, BitConverter.ToInt32(sent.Span[24..28]));
        Assert.Equal(32000, BitConverter.ToInt32(sent.Span[40..44]));
        Assert.Equal("Voice input", dictation.Tip);
    }

    [Fact]
    public void Command_ReportsARecordingThatCouldNotStart()
    {
        recorder.Setup(target => target.Start(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("No microphone."));

        dictation.Command.Execute(null);

        Assert.False(dictation.IsRecording);
        Assert.Equal(["No microphone."], failures);
    }

    [Fact]
    public void Command_IgnoresARecordingTooShortToHoldSpeech()
    {
        recorder.Setup(target => target.Stop()).ReturnsAsync(Speech(0.1));

        dictation.Command.Execute(null);
        dictation.Command.Execute(null);

        Assert.Equal(["Nothing was recorded."], failures);
        Assert.False(dictation.IsTranscribing);
        connection.Verify(target => target.Transcribe(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void Command_ReportsWhenNothingWasHeard()
    {
        connection.Setup(target => target.Transcribe(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>())).ReturnsAsync("  ");

        dictation.Command.Execute(null);
        dictation.Command.Execute(null);

        Assert.Equal(["Nothing was heard."], failures);
        Assert.Empty(dictated);
    }

    [Fact]
    public void Command_ShowsWhyTheServerCouldNotTranscribeAndStaysUsable()
    {
        connection.Setup(target => target.Transcribe(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>())).ThrowsAsync(new RemoteException("Voice input is turned off on the server."));

        dictation.Command.Execute(null);
        dictation.Command.Execute(null);

        Assert.Equal(["Voice input is turned off on the server."], failures);
        Assert.False(dictation.IsTranscribing);
        Assert.True(dictation.Command.CanExecute(null));
    }

    [Fact]
    public void Command_IsUnavailableWhenRecordingMayNotStartButStillStopsARecording()
    {
        allowed = false;
        dictation.Refresh();
        Assert.False(dictation.Command.CanExecute(null));

        allowed = true;
        dictation.Refresh();
        dictation.Command.Execute(null);
        allowed = false;
        dictation.Refresh();

        Assert.True(dictation.Command.CanExecute(null));
    }

    [Fact]
    public void Command_IsUnavailableWhileTheSpeechIsBeingTurnedIntoText()
    {
        TaskCompletionSource<string> pending = new();
        connection.Setup(target => target.Transcribe(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>())).Returns(pending.Task);
        dictation.Command.Execute(null);

        dictation.Command.Execute(null);

        Assert.True(dictation.IsTranscribing);
        Assert.Equal("Turning speech into text", dictation.Tip);
        Assert.False(dictation.Command.CanExecute(null));

        pending.SetResult("done");

        Assert.Equal(["done"], dictated);
        Assert.True(dictation.Command.CanExecute(null));
    }

    [Fact]
    public async Task Stop_EndsARunningRecordingAndTurnsItIntoText_AndDoesNothingOtherwise()
    {
        await dictation.Stop();
        recorder.Verify(target => target.Stop(), Times.Never);

        dictation.Command.Execute(null);
        await dictation.Stop();

        Assert.Equal(["hello there"], dictated);
        Assert.False(dictation.IsRecording);
    }

    [Fact]
    public void Cancel_ReleasesTheMicrophoneAndThrowsTheRecordingAway()
    {
        dictation.Command.Execute(null);

        dictation.Cancel();

        recorder.Verify(target => target.Stop(), Times.Once);
        Assert.False(dictation.IsRecording);
        Assert.Empty(dictated);
        connection.Verify(target => target.Transcribe(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void Cancel_WhenNothingIsRecording_DoesNothing()
    {
        dictation.Cancel();

        recorder.Verify(target => target.Stop(), Times.Never);
    }
}

public sealed class DictationInScreensTests
{
    private readonly Mock<IServerConnection> connection = new();

    private readonly Mock<IVoiceRecorder> recorder = new();

    public DictationInScreensTests()
    {
        recorder.SetupGet(target => target.SampleRate).Returns(16000);
        recorder.Setup(target => target.Start(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        recorder.Setup(target => target.Stop()).ReturnsAsync(new byte[32000]);
        connection.Setup(target => target.Transcribe(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>())).ReturnsAsync("hello there");
        connection.Setup(target => target.ListEntries(It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        connection.Setup(target => target.GetSummaries(It.IsAny<SummaryKind>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        connection.Setup(target => target.GetChat(It.IsAny<CancellationToken>())).ReturnsAsync(new ChatHistory { Messages = [], Working = false });
    }

    [Theory]
    [InlineData("", "hello there")]
    [InlineData("so far", "so far hello there")]
    [InlineData("so far ", "so far hello there")]
    [InlineData("so far\n", "so far\nhello there")]
    public void Journal_AddsWhatWasSaidToTheEndOfTodaysEntry(string typed, string expected)
    {
        JournalViewModel journal = new(connection.Object, () => new DateOnly(2026, 10, 16), recorder.Object) { EntryText = typed };

        journal.Dictation.Command.Execute(null);
        journal.Dictation.Command.Execute(null);

        Assert.Equal(expected, journal.EntryText);
    }

    [Fact]
    public void Journal_ShowsAFailureAsItsError()
    {
        recorder.Setup(target => target.Start(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("Voice input needs the microphone permission."));
        JournalViewModel journal = new(connection.Object, () => new DateOnly(2026, 10, 16), recorder.Object);

        journal.Dictation.Command.Execute(null);

        Assert.Equal("Voice input needs the microphone permission.", journal.Error);
    }

    [Fact]
    public void Journal_HasNoVoiceInputWithoutARecorder()
    {
        Assert.False(new JournalViewModel(connection.Object, () => new DateOnly(2026, 10, 16)).Dictation.CanDictate);
    }

    [Theory]
    [InlineData("", "hello there")]
    [InlineData("What about", "What about hello there")]
    public void Chat_AddsWhatWasSaidToTheEndOfTheQuestion(string typed, string expected)
    {
        ChatViewModel chat = new(connection.Object, recorder.Object) { Draft = typed };

        chat.Dictation.Command.Execute(null);
        chat.Dictation.Command.Execute(null);

        Assert.Equal(expected, chat.Draft);
        Assert.True(chat.SendCommand.CanExecute(null));
    }

    [Fact]
    public void Chat_ShowsAFailureAsItsError()
    {
        connection.Setup(target => target.Transcribe(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>())).ThrowsAsync(new RemoteException("Voice input is turned off on the server."));
        ChatViewModel chat = new(connection.Object, recorder.Object);

        chat.Dictation.Command.Execute(null);
        chat.Dictation.Command.Execute(null);

        Assert.Equal("Voice input is turned off on the server.", chat.Error);
    }

    private SessionViewModel Session() => new(connection.Object, new ConnectionSettings { Host = "host", Port = 1, AccessKey = "k" }, "v", () => new DateOnly(2026, 10, 16), recorder.Object);

    [Fact]
    public void Session_LeavingTheJournalTabEndsTheRecordingAndKeepsTheText()
    {
        SessionViewModel session = Session();
        session.Journal.Dictation.Command.Execute(null);

        session.SelectTabCommand.Execute(AppTab.Summaries);

        Assert.False(session.Journal.Dictation.IsRecording);
        Assert.Equal("hello there", session.Journal.EntryText);
    }

    [Fact]
    public void Session_LeavingTheAskTabEndsTheRecordingAndKeepsTheText()
    {
        SessionViewModel session = Session();
        session.SelectTabCommand.Execute(AppTab.Chat);
        session.Chat.Dictation.Command.Execute(null);

        session.SelectTabCommand.Execute(AppTab.Journal);

        Assert.False(session.Chat.Dictation.IsRecording);
        Assert.Equal("hello there", session.Chat.Draft);
    }

    [Fact]
    public async Task Session_LeavingTheJournalTabSavesTodaysChangedText()
    {
        connection.Setup(target => target.SaveEntry(It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((DateOnly date, string text, CancellationToken _) => new JournalEntry { Date = date, Text = text, UpdatedAt = DateTimeOffset.UnixEpoch });
        SessionViewModel session = Session();
        await session.Journal.Load();
        session.Journal.EntryText = "typed just now";

        session.SelectTabCommand.Execute(AppTab.Summaries);

        connection.Verify(target => target.SaveEntry(new DateOnly(2026, 10, 16), "typed just now", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Session_ChangingToTheSameTabKeepsRecording()
    {
        SessionViewModel session = Session();
        session.Journal.Dictation.Command.Execute(null);

        session.SelectTabCommand.Execute(AppTab.Journal);

        Assert.True(session.Journal.Dictation.IsRecording);
    }

    [Fact]
    public void Session_DetachReleasesTheMicrophoneWithoutTranscribing()
    {
        SessionViewModel session = Session();
        session.Journal.Dictation.Command.Execute(null);

        session.Detach();

        Assert.False(session.Journal.Dictation.IsRecording);
        connection.Verify(target => target.Transcribe(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
