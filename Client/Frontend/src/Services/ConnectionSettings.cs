namespace Markwardt.ClarityJournal.Client;

/// <summary>
/// Where and how the app reaches the server.
/// </summary>
internal sealed record ConnectionSettings
{
    /// <summary>
    /// Gets the server's host name or address.
    /// </summary>
    public required string Host { get; init; }

    /// <summary>
    /// Gets the server's TCP port.
    /// </summary>
    public required int Port { get; init; }

    /// <summary>
    /// Gets the access key the server requires.
    /// </summary>
    public required string AccessKey { get; init; }

    /// <summary>
    /// Gets the SHA-256 fingerprint of the server's certificate as first seen, which every later connection must present again; <see langword="null" /> before the first connection.
    /// </summary>
    public string? Fingerprint { get; init; }
}
