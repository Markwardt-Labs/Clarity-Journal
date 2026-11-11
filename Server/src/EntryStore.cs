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
    Task<JournalEntry?> Get(DateOnly date, CancellationToken cancellation = default);

    /// <summary>
    /// Lists the entries between two days, both included.
    /// </summary>
    /// <param name="from">The first day, or <see langword="null" /> for no lower bound.</param>
    /// <param name="to">The last day, or <see langword="null" /> for no upper bound.</param>
    /// <param name="cancellation">Cancels the read.</param>
    /// <returns>The entries, oldest first.</returns>
    Task<IReadOnlyList<JournalEntry>> List(DateOnly? from, DateOnly? to, CancellationToken cancellation = default);

    /// <summary>
    /// Counts the entries between two days, both included.
    /// </summary>
    /// <param name="from">The first day, or <see langword="null" /> for no lower bound.</param>
    /// <param name="to">The last day, or <see langword="null" /> for no upper bound.</param>
    /// <param name="cancellation">Cancels the read.</param>
    /// <returns>The number of entries.</returns>
    Task<int> Count(DateOnly? from, DateOnly? to, CancellationToken cancellation = default);

    /// <summary>
    /// Writes the entry of a day, replacing the one it has, and gives the save the next revision. When the text differs from the stored one the title and summary are cleared, since they described the old words; the same text keeps them.
    /// </summary>
    /// <param name="date">The day.</param>
    /// <param name="text">The entry text.</param>
    /// <param name="at">When the entry is saved.</param>
    /// <param name="cancellation">Cancels the write.</param>
    /// <returns>The stored entry.</returns>
    Task<JournalEntry> Save(DateOnly date, string text, DateTimeOffset at, CancellationToken cancellation = default);

    /// <summary>
    /// Deletes the entry of a day.
    /// </summary>
    /// <param name="date">The day.</param>
    /// <param name="cancellation">Cancels the write.</param>
    /// <returns><see langword="true" /> if the day had an entry.</returns>
    Task<bool> Delete(DateOnly date, CancellationToken cancellation = default);

    /// <summary>
    /// Reads what a list shows of the entry of a day.
    /// </summary>
    /// <param name="date">The day.</param>
    /// <param name="cancellation">Cancels the read.</param>
    /// <returns>The digest, or <see langword="null" /> if the day has no entry.</returns>
    Task<EntryDigest?> GetDigest(DateOnly date, CancellationToken cancellation = default);

    /// <summary>
    /// Lists what a list shows of the days that have an entry, newest first, one page at a time.
    /// </summary>
    /// <param name="before">The day before which days are listed, or <see langword="null" /> to start with the newest.</param>
    /// <param name="limit">How many days at most.</param>
    /// <param name="cancellation">Cancels the read.</param>
    /// <returns>The digests, newest first.</returns>
    Task<IReadOnlyList<EntryDigest>> ListDigests(DateOnly? before, int limit, CancellationToken cancellation = default);

    /// <summary>
    /// Lists the entries of days before a day that lack a title or a summary, which is what still needs generating.
    /// </summary>
    /// <param name="before">The day before which entries are listed; today, because a day is only titled once it is over.</param>
    /// <param name="cancellation">Cancels the read.</param>
    /// <returns>The entries, newest first.</returns>
    Task<IReadOnlyList<JournalEntry>> ListUntitled(DateOnly before, CancellationToken cancellation = default);

    /// <summary>
    /// Stores the title and summary of an entry, without counting as a save: summaries do not read the entry again for it.
    /// </summary>
    /// <param name="date">The day.</param>
    /// <param name="text">The text the title and summary were made from; nothing is stored if the entry has a different text by now, since the title would describe words that are gone.</param>
    /// <param name="title">The title.</param>
    /// <param name="summary">The summary.</param>
    /// <param name="cancellation">Cancels the write.</param>
    /// <returns><see langword="true" /> if they were stored.</returns>
    Task<bool> SetDigest(DateOnly date, string text, string title, string summary, CancellationToken cancellation = default);

    /// <summary>
    /// Lists the entries saved after a revision, which is how summaries find what is new without reading the whole journal.
    /// </summary>
    /// <param name="after">The revision after which saves are listed.</param>
    /// <param name="through">The last revision listed.</param>
    /// <param name="cancellation">Cancels the read.</param>
    /// <returns>The entries, ordered by revision.</returns>
    Task<IReadOnlyList<RevisedEntry>> ListSince(long after, long through, CancellationToken cancellation = default);

    /// <summary>
    /// Reads the revision of the latest save.
    /// </summary>
    /// <param name="cancellation">Cancels the read.</param>
    /// <returns>The revision, or zero if nothing was ever saved.</returns>
    Task<long> LatestRevision(CancellationToken cancellation = default);
}

