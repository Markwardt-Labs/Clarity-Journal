namespace Markwardt.ClarityJournal;

/// <inheritdoc />
internal sealed class MessageChannel(Stream stream) : IMessageChannel
{
    private readonly StreamReader reader = new(stream, new UTF8Encoding(false), false, leaveOpen: true);

    private readonly JsonSerializerOptions options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    /// <inheritdoc />
    public async Task SendLine(string line, CancellationToken cancellation = default)
    {
        await stream.WriteAsync(Encoding.UTF8.GetBytes(line + "\n"), cancellation);
        await stream.FlushAsync(cancellation);
    }

    /// <inheritdoc />
    public async Task<string?> ReceiveLine(CancellationToken cancellation = default)
        => await reader.ReadLineAsync(cancellation);

    /// <inheritdoc />
    public Task Send<T>(T message, CancellationToken cancellation = default)
        => SendLine(JsonSerializer.Serialize(message, options), cancellation);

    /// <inheritdoc />
    public async Task<T?> Receive<T>(CancellationToken cancellation = default)
        where T : class
    {
        string? line = await ReceiveLine(cancellation);

        if (line is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(line, options);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Received an invalid message: {exception.Message}", exception);
        }
    }
}
