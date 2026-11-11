namespace Markwardt.ClarityJournal;

/// <summary>
/// Newline-delimited text and JSON messages over a connected stream.
/// </summary>
public interface IMessageChannel
{
    /// <summary>
    /// Writes one raw text line.
    /// </summary>
    /// <param name="line">The line, without a terminator.</param>
    /// <param name="cancellation">Cancels the write.</param>
    Task SendLine(string line, CancellationToken cancellation = default);

    /// <summary>
    /// Reads one raw text line.
    /// </summary>
    /// <param name="cancellation">Cancels the read.</param>
    /// <returns>The line without its terminator, or <see langword="null" /> when the peer closed the stream.</returns>
    Task<string?> ReceiveLine(CancellationToken cancellation = default);

    /// <summary>
    /// Writes a message as one JSON line.
    /// </summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="message">The message to write.</param>
    /// <param name="cancellation">Cancels the write.</param>
    Task Send<T>(T message, CancellationToken cancellation = default);

    /// <summary>
    /// Reads one JSON line as a message.
    /// </summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="cancellation">Cancels the read.</param>
    /// <returns>The message, or <see langword="null" /> when the peer closed the stream.</returns>
    /// <exception cref="InvalidDataException">The line is not a valid message.</exception>
    Task<T?> Receive<T>(CancellationToken cancellation = default)
        where T : class;

    /// <summary>
    /// Creates <see cref="IMessageChannel" /> instances.
    /// </summary>
    public interface IFactory
    {
        /// <summary>
        /// Creates a channel over the given stream.
        /// </summary>
        /// <param name="stream">The connected stream; it stays open when the channel is dropped.</param>
        /// <returns>The channel.</returns>
        IMessageChannel Create(Stream stream);
    }

    /// <inheritdoc />
    public sealed class Factory : IFactory
    {
        /// <inheritdoc />
        public IMessageChannel Create(Stream stream)
            => new MessageChannel(stream);
    }
}
