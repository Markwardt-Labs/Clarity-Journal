namespace Markwardt.ClarityJournal.Client;

/// <summary>
/// The chat screen: questions about the journal, answered by Claude Code on the server.
/// </summary>
internal sealed class ChatViewModel : ViewModelBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ChatViewModel" /> class.
    /// </summary>
    /// <param name="connection">The open connection.</param>
    /// <param name="recorder">Records speech for voice input, or <see langword="null" /> when this client cannot record.</param>
    public ChatViewModel(IServerConnection connection, IVoiceRecorder? recorder = null)
    {
        this.connection = connection;
        SendCommand = new AsyncCommand(_ => Send(), () => !string.IsNullOrWhiteSpace(draft));
        AskClearCommand = new RelayCommand(_ => IsConfirmingClear = true, () => Messages.Count > 0);
        CancelClearCommand = new RelayCommand(_ => IsConfirmingClear = false);
        ClearCommand = new AsyncCommand(_ => Clear());
        Dictation = new Dictation(connection, recorder, () => true);
        Dictation.Dictated += text => Draft = draft.Length == 0 || char.IsWhiteSpace(draft[^1]) ? draft + text : $"{draft} {text}";
        Dictation.Failed += message => Error = message;
    }

    private readonly IServerConnection connection;

    private string draft = string.Empty;

    private bool isWorking;

    private bool isConfirmingClear;

    /// <summary>
    /// Gets the voice input of the message box, which adds what is said to the end of the question.
    /// </summary>
    public IDictation Dictation { get; }

    /// <summary>
    /// Gets the messages, oldest first.
    /// </summary>
    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];

    /// <summary>
    /// Gets or sets the question being typed.
    /// </summary>
    public string Draft
    {
        get => draft;
        set
        {
            if (Set(ref draft, value))
            {
                SendCommand.Refresh();
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether Claude Code is working on an answer.
    /// </summary>
    public bool IsWorking
    {
        get => isWorking;
        private set => Set(ref isWorking, value);
    }

    /// <summary>
    /// Gets a value indicating whether the dialog that asks the user to confirm clearing the chat is showing.
    /// </summary>
    public bool IsConfirmingClear
    {
        get => isConfirmingClear;
        private set => Set(ref isConfirmingClear, value);
    }

    /// <summary>
    /// Gets the command that sends the question.
    /// </summary>
    public AsyncCommand SendCommand { get; }

    /// <summary>
    /// Gets the command that opens the dialog that asks the user to confirm clearing the chat; it is unavailable while the chat is empty.
    /// </summary>
    public RelayCommand AskClearCommand { get; }

    /// <summary>
    /// Gets the command that closes the confirmation dialog and keeps the chat.
    /// </summary>
    public RelayCommand CancelClearCommand { get; }

    /// <summary>
    /// Gets the command, offered by the confirmation dialog, that deletes the chat.
    /// </summary>
    public AsyncCommand ClearCommand { get; }

    /// <summary>
    /// Reads the chat.
    /// </summary>
    /// <returns>A task that completes once the chat is read or the read failed.</returns>
    public async Task Load()
    {
        ChatHistory? history = null;

        if (!await Attempt(async () => history = await connection.GetChat(CancellationToken.None)) || history is null)
        {
            return;
        }

        Messages.Clear();

        foreach (ChatMessage message in history.Messages)
        {
            Messages.Add(new ChatMessageViewModel(message));
        }

        IsWorking = history.Working;
        Changed();
    }

    /// <summary>
    /// Reacts to an event pushed by the server.
    /// </summary>
    /// <param name="remoteEvent">The event.</param>
    /// <returns>A task that completes once the screen is up to date.</returns>
    public Task Handle(RemoteEvent remoteEvent)
    {
        switch (remoteEvent.Kind)
        {
            case RemoteEventKind.ChatMessageAdded when remoteEvent.Message is { } message && Messages.All(existing => existing.Id != message.Id):
                Messages.Add(new ChatMessageViewModel(message));
                Changed();
                break;
            case RemoteEventKind.ChatWorkingChanged:
                IsWorking = remoteEvent.Working == true;
                break;
            case RemoteEventKind.ChatCleared:
                Messages.Clear();
                IsConfirmingClear = false;
                Changed();
                break;
            case RemoteEventKind.ChatTrimmed:
                return Load();
        }

        return Task.CompletedTask;
    }

    private void Changed() => AskClearCommand.Refresh();

    private async Task Send()
    {
        string text = draft.Trim();
        Draft = string.Empty;

        if (!await Attempt(() => connection.SendChat(text, CancellationToken.None)))
        {
            Draft = text;
        }
    }

    private async Task Clear()
    {
        await Attempt(() => connection.ClearChat(CancellationToken.None));
        IsConfirmingClear = false;
    }
}
