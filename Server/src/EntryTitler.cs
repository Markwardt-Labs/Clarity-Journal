namespace Markwardt.ClarityJournal.Server;

/// <summary>
/// Has Claude Code write a short title and a one or two sentence summary for each journal entry of a finished day that does not have them yet.
/// </summary>
internal interface IEntryTitler
{
    /// <summary>
    /// Generates the missing titles and summaries of the days before today and announces each one. Today's entry is left alone because it is still being written, and an entry whose text changed while its title was being made is left for the next run.
    /// </summary>
    /// <param name="config">The server settings.</param>
    /// <param name="cancellation">Cancels the run.</param>
    /// <returns><see langword="true" /> if every entry was dealt with; <see langword="false" /> if Claude Code or the storage failed, in which case the run stopped and a later run continues with what is still missing. An answer that cannot be read as a title skips that entry only.</returns>
    Task<bool> Run(ServerConfig config, CancellationToken cancellation = default);
}

/// <inheritdoc />
internal sealed class EntryTitler(IEntryStore entries, IClaudeRunner claude, IPrompts prompts, IJournalExport export, IEventHub events, TimeProvider time, ILog log) : IEntryTitler
{
    private readonly int maxTitle = 80;

    private readonly int maxSummary = 300;

    /// <inheritdoc />
    public async Task<bool> Run(ServerConfig config, CancellationToken cancellation = default)
    {
        try
        {
            IReadOnlyList<JournalEntry> pending = await entries.ListUntitled(DateOnly.FromDateTime(time.GetLocalNow().DateTime), cancellation);

            if (pending.Count == 0)
            {
                return true;
            }

            log.Write($"Writing titles for {pending.Count} entries");

            foreach (JournalEntry entry in pending)
            {
                string reply = await claude.Run(config, new ClaudeRequest { Prompt = prompts.Title(entry), SystemPrompt = prompts.TitleSystem, ReplacesSystemPrompt = true, Folder = export.Folder }, cancellation);

                if (Parse(reply) is not { } digest)
                {
                    log.Write($"The title for {entry.Date:yyyy-MM-dd} could not be read from the answer, so it is tried again next time");
                    continue;
                }

                if (await entries.SetDigest(entry.Date, entry.Text, digest.Title, digest.Summary, cancellation))
                {
                    events.Publish(new RemoteEvent { Kind = RemoteEventKind.EntryChanged, Date = entry.Date, Digest = await entries.GetDigest(entry.Date, cancellation) });
                }
            }

            return true;
        }
        catch (Exception exception) when (exception is ClaudeException or SqliteException or IOException)
        {
            log.Write($"Writing titles failed: {exception.Message}");
            return false;
        }
    }

    private (string Title, string Summary)? Parse(string reply)
    {
        int start = reply.IndexOf('{', StringComparison.Ordinal);
        int end = reply.LastIndexOf('}');

        if (start < 0 || end < start)
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(reply[start..(end + 1)]);
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("title", out JsonElement title) || !root.TryGetProperty("summary", out JsonElement summary) || title.ValueKind != JsonValueKind.String || summary.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            string cleanTitle = Limit(Clean(title.GetString()!).TrimEnd('.'), maxTitle);
            string cleanSummary = Limit(Clean(summary.GetString()!), maxSummary);
            return cleanTitle.Length == 0 || cleanSummary.Length == 0 ? null : (cleanTitle, cleanSummary);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private string Clean(string text)
        => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim('"', '\'', '“', '”').Trim();

    private string Limit(string text, int length) => text.Length <= length ? text : text[..length].TrimEnd() + "...";
}
