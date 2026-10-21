namespace Markwardt.ClarityJournal.Server;

/// <summary>
/// Updates the week, month, year and lifetime summaries with Claude Code, reading only what was saved since each summary was last updated.
/// </summary>
internal interface ISummarizer
{
    /// <summary>
    /// Updates every summary that has entries it has not taken in yet. Does nothing, and asks Claude Code nothing, when no entry was saved since the last update.
    /// </summary>
    /// <param name="config">The server settings.</param>
    /// <param name="cancellation">Cancels the update.</param>
    /// <returns><see langword="true" /> if every summary is up to date afterward; <see langword="false" /> if Claude Code or the storage failed, in which case the failure was reported to the clients and a later run continues where this one stopped.</returns>
    Task<bool> Run(ServerConfig config, CancellationToken cancellation);
}

/// <inheritdoc />
internal sealed class Summarizer(IEntryStore entries, ISummaryStore summaries, IClaudeRunner claude, IPrompts prompts, IJournalExport export, IEventHub events, TimeProvider time, ILog log) : ISummarizer
{
    private readonly SummaryKind[] kinds = [SummaryKind.Week, SummaryKind.Month, SummaryKind.Year, SummaryKind.Lifetime];

    /// <inheritdoc />
    public async Task<bool> Run(ServerConfig config, CancellationToken cancellation)
    {
        try
        {
            long taken = await summaries.GetSummarizedRevision(cancellation);
            long latest = await entries.LatestRevision(cancellation);
            IReadOnlyList<RevisedEntry> pending = await entries.ListSince(taken, latest, cancellation);

            if (pending.Count == 0)
            {
                log.Write("No new or edited entries to summarize");
                await summaries.SetLastRun(time.GetUtcNow(), cancellation);
                return true;
            }

            log.Write($"Summarizing {pending.Count} new or edited entries");

            foreach (SummaryKind kind in kinds)
            {
                foreach (IGrouping<SummaryPeriod, RevisedEntry> group in pending.GroupBy(revised => kind.PeriodOf(revised.Entry.Date)))
                {
                    await Update(config, group.Key, group, cancellation);
                }
            }

            await summaries.SetSummarizedRevision(latest, cancellation);
            await summaries.SetLastRun(time.GetUtcNow(), cancellation);
            return true;
        }
        catch (Exception exception) when (exception is ClaudeException or SqliteException or IOException)
        {
            log.Write($"Summarizing failed: {exception.Message}");
            events.Publish(new RemoteEvent { Kind = RemoteEventKind.Failed, Error = $"Summaries could not be updated: {exception.Message}" });
            return false;
        }
    }

    private async Task Update(ServerConfig config, SummaryPeriod period, IEnumerable<RevisedEntry> pending, CancellationToken cancellation)
    {
        StoredSummary? stored = await summaries.Get(period.Kind, period.Id, cancellation);
        long through = stored?.ThroughRevision ?? 0;
        List<RevisedEntry> fresh = [.. pending.Where(revised => revised.Revision > through).OrderBy(revised => revised.Revision)];

        if (fresh.Count == 0)
        {
            return;
        }

        string? text = stored?.Summary.Text;
        DateOnly start = stored?.Summary.Start ?? fresh.Min(revised => revised.Entry.Date);
        DateOnly end = stored?.Summary.End ?? fresh.Max(revised => revised.Entry.Date);

        foreach (List<RevisedEntry> batch in Batch(fresh, config.SummaryBatchCharacters))
        {
            string prompt = prompts.Summary(period, text, [.. batch.Select(revised => revised.Entry).OrderBy(entry => entry.Date)]);
            text = await claude.Run(config, new ClaudeRequest { Prompt = prompt, SystemPrompt = prompts.SummarySystem, ReplacesSystemPrompt = true, Folder = export.Folder }, cancellation);

            if (period.Kind == SummaryKind.Lifetime)
            {
                start = new[] { start, batch.Min(revised => revised.Entry.Date) }.Min();
                end = new[] { end, batch.Max(revised => revised.Entry.Date) }.Max();
            }
            else
            {
                start = period.Start;
                end = period.End;
            }

            Summary summary = new()
            {
                Kind = period.Kind,
                Period = period.Id,
                Start = start,
                End = end,
                Text = text,
                EntryCount = period.Kind == SummaryKind.Lifetime ? await entries.Count(null, null, cancellation) : await entries.Count(start, end, cancellation),
                UpdatedAt = time.GetUtcNow()
            };

            await summaries.Save(new StoredSummary { Summary = summary, ThroughRevision = batch.Max(revised => revised.Revision) }, cancellation);
            await export.WriteSummary(summary, cancellation);
            events.Publish(new RemoteEvent { Kind = RemoteEventKind.SummariesChanged });
        }
    }

    private IEnumerable<List<RevisedEntry>> Batch(List<RevisedEntry> entries, int limit)
    {
        List<RevisedEntry> batch = [];
        int length = 0;

        foreach (RevisedEntry entry in entries)
        {
            if (batch.Count > 0 && length + entry.Entry.Text.Length > limit)
            {
                yield return batch;
                batch = [];
                length = 0;
            }

            batch.Add(entry);
            length += entry.Entry.Text.Length;
        }

        if (batch.Count > 0)
        {
            yield return batch;
        }
    }
}
