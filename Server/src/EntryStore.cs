namespace Markwardt.ClarityJournal.Server;

/// <summary>
/// Stores the journal entries, one per day.
/// </summary>
internal interface IEntryStore
{
    /// <summary>
    /// Reads the entry of a day.
    /// </summary>
    /// <param name="date">The day.</param>
    /// <param name="cancellation">Cancels the read.</param>
    /// <returns>The entry, or <see langword="null" /> if the day has none.</returns>
    Task<JournalEntry?> Get(DateOnly date, CancellationToken cancellation);

    /// <summary>
    /// Lists the entries between two days, both included.
    /// </summary>
    /// <param name="from">The first day, or <see langword="null" /> for no lower bound.</param>
    /// <param name="to">The last day, or <see langword="null" /> for no upper bound.</param>
    /// <param name="cancellation">Cancels the read.</param>
    /// <returns>The entries, oldest first.</returns>
    Task<IReadOnlyList<JournalEntry>> List(DateOnly? from, DateOnly? to, CancellationToken cancellation);

    /// <summary>
    /// Counts the entries between two days, both included.
    /// </summary>
    /// <param name="from">The first day, or <see langword="null" /> for no lower bound.</param>
    /// <param name="to">The last day, or <see langword="null" /> for no upper bound.</param>
    /// <param name="cancellation">Cancels the read.</param>
    /// <returns>The number of entries.</returns>
    Task<int> Count(DateOnly? from, DateOnly? to, CancellationToken cancellation);

    /// <summary>
    /// Writes the entry of a day, replacing the one it has, and gives the save the next revision.
    /// </summary>
    /// <param name="date">The day.</param>
    /// <param name="text">The entry text.</param>
    /// <param name="at">When the entry is saved.</param>
    /// <param name="cancellation">Cancels the write.</param>
    /// <returns>The stored entry.</returns>
    Task<JournalEntry> Save(DateOnly date, string text, DateTimeOffset at, CancellationToken cancellation);

    /// <summary>
    /// Deletes the entry of a day.
    /// </summary>
    /// <param name="date">The day.</param>
    /// <param name="cancellation">Cancels the write.</param>
    /// <returns><see langword="true" /> if the day had an entry.</returns>
    Task<bool> Delete(DateOnly date, CancellationToken cancellation);

    /// <summary>
    /// Lists the entries saved after a revision, which is how summaries find what is new without reading the whole journal.
    /// </summary>
    /// <param name="after">The revision after which saves are listed.</param>
    /// <param name="through">The last revision listed.</param>
    /// <param name="cancellation">Cancels the read.</param>
    /// <returns>The entries, ordered by revision.</returns>
    Task<IReadOnlyList<RevisedEntry>> ListSince(long after, long through, CancellationToken cancellation);

    /// <summary>
    /// Reads the revision of the latest save.
    /// </summary>
    /// <param name="cancellation">Cancels the read.</param>
    /// <returns>The revision, or zero if nothing was ever saved.</returns>
    Task<long> LatestRevision(CancellationToken cancellation);
}

/// <inheritdoc />
internal sealed class EntryStore(IDatabase database) : IEntryStore
{
    private readonly string dateFormat = "yyyy-MM-dd";

    private readonly string select = "SELECT date, text, updated_at, revision FROM entries";

    /// <inheritdoc />
    public async Task<JournalEntry?> Get(DateOnly date, CancellationToken cancellation)
    {
        await using SqliteConnection connection = await database.Open(cancellation);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"{select} WHERE date = $date";
        command.Parameters.AddWithValue("$date", Format(date));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellation);
        return await reader.ReadAsync(cancellation) ? Read(reader).Entry : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JournalEntry>> List(DateOnly? from, DateOnly? to, CancellationToken cancellation)
    {
        await using SqliteConnection connection = await database.Open(cancellation);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"{select} WHERE ($from IS NULL OR date >= $from) AND ($to IS NULL OR date <= $to) ORDER BY date";
        Bind(command, from, to);
        return (await ReadAll(command, cancellation)).Select(revised => revised.Entry).ToList();
    }

    /// <inheritdoc />
    public async Task<int> Count(DateOnly? from, DateOnly? to, CancellationToken cancellation)
    {
        await using SqliteConnection connection = await database.Open(cancellation);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM entries WHERE ($from IS NULL OR date >= $from) AND ($to IS NULL OR date <= $to)";
        Bind(command, from, to);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellation), CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public async Task<JournalEntry> Save(DateOnly date, string text, DateTimeOffset at, CancellationToken cancellation)
    {
        await using SqliteConnection connection = await database.Open(cancellation);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE meta SET value = CAST(CAST(value AS INTEGER) + 1 AS TEXT) WHERE key = 'revision';
            INSERT INTO entries (date, text, revision, updated_at)
            VALUES ($date, $text, (SELECT CAST(value AS INTEGER) FROM meta WHERE key = 'revision'), $at)
            ON CONFLICT (date) DO UPDATE SET text = excluded.text, revision = excluded.revision, updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$date", Format(date));
        command.Parameters.AddWithValue("$text", text);
        command.Parameters.AddWithValue("$at", at.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellation);
        await transaction.CommitAsync(cancellation);
        return new JournalEntry { Date = date, Text = text, UpdatedAt = at };
    }

    /// <inheritdoc />
    public async Task<bool> Delete(DateOnly date, CancellationToken cancellation)
    {
        await using SqliteConnection connection = await database.Open(cancellation);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM entries WHERE date = $date";
        command.Parameters.AddWithValue("$date", Format(date));
        return await command.ExecuteNonQueryAsync(cancellation) > 0;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RevisedEntry>> ListSince(long after, long through, CancellationToken cancellation)
    {
        await using SqliteConnection connection = await database.Open(cancellation);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"{select} WHERE revision > $after AND revision <= $through ORDER BY revision";
        command.Parameters.AddWithValue("$after", after);
        command.Parameters.AddWithValue("$through", through);
        return await ReadAll(command, cancellation);
    }

    /// <inheritdoc />
    public async Task<long> LatestRevision(CancellationToken cancellation)
    {
        await using SqliteConnection connection = await database.Open(cancellation);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT CAST(value AS INTEGER) FROM meta WHERE key = 'revision'";
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellation), CultureInfo.InvariantCulture);
    }

    private string Format(DateOnly date) => date.ToString(dateFormat, CultureInfo.InvariantCulture);

    private void Bind(SqliteCommand command, DateOnly? from, DateOnly? to)
    {
        command.Parameters.AddWithValue("$from", from is { } first ? Format(first) : DBNull.Value);
        command.Parameters.AddWithValue("$to", to is { } last ? Format(last) : DBNull.Value);
    }

    private RevisedEntry Read(SqliteDataReader reader) => new()
    {
        Entry = new JournalEntry
        {
            Date = DateOnly.ParseExact(reader.GetString(0), dateFormat, CultureInfo.InvariantCulture),
            Text = reader.GetString(1),
            UpdatedAt = DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
        },
        Revision = reader.GetInt64(3)
    };

    private async Task<IReadOnlyList<RevisedEntry>> ReadAll(SqliteCommand command, CancellationToken cancellation)
    {
        List<RevisedEntry> entries = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            entries.Add(Read(reader));
        }

        return entries;
    }
}
