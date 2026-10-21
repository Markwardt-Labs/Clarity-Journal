namespace Markwardt.ClarityJournal.Server;

/// <summary>
/// The SQLite database that holds the journal.
/// </summary>
internal interface IDatabase
{
    /// <summary>
    /// Creates the database file and its tables when they do not exist yet.
    /// </summary>
    /// <param name="folder">The folder that holds the database file; created when missing.</param>
    /// <param name="cancellation">Cancels the setup.</param>
    Task Initialize(string folder, CancellationToken cancellation);

    /// <summary>
    /// Opens a connection to the database.
    /// </summary>
    /// <param name="cancellation">Cancels the open.</param>
    /// <returns>The open connection, which the caller disposes.</returns>
    /// <exception cref="InvalidOperationException"><see cref="Initialize" /> has not run.</exception>
    Task<SqliteConnection> Open(CancellationToken cancellation);
}

/// <inheritdoc />
internal sealed class Database : IDatabase
{
    private readonly string schema = """
        CREATE TABLE IF NOT EXISTS entries (
            date TEXT NOT NULL PRIMARY KEY,
            text TEXT NOT NULL,
            revision INTEGER NOT NULL,
            updated_at TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS entries_revision ON entries (revision);
        CREATE TABLE IF NOT EXISTS summaries (
            kind TEXT NOT NULL,
            period TEXT NOT NULL,
            start TEXT NOT NULL,
            finish TEXT NOT NULL,
            text TEXT NOT NULL,
            entry_count INTEGER NOT NULL,
            through_revision INTEGER NOT NULL,
            updated_at TEXT NOT NULL,
            PRIMARY KEY (kind, period)
        );
        CREATE TABLE IF NOT EXISTS chat_messages (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            role TEXT NOT NULL,
            text TEXT NOT NULL,
            at TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS meta (
            key TEXT NOT NULL PRIMARY KEY,
            value TEXT NOT NULL
        );
        INSERT OR IGNORE INTO meta (key, value) VALUES ('revision', '0');
        """;

    private string connectionString = string.Empty;

    /// <inheritdoc />
    public async Task Initialize(string folder, CancellationToken cancellation)
    {
        Directory.CreateDirectory(folder);
        connectionString = new SqliteConnectionStringBuilder { DataSource = Path.Combine(folder, "journal.db") }.ToString();

        await using SqliteConnection connection = await Open(cancellation);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode = WAL;";
        await command.ExecuteNonQueryAsync(cancellation);
        command.CommandText = schema;
        await command.ExecuteNonQueryAsync(cancellation);
    }

    /// <inheritdoc />
    public async Task<SqliteConnection> Open(CancellationToken cancellation)
    {
        if (connectionString.Length == 0)
        {
            throw new InvalidOperationException("The database is not initialized.");
        }

        SqliteConnection connection = new(connectionString);
        await connection.OpenAsync(cancellation);
        return connection;
    }
}
