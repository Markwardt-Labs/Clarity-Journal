namespace Markwardt.ClarityJournal.Client;

/// <summary>
/// Decides whether to trust the certificate a server presents. A certificate that a public authority vouches for, such as the one Cloudflare presents for a hostname behind it, is trusted like any website's and is never pinned, since such certificates are renewed and change often. A certificate nobody vouches for, such as a server's own self-signed one, is trusted on first use and then must stay the same.
/// </summary>
internal interface ICertificateTrust
{
    /// <summary>
    /// Decides whether a presented certificate is acceptable.
    /// </summary>
    /// <param name="pinned">The fingerprint pinned on an earlier connection, or <see langword="null" /> or empty if none was.</param>
    /// <param name="presented">The SHA-256 fingerprint of the presented certificate.</param>
    /// <param name="errors">What the platform found wrong with the certificate; none means a public authority vouches for it.</param>
    /// <returns><see langword="true" /> to continue connecting.</returns>
    bool Accept(string? pinned, string presented, SslPolicyErrors errors);

    /// <summary>
    /// Gives the fingerprint to pin for later connections.
    /// </summary>
    /// <param name="presented">The SHA-256 fingerprint of the presented certificate.</param>
    /// <param name="errors">What the platform found wrong with the certificate.</param>
    /// <returns>The fingerprint to pin, or an empty string for a certificate a public authority vouches for, which is not pinned.</returns>
    string Pin(string presented, SslPolicyErrors errors);
}

/// <inheritdoc />
internal sealed class CertificateTrust : ICertificateTrust
{
    /// <inheritdoc />
    public bool Accept(string? pinned, string presented, SslPolicyErrors errors) => errors == SslPolicyErrors.None || string.IsNullOrEmpty(pinned) || string.Equals(pinned, presented, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public string Pin(string presented, SslPolicyErrors errors) => errors == SslPolicyErrors.None ? string.Empty : presented;
}
