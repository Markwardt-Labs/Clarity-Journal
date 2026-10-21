namespace Markwardt.ClarityJournal.Tests.Integration;

public sealed class RealSpeechTests(Xunit.Abstractions.ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder temp = new();

    private readonly string? model = Environment.GetEnvironmentVariable("CLARITY_REAL_WHISPER_MODEL");

    private readonly string? wav = Environment.GetEnvironmentVariable("CLARITY_REAL_WHISPER_WAV");

    public void Dispose() => temp.Dispose();

    [Fact]
    public async Task Transcribe_TheRealWhisperModelTurnsARecordingIntoItsWords()
    {
        if (model is null || wav is null)
        {
            return;
        }

        SpeechService speech = new(new WhisperEngine());
        speech.Configure(new ServerConfig { Key = "k", DataFolder = temp.Path, SpeechModel = model });

        string text = await speech.Transcribe(Convert.ToBase64String(await File.ReadAllBytesAsync(wav)), CancellationToken.None);
        output.WriteLine(text);

        Assert.Contains("ask not what your country can do for you", text, StringComparison.OrdinalIgnoreCase);
    }
}
