namespace DevTeam.TestInfrastructure;

public sealed class FakeCommandRunner : ICommandRunner
{
    public CommandExecutionSpec? LastSpec { get; private set; }

    public Task<CommandExecutionResult> RunAsync(CommandExecutionSpec spec, CancellationToken cancellationToken = default)
    {
        LastSpec = spec;
        return Task.FromResult(new CommandExecutionResult
        {
            ExitCode = 0,
            StdOut = "ok"
        });
    }
}

public sealed class FakeAgentClient(string output) : IAgentClient
{
    public string Name => "fake-agent";

    public Task<AgentInvocationResult> InvokeAsync(AgentInvocationRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new AgentInvocationResult
        {
            BackendName = Name,
            ExitCode = 0,
            StdOut = output
        });
    }
}

public sealed class SwitchingAgentClient(Func<AgentInvocationRequest, string> selector) : IAgentClient
{
    public string Name => "switching-agent";

    public Task<AgentInvocationResult> InvokeAsync(AgentInvocationRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new AgentInvocationResult
        {
            BackendName = Name,
            SessionId = request.SessionId ?? "",
            ExitCode = 0,
            StdOut = selector(request)
        });
    }
}

public sealed class RecordingAgentClient : IAgentClient
{
    private readonly IReadOnlyList<string> _outputs;
    private readonly object _gate = new();
    private int _invocationCount;

    public RecordingAgentClient(params string[] outputs)
    {
        _outputs = outputs.Length == 0
            ? ["OUTCOME: completed\nSUMMARY:\nDone."]
            : outputs;
    }

    public string Name => "recording-agent";
    public string? LastPrompt { get; private set; }
    public List<RecordedAgentRequest> Requests { get; } = [];

    public Task<AgentInvocationResult> InvokeAsync(AgentInvocationRequest request, CancellationToken cancellationToken = default)
    {
        string output;
        lock (_gate)
        {
            LastPrompt = request.Prompt;
            Requests.Add(new RecordedAgentRequest
            {
                Prompt = request.Prompt,
                SessionId = request.SessionId ?? "",
                Model = request.Model ?? "",
                WorkingDirectory = request.WorkingDirectory
            });
            output = _outputs[Math.Min(_invocationCount, _outputs.Count - 1)];
            _invocationCount++;
        }

        return Task.FromResult(new AgentInvocationResult
        {
            BackendName = Name,
            ExitCode = 0,
            SessionId = request.SessionId ?? "",
            StdOut = output
        });
    }
}

public sealed class RecordedAgentRequest
{
    public string Prompt { get; init; } = "";
    public string SessionId { get; init; } = "";
    public string Model { get; init; } = "";
    public string WorkingDirectory { get; init; } = "";
}

public sealed class FileWritingAgentClient(string fileName, string output) : IAgentClient
{
    public string Name => "file-writing-agent";

    public Task<AgentInvocationResult> InvokeAsync(AgentInvocationRequest request, CancellationToken cancellationToken = default)
    {
        File.WriteAllText(Path.Combine(request.WorkingDirectory, fileName), "generated");
        return Task.FromResult(new AgentInvocationResult
        {
            BackendName = Name,
            ExitCode = 0,
            StdOut = output
        });
    }
}

public sealed class FakeConcurrentAgentClient(string output) : IAgentClient
{
    private int _currentInvocations;

    public string Name => "fake-concurrent-agent";
    public int MaxConcurrentInvocations { get; private set; }

    public async Task<AgentInvocationResult> InvokeAsync(AgentInvocationRequest request, CancellationToken cancellationToken = default)
    {
        var current = Interlocked.Increment(ref _currentInvocations);
        MaxConcurrentInvocations = Math.Max(MaxConcurrentInvocations, current);
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(150), cancellationToken);
            return new AgentInvocationResult
            {
                BackendName = Name,
                ExitCode = 0,
                StdOut = output
            };
        }
        finally
        {
            Interlocked.Decrement(ref _currentInvocations);
        }
    }
}

public sealed class FakeStaggeredAgentClient(string output, params TimeSpan[] delays) : IAgentClient
{
    private int _invocationCount;

    public string Name => "fake-staggered-agent";

    public async Task<AgentInvocationResult> InvokeAsync(AgentInvocationRequest request, CancellationToken cancellationToken = default)
    {
        var invocation = Interlocked.Increment(ref _invocationCount) - 1;
        var delay = invocation < delays.Length ? delays[invocation] : delays.LastOrDefault();
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken);
        }

        return new AgentInvocationResult
        {
            BackendName = Name,
            ExitCode = 0,
            StdOut = output
        };
    }
}

public sealed class FuncAgentClientFactory(Func<string, IAgentClient> factory) : IAgentClientFactory
{
    public IAgentClient Create(string backend) => factory(backend);
}

public static class TestFileSystem
{
    public static void DeleteDirectoryWithRetries(string path)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Directory.Delete(path, true);
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException) when (attempt < 4)
            {
                Thread.Sleep(100);
            }
        }

        try
        {
            if (Directory.Exists(path))
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                    }
                    catch
                    {
                        // Best-effort attribute reset for temp test files.
                    }
                }

                Directory.Delete(path, true);
            }
        }
        catch
        {
            // Best-effort cleanup for temporary test directories.
        }
    }
}
