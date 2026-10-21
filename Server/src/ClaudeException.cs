namespace Markwardt.ClarityJournal.Server;

/// <summary>
/// Claude Code could not be started, failed or did not answer in time.
/// </summary>
/// <param name="message">What went wrong.</param>
internal sealed class ClaudeException(string message) : Exception(message);
