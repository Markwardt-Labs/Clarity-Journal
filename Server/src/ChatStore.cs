namespace Markwardt.ClarityJournal.Server;

/// <summary>
/// Stores the chat about the journal.
/// </summary>
internal interface IChatStore
{
    /// <summary>
    /// Appends a message to the chat.
    /// </summary>
    /// <param name="role">Who wrote the message.</param>
    /// <param name="text">The message text.</param>
    /// <param name="at">When the message was written.</param>
    /// <param name="cancellation">Cancels the write.</param>
    /// <returns>The stored message.</returns>
    Task<ChatMessage> Add(ChatRole role, string text, DateTimeOffset at, CancellationToken cancellation);

    /// <summary>
    /// Lists the whole chat.
    /// </summary>
    /// <param name="cancellation">Cancels the read.</param>
    /// <returns>The messages, oldest first.</returns>
    Task<IReadOnlyList<ChatMessage>> List(CancellationToken cancellation);

    /// <summary>
    /// Deletes every message.
    /// </summary>
    /// <param name="cancellation">Cancels the write.</param>
    Task Clear(CancellationToken cancellation);

    /// <summary>
    /// Deletes the messages written before a moment.
    /// </summary>
    /// <param name="cutoff">The moment; messages written at exactly this moment stay.</param>
    /// <param name="cancellation">Cancels the write.</param>
    /// <returns>How many messages were deleted.</returns>
    Task<int> DeleteBefore(DateTimeOffset cutoff, CancellationToken cancellation);
}

/// <inheritdoc />
internal sealed class ChatStore(IDatabase database) : IChatStore
{
    /// <inheritdoc />
    public async Task<ChatMessage> Add(ChatRole role, string text, DateTimeOffset at, CancellationToken cancellation)
    {
        await using SqliteConnection connection = await database.Open(cancellation);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "INSERT INTO chat_messages (role, text, at) VALUES ($role, $text, $at); SELECT last_insert_rowid()";
        command.Parameters.AddWithValue("$role", role.ToString());
        command.Parameters.AddWithValue("$text", text);
        command.Parameters.AddWithValue("$at", at.ToString("O", CultureInfo.InvariantCulture));
        long id = Convert.ToInt64(await command.ExecuteScalarAsync(cancellation), CultureInfo.InvariantCulture);
        return new ChatMessage { Id = id, Role = role, Text = text, At = at };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChatMessage>> List(CancellationToken cancellation)
    {
        await using SqliteConnection connection = await database.Open(cancellation);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT id, role, text, at FROM chat_messages ORDER BY id";
        List<ChatMessage> messages = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            messages.Add(new ChatMessage
            {
                Id = reader.GetInt64(0),
                Role = Enum.Parse<ChatRole>(reader.GetString(1)),
                Text = reader.GetString(2),
                At = DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            });
        }

        return messages;
    }

    /// <inheritdoc />
    public async Task Clear(CancellationToken cancellation)
    {
        await using SqliteConnection connection = await database.Open(cancellation);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM chat_messages";
        await command.ExecuteNonQueryAsync(cancellation);
    }

    /// <inheritdoc />
    public async Task<int> DeleteBefore(DateTimeOffset cutoff, CancellationToken cancellation)
    {
        List<long> old = [.. (await List(cancellation)).Where(message => message.At < cutoff).Select(message => message.Id)];

        if (old.Count == 0)
        {
            return 0;
        }

        await using SqliteConnection connection = await database.Open(cancellation);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM chat_messages WHERE id = $id";
        SqliteParameter id = command.Parameters.Add("$id", SqliteType.Integer);

        foreach (long value in old)
        {
            id.Value = value;
            await command.ExecuteNonQueryAsync(cancellation);
        }

        await transaction.CommitAsync(cancellation);
        return old.Count;
    }
}
