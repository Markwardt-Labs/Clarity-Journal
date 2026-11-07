namespace Markwardt.ClarityJournal;

/// <summary>
/// What a list of entries shows of one day: the AI generated title and summary, and the start of the text for the days that do not have them yet.
/// </summary>
public sealed record EntryDigest
{
    /// <summary>
    /// Gets the day of the entry.
    /// </summary>
    public required DateOnly Date { get; init; }

    /// <summary>
    /// Gets the short title the server generated, or <see langword="null" /> if the entry has none yet: the server titles a day only once it is over, and again after the text changed.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Gets the one or two sentence summary the server generated, or <see langword="null" /> if the entry has none yet.
    /// </summary>
    public string? Summary { get; init; }

    /// <summary>
    /// Gets the start of the entry text on a single line, which a list shows while <see cref="Title" /> and <see cref="Summary" /> are missing.
    /// </summary>
    public required string Excerpt { get; init; }
}
