using System.Diagnostics;
using System.Reflection;
using System.Text;
using GitHub.Copilot.SDK;

namespace DevTeam.Core;

public sealed class AgentInvocationRequest
{
    public string Prompt { get; init; } = "";
    public string? Model { get; init; }
    public string? SessionId { get; init; }
    public string WorkingDirectory { get; init; } = Directory.GetCurrentDirectory();
    public string? WorkspacePath { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(20);
    public IReadOnlyList<string> ExtraArguments { get; init; } = [];
    public bool EnableWorkspaceMcp { get; init; }
    public string WorkspaceMcpServerName { get; init; } = "devteam-workspace";
    public string? ToolHostPath { get; init; }
}

public sealed class AgentInvocationResult
{
    public string BackendName { get; init; } = "";
    public string SessionId { get; init; } = "";
    public int ExitCode { get; init; }
    public string StdOut { get; init; } = "";
    public string StdErr { get; init; } = "";
    public bool Success => ExitCode == 0;
}

public sealed class LocalMcpCommandSpec
{
    public string Command { get; init; } = "";
    public IReadOnlyList<string> Arguments { get; init; } = [];
    public string WorkingDirectory { get; init; } = Directory.GetCurrentDirectory();
}

public static class WorkspaceMcpSessionConfigFactory
{
    public static SessionConfig BuildSessionConfig(AgentInvocationRequest request)
    {
        var sessionConfig = new SessionConfig
        {
            ClientName = "devteam-runtime",
            Model = request.Model,
            SessionId = request.SessionId,
            OnPermissionRequest = PermissionHandler.ApproveAll,
            WorkingDirectory = request.WorkingDirectory,
            Streaming = true,
            SkillDirectories = ResolveSkillDirectories(request.WorkingDirectory)
        };

        var mcpConfig = CreateLocalMcpServerConfig(request);
        if (mcpConfig is not null)
        {
            sessionConfig.McpServers = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                [request.WorkspaceMcpServerName] = mcpConfig
            };
        }

        return sessionConfig;
    }

    public static McpLocalServerConfig? CreateLocalMcpServerConfig(AgentInvocationRequest request)
    {
        if (!request.EnableWorkspaceMcp || string.IsNullOrWhiteSpace(request.WorkspacePath))
        {
            return null;
        }

        var command = BuildLocalCommandSpec(request);
        return new McpLocalServerConfig
        {
            Type = "local",
            Command = command.Command,
            Args = command.Arguments.ToList(),
            Cwd = command.WorkingDirectory,
            Tools = ["*"],
            Timeout = Math.Max(30000, (int)Math.Ceiling(request.Timeout.TotalMilliseconds))
        };
    }

    public static LocalMcpCommandSpec BuildLocalCommandSpec(AgentInvocationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.WorkspacePath))
        {
            throw new InvalidOperationException("WorkspacePath is required when workspace MCP is enabled.");
        }

        var toolHostPath = ResolveToolHostPath(request.ToolHostPath);
        var workingDirectory = Path.GetDirectoryName(toolHostPath) ?? request.WorkingDirectory;
        if (toolHostPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            return new LocalMcpCommandSpec
            {
                Command = "dotnet",
                Arguments =
                [
                    toolHostPath,
                    "workspace-mcp",
                    "--workspace",
                    request.WorkspacePath
                ],
                WorkingDirectory = workingDirectory
            };
        }

        return new LocalMcpCommandSpec
        {
            Command = toolHostPath,
            Arguments =
            [
                "workspace-mcp",
                "--workspace",
                request.WorkspacePath
            ],
            WorkingDirectory = workingDirectory
        };
    }

    private static string ResolveToolHostPath(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return Path.GetFullPath(explicitPath);
        }

        var entryAssemblyPath = Assembly.GetEntryAssembly()?.Location;
        if (!string.IsNullOrWhiteSpace(entryAssemblyPath))
        {
            return Path.GetFullPath(entryAssemblyPath);
        }

        throw new InvalidOperationException("Unable to resolve the DevTeam CLI host path for the workspace MCP server.");
    }

    private static List<string> ResolveSkillDirectories(string workingDirectory)
    {
        var repoRoot = FindRepoRoot(workingDirectory);
        var paths = new List<string>();
        foreach (var relative in new[] { ".devteam-source\\superpowers" })
        {
            var fullPath = Path.Combine(repoRoot, relative);
            if (Directory.Exists(fullPath))
            {
                paths.Add(fullPath);
            }
        }
        return paths;
    }

    private static string FindRepoRoot(string workingDirectory)
    {
        var current = new DirectoryInfo(Path.GetFullPath(workingDirectory));
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".devteam-source")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }

        return Path.GetFullPath(workingDirectory);
    }
}

public sealed class CommandExecutionSpec
{
    public string FileName { get; init; } = "";
    public IReadOnlyList<string> Arguments { get; init; } = [];
    public string WorkingDirectory { get; init; } = Directory.GetCurrentDirectory();
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(20);
}