/// <inheritdoc />
internal sealed class EntryStore(IDatabase database) : IEntryStore
{
    private readonly string dateFormat = "yyyy-MM-dd";

    private readonly string select = "SELECT date, text, updated_at, revision FROM entries";

    private readonly string selectDigest = "SELECT date, title, summary, substr(text, 1, 400) FROM entries";

    /// <inheritdoc />
    public async Task<JournalEntry?> Get(DateOnly date, CancellationToken cancellation = default)
    {
        await using SqliteConnection connection = await database.Open(cancellation);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"{select} WHERE date = $date";
        command.Parameters.AddWithValue("$date", Format(date));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellation);
        return await reader.ReadAsync(cancellation) ? Read(reader).Entry : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JournalEntry>> List(DateOnly? from, DateOnly? to, CancellationToken cancellation = default)
    {
        await using SqliteConnection connection = await database.Open(cancellation);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"{select} WHERE ($from IS NULL OR date >= $from) AND ($to IS NULL OR date <= $to) ORDER BY date";
        Bind(command, from, to);
        return (await ReadAll(command, cancellation)).Select(revised => revised.Entry).ToList();
    }

    /// <inheritdoc />
    public async Task<int> Count(DateOnly? from, DateOnly? to, CancellationToken cancellation = default)
    {
        await using SqliteConnection connection = await database.Open(cancellation);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM entries WHERE ($from IS NULL OR date >= $from) AND ($to IS NULL OR date <= $to)";
        Bind(command, from, to);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellation), CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public async Task<JournalEntry> Save(DateOnly date, string text, DateTimeOffset at, CancellationToken cancellation = default)
    {
        await using SqliteConnection connection = await database.Open(cancellation);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE meta SET value = CAST(CAST(value AS INTEGER) + 1 AS TEXT) WHERE key = 'revision';
            INSERT INTO entries (date, text, revision, updated_at)
            VALUES ($date, $text, (SELECT CAST(value AS INTEGER) FROM meta WHERE key = 'revision'), $at)
            ON CONFLICT (date) DO UPDATE SET
                title = CASE WHEN entries.text = excluded.text THEN entries.title ELSE NULL END,
                summary = CASE WHEN entries.text = excluded.text THEN entries.summary ELSE NULL END,
                text = excluded.text, revision = excluded.revision, updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$date", Format(date));
        command.Parameters.AddWithValue("$text", text);
        command.Parameters.AddWithValue("$at", at.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellation);
        await transaction.CommitAsync(cancellation);
        return new JournalEntry { Date = date, Text = text, UpdatedAt = at };
    }

    /// <inheritdoc />
    public async Task<bool> Delete(DateOnly date, CancellationToken cancellation = default)
    {
        await using SqliteConnection connection = await database.Open(cancellation);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM entries WHERE date = $date";
        command.Parameters.AddWithValue("$date", Format(date));
        return await command.ExecuteNonQueryAsync(cancellation) > 0;
    }

