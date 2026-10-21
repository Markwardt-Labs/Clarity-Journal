namespace Markwardt.ClarityJournal.Tests.Unit;

public sealed class ConfigLoaderTests
{
    private readonly TempFolder temp = new();

    private async Task<ServerConfig> Load(string json)
    {
        string path = temp.Combine("config.json");
        await File.WriteAllTextAsync(path, json);
        using (temp)
        {
            return await new ConfigLoader().Load(path, CancellationToken.None);
        }
    }

    [Fact]
    public async Task Load_AppliesDefaults()
    {
        ServerConfig config = await Load("""{ "key": "secret" }""");

        Assert.Equal(52681, config.Port);
        Assert.Equal("claude", config.ClaudeCommand);
        Assert.Equal(new TimeOnly(20, 0), config.SummaryTime);
        Assert.Equal(10, config.ClaudeTimeoutMinutes);
        Assert.Equal(2, config.ChatRetentionDays);
        Assert.Equal("small.en", config.SpeechModel);
        Assert.Null(config.SpeechCommand);
    }

    [Fact]
    public async Task Load_ReadsEverySetting()
    {
        ServerConfig config = await Load("""{ "port": 4000, "key": "k", "dataFolder": "/data", "claudeCommand": "/bin/claude", "claudeArguments": ["--model", "sonnet"], "claudeTimeoutMinutes": 3, "summaryTime": "06:30:00", "summaryBatchCharacters": 5000, "chatRetentionDays": 7, "speechModel": "tiny.en", "speechCommand": "/bin/say", "speechArguments": ["{file}"] }""");

        Assert.Equal(4000, config.Port);
        Assert.Equal("/data", config.DataFolder);
        Assert.Equal(["--model", "sonnet"], config.ClaudeArguments);
        Assert.Equal(new TimeOnly(6, 30), config.SummaryTime);
        Assert.Equal(5000, config.SummaryBatchCharacters);
        Assert.Equal(7, config.ChatRetentionDays);
        Assert.Equal("tiny.en", config.SpeechModel);
        Assert.Equal("/bin/say", config.SpeechCommand);
        Assert.Equal(["{file}"], config.SpeechArguments);
    }

    [Theory]
    [InlineData("""{ "port": 0, "key": "k" }""")]
    [InlineData("""{ "port": 70000, "key": "k" }""")]
    [InlineData("""{ "key": "" }""")]
    [InlineData("""{ "key": "k", "claudeTimeoutMinutes": 0 }""")]
    [InlineData("""{ "key": "k", "summaryBatchCharacters": 10 }""")]
    [InlineData("""{ "key": "k", "chatRetentionDays": 0 }""")]
    [InlineData("not json")]
    [InlineData("null")]
    public async Task Load_RejectsInvalidSettings(string json)
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => Load(json));
    }

    [Fact]
    public async Task Load_MissingFile_Throws()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(() => new ConfigLoader().Load(temp.Combine("missing.json"), CancellationToken.None));
        temp.Dispose();
    }
}
