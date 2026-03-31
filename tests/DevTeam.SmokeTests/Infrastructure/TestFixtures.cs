using DevTeam.Cli;
using DevTeam.Core;

namespace DevTeam.SmokeTests;

internal sealed class TestHarness : IDisposable
{
    public TestHarness()
    {
        TempRoot = Path.Combine(Path.GetTempPath(), "devteam-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(TempRoot);
        RepoRoot = FindRepoRootForTests();
        Store = new WorkspaceStore(Path.Combine(TempRoot, ".devteam"));
        State = Store.Initialize(RepoRoot, 25, 6);
        Runtime = new DevTeamRuntime();
    }

    public string TempRoot { get; }
    public string RepoRoot { get; }
    public WorkspaceStore Store { get; }
    public WorkspaceState State { get; }
    public DevTeamRuntime Runtime { get; }

    public void Dispose()
    {
        if (Directory.Exists(TempRoot))
        {
            TestFileSystem.DeleteDirectoryWithRetries(TempRoot);
        }
    }

    internal static string FindRepoRootForTests()
    {
        var directory = AppContext.BaseDirectory;
        var current = new DirectoryInfo(directory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".devteam-source")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new InvalidOperationException("Could not locate repository root.");
    }
}

internal sealed class CliInvocationResult
{
    public int ExitCode { get; init; }
    public string StdOut { get; init; } = "";
    public string StdErr { get; init; } = "";
}

internal sealed class FakeCommandRunner : ICommandRunner
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

internal sealed class FakeAgentClient(string output) : IAgentClient
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

internal sealed class SwitchingAgentClient(Func<AgentInvocationRequest, string> selector) : IAgentClient
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

internal sealed class RecordingAgentClient : IAgentClient
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
                Model = request.Model ?? ""
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

internal sealed class RecordedAgentRequest
{
    public string Prompt { get; init; } = "";
    public string SessionId { get; init; } = "";
    public string Model { get; init; } = "";
}

internal sealed class FileWritingAgentClient(string fileName, string output) : IAgentClient
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

internal sealed class FakeConcurrentAgentClient(string output) : IAgentClient
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

internal sealed class FakeStaggeredAgentClient(string output, params TimeSpan[] delays) : IAgentClient
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

internal static class TestFileSystem
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
                    }
                }

                Directory.Delete(path, true);
            }
        }
        catch
        {
        }
    }
}
