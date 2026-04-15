namespace DevTeam.Core;

/// <summary>
/// One-shot codebase reconnaissance. Runs a read-only Navigator-style agent pass against
/// the repository and produces a <c>CODEBASE_CONTEXT.md</c> that gets injected into every
/// subsequent planner/architect prompt so the AI avoids duplicating existing patterns.
/// </summary>
public sealed class ReconService(IAgentClientFactory agentClientFactory)
{
    private readonly IAgentClientFactory _agentClientFactory = agentClientFactory;

    /// <summary>
    /// Runs the recon agent and returns the markdown context summary.
    /// Stores the result in <paramref name="state"/> and persists it to disk via <paramref name="store"/>.
    /// </summary>
    public async Task<string> RunAsync(
        WorkspaceState state,
        WorkspaceStore store,
        string backend,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var client = _agentClientFactory.Create(backend);
        var prompt = BuildReconPrompt(state.RepoRoot, state.ActiveGoal?.GoalText);
        var result = await client.InvokeAsync(new AgentInvocationRequest
        {
            Prompt = prompt,
            WorkingDirectory = state.RepoRoot,
            Timeout = timeout
        }, cancellationToken);

        var parsed = AgentPromptBuilder.ParseResponse(result);
        var context = parsed.Summary.Trim();
        if (string.IsNullOrWhiteSpace(context))
        {
            context = result.StdOut.Trim();
        }

        state.CodebaseContext = context;
        store.Save(state);
        return context;
    }

    /// <summary>Builds the recon prompt for a repository.</summary>
    internal static string BuildReconPrompt(string repoRoot, string? goal)
    {
        var goalSection = string.IsNullOrWhiteSpace(goal)
            ? ""
            : $"\nActive goal (for context only — do not implement):\n{goal.Trim()}\n";

        return $"""
        You are a codebase reconnaissance agent. Your mission is to explore the repository and produce
        a concise, structured codebase context document that will be used to guide an AI development team.
        This is a READ-ONLY pass — do NOT create, edit, or delete any files.

        Repository root: {repoRoot}{goalSection}

        Using read-only tools (rg, ls/dir, cat, find, git log), investigate:
        1. **Tech stack** — programming languages, frameworks, key package references (package.json, *.csproj, requirements.txt, go.mod, etc.)
        2. **Folder structure** — primary source directories and their purpose (max 2 levels deep)
        3. **Test framework** — test runner, test directory location, naming convention (*.spec.ts, *Tests.cs, test_*.py, etc.)
        4. **Entry points** — main executables, CLI commands, API routes, or startup files
        5. **Coding conventions** — naming patterns (PascalCase/camelCase), error handling style, file size norms, DI patterns
        6. **Fragile areas** — any files >400 LOC, directories without tests, obvious tech debt markers

        Write your findings as a concise markdown document (300–500 words). Use headers. Be specific and factual.
        Avoid guessing — if you cannot determine something from the files, say "not found".

        Reply in exactly this shape:
        OUTCOME: completed|failed
        SUMMARY:
        <your codebase context markdown document>
        ISSUES:
        (none)
        SUPERPOWERS_USED:
        (none)
        TOOLS_USED:
        - <tools you actually used>
        QUESTIONS:
        (none)
        """;
    }
}
