using System.Reflection;
using GitHub.Copilot.SDK;

namespace DevTeam.Core;

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

        var allMcpServers = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        var mcpConfig = CreateLocalMcpServerConfig(request);
        if (mcpConfig is not null)
        {
            allMcpServers[request.WorkspaceMcpServerName] = mcpConfig;
        }

        foreach (var external in request.ExternalMcpServers)
        {
            if (!external.Enabled || string.IsNullOrWhiteSpace(external.Name) || string.IsNullOrWhiteSpace(external.Command))
            {
                continue;
            }

            allMcpServers[external.Name] = new McpLocalServerConfig
            {
                Type = "local",
                Command = external.Command,
                Args = external.Args.ToList(),
                Cwd = external.Cwd ?? request.WorkingDirectory,
                Tools = ["*"]
            };
        }

        if (allMcpServers.Count > 0)
        {
            sessionConfig.McpServers = allMcpServers;
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
        foreach (var relative in new[] { Path.Combine(".devteam-source", "superpowers") })
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
