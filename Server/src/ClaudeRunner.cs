namespace Markwardt.ClarityJournal.Server;

/// <summary>
/// Runs the local Claude Code command.
/// </summary>
internal interface IClaudeRunner
{
    /// <summary>
    /// Runs Claude Code once and waits for its answer.
    /// </summary>
    /// <param name="config">The server settings naming the executable, its extra arguments and the time limit.</param>
    /// <param name="request">What to run.</param>
    /// <param name="cancellation">Cancels the run, which also ends the process.</param>
    /// <returns>The text of the answer.</returns>
    /// <exception cref="ClaudeException">Claude Code could not be started, failed or took longer than the time limit.</exception>
    Task<string> Run(ServerConfig config, ClaudeRequest request, CancellationToken cancellation);
}

/// <inheritdoc />
internal sealed class ClaudeRunner : IClaudeRunner
{
    /// <inheritdoc />
    public async Task<string> Run(ServerConfig config, ClaudeRequest request, CancellationToken cancellation)
    {
        ProcessStartInfo start = new(config.ClaudeCommand)
        {
            WorkingDirectory = request.Folder,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
        };

        start.Environment["CLAUDE_CODE_DISABLE_BACKGROUND_TASKS"] = "1";

        foreach (string argument in Arguments(config, request))
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = new() { StartInfo = start };

        try
        {
            process.Start();
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            throw new ClaudeException($"Claude Code could not be started ('{config.ClaudeCommand}'): {exception.Message}");
        }

        using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        limit.CancelAfter(TimeSpan.FromMinutes(config.ClaudeTimeoutMinutes));

        try
        {
            Task<string> output = process.StandardOutput.ReadToEndAsync(limit.Token);
            Task<string> errors = process.StandardError.ReadToEndAsync(limit.Token);

            try
            {
                await process.StandardInput.WriteAsync(request.Prompt.AsMemory(), limit.Token);
                process.StandardInput.Close();
            }
            catch (IOException)
            {
            }

            await process.WaitForExitAsync(limit.Token);
            return Parse(await output, (await errors).Trim(), process.ExitCode);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            Stop(process);
            throw new ClaudeException($"Claude Code did not answer within {config.ClaudeTimeoutMinutes} minutes.");
        }
        catch (OperationCanceledException)
        {
            Stop(process);
            throw;
        }
    }

    private IEnumerable<string> Arguments(ServerConfig config, ClaudeRequest request)
    {
        yield return "-p";
        yield return "--output-format";
        yield return "json";
        yield return "--strict-mcp-config";
        yield return request.ReplacesSystemPrompt ? "--system-prompt" : "--append-system-prompt";
        yield return request.SystemPrompt;
        yield return "--tools";
        yield return string.Join(',', request.Tools);

        if (request.Tools.Count > 0)
        {
            yield return "--permission-mode";
            yield return "dontAsk";
            yield return "--allowedTools";
            yield return string.Join(',', request.Tools);
        }

        if (request.Session is { } session)
        {
            yield return request.Resume ? "--resume" : "--session-id";
            yield return session.ToString();
        }
        else
        {
            yield return "--no-session-persistence";
        }

        foreach (string argument in config.ClaudeArguments)
        {
            yield return argument;
        }
    }

    private string Parse(string output, string errors, int exitCode)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(output);
            JsonElement root = document.RootElement;
            string? result = root.TryGetProperty("result", out JsonElement text) && text.ValueKind == JsonValueKind.String ? text.GetString() : null;

            if (root.TryGetProperty("is_error", out JsonElement error) && error.ValueKind == JsonValueKind.True)
            {
                throw new ClaudeException(string.IsNullOrWhiteSpace(result) ? "Claude Code failed." : result);
            }

            if (exitCode == 0 && !string.IsNullOrWhiteSpace(result))
            {
                return result.Trim();
            }
        }
        catch (JsonException)
        {
        }

        throw new ClaudeException(errors.Length > 0 ? errors : $"Claude Code stopped without an answer (exit code {exitCode}).");
    }

    private void Stop(Process process)
    {
        try
        {
            process.Kill(true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
        }
    }
}
