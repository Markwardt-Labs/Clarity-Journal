namespace Markwardt.ClarityJournal.Tests.Unit;

public sealed class ChatViewModelTests
{
    private readonly Mock<IServerConnection> connection = new();

    private readonly ChatViewModel model;

    public ChatViewModelTests()
    {
        connection.Setup(target => target.GetChat(It.IsAny<CancellationToken>())).ReturnsAsync(new ChatHistory { Messages = [Message(1, ChatRole.User, "hi"), Message(2, ChatRole.Assistant, "hello")], Working = true });
        model = new ChatViewModel(connection.Object);
    }

    private ChatMessage Message(long id, ChatRole role, string text) => new() { Id = id, Role = role, Text = text, At = new DateTimeOffset(2026, 10, 16, 20, 5, 0, TimeSpan.Zero) };

    [Fact]
    public async Task Load_ShowsTheMessagesAndWhetherAnAnswerIsComing()
    {
        await model.Load();

        Assert.Equal(["hi", "hello"], model.Messages.Select(message => message.Text));
        Assert.Equal([true, false], model.Messages.Select(message => message.IsUser));
        Assert.True(model.Messages[1].IsAssistant);
        Assert.True(model.IsWorking);
        Assert.True(model.AskClearCommand.CanExecute(null));
    }

    [Fact]
    public async Task Clear_AsksForConfirmationBeforeDeletingAnything()
    {
        await model.Load();

        model.AskClearCommand.Execute(null);
        Assert.True(model.IsConfirmingClear);
        connection.Verify(target => target.ClearChat(It.IsAny<CancellationToken>()), Times.Never);

        model.CancelClearCommand.Execute(null);
        Assert.False(model.IsConfirmingClear);
        Assert.Equal(2, model.Messages.Count);
        connection.Verify(target => target.ClearChat(It.IsAny<CancellationToken>()), Times.Never);

        model.AskClearCommand.Execute(null);
        model.ClearCommand.Execute(null);
        Assert.False(model.IsConfirmingClear);
        connection.Verify(target => target.ClearChat(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void AskClear_IsUnavailableWhileTheChatIsEmpty()
    {
        Assert.False(model.AskClearCommand.CanExecute(null));
    }

    [Fact]
    public async Task Clear_WhenTheServerFails_ClosesTheDialogAndShowsTheError()
    {
        await model.Load();
        connection.Setup(target => target.ClearChat(It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("offline"));
        model.AskClearCommand.Execute(null);

        model.ClearCommand.Execute(null);

        Assert.False(model.IsConfirmingClear);
        Assert.Equal("offline", model.Error);
        Assert.Equal(2, model.Messages.Count);
    }

    [Fact]
    public void SendCommand_NeedsText()
    {
        Assert.False(model.SendCommand.CanExecute(null));

        model.Draft = "  ";
        Assert.False(model.SendCommand.CanExecute(null));

        model.Draft = "What did I do?";
        Assert.True(model.SendCommand.CanExecute(null));
    }

    [Fact]
    public void Send_SendsTheTrimmedQuestionAndClearsTheBox()
    {
        model.Draft = "  What did I do?  ";

        model.SendCommand.Execute(null);

        connection.Verify(target => target.SendChat("What did I do?", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(string.Empty, model.Draft);
    }

    [Fact]
    public void Send_WhenItFails_PutsTheQuestionBackAndShowsTheError()
    {
        connection.Setup(target => target.SendChat(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("offline"));
        model.Draft = "question";

        model.SendCommand.Execute(null);

        Assert.Equal("question", model.Draft);
        Assert.Equal("offline", model.Error);
    }

    [Fact]
    public async Task Handle_AddedMessage_IsAppendedOnceEvenIfItArrivesTwice()
    {
        await model.Load();
        RemoteEvent added = new() { Kind = RemoteEventKind.ChatMessageAdded, Message = Message(3, ChatRole.Assistant, "answer") };

        await model.Handle(added);
        await model.Handle(added);

        Assert.Equal(3, model.Messages.Count);
        Assert.Equal("answer", model.Messages[^1].Text);
    }

    [Fact]
    public async Task Handle_WorkingChangedAndCleared_UpdateTheScreen()
    {
        await model.Load();

        await model.Handle(new RemoteEvent { Kind = RemoteEventKind.ChatWorkingChanged, Working = false });
        Assert.False(model.IsWorking);

        model.AskClearCommand.Execute(null);
        await model.Handle(new RemoteEvent { Kind = RemoteEventKind.ChatCleared });
        Assert.Empty(model.Messages);
        Assert.False(model.IsConfirmingClear);
        Assert.False(model.AskClearCommand.CanExecute(null));
    }

    [Fact]
    public async Task Handle_ChatTrimmed_ReadsTheChatAgain()
    {
        await model.Load();
        connection.Setup(target => target.GetChat(It.IsAny<CancellationToken>())).ReturnsAsync(new ChatHistory { Messages = [Message(2, ChatRole.Assistant, "hello")], Working = false });

        await model.Handle(new RemoteEvent { Kind = RemoteEventKind.ChatTrimmed });

        Assert.Equal(["hello"], model.Messages.Select(message => message.Text));
    }

    [Fact]
    public void Message_ShowsItsLocalTime()
    {
        ChatMessageViewModel message = new(Message(1, ChatRole.User, "x"));

        Assert.Equal(new DateTimeOffset(2026, 10, 16, 20, 5, 0, TimeSpan.Zero).ToLocalTime().ToString("h:mm tt", CultureInfo.InvariantCulture), message.Time);
    }
}
