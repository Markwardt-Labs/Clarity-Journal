namespace Markwardt.ClarityJournal.Server;

/// <summary>
/// Helpers that turn entries into what a list of entries shows.
/// </summary>
internal static class EntryDigests
{
    extension(string text)
    {
        /// <summary>
        /// Shortens entry text to the start of it on a single line.
        /// </summary>
        /// <returns>The text with every run of whitespace turned into one space, cut to 160 characters with <c>...</c> added when it was longer.</returns>
        public string ToExcerpt()
        {
            string line = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return line.Length <= 160 ? line : line[..160].TrimEnd() + "...";
        }
    }
}
