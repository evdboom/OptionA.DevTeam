using System.Diagnostics.CodeAnalysis;

namespace DevTeam.Core;

internal static class SeedData
{
    private static readonly Dictionary<string, RoleModelPolicy> DefaultPolicies = new(StringComparer.OrdinalIgnoreCase)
    {
        ["orchestrator"] = new() { PrimaryModel = CoreConstants.Models.ClaudeSonnet46, FallbackModel = CoreConstants.Models.ClaudeHaiku45, AllowPremium = false },
        ["planner"] = new() { PrimaryModel = CoreConstants.Models.ClaudeSonnet46, FallbackModel = CoreConstants.Models.ClaudeHaiku45, AllowPremium = false },
        ["architect"] = new() { PrimaryModel = CoreConstants.Models.ClaudeOpus46, FallbackModel = CoreConstants.Models.Gpt54, AllowPremium = true, ModelPool = [CoreConstants.Models.ClaudeOpus46, CoreConstants.Models.ClaudeOpus47, CoreConstants.Models.Gpt55] },
        ["developer"] = new() { PrimaryModel = CoreConstants.Models.Gpt54, FallbackModel = CoreConstants.Models.Gpt54Mini, AllowPremium = false, ModelPool = [CoreConstants.Models.Gpt54, CoreConstants.Models.ClaudeSonnet46] },
        ["backend-developer"] = new() { PrimaryModel = CoreConstants.Models.Gpt54, FallbackModel = CoreConstants.Models.Gpt54Mini, AllowPremium = false, ModelPool = [CoreConstants.Models.Gpt54, CoreConstants.Models.ClaudeSonnet46] },
        ["frontend-developer"] = new() { PrimaryModel = CoreConstants.Models.ClaudeSonnet46, FallbackModel = CoreConstants.Models.Gpt54Mini, AllowPremium = false, ModelPool = [CoreConstants.Models.ClaudeSonnet46, CoreConstants.Models.Gpt54] },
        ["fullstack-developer"] = new() { PrimaryModel = CoreConstants.Models.ClaudeSonnet46, FallbackModel = CoreConstants.Models.Gpt54Mini, AllowPremium = false, ModelPool = [CoreConstants.Models.ClaudeSonnet46, CoreConstants.Models.Gpt54] },
        ["tester"] = new() { PrimaryModel = "gemini-3.1-pro-preview", FallbackModel = CoreConstants.Models.Gpt54Mini, AllowPremium = false, ModelPool = ["gemini-3.1-pro-preview", CoreConstants.Models.Gpt54, CoreConstants.Models.ClaudeSonnet46] },
        ["reviewer"] = new() { PrimaryModel = CoreConstants.Models.ClaudeOpus46, FallbackModel = CoreConstants.Models.Gpt54, AllowPremium = true, ModelPool = [CoreConstants.Models.ClaudeOpus46, CoreConstants.Models.ClaudeOpus47, CoreConstants.Models.Gpt55] },
        ["auditor"] = new() { PrimaryModel = CoreConstants.Models.ClaudeOpus46, FallbackModel = CoreConstants.Models.Gpt54, AllowPremium = true, ModelPool = [CoreConstants.Models.ClaudeOpus46, CoreConstants.Models.ClaudeOpus47, CoreConstants.Models.Gpt55] },
        ["ux"] = new() { PrimaryModel = CoreConstants.Models.ClaudeSonnet46, FallbackModel = CoreConstants.Models.ClaudeHaiku45, AllowPremium = false },
        ["user"] = new() { PrimaryModel = CoreConstants.Models.Gpt5Mini, FallbackModel = CoreConstants.Models.Gpt5Mini, AllowPremium = false },
        ["game-designer"] = new() { PrimaryModel = "gemini-3.1-pro-preview", FallbackModel = "gemini-3-flash-preview", AllowPremium = false },
        ["navigator"] = new() { PrimaryModel = CoreConstants.Models.ClaudeOpus46, FallbackModel = CoreConstants.Models.Gpt54, AllowPremium = true, ModelPool = [CoreConstants.Models.ClaudeOpus46, CoreConstants.Models.ClaudeOpus47, CoreConstants.Models.Gpt55] },
        ["analyst"] = new() { PrimaryModel = CoreConstants.Models.ClaudeSonnet46, FallbackModel = CoreConstants.Models.ClaudeHaiku45, AllowPremium = false },
        ["security"] = new() { PrimaryModel = CoreConstants.Models.ClaudeSonnet46, FallbackModel = CoreConstants.Models.ClaudeHaiku45, AllowPremium = false },
        ["docs"] = new() { PrimaryModel = CoreConstants.Models.Gpt5Mini, FallbackModel = CoreConstants.Models.Gpt5Mini, AllowPremium = false },
        ["devops"] = new() { PrimaryModel = CoreConstants.Models.Gpt54, FallbackModel = CoreConstants.Models.Gpt54Mini, AllowPremium = false, ModelPool = [CoreConstants.Models.Gpt54, CoreConstants.Models.ClaudeSonnet46] },
        ["refactorer"] = new() { PrimaryModel = CoreConstants.Models.ClaudeSonnet46, FallbackModel = CoreConstants.Models.Gpt54Mini, AllowPremium = false, ModelPool = [CoreConstants.Models.ClaudeSonnet46, CoreConstants.Models.Gpt54] }
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
        state.Skills = loader.LoadSkills(repoRoot);
        state.McpServers = loader.LoadMcpServers(repoRoot);
        return state;
    }

    [SuppressMessage("Major Code Smell", "S3776", Justification = "Metadata hydration is explicit field-by-field backfill logic.")]
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
            var loadedProviders = loader.LoadProviders(repoRoot);
            if (loadedProviders.Count > 0)
            {
                state.Providers = loadedProviders;
                changed = true;
            }
        }

        if (state.Modes.Count == 0)
        {
            state.Modes = loader.LoadModes(repoRoot);
            changed = true;
        }

        if (state.Skills.Count == 0)
        {
            state.Skills = loader.LoadSkills(repoRoot);
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
            state.Runtime.ActiveModeSlug = state.Modes[0].Slug;
            changed = true;
        }

        return changed;
    }

    public static RoleModelPolicy GetPolicy(WorkspaceState state, string roleSlug)
    {
        var defaultModel = state.Models.FirstOrDefault(model => model.IsDefault)?.Name ?? CoreConstants.Models.Gpt5Mini;
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
