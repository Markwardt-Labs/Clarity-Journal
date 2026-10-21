namespace Markwardt.ClarityJournal.Client;

/// <summary>
/// Voice input for a text box: records speech from the microphone, has the server turn it into text and hands the text over.
/// </summary>
internal interface IDictation : INotifyPropertyChanged
{
    /// <summary>
    /// Raised with the text that was heard once a recording was turned into text.
    /// </summary>
    event Action<string>? Dictated;

    /// <summary>
    /// Raised with a message when recording or turning the recording into text failed.
    /// </summary>
    event Action<string>? Failed;

    /// <summary>
    /// Gets a value indicating whether this client can record speech, which shows the voice input button.
    /// </summary>
    bool CanDictate { get; }

    /// <summary>
    /// Gets a value indicating whether speech is being recorded.
    /// </summary>
    bool IsRecording { get; }

    /// <summary>
    /// Gets a value indicating whether a recording is being turned into text on the server.
    /// </summary>
    bool IsTranscribing { get; }

    /// <summary>
    /// Gets the tooltip of the voice input button.
    /// </summary>
    string Tip { get; }

    /// <summary>
    /// Gets the command that starts recording, or ends the recording and has it turned into text; it is unavailable while a recording is being turned into text, and when recording may not start.
    /// </summary>
    AsyncCommand Command { get; }

    /// <summary>
    /// Ends a running recording and has it turned into text, as the command does; does nothing when nothing is being recorded.
    /// </summary>
    /// <returns>A task that completes once the text was handed over or the attempt failed.</returns>
    Task Stop();

    /// <summary>
    /// Ends a running recording and throws it away.
    /// </summary>
    void Cancel();

    /// <summary>
    /// Re-evaluates whether recording may start.
    /// </summary>
    void Refresh();
}

/// <inheritdoc />
internal sealed class Dictation : ViewModelBase, IDictation
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Dictation" /> class.
    /// </summary>
    /// <param name="connection">The open connection.</param>
    /// <param name="recorder">Records speech, or <see langword="null" /> when this client cannot record.</param>
    /// <param name="canStart">Tells whether recording may start now, such as only while the text box can be edited.</param>
    public Dictation(IServerConnection connection, IVoiceRecorder? recorder, Func<bool> canStart)
    {
        this.connection = connection;
        this.recorder = recorder;
        Command = new AsyncCommand(_ => recording ? Finish() : Begin(), () => !transcribing && (recording || canStart()));
    }

    private readonly IServerConnection connection;

    private readonly IVoiceRecorder? recorder;

    private readonly TimeSpan longestRecording = TimeSpan.FromSeconds(60);

    private readonly TimeSpan shortestRecording = TimeSpan.FromMilliseconds(300);

    private CancellationTokenSource? recordingLimit;

    private bool recording;

    private bool transcribing;

    /// <inheritdoc />
    public event Action<string>? Dictated;

    /// <inheritdoc />
    public event Action<string>? Failed;

    /// <inheritdoc />
    public bool CanDictate => recorder is not null;

    /// <inheritdoc />
    public bool IsRecording => recording;

    /// <inheritdoc />
    public bool IsTranscribing => transcribing;

    /// <inheritdoc />
    public string Tip => recording ? "Stop recording and add the text" : transcribing ? "Turning speech into text" : "Voice input";

    /// <inheritdoc />
    public AsyncCommand Command { get; }

    /// <inheritdoc />
    public async Task Stop()
    {
        if (recording)
        {
            await Finish();
        }
    }

    /// <inheritdoc />
    public void Cancel()
    {
        if (!recording)
        {
            return;
        }

        recordingLimit?.Cancel();
        recordingLimit = null;
        _ = recorder!.Stop();
        Set(false, false);
    }

    /// <inheritdoc />
    public void Refresh() => Command.Refresh();

    private void Set(bool isRecording, bool isTranscribing)
    {
        recording = isRecording;
        transcribing = isTranscribing;
        Raise(nameof(IsRecording));
        Raise(nameof(IsTranscribing));
        Raise(nameof(Tip));
        Command.Refresh();
    }

    private async Task Begin()
    {
        try
        {
            await recorder!.Start(CancellationToken.None);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Failed?.Invoke(exception.Message);
            return;
        }

        Set(true, false);
        CancellationTokenSource limit = new();
        recordingLimit = limit;
        _ = StopWhenTooLong(limit.Token);
    }

    private async Task StopWhenTooLong(CancellationToken cancellation)
    {
        try
        {
            await Task.Delay(longestRecording, cancellation);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await Stop();
    }

    private async Task Finish()
    {
        recordingLimit?.Cancel();
        recordingLimit = null;
        Set(false, true);

        try
        {
            ReadOnlyMemory<byte> pcm = await recorder!.Stop();

            if (pcm.Length < recorder.SampleRate * 2 * shortestRecording.TotalSeconds)
            {
                Failed?.Invoke("Nothing was recorded.");
                return;
            }

            string text = string.Empty;

            if (!await Attempt(async () => text = await connection.Transcribe(Wav(pcm, recorder.SampleRate), CancellationToken.None)))
            {
                Failed?.Invoke(Error!);
            }
            else if (text.Trim().Length == 0)
            {
                Failed?.Invoke("Nothing was heard.");
            }
            else
            {
                Dictated?.Invoke(text.Trim());
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Failed?.Invoke(exception.Message);
        }
        finally
        {
            Set(false, false);
        }
    }

    private ReadOnlyMemory<byte> Wav(ReadOnlyMemory<byte> pcm, int sampleRate)
    {
        byte[] wav = new byte[44 + pcm.Length];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(wav, 0);
        BitConverter.TryWriteBytes(wav.AsSpan(4), 36 + pcm.Length);
        Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(wav, 8);
        BitConverter.TryWriteBytes(wav.AsSpan(16), 16);
        BitConverter.TryWriteBytes(wav.AsSpan(20), (short)1);
        BitConverter.TryWriteBytes(wav.AsSpan(22), (short)1);
        BitConverter.TryWriteBytes(wav.AsSpan(24), sampleRate);
        BitConverter.TryWriteBytes(wav.AsSpan(28), sampleRate * 2);
        BitConverter.TryWriteBytes(wav.AsSpan(32), (short)2);
        BitConverter.TryWriteBytes(wav.AsSpan(34), (short)16);
        Encoding.ASCII.GetBytes("data").CopyTo(wav, 36);
        BitConverter.TryWriteBytes(wav.AsSpan(40), pcm.Length);
        pcm.CopyTo(wav.AsMemory(44));
        return wav;
    }
}
