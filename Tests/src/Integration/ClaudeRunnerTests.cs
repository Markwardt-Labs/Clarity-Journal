namespace Markwardt.ClarityJournal.Tests.Integration;

public sealed class ClaudeRunnerTests : IDisposable
{
    private readonly TempFolder temp = new();

    private readonly ClaudeRunner runner = new();

    public void Dispose() => temp.Dispose();

    private ServerConfig Script(string body, params string[] extra)
    {
        string path = temp.Combine("fake-claude.sh");
        File.WriteAllText(path, "#!/bin/sh\n" + body + "\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return new ServerConfig { Key = "k", ClaudeCommand = path, ClaudeArguments = extra, ClaudeTimeoutMinutes = 1 };
    }

    private string Recording(string result) => $"d=\"$(dirname \"$0\")\"\nprintf '%s\\n' \"$@\" > \"$d/args.txt\"\ncat > \"$d/stdin.txt\"\nprintf '%s' \"$CLAUDE_CODE_DISABLE_BACKGROUND_TASKS\" > \"$d/env.txt\"\npwd > \"$d/cwd.txt\"\nprintf '%s' '{result}'";

    private string[] Args() => File.ReadAllLines(temp.Combine("args.txt"));

    [Fact]
    public async Task Run_WithoutTools_SendsThePromptOnStdinAndReturnsTheTrimmedResult()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        ServerConfig config = Script(Recording("{\"type\":\"result\",\"is_error\":false,\"result\":\"  the summary \\n\",\"session_id\":\"s\"}"));

        string reply = await runner.Run(config, new ClaudeRequest { Prompt = "Summarize this \"journal\"\nplease", SystemPrompt = "You summarize.", ReplacesSystemPrompt = true, Folder = temp.Path }, CancellationToken.None);

        Assert.Equal("the summary", reply);
        Assert.Equal("Summarize this \"journal\"\nplease", File.ReadAllText(temp.Combine("stdin.txt")));
        Assert.Equal(["-p", "--output-format", "json", "--strict-mcp-config", "--system-prompt", "You summarize.", "--tools", string.Empty, "--no-session-persistence"], Args());
        Assert.Equal("1", File.ReadAllText(temp.Combine("env.txt")));
        Assert.Equal(Path.GetFullPath(temp.Path), File.ReadAllText(temp.Combine("cwd.txt")).Trim());
    }

    [Fact]
    public async Task Run_WithTools_PreApprovesOnlyThemAndAppendsTheSystemPrompt()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        ServerConfig config = Script(Recording("{\"type\":\"result\",\"is_error\":false,\"result\":\"ok\"}"), "--model", "sonnet");
        Guid session = Guid.NewGuid();

        await runner.Run(config, new ClaudeRequest { Prompt = "q", SystemPrompt = "You answer.", Folder = temp.Path, Tools = ["Read", "Grep", "Glob"], Session = session }, CancellationToken.None);

        Assert.Equal(["-p", "--output-format", "json", "--strict-mcp-config", "--append-system-prompt", "You answer.", "--tools", "Read,Grep,Glob", "--permission-mode", "dontAsk", "--allowedTools", "Read,Grep,Glob", "--session-id", session.ToString(), "--model", "sonnet"], Args());
    }

    [Fact]
    public async Task Run_Resume_ContinuesTheSession()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        ServerConfig config = Script(Recording("{\"type\":\"result\",\"is_error\":false,\"result\":\"ok\"}"));
        Guid session = Guid.NewGuid();

        await runner.Run(config, new ClaudeRequest { Prompt = "q", SystemPrompt = "s", Folder = temp.Path, Session = session, Resume = true }, CancellationToken.None);

        Assert.Equal(["--resume", session.ToString()], Args().SkipWhile(argument => argument != "--resume").ToArray());
    }

    [Fact]
    public async Task Run_ClaudeReportsAnError_ThrowsWithItsMessage()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        ServerConfig config = Script("cat > /dev/null\nprintf '%s' '{\"type\":\"result\",\"is_error\":true,\"result\":\"Not logged in\"}'\nexit 1");

        ClaudeException exception = await Assert.ThrowsAsync<ClaudeException>(() => runner.Run(config, new ClaudeRequest { Prompt = "q", SystemPrompt = "s", Folder = temp.Path }, CancellationToken.None));

        Assert.Equal("Not logged in", exception.Message);
    }

    [Fact]
    public async Task Run_ProcessFailsWithoutJson_ThrowsWithItsErrorOutput()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        ServerConfig config = Script("cat > /dev/null\necho 'something broke' >&2\nexit 3");

        ClaudeException exception = await Assert.ThrowsAsync<ClaudeException>(() => runner.Run(config, new ClaudeRequest { Prompt = "q", SystemPrompt = "s", Folder = temp.Path }, CancellationToken.None));

        Assert.Equal("something broke", exception.Message);
    }

    [Fact]
    public async Task Run_EmptyAnswer_Throws()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        ServerConfig config = Script("cat > /dev/null\nprintf '%s' '{\"type\":\"result\",\"is_error\":false,\"result\":\"   \"}'");

        await Assert.ThrowsAsync<ClaudeException>(() => runner.Run(config, new ClaudeRequest { Prompt = "q", SystemPrompt = "s", Folder = temp.Path }, CancellationToken.None));
    }

    [Fact]
    public async Task Run_MissingExecutable_ThrowsClaudeException()
    {
        ServerConfig config = new() { Key = "k", ClaudeCommand = temp.Combine("does-not-exist") };

        ClaudeException exception = await Assert.ThrowsAsync<ClaudeException>(() => runner.Run(config, new ClaudeRequest { Prompt = "q", SystemPrompt = "s", Folder = temp.Path }, CancellationToken.None));

        Assert.Contains("could not be started", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_Cancelled_StopsTheProcess()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        ServerConfig config = Script("echo $$ > \"$(dirname \"$0\")/pid.txt\"\nsleep 60");
        using CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(500));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.Run(config, new ClaudeRequest { Prompt = "q", SystemPrompt = "s", Folder = temp.Path }, cancellation.Token));

        int pid = int.Parse(File.ReadAllText(temp.Combine("pid.txt")).Trim(), CultureInfo.InvariantCulture);
        await Task.Delay(300);
        Assert.False(Directory.Exists($"/proc/{pid}") && !File.ReadAllText($"/proc/{pid}/stat").Contains(") Z", StringComparison.Ordinal));
    }
}
