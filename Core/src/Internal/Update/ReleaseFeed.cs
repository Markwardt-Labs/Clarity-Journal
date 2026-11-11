namespace Markwardt.ClarityJournal;

/// <inheritdoc />
internal sealed class ReleaseFeed(HttpClient http, string repository) : IReleaseFeed
{
    private readonly TimeSpan lookupTimeout = TimeSpan.FromSeconds(20);

    /// <inheritdoc />
    public async Task<ReleaseInfo?> Latest(CancellationToken cancellation = default)
    {
        using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        limit.CancelAfter(lookupTimeout);

        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, $"https://api.github.com/repos/{repository}/releases/latest");
            request.Headers.UserAgent.ParseAdd("ClarityJournal-Updater");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, limit.Token);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new IOException($"GitHub answered {(int)response.StatusCode} when looking for the newest release.");
            }

            using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(limit.Token), cancellationToken: limit.Token);
            return Read(document.RootElement);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            throw new IOException("GitHub did not answer in time.");
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            throw new IOException($"The newest release could not be read from GitHub: {exception.Message}", exception);
        }
    }

    /// <inheritdoc />
    public async Task Download(ReleaseAsset asset, string path, IProgress<double>? progress, CancellationToken cancellation = default)
    {
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, asset.Url);
            request.Headers.UserAgent.ParseAdd("ClarityJournal-Updater");
            using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);

            if (!response.IsSuccessStatusCode)
            {
                throw new IOException($"GitHub answered {(int)response.StatusCode} when downloading {asset.Name}.");
            }

            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long total = 0;
            byte[] buffer = new byte[81920];

            await using (Stream source = await response.Content.ReadAsStreamAsync(cancellation))
            await using (FileStream target = new(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                int count;

                while ((count = await source.ReadAsync(buffer, cancellation)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, count), cancellation);
                    hash.AppendData(buffer, 0, count);
                    total += count;
                    progress?.Report(asset.Size > 0 ? Math.Min(1, (double)total / asset.Size) : 0);
                }
            }

            if (total != asset.Size)
            {
                throw new IOException($"{asset.Name} was downloaded incompletely ({total} of {asset.Size} bytes).");
            }

            if (asset.Sha256 is { } expected && !string.Equals(Convert.ToHexString(hash.GetHashAndReset()), expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException($"{asset.Name} does not match the checksum of the release.");
            }
        }
        catch (Exception exception)
        {
            Discard(path);

            if (exception is HttpRequestException)
            {
                throw new IOException($"{asset.Name} could not be downloaded: {exception.Message}", exception);
            }

            throw;
        }
    }

    private ReleaseInfo? Read(JsonElement release)
    {
        if (release.ValueKind != JsonValueKind.Object || !release.TryGetProperty("tag_name", out JsonElement tag) || !AppVersion.TryParse(tag.GetString(), out AppVersion? version))
        {
            return null;
        }

        List<ReleaseAsset> assets = [];

        if (release.TryGetProperty("assets", out JsonElement listed) && listed.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in listed.EnumerateArray())
            {
                string? name = item.TryGetProperty("name", out JsonElement nameValue) ? nameValue.GetString() : null;
                string? url = item.TryGetProperty("browser_download_url", out JsonElement urlValue) ? urlValue.GetString() : null;
                string? digest = item.TryGetProperty("digest", out JsonElement digestValue) && digestValue.ValueKind == JsonValueKind.String ? digestValue.GetString() : null;

                if (name is null || url is null || !item.TryGetProperty("size", out JsonElement size) || !size.TryGetInt64(out long bytes))
                {
                    continue;
                }

                assets.Add(new ReleaseAsset { Name = name, Url = url, Size = bytes, Sha256 = digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest["sha256:".Length..] : null });
            }
        }

        return new ReleaseInfo { Version = version, Assets = assets };
    }

    private void Discard(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
