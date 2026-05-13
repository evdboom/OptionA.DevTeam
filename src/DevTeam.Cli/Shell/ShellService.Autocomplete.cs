using DevTeam.Core;

namespace DevTeam.Cli.Shell;

// Tab-completion support for the interactive shell.
internal sealed partial class ShellService
{
    /// <summary>
    /// User-facing slash commands recognised by the interactive shell.
    /// Primary form only — aliases (e.g. "quit", "run-loop") are omitted so
    /// the autocomplete list stays concise. Hidden extras (e.g. "adventure")
    /// are intentionally excluded to preserve their discovery aspect.
    /// </summary>
    private static readonly string[] KnownCommands =
    [
        "add-issue",
        "answer",
        "approve",
        "brownfield-log",
        "budget",
        "bug",
        "check-update",
        "connect",
        "customize",
        "diff-run",
        "disconnect",
        "edit-issue",
        "exit",
        "export",
        "feedback",
        "goal",
        "help",
        "history",
        "import",
        "init",
        "keep-awake",
        "max-iterations",
        "max-subagents",
        "mode",
        "pipeline",
        "plan",
        "preview",
        "provider",
        "questions",
        "recon",
        "roles",
        "run",
        "set-auto-approve",
        "set-mode",
        "set-pipeline",
        "set-provider",
        "start-here",
        "status",
        "stop",
        "sync",
        "update",
        "wait",
        "worktrees",
    ];

    /// <summary>
    /// Returns tab-completion candidates for the current input buffer.
    /// The returned strings are full replacements for the input (including / or @ prefix).
    /// </summary>
    internal IReadOnlyList<string> GetCompletions(string input)
    {
        if (string.IsNullOrEmpty(input))
            return [];

        // Slash-command completion: "/sta" → ["/status", "/start-here", "/stop"]
        if (input.StartsWith("/", StringComparison.Ordinal))
        {
            var partial = input[1..].ToLowerInvariant();
            return KnownCommands
                .Where(cmd => cmd.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                .Select(cmd => "/" + cmd)
                .ToList();
        }

        // Role completion: "@arc" → ["@architect"]
        if (input.StartsWith("@", StringComparison.Ordinal))
        {
            var partial = input[1..];
            // Only complete the role slug portion (no space yet typed)
            if (partial.Contains(' '))
                return [];

            TryLoadState(out var state);
            if (state is null)
                return [];

            return DevTeamRuntime.GetKnownRoleSlugs(state)
                .Where(slug => slug.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                .Select(slug => "@" + slug + " ")
                .ToList();
        }

        return [];
    }
}
