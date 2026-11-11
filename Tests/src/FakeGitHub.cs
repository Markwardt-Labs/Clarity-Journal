namespace Markwardt.ClarityJournal.Tests;

internal sealed class FakeGitHub(string version, IReadOnlyDictionary<string, byte[]> files) : HttpMessageHandler
{
    private readonly object gate = new();

    private readonly List<string> requested = [];

    public HttpStatusCode LatestStatus { get; set; } = HttpStatusCode.OK;

    public bool CorruptDownloads { get; set; }

    public List<string> Requested
    {
        get
        {
            lock (gate)
            {
                return [.. requested];
            }
        }
    }

    public ReleaseFeed Feed(string repository = "owner/repo")
        => new(new HttpClient(this), repository);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
    {
        string address = request.RequestUri!.ToString();

        lock (gate)
        {
            requested.Add(address);
        }

        if (address.EndsWith("/releases/latest", StringComparison.Ordinal))
        {
            return Task.FromResult(LatestStatus != HttpStatusCode.OK ? new HttpResponseMessage(LatestStatus) : new HttpResponseMessage { Content = new StringContent(Json()) });
        }

        string name = address[(address.LastIndexOf('/') + 1)..];
        return Task.FromResult(files.TryGetValue(name, out byte[]? content) ? new HttpResponseMessage { Content = new ByteArrayContent(CorruptDownloads ? [.. content.Select(value => (byte)(value ^ 1))] : content) } : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private string Json()
        => JsonSerializer.Serialize(new
        {
            tag_name = version,
            draft = false,
            prerelease = false,
            assets = files.Select(file => new { name = file.Key, browser_download_url = $"https://example.invalid/download/{file.Key}", size = file.Value.Length, digest = "sha256:" + Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant() })
        });
}
