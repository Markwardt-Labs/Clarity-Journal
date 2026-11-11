namespace Markwardt.ClarityJournal.Client;

/// <summary>
/// The server presented a different certificate than the one trusted when the app first connected to it.
/// </summary>
/// <param name="message">What the user should be told.</param>
internal sealed class CertificateChangedException(string message) : IOException(message);
