namespace Markwardt.ClarityJournal.Server;

/// <summary>
/// The chat in which the user asks Claude Code about the journal.
/// </summary>
internal interface IChatService
{
    /// <summary>
    /// Reads the chat.
    /// </summary>
    /// <param name="cancellation">Cancels the read.</param>
    /// <returns>The messages and whether an answer is being written.</returns>
    Task<ChatHistory> Get(CancellationToken cancellation);

    /// <summary>
    /// Adds a question to the chat and starts answering it in the background; the answer arrives as an event. Questions asked while one is answered wait their turn.
    /// </summary>
    /// <param name="config">The server settings.</param>
    /// <param name="text">The question; it must not be blank.</param>
    /// <param name="lifetime">Ends the answering when the server stops.</param>
    /// <exception cref="RequestException">The question is blank.</exception>
    Task Send(ServerConfig config, string text, CancellationToken lifetime);

    /// <summary>
    /// Deletes the chat and the Claude Code conversation behind it.
    /// </summary>
    /// <param name="cancellation">Cancels the write.</param>
    Task Clear(CancellationToken cancellation);

    /// <summary>
    /// Deletes the messages written before a moment, which keeps the chat to the last few days. When none remain, the Claude Code conversation behind the chat is forgotten too, so the next question starts fresh. Waits for an answer being written.
    /// </summary>
    /// <param name="cutoff">The moment; older messages are deleted.</param>
    /// <param name="cancellation">Cancels the write.</param>
    /// <returns>How many messages were deleted.</returns>
    Task<int> Trim(DateTimeOffset cutoff, CancellationToken cancellation);
}

/// <inheritdoc />
internal sealed class ChatService(IChatStore chats, IMetaStore meta, IClaudeRunner claude, IPrompts prompts, IJournalExport export, IEventHub events, TimeProvider time, ILog log) : IChatService
{
    private readonly SemaphoreSlim turn = new(1, 1);

    private readonly string sessionKey = "chat_session";

    private readonly string[] tools = ["Read", "Grep", "Glob"];

    private int waiting;

    /// <inheritdoc />
    public async Task<ChatHistory> Get(CancellationToken cancellation) => new() { Messages = await chats.List(cancellation), Working = Volatile.Read(ref waiting) > 0 };

    /// <inheritdoc />
    public async Task Send(ServerConfig config, string text, CancellationToken lifetime)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new RequestException("The question is empty.");
        }

        ChatMessage question = await chats.Add(ChatRole.User, text.Trim(), time.GetUtcNow(), lifetime);
        Publish(question);

        if (Interlocked.Increment(ref waiting) == 1)
        {
            events.Publish(new RemoteEvent { Kind = RemoteEventKind.ChatWorkingChanged, Working = true });
        }

        _ = Answer(config, question.Text, lifetime);
    }

    /// <inheritdoc />
    public async Task Clear(CancellationToken cancellation)
    {
        await turn.WaitAsync(cancellation);

        try
        {
            await chats.Clear(cancellation);
            await meta.Remove(sessionKey, cancellation);
        }
        finally
        {
            turn.Release();
        }

        events.Publish(new RemoteEvent { Kind = RemoteEventKind.ChatCleared });
    }

    /// <inheritdoc />
    public async Task<int> Trim(DateTimeOffset cutoff, CancellationToken cancellation)
    {
        await turn.WaitAsync(cancellation);

        try
        {
            int removed = await chats.DeleteBefore(cutoff, cancellation);

            if (removed > 0)
            {
                if ((await chats.List(cancellation)).Count == 0)
                {
                    await meta.Remove(sessionKey, cancellation);
                }

                events.Publish(new RemoteEvent { Kind = RemoteEventKind.ChatTrimmed });
            }

            return removed;
        }
        finally
        {
            turn.Release();
        }
    }

    private void Publish(ChatMessage message) => events.Publish(new RemoteEvent { Kind = RemoteEventKind.ChatMessageAdded, Message = message });

    private async Task Answer(ServerConfig config, string question, CancellationToken lifetime)
    {
        try
        {
            await turn.WaitAsync(lifetime);

            try
            {
                string reply;

                try
                {
                    reply = await Ask(config, question, lifetime);
                }
                catch (ClaudeException exception)
                {
                    log.Write($"Chat answer failed: {exception.Message}");
                    reply = $"I could not answer that: {exception.Message}";
                }

                Publish(await chats.Add(ChatRole.Assistant, reply, time.GetUtcNow(), lifetime));
            }
            finally
            {
                turn.Release();
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            if (Interlocked.Decrement(ref waiting) == 0)
            {
                events.Publish(new RemoteEvent { Kind = RemoteEventKind.ChatWorkingChanged, Working = false });
            }
        }
    }

    private async Task<string> Ask(ServerConfig config, string question, CancellationToken lifetime)
    {
        string? stored = await meta.Get(sessionKey, lifetime);
        bool resume = Guid.TryParse(stored, out Guid existing);
        Guid session = resume ? existing : Guid.NewGuid();

        try
        {
            string reply = await claude.Run(config, new ClaudeRequest { Prompt = question, SystemPrompt = prompts.ChatSystem, Folder = export.Folder, Tools = tools, Session = session, Resume = resume }, lifetime);

            if (!resume)
            {
                await meta.Set(sessionKey, session.ToString(), lifetime);
            }

            return reply;
        }
        catch (ClaudeException exception) when (resume && exception.Message.Contains("No conversation found", StringComparison.OrdinalIgnoreCase))
        {
            await meta.Remove(sessionKey, lifetime);
            throw;
        }
    }
}
