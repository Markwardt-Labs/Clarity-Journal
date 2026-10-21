namespace Markwardt.ClarityJournal.Server;

/// <summary>
/// The instructions the server gives Claude Code.
/// </summary>
internal interface IPrompts
{
    /// <summary>
    /// Gets the system prompt of a summary run.
    /// </summary>
    string SummarySystem { get; }

    /// <summary>
    /// Gets the system prompt of a chat run, which tells Claude Code how the journal files are laid out.
    /// </summary>
    string ChatSystem { get; }

    /// <summary>
    /// Builds the prompt that updates the summary of a period.
    /// </summary>
    /// <param name="period">The period to summarize.</param>
    /// <param name="previous">The previous summary of the period, or <see langword="null" /> if it has none yet.</param>
    /// <param name="entries">The entries added or edited since the previous summary, oldest day first.</param>
    /// <returns>The prompt.</returns>
    string Summary(SummaryPeriod period, string? previous, IReadOnlyList<JournalEntry> entries);

    /// <summary>
    /// Names a period the way people say it.
    /// </summary>
    /// <param name="period">The period.</param>
    /// <returns>A short label such as <c>the week of 2026-10-12 to 2026-10-18</c>.</returns>
    string Label(SummaryPeriod period);
}

/// <inheritdoc />
internal sealed class Prompts : IPrompts
{
    /// <inheritdoc />
    public string SummarySystem { get; } = "You write and maintain summaries of one person's private journal. Write in the second person (\"you\"), warm but factual, and never invent details the journal does not contain. Reply with the complete updated summary as markdown and nothing else: no preamble, no closing remarks, and no code fence around the whole reply. Prefer short paragraphs, with a short bullet list where it helps. Cover the themes, notable events, moods, people, goals and progress, and how things changed over the period. Stay concise: about 250 words for a week, 400 for a month, 600 for a year and 800 for a lifetime.";

    /// <inheritdoc />
    public string ChatSystem { get; } = "You answer questions about the user's private journal. The journal is in the current folder: entries/<year>/<yyyy-MM-dd>.md holds the entry of each day that has one, summaries/lifetime.md summarizes everything, summaries/years/<yyyy>.md, summaries/months/<yyyy-MM>.md and summaries/weeks/<monday as yyyy-MM-dd>.md summarize a year, a month and a week. Start from the summaries for broad questions and read or search the entries for specifics. Ground every answer in these files and name the dates it comes from; when the journal does not say, say so instead of guessing. You can only read the journal and never change it. Answer in markdown, conversational and kind, and keep it short unless asked for detail.";

    /// <inheritdoc />
    public string Summary(SummaryPeriod period, string? previous, IReadOnlyList<JournalEntry> entries)
    {
        StringBuilder prompt = new();
        prompt.AppendLine(CultureInfo.InvariantCulture, $"Update the journal summary for {Label(period)}.");
        prompt.AppendLine();
        prompt.AppendLine("Previous summary (it already covers the earlier entries: keep what still matters, fold in the new entries, and condense older material instead of letting the summary grow):");
        prompt.AppendLine(string.IsNullOrWhiteSpace(previous) ? "(none yet, this is the first summary)" : previous);
        prompt.AppendLine();
        prompt.AppendLine("Entries added or edited since that summary (an entry here replaces any earlier version of the same day):");

        foreach (JournalEntry entry in entries)
        {
            prompt.AppendLine();
            prompt.AppendLine(CultureInfo.InvariantCulture, $"### {entry.Date:yyyy-MM-dd} ({entry.Date.DayOfWeek})");
            prompt.AppendLine(entry.Text);
        }

        return prompt.ToString();
    }

    /// <inheritdoc />
    public string Label(SummaryPeriod period) => period.Kind switch
    {
        SummaryKind.Week => $"the week of {period.Start:yyyy-MM-dd} to {period.End:yyyy-MM-dd}",
        SummaryKind.Month => $"the month {period.Id}",
        SummaryKind.Year => $"the year {period.Id}",
        _ => "the person's whole lifetime"
    };
}