public sealed class CommandExecutionResult
{
    public int ExitCode { get; init; }
    public string StdOut { get; init; } = "";
    public string StdErr { get; init; } = "";
}

public interface ICommandRunner
{
    Task<CommandExecutionResult> RunAsync(CommandExecutionSpec spec, CancellationToken cancellationToken = default);
}

public interface IAgentClient
{
    string Name { get; }
    Task<AgentInvocationResult> InvokeAsync(AgentInvocationRequest request, CancellationToken cancellationToken = default);
}

public sealed class ProcessCommandRunner : ICommandRunner
{
    public async Task<CommandExecutionResult> RunAsync(CommandExecutionSpec spec, CancellationToken cancellationToken = default)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = spec.FileName,
                WorkingDirectory = spec.WorkingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        foreach (var argument in spec.Arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start process '{spec.FileName}'.");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(spec.Timeout);

        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
        await process.WaitForExitAsync(timeoutCts.Token);

        return new CommandExecutionResult
        {
            ExitCode = process.ExitCode,
            StdOut = await stdoutTask,
            StdErr = await stderrTask
        };
    }
}

public sealed class CopilotCliAgentClient(ICommandRunner? runner = null) : IAgentClient
{
    private readonly ICommandRunner _runner = runner ?? new ProcessCommandRunner();

    public string Name => "copilot-cli";

    public async Task<AgentInvocationResult> InvokeAsync(AgentInvocationRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            throw new InvalidOperationException("Prompt is required.");
        }

        var arguments = new List<string>();
        foreach (var argument in request.ExtraArguments)
        {
            arguments.Add(argument);
        }

        if (!string.IsNullOrWhiteSpace(request.Model))
        {
            arguments.Add("--model");
            arguments.Add(request.Model);
        }

        arguments.Add("--no-ask-user");
        arguments.Add("-p");
        arguments.Add(request.Prompt);

        var result = await _runner.RunAsync(
            new CommandExecutionSpec
            {
                FileName = "copilot",
                Arguments = arguments,
                WorkingDirectory = request.WorkingDirectory,
                Timeout = request.Timeout
            },
            cancellationToken);

        return new AgentInvocationResult
        {
            BackendName = Name,
            SessionId = request.SessionId ?? "",
            ExitCode = result.ExitCode,
            StdOut = result.StdOut,
            StdErr = result.StdErr
        };
    }
}

public sealed class CopilotSdkAgentClient : IAgentClient
{
    public string Name => "copilot-sdk";

    public async Task<AgentInvocationResult> InvokeAsync(AgentInvocationRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            throw new InvalidOperationException("Prompt is required.");
        }

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var sawDelta = false;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var clientOptions = new CopilotClientOptions
        {
            Cwd = request.WorkingDirectory,
            CliArgs = request.ExtraArguments.ToArray()
        };

        await using var client = new CopilotClient(clientOptions);
        await client.StartAsync(cancellationToken);

        var sessionConfig = WorkspaceMcpSessionConfigFactory.BuildSessionConfig(request);

        await using var session = await client.CreateSessionAsync(sessionConfig, cancellationToken);
        using var subscription = session.On(evt =>
        {
            switch (evt)
            {
                case AssistantMessageDeltaEvent delta when !string.IsNullOrWhiteSpace(delta.Data?.DeltaContent):
                    sawDelta = true;
                    stdout.Append(delta.Data?.DeltaContent);
                    break;
                case AssistantMessageEvent message when !sawDelta && !string.IsNullOrWhiteSpace(message.Data?.Content):
                    stdout.Append(message.Data?.Content);
                    break;
                case SessionErrorEvent error when !string.IsNullOrWhiteSpace(error.Data?.Message):
                    stderr.AppendLine(error.Data?.Message);
                    done.TrySetResult();
                    break;
                case SessionIdleEvent:
                    done.TrySetResult();
                    break;
            }
        });

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(request.Timeout);
        await session.SendAsync(new MessageOptions { Prompt = request.Prompt }, timeoutCts.Token);
        await done.Task.WaitAsync(timeoutCts.Token);

        return new AgentInvocationResult
        {
            BackendName = Name,
            SessionId = session.SessionId,
            ExitCode = stderr.Length == 0 ? 0 : 1,
            StdOut = stdout.ToString(),
            StdErr = stderr.ToString()
        };
    }
}

public static class AgentClientFactory
{
    public static IAgentClient Create(string backend, ICommandRunner? runner = null)
    {
        return backend.Trim().ToLowerInvariant() switch
        {
            "sdk" or "copilot-sdk" => new CopilotSdkAgentClient(),
            "cli" or "copilot-cli" => new CopilotCliAgentClient(runner),
            _ => throw new InvalidOperationException(
                $"Unknown agent backend '{backend}'. Expected 'cli' or 'sdk'.")
        };
    }
}
