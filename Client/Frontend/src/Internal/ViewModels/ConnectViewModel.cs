namespace Markwardt.ClarityJournal.Client;

/// <summary>
/// The first screen: asks for the server's host, port and access key.
/// </summary>
internal sealed class ConnectViewModel : ViewModelBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ConnectViewModel" /> class.
    /// </summary>
    /// <param name="factory">Opens the connection.</param>
    /// <param name="initial">The settings to prefill, if any; its certificate fingerprint is kept for as long as the host and port stay the same.</param>
    /// <param name="version">The product name and version to show.</param>
    public ConnectViewModel(IServerConnection.IFactory factory, ConnectionSettings? initial, string version)
    {
        this.factory = factory;
        this.initial = initial;
        Version = version;
        host = initial?.Host ?? string.Empty;
        port = initial?.Port.ToString(CultureInfo.InvariantCulture) ?? "52681";
        accessKey = initial?.AccessKey ?? string.Empty;
        ConnectCommand = new AsyncCommand(_ => Connect(false));
        TrustCommand = new AsyncCommand(_ => Connect(true));
    }

    private readonly IServerConnection.IFactory factory;

    private readonly ConnectionSettings? initial;

    private string host;

    private string port;

    private string accessKey;

    private bool certificateChanged;

    /// <summary>
    /// Raised with the open connection and the settings that produced it.
    /// </summary>
    public event Action<IServerConnection, ConnectionSettings>? Connected;

    /// <summary>
    /// Gets the product name and version.
    /// </summary>
    public string Version { get; }

    /// <summary>
    /// Gets or sets the server host name or address.
    /// </summary>
    public string Host
    {
        get => host;
        set => Set(ref host, value);
    }

    /// <summary>
    /// Gets or sets the server TCP port as typed.
    /// </summary>
    public string Port
    {
        get => port;
        set => Set(ref port, value);
    }

    /// <summary>
    /// Gets or sets the access key.
    /// </summary>
    public string AccessKey
    {
        get => accessKey;
        set => Set(ref accessKey, value);
    }

    /// <summary>
    /// Gets a value indicating whether the last connect failed because the server's certificate is not the one trusted before, which <see cref="TrustCommand" /> can accept.
    /// </summary>
    public bool CertificateChanged
    {
        get => certificateChanged;
        private set => Set(ref certificateChanged, value);
    }

    /// <summary>
    /// Gets the command that connects.
    /// </summary>
    public AsyncCommand ConnectCommand { get; }

    /// <summary>
    /// Gets the command that connects and trusts whatever certificate the server presents.
    /// </summary>
    public AsyncCommand TrustCommand { get; }

    /// <summary>
    /// Shows a failure, such as a failed automatic reconnect, on this screen.
    /// </summary>
    /// <param name="message">The failure message.</param>
    /// <param name="certificateChanged">Whether the failure is a changed server certificate.</param>
    public void Fail(string message, bool certificateChanged = false)
    {
        Error = message;
        CertificateChanged = certificateChanged;
    }

    private async Task Connect(bool trustNewCertificate)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            Error = "Enter the server host.";
            return;
        }

        if (!int.TryParse(port, CultureInfo.InvariantCulture, out int number) || number is < 1 or > 65535)
        {
            Error = "The port must be a number from 1 to 65535.";
            return;
        }

        if (string.IsNullOrEmpty(accessKey))
        {
            Error = "Enter the access key.";
            return;
        }

        bool sameServer = initial is not null && string.Equals(initial.Host, host.Trim(), StringComparison.OrdinalIgnoreCase) && initial.Port == number;
        ConnectionSettings settings = new() { Host = host.Trim(), Port = number, AccessKey = accessKey, Fingerprint = sameServer && !trustNewCertificate ? initial!.Fingerprint : null };
        IServerConnection? connection = null;
        CertificateChanged = false;

        bool connected = await Attempt(async () =>
        {
            try
            {
                connection = await factory.Connect(settings, CancellationToken.None);
            }
            catch (CertificateChangedException)
            {
                CertificateChanged = true;
                throw;
            }
        });

        if (connected && connection is not null)
        {
            Connected?.Invoke(connection, settings with { Fingerprint = connection.Fingerprint });
        }
    }
}
