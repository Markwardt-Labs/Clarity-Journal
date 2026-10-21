namespace Markwardt.ClarityJournal.Tests.Unit;

public sealed class ConnectViewModelTests
{
    private readonly Mock<IServerConnection.IFactory> factory = new();

    private readonly Mock<IServerConnection> connection = new();

    private readonly List<(IServerConnection Connection, ConnectionSettings Settings)> connected = [];

    public ConnectViewModelTests() => connection.SetupGet(target => target.Fingerprint).Returns("ABCD");

    private ConnectViewModel Create(ConnectionSettings? initial = null)
    {
        ConnectViewModel model = new(factory.Object, initial, "Clarity Journal 1.0.0");
        model.Connected += (opened, settings) => connected.Add((opened, settings));
        return model;
    }

    [Fact]
    public void New_PrefillsTheDefaultPortOrTheSavedServer()
    {
        Assert.Equal("52681", Create().Port);

        ConnectViewModel saved = Create(new ConnectionSettings { Host = "10.0.0.5", Port = 4000, AccessKey = "key" });
        Assert.Equal(("10.0.0.5", "4000", "key"), (saved.Host, saved.Port, saved.AccessKey));
    }

    [Theory]
    [InlineData("", "52681", "key", "Enter the server host.")]
    [InlineData("host", "abc", "key", "The port must be a number from 1 to 65535.")]
    [InlineData("host", "0", "key", "The port must be a number from 1 to 65535.")]
    [InlineData("host", "70000", "key", "The port must be a number from 1 to 65535.")]
    [InlineData("host", "52681", "", "Enter the access key.")]
    public void Connect_InvalidInput_ShowsAMessageWithoutConnecting(string host, string port, string key, string message)
    {
        ConnectViewModel model = Create();
        (model.Host, model.Port, model.AccessKey) = (host, port, key);

        model.ConnectCommand.Execute(null);

        Assert.Equal(message, model.Error);
        factory.Verify(target => target.Connect(It.IsAny<ConnectionSettings>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void Connect_Success_RaisesConnectedWithTheServersFingerprint()
    {
        factory.Setup(target => target.Connect(It.IsAny<ConnectionSettings>(), It.IsAny<CancellationToken>())).ReturnsAsync(connection.Object);
        ConnectViewModel model = Create();
        (model.Host, model.AccessKey) = (" 10.0.0.5 ", "key");

        model.ConnectCommand.Execute(null);

        (IServerConnection opened, ConnectionSettings settings) = Assert.Single(connected);
        Assert.Same(connection.Object, opened);
        Assert.Equal(("10.0.0.5", 52681, "key", "ABCD"), (settings.Host, settings.Port, settings.AccessKey, settings.Fingerprint));
        Assert.False(model.HasError);
    }

    [Fact]
    public void Connect_Failure_ShowsTheMessage()
    {
        factory.Setup(target => target.Connect(It.IsAny<ConnectionSettings>(), It.IsAny<CancellationToken>())).ThrowsAsync(new UnauthorizedAccessException("The server rejected the access key."));
        ConnectViewModel model = Create();
        (model.Host, model.AccessKey) = ("host", "wrong");

        model.ConnectCommand.Execute(null);

        Assert.Equal("The server rejected the access key.", model.Error);
        Assert.Empty(connected);
        Assert.False(model.CertificateChanged);
    }

    [Fact]
    public void Connect_SameServerAsSaved_StillRequiresTheSavedCertificate()
    {
        ConnectionSettings saved = new() { Host = "host", Port = 52681, AccessKey = "key", Fingerprint = "OLD" };
        factory.Setup(target => target.Connect(It.IsAny<ConnectionSettings>(), It.IsAny<CancellationToken>())).ReturnsAsync(connection.Object);

        Create(saved).ConnectCommand.Execute(null);

        factory.Verify(target => target.Connect(It.Is<ConnectionSettings>(settings => settings.Fingerprint == "OLD"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Connect_DifferentServerThanSaved_DoesNotCarryTheOldCertificateOver()
    {
        ConnectionSettings saved = new() { Host = "host", Port = 52681, AccessKey = "key", Fingerprint = "OLD" };
        factory.Setup(target => target.Connect(It.IsAny<ConnectionSettings>(), It.IsAny<CancellationToken>())).ReturnsAsync(connection.Object);
        ConnectViewModel model = Create(saved);
        model.Host = "other-host";

        model.ConnectCommand.Execute(null);

        factory.Verify(target => target.Connect(It.Is<ConnectionSettings>(settings => settings.Fingerprint == null), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Connect_CertificateChanged_OffersToTrustTheNewOneAndThenDoes()
    {
        ConnectionSettings saved = new() { Host = "host", Port = 52681, AccessKey = "key", Fingerprint = "OLD" };
        factory.Setup(target => target.Connect(It.Is<ConnectionSettings>(settings => settings.Fingerprint == "OLD"), It.IsAny<CancellationToken>())).ThrowsAsync(new CertificateChangedException("changed"));
        factory.Setup(target => target.Connect(It.Is<ConnectionSettings>(settings => settings.Fingerprint == null), It.IsAny<CancellationToken>())).ReturnsAsync(connection.Object);
        ConnectViewModel model = Create(saved);

        model.ConnectCommand.Execute(null);
        Assert.True(model.CertificateChanged);
        Assert.Equal("changed", model.Error);

        model.TrustCommand.Execute(null);
        Assert.False(model.CertificateChanged);
        Assert.Equal("ABCD", Assert.Single(connected).Settings.Fingerprint);
    }

    [Fact]
    public void Fail_ShowsAMessageFromOutside()
    {
        ConnectViewModel model = Create();

        model.Fail("could not reconnect", true);

        Assert.Equal("could not reconnect", model.Error);
        Assert.True(model.CertificateChanged);
    }
}
