namespace Markwardt.ClarityJournal.Server;

/// <summary>
/// Small named values the server keeps next to the journal, such as when summaries last ran.
/// </summary>
internal interface IMetaStore
{
    /// <summary>
    /// Reads a value.
    /// </summary>
    /// <param name="key">The name of the value.</param>
    /// <param name="cancellation">Cancels the read.</param>
    /// <returns>The value, or <see langword="null" /> if it was never set.</returns>
    Task<string?> Get(string key, CancellationToken cancellation);

    /// <summary>
    /// Writes a value, replacing the one the key already has.
    /// </summary>
    /// <param name="key">The name of the value.</param>
    /// <param name="value">The value.</param>
    /// <param name="cancellation">Cancels the write.</param>
    Task Set(string key, string value, CancellationToken cancellation);

    /// <summary>
    /// Removes a value.
    /// </summary>
    /// <param name="key">The name of the value.</param>
    /// <param name="cancellation">Cancels the write.</param>
    Task Remove(string key, CancellationToken cancellation);
}

/// <inheritdoc />
internal sealed class MetaStore(IDatabase database) : IMetaStore
{
    /// <inheritdoc />
    public async Task<string?> Get(string key, CancellationToken cancellation)
    {
        await using SqliteConnection connection = await database.Open(cancellation);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM meta WHERE key = $key";
        command.Parameters.AddWithValue("$key", key);
        return await command.ExecuteScalarAsync(cancellation) as string;
    }

    /// <inheritdoc />
    public async Task Set(string key, string value, CancellationToken cancellation)
    {
        await using SqliteConnection connection = await database.Open(cancellation);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "INSERT INTO meta (key, value) VALUES ($key, $value) ON CONFLICT (key) DO UPDATE SET value = excluded.value";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        await command.ExecuteNonQueryAsync(cancellation);
    }

    /// <inheritdoc />
    public async Task Remove(string key, CancellationToken cancellation)
    {
        await using SqliteConnection connection = await database.Open(cancellation);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM meta WHERE key = $key";
        command.Parameters.AddWithValue("$key", key);
        await command.ExecuteNonQueryAsync(cancellation);
    }
}
