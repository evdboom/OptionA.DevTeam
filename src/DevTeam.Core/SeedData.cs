namespace DevTeam.Core;

internal static class SeedData
{
    private static readonly Dictionary<string, RoleModelPolicy> DefaultPolicies = new(StringComparer.OrdinalIgnoreCase)
    {
        ["orchestrator"] = new() { PrimaryModel = "claude-sonnet-4.6", FallbackModel = "claude-haiku-4.5", AllowPremium = false },
        ["planner"] = new() { PrimaryModel = "claude-sonnet-4.6", FallbackModel = "claude-haiku-4.5", AllowPremium = false },
        ["architect"] = new() { PrimaryModel = "claude-opus-4.6", FallbackModel = "gpt-5.4", AllowPremium = true },
        ["developer"] = new() { PrimaryModel = "gpt-5.4", FallbackModel = "gpt-5.4-mini", AllowPremium = false, ModelPool = ["gpt-5.4", "claude-sonnet-4.6"] },
        ["backend-developer"] = new() { PrimaryModel = "gpt-5.4", FallbackModel = "gpt-5.4-mini", AllowPremium = false, ModelPool = ["gpt-5.4", "claude-sonnet-4.6"] },
        ["frontend-developer"] = new() { PrimaryModel = "claude-sonnet-4.6", FallbackModel = "gpt-5.4-mini", AllowPremium = false, ModelPool = ["claude-sonnet-4.6", "gpt-5.4"] },
        ["fullstack-developer"] = new() { PrimaryModel = "claude-sonnet-4.6", FallbackModel = "gpt-5.4-mini", AllowPremium = false, ModelPool = ["claude-sonnet-4.6", "gpt-5.4"] },
        ["tester"] = new() { PrimaryModel = "gemini-3.1-pro-preview", FallbackModel = "gpt-5.4-mini", AllowPremium = false, ModelPool = ["gemini-3.1-pro-preview", "gpt-5.4", "claude-sonnet-4.6"] },
        ["reviewer"] = new() { PrimaryModel = "claude-opus-4.6", FallbackModel = "gpt-5.4", AllowPremium = true },
        ["auditor"] = new() { PrimaryModel = "claude-opus-4.6", FallbackModel = "gpt-5.4", AllowPremium = true },
        ["ux"] = new() { PrimaryModel = "claude-sonnet-4.6", FallbackModel = "claude-haiku-4.5", AllowPremium = false },
        ["user"] = new() { PrimaryModel = "gpt-5-mini", FallbackModel = "gpt-5-mini", AllowPremium = false },
        ["game-designer"] = new() { PrimaryModel = "gemini-3.1-pro-preview", FallbackModel = "gemini-3-flash-preview", AllowPremium = false },
        ["navigator"] = new() { PrimaryModel = "claude-opus-4.6", FallbackModel = "gpt-5.4", AllowPremium = true },
        ["analyst"] = new() { PrimaryModel = "claude-sonnet-4.6", FallbackModel = "claude-haiku-4.5", AllowPremium = false },
        ["security"] = new() { PrimaryModel = "claude-sonnet-4.6", FallbackModel = "claude-haiku-4.5", AllowPremium = false },
        ["docs"] = new() { PrimaryModel = "gpt-5-mini", FallbackModel = "gpt-5-mini", AllowPremium = false },
        ["devops"] = new() { PrimaryModel = "gpt-5.4", FallbackModel = "gpt-5.4-mini", AllowPremium = false, ModelPool = ["gpt-5.4", "claude-sonnet-4.6"] },
        ["refactorer"] = new() { PrimaryModel = "claude-sonnet-4.6", FallbackModel = "gpt-5.4-mini", AllowPremium = false, ModelPool = ["claude-sonnet-4.6", "gpt-5.4"] }
    };

