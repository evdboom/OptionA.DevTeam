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

        if (request.Provider is not null)
        {
            sessionConfig.Provider = BuildProviderConfig(request.Provider);
            sessionConfig.EnableConfigDiscovery = false;
        }

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

        if (request.Hooks is not null)
        {
            sessionConfig.Hooks = BuildSessionHooks(request.Hooks);
        }

        if (request.CustomAgents.Count > 0)
        {
            sessionConfig.CustomAgents = request.CustomAgents
                .Select(a => new CustomAgentConfig
                {
                    Name = a.Name,
                    DisplayName = a.DisplayName,
                    Description = a.Description,
                    Tools = a.Tools.ToList(),
                    Prompt = a.Prompt,
                    Infer = a.Infer
                })
                .ToList();
        }

        return sessionConfig;
    }

    public static SessionHooks BuildSessionHooks(SessionHooksConfig config)
    {
        var hooks = new SessionHooks();

        if (config.OnPreToolUse is not null)
        {
            var callback = config.OnPreToolUse;
            hooks.OnPreToolUse = (input, _) =>
            {
                var decision = callback(input.ToolName ?? "", input.ToolArgs?.ToString() ?? "");
                var kind = decision switch
                {
                    PreToolDecision.Deny => "deny",
                    PreToolDecision.Ask  => "ask",
                    _                    => "allow"
                };
                return Task.FromResult<PreToolUseHookOutput?>(new PreToolUseHookOutput { PermissionDecision = kind });
            };
        }

        if (config.OnPostToolUse is not null)
        {
            var callback = config.OnPostToolUse;
            hooks.OnPostToolUse = (input, _) =>
            {
                callback(input.ToolName ?? "", input.ToolArgs?.ToString() ?? "", input.ToolResult?.ToString() ?? "");
                return Task.FromResult<PostToolUseHookOutput?>(null);
            };
        }

        if (config.OnSessionStart is not null)
        {
            var callback = config.OnSessionStart;
            hooks.OnSessionStart = (input, _) =>
            {
                callback(input.Source ?? "");
                return Task.FromResult<SessionStartHookOutput?>(null);
            };
        }

        if (config.OnSessionEnd is not null)
        {
            var callback = config.OnSessionEnd;
            hooks.OnSessionEnd = (input, _) =>
            {
                callback(input.Reason ?? "");
                return Task.FromResult<SessionEndHookOutput?>(null);
            };
        }

        if (config.OnErrorOccurred is not null)
        {
            var callback = config.OnErrorOccurred;
            hooks.OnErrorOccurred = (input, _) =>
            {
                var decision = callback(input.ErrorContext ?? "", input.Error ?? "");
                var kind = decision switch
                {
                    ErrorHandlingDecision.Skip  => "skip",
                    ErrorHandlingDecision.Abort => "abort",
                    _                           => "retry"
                };
                return Task.FromResult<ErrorOccurredHookOutput?>(new ErrorOccurredHookOutput { ErrorHandling = kind });
            };
        }

        return hooks;
    }

    public static ProviderConfig BuildProviderConfig(ProviderDefinition provider)
    {
        if (string.IsNullOrWhiteSpace(provider.Name))
        {
            throw new InvalidOperationException("Provider name must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(provider.Type))
        {
            throw new InvalidOperationException($"Provider '{provider.Name}' is missing Type.");
        }

        if (string.IsNullOrWhiteSpace(provider.BaseUrl))
        {
            throw new InvalidOperationException($"Provider '{provider.Name}' is missing BaseUrl.");
        }

        var apiKey = ReadSecret(provider.ApiKeyEnvVar);
        var bearerToken = ReadSecret(provider.BearerTokenEnvVar);
        if (string.IsNullOrWhiteSpace(apiKey) && string.IsNullOrWhiteSpace(bearerToken))
        {
            throw new InvalidOperationException(
                $"Provider '{provider.Name}' requires an API key or bearer token. Set {DescribeSecretSources(provider)} and try again.");
        }

        return new ProviderConfig
        {
            Type = provider.Type.Trim(),
            WireApi = provider.WireApi.Trim(),
            BaseUrl = provider.BaseUrl.Trim(),
            ApiKey = apiKey ?? "",
            BearerToken = bearerToken ?? "",
            Azure = string.IsNullOrWhiteSpace(provider.AzureApiVersion)
                ? null
                : new AzureOptions { ApiVersion = provider.AzureApiVersion.Trim() }
        };
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
        foreach (var relative in new[] { Path.Combine(".github", "skills"), Path.Combine(".devteam-source", "skills"), Path.Combine(".devteam-source", "superpowers") })
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

    private static string? ReadSecret(string envVar) =>
        string.IsNullOrWhiteSpace(envVar)
            ? null
            : Environment.GetEnvironmentVariable(envVar.Trim());

    private static string DescribeSecretSources(ProviderDefinition provider)
    {
        var names = new[] { provider.ApiKeyEnvVar, provider.BearerTokenEnvVar }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return names.Count == 0 ? "the required provider secret environment variables" : string.Join(" or ", names);
    }
}
