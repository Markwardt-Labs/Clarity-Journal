namespace Markwardt.ClarityJournal.Tests.Unit;

public sealed class CertificateTrustTests
{
    private readonly CertificateTrust trust = new();

    [Theory]
    [InlineData(null, "AAAA")]
    [InlineData("", "AAAA")]
    [InlineData("AAAA", "AAAA")]
    [InlineData("aaaa", "AAAA")]
    public void Accept_SelfSignedCertificate_IsTrustedOnFirstUseThenMustStayTheSame(string? pinned, string presented)
    {
        Assert.True(trust.Accept(pinned, presented, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    [Fact]
    public void Accept_SelfSignedCertificateThatDiffersFromThePin_IsRefused()
    {
        Assert.False(trust.Accept("AAAA", "BBBB", SslPolicyErrors.RemoteCertificateChainErrors));
        Assert.False(trust.Accept("AAAA", "BBBB", SslPolicyErrors.RemoteCertificateNameMismatch));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("AAAA")]
    public void Accept_CertificateAPublicAuthorityVouchesFor_IsAlwaysTrustedEvenWhenItIsRenewed(string? pinned)
    {
        Assert.True(trust.Accept(pinned, "RENEWED-CLOUDFLARE-CERTIFICATE", SslPolicyErrors.None));
    }

    [Fact]
    public void Pin_RemembersOnlyCertificatesNoPublicAuthorityVouchesFor()
    {
        Assert.Equal("AAAA", trust.Pin("AAAA", SslPolicyErrors.RemoteCertificateChainErrors));
        Assert.Equal(string.Empty, trust.Pin("AAAA", SslPolicyErrors.None));
    }
}
