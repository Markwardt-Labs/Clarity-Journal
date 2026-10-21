namespace Markwardt.ClarityJournal.Server;

/// <summary>
/// The server's settings, loaded from the JSON config file.
/// </summary>
internal sealed record ServerConfig
{
    /// <summary>
    /// Gets the port the app connects to, over secure WebSocket (52681 unless set).
    /// </summary>
    public int Port { get; init; } = 52681;

    /// <summary>
    /// Gets the key the app must supply to connect.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// Gets the folder holding everything the server stores: the journal database, the files Claude Code reads and the generated certificate. Created when missing; the <c>ClarityJournal</c> folder of the server user's application data unless set.
    /// </summary>
    public string DataFolder { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClarityJournal");

    /// <summary>
    /// Gets the Claude Code executable to run for summaries and chat answers.
    /// </summary>
    public string ClaudeCommand { get; init; } = "claude";

    /// <summary>
    /// Gets the extra arguments passed to every Claude Code run, such as <c>--model</c> and a model name.
    /// </summary>
    public IReadOnlyList<string> ClaudeArguments { get; init; } = [];

    /// <summary>
    /// Gets how many minutes one Claude Code run may take before it is stopped.
    /// </summary>
    public int ClaudeTimeoutMinutes { get; init; } = 10;

    /// <summary>
    /// Gets the local time of day, on Fridays, at which summaries are updated (<c>20:00:00</c> unless set).
    /// </summary>
    public TimeOnly SummaryTime { get; init; } = new(20, 0);

    /// <summary>
    /// Gets how many characters of entries go into one summary prompt at most; more entries are folded into the summary in several runs.
    /// </summary>
    public int SummaryBatchCharacters { get; init; } = 40000;

    /// <summary>
    /// Gets how many days of chat the server keeps; older messages are deleted by a check that runs once a day.
    /// </summary>
    public int ChatRetentionDays { get; init; } = 2;

    /// <summary>
    /// Gets the Whisper model that turns speech into text inside the server for voice input: a model name such as <c>small.en</c>, downloaded into the <c>models</c> folder of the data folder the first time, or the path of an existing <c>ggml</c> model file. Empty turns the built-in voice input off, unless <see cref="SpeechCommand" /> is set.
    /// </summary>
    public string? SpeechModel { get; init; } = "small.en";

    /// <summary>
    /// Gets the executable that turns speech into text for voice input instead of the built-in <see cref="SpeechModel" />; when empty, the built-in one is used.
    /// </summary>
    public string? SpeechCommand { get; init; }

    /// <summary>
    /// Gets the arguments of the speech command, in which <c>{file}</c> stands for the path of the recorded WAV file; the command prints the text to its standard output.
    /// </summary>
    public IReadOnlyList<string> SpeechArguments { get; init; } = [];

    /// <summary>
    /// Gets the PFX certificate file presented to the app; when empty, a self-signed certificate is created in the data folder.
    /// </summary>
    public string? CertificateFile { get; init; }

    /// <summary>
    /// Gets the password of the PFX certificate, if it has one.
    /// </summary>
    public string? CertificatePassword { get; init; }
}