    public static WorkspaceState BuildInitialState(string repoRoot, double totalCreditCap, double premiumCreditCap,
        IConfigurationLoader? loader = null)
    {
        loader ??= new FileSystemConfigurationLoader();
        repoRoot = Path.GetFullPath(repoRoot);
        var state = new WorkspaceState
        {
            RepoRoot = repoRoot,
            Runtime = RuntimeConfiguration.CreateDefault(),
            Budget = new BudgetState
            {
                TotalCreditCap = totalCreditCap,
                PremiumCreditCap = premiumCreditCap
            }
        };

        state.Models = loader.LoadModels(repoRoot);
        state.Providers = loader.LoadProviders(repoRoot);
        state.Modes = loader.LoadModes(repoRoot);
        state.Roles = loader.LoadRoles(repoRoot);
        state.Superpowers = loader.LoadSuperpowers(repoRoot);
        state.McpServers = loader.LoadMcpServers(repoRoot);
        return state;
    }

    public static bool HydrateMissingWorkspaceMetadata(WorkspaceState state, IConfigurationLoader? loader = null)
    {
        loader ??= new FileSystemConfigurationLoader();
        var repoRoot = string.IsNullOrWhiteSpace(state.RepoRoot)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(state.RepoRoot);
        var changed = false;

        if (state.Models.Count == 0)
        {
            state.Models = loader.LoadModels(repoRoot);
            changed = true;
        }

        if (state.Roles.Count == 0)
        {
            state.Roles = loader.LoadRoles(repoRoot);
            changed = true;
        }

        if (state.Providers.Count == 0)
        {
            state.Providers = loader.LoadProviders(repoRoot);
            changed = true;
        }

        if (state.Modes.Count == 0)
        {
            state.Modes = loader.LoadModes(repoRoot);
            changed = true;
        }

        if (state.Superpowers.Count == 0)
        {
            state.Superpowers = loader.LoadSuperpowers(repoRoot);
            changed = true;
        }

        if (state.McpServers.Count == 0)
        {
            state.McpServers = loader.LoadMcpServers(repoRoot);
            changed = true;
        }

        if (!string.Equals(state.RepoRoot, repoRoot, StringComparison.OrdinalIgnoreCase))
        {
            state.RepoRoot = repoRoot;
            changed = true;
        }

        if (state.Runtime is null)
        {
            state.Runtime = RuntimeConfiguration.CreateDefault();
            changed = true;
        }

        if (state.AgentSessions is null)
        {
            state.AgentSessions = [];
            changed = true;
        }

        if (state.ExecutionSelection is null)
        {
            state.ExecutionSelection = new ExecutionSelectionState();
            changed = true;
        }

        if (state.Runtime.DefaultPipelineRoles.Count == 0)
        {
            state.Runtime.DefaultPipelineRoles = RuntimeConfiguration.CreateDefault().DefaultPipelineRoles;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(state.Runtime.ActiveModeSlug))
        {
            state.Runtime.ActiveModeSlug = RuntimeConfiguration.CreateDefault().ActiveModeSlug;
            changed = true;
        }

        if (state.Modes.Count > 0 && state.Modes.All(mode => !string.Equals(mode.Slug, state.Runtime.ActiveModeSlug, StringComparison.OrdinalIgnoreCase)))
        {
            state.Runtime.ActiveModeSlug = state.Modes.First().Slug;
            changed = true;
        }

        return changed;
    }

    public static RoleModelPolicy GetPolicy(WorkspaceState state, string roleSlug)
    {
        var defaultModel = state.Models.FirstOrDefault(model => model.IsDefault)?.Name ?? "gpt-5-mini";
        var suggested = state.Roles.FirstOrDefault(role => role.Slug == roleSlug)?.SuggestedModel;
        var hasSuggested = !string.IsNullOrWhiteSpace(suggested);
        if (DefaultPolicies.TryGetValue(roleSlug, out var policy))
        {
            return new RoleModelPolicy
            {
                PrimaryModel = hasSuggested ? suggested! : policy.PrimaryModel,
                FallbackModel = policy.FallbackModel,
                AllowPremium = policy.AllowPremium,
                ModelPool = hasSuggested ? [] : policy.ModelPool
            };
        }

        return new RoleModelPolicy
        {
            PrimaryModel = hasSuggested ? suggested! : defaultModel,
            FallbackModel = defaultModel,
            AllowPremium = false
        };
    }
}
