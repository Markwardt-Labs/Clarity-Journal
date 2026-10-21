namespace Markwardt.ClarityJournal.Server;

/// <summary>
/// Stores the summaries and the progress of summarizing.
/// </summary>
internal interface ISummaryStore
{
    /// <summary>
    /// Reads one summary.
    /// </summary>
    /// <param name="kind">The kind of period.</param>
    /// <param name="period">The period identifier.</param>
    /// <param name="cancellation">Cancels the read.</param>
    /// <returns>The summary, or <see langword="null" /> if the period has none yet.</returns>
    Task<StoredSummary?> Get(SummaryKind kind, string period, CancellationToken cancellation);

    /// <summary>
    /// Lists the summaries of a kind, or all of them.
    /// </summary>
    /// <param name="kind">The kind, or <see langword="null" /> for every kind.</param>
    /// <param name="cancellation">Cancels the read.</param>
    /// <returns>The summaries, newest period first.</returns>
    Task<IReadOnlyList<Summary>> List(SummaryKind? kind, CancellationToken cancellation);

    /// <summary>
    /// Writes a summary, replacing the one its period has.
    /// </summary>
    /// <param name="summary">The summary to write.</param>
    /// <param name="cancellation">Cancels the write.</param>
    Task Save(StoredSummary summary, CancellationToken cancellation);

    /// <summary>
    /// Reads the revision through which every summary has taken in every entry.
    /// </summary>
    /// <param name="cancellation">Cancels the read.</param>
    /// <returns>The revision, or zero before the first complete run.</returns>
    Task<long> GetSummarizedRevision(CancellationToken cancellation);

    /// <summary>
    /// Records the revision through which every summary has taken in every entry.
    /// </summary>
    /// <param name="revision">The revision.</param>
    /// <param name="cancellation">Cancels the write.</param>
    Task SetSummarizedRevision(long revision, CancellationToken cancellation);

    /// <summary>
    /// Reads when summaries were last updated.
    /// </summary>
    /// <param name="cancellation">Cancels the read.</param>
    /// <returns>The time, or <see langword="null" /> if they never were.</returns>
    Task<DateTimeOffset?> GetLastRun(CancellationToken cancellation);

    /// <summary>
    /// Records when summaries were last updated.
    /// </summary>
    /// <param name="at">The time.</param>
    /// <param name="cancellation">Cancels the write.</param>
    Task SetLastRun(DateTimeOffset at, CancellationToken cancellation);
}

/// <inheritdoc />
internal sealed class SummaryStore(IDatabase database, IMetaStore meta) : ISummaryStore
{
    private readonly string dateFormat = "yyyy-MM-dd";

    private readonly string select = "SELECT kind, period, start, finish, text, entry_count, updated_at, through_revision FROM summaries";

    /// <inheritdoc />
    public async Task<StoredSummary?> Get(SummaryKind kind, string period, CancellationToken cancellation)
    {
        await using SqliteConnection connection = await database.Open(cancellation);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"{select} WHERE kind = $kind AND period = $period";
        command.Parameters.AddWithValue("$kind", kind.ToString());
        command.Parameters.AddWithValue("$period", period);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellation);
        return await reader.ReadAsync(cancellation) ? Read(reader) : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Summary>> List(SummaryKind? kind, CancellationToken cancellation)
    {
        await using SqliteConnection connection = await database.Open(cancellation);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"{select} WHERE ($kind IS NULL OR kind = $kind) ORDER BY start DESC, kind";
        command.Parameters.AddWithValue("$kind", kind is { } value ? value.ToString() : DBNull.Value);
        List<Summary> summaries = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            summaries.Add(Read(reader).Summary);
        }

        return summaries;
    }

    /// <inheritdoc />
    public async Task Save(StoredSummary summary, CancellationToken cancellation)
    {
        await using SqliteConnection connection = await database.Open(cancellation);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO summaries (kind, period, start, finish, text, entry_count, through_revision, updated_at)
            VALUES ($kind, $period, $start, $finish, $text, $count, $through, $at)
            ON CONFLICT (kind, period) DO UPDATE SET start = excluded.start, finish = excluded.finish, text = excluded.text,
                entry_count = excluded.entry_count, through_revision = excluded.through_revision, updated_at = excluded.updated_at
            """;
        command.Parameters.AddWithValue("$kind", summary.Summary.Kind.ToString());
        command.Parameters.AddWithValue("$period", summary.Summary.Period);
        command.Parameters.AddWithValue("$start", summary.Summary.Start.ToString(dateFormat, CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$finish", summary.Summary.End.ToString(dateFormat, CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$text", summary.Summary.Text);
        command.Parameters.AddWithValue("$count", summary.Summary.EntryCount);
        command.Parameters.AddWithValue("$through", summary.ThroughRevision);
        command.Parameters.AddWithValue("$at", summary.Summary.UpdatedAt.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellation);
    }

    /// <inheritdoc />
    public async Task<long> GetSummarizedRevision(CancellationToken cancellation) => long.TryParse(await meta.Get("summarized_revision", cancellation), CultureInfo.InvariantCulture, out long revision) ? revision : 0;

    /// <inheritdoc />
    public Task SetSummarizedRevision(long revision, CancellationToken cancellation) => meta.Set("summarized_revision", revision.ToString(CultureInfo.InvariantCulture), cancellation);

    /// <inheritdoc />
    public async Task<DateTimeOffset?> GetLastRun(CancellationToken cancellation) => DateTimeOffset.TryParse(await meta.Get("last_summarized", cancellation), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset at) ? at : null;

    /// <inheritdoc />
    public Task SetLastRun(DateTimeOffset at, CancellationToken cancellation) => meta.Set("last_summarized", at.ToString("O", CultureInfo.InvariantCulture), cancellation);

    private StoredSummary Read(SqliteDataReader reader) => new()
    {
        Summary = new Summary
        {
            Kind = Enum.Parse<SummaryKind>(reader.GetString(0)),
            Period = reader.GetString(1),
            Start = DateOnly.ParseExact(reader.GetString(2), dateFormat, CultureInfo.InvariantCulture),
            End = DateOnly.ParseExact(reader.GetString(3), dateFormat, CultureInfo.InvariantCulture),
            Text = reader.GetString(4),
            EntryCount = reader.GetInt32(5),
            UpdatedAt = DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
        },
        ThroughRevision = reader.GetInt64(7)
    };
}