    /// <inheritdoc />
    public async Task<EntryDigest?> GetDigest(DateOnly date, CancellationToken cancellation = default)
    {
        await using SqliteConnection connection = await database.Open(cancellation);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"{selectDigest} WHERE date = $date";
        command.Parameters.AddWithValue("$date", Format(date));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellation);
        return await reader.ReadAsync(cancellation) ? ReadDigest(reader) : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EntryDigest>> ListDigests(DateOnly? before, int limit, CancellationToken cancellation = default)
    {
        await using SqliteConnection connection = await database.Open(cancellation);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"{selectDigest} WHERE ($before IS NULL OR date < $before) ORDER BY date DESC LIMIT $limit";
        command.Parameters.AddWithValue("$before", before is { } day ? Format(day) : DBNull.Value);
        command.Parameters.AddWithValue("$limit", limit);
        List<EntryDigest> digests = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            digests.Add(ReadDigest(reader));
        }

        return digests;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JournalEntry>> ListUntitled(DateOnly before, CancellationToken cancellation = default)
    {
        await using SqliteConnection connection = await database.Open(cancellation);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"{select} WHERE date < $before AND (title IS NULL OR summary IS NULL) ORDER BY date DESC";
        command.Parameters.AddWithValue("$before", Format(before));
        return (await ReadAll(command, cancellation)).Select(revised => revised.Entry).ToList();
    }

    /// <inheritdoc />
    public async Task<bool> SetDigest(DateOnly date, string text, string title, string summary, CancellationToken cancellation = default)
    {
        await using SqliteConnection connection = await database.Open(cancellation);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE entries SET title = $title, summary = $summary WHERE date = $date AND text = $text";
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$summary", summary);
        command.Parameters.AddWithValue("$date", Format(date));
        command.Parameters.AddWithValue("$text", text);
        return await command.ExecuteNonQueryAsync(cancellation) > 0;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RevisedEntry>> ListSince(long after, long through, CancellationToken cancellation = default)
    {
        await using SqliteConnection connection = await database.Open(cancellation);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"{select} WHERE revision > $after AND revision <= $through ORDER BY revision";
        command.Parameters.AddWithValue("$after", after);
        command.Parameters.AddWithValue("$through", through);
        return await ReadAll(command, cancellation);
    }

    /// <inheritdoc />
    public async Task<long> LatestRevision(CancellationToken cancellation = default)
    {
        await using SqliteConnection connection = await database.Open(cancellation);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT CAST(value AS INTEGER) FROM meta WHERE key = 'revision'";
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellation), CultureInfo.InvariantCulture);
    }

    private string Format(DateOnly date)
        => date.ToString(dateFormat, CultureInfo.InvariantCulture);

    private void Bind(SqliteCommand command, DateOnly? from, DateOnly? to)
    {
        command.Parameters.AddWithValue("$from", from is { } first ? Format(first) : DBNull.Value);
        command.Parameters.AddWithValue("$to", to is { } last ? Format(last) : DBNull.Value);
    }

    private EntryDigest ReadDigest(SqliteDataReader reader)
        => new()
        {
            Date = DateOnly.ParseExact(reader.GetString(0), dateFormat, CultureInfo.InvariantCulture),
            Title = reader.IsDBNull(1) ? null : reader.GetString(1),
            Summary = reader.IsDBNull(2) ? null : reader.GetString(2),
            Excerpt = reader.GetString(3).ToExcerpt()
        };

    private RevisedEntry Read(SqliteDataReader reader)
        => new()
        {
            Entry = new JournalEntry
            {
                Date = DateOnly.ParseExact(reader.GetString(0), dateFormat, CultureInfo.InvariantCulture),
                Text = reader.GetString(1),
                UpdatedAt = DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            },
            Revision = reader.GetInt64(3)
        };

    private async Task<IReadOnlyList<RevisedEntry>> ReadAll(SqliteCommand command, CancellationToken cancellation = default)
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
