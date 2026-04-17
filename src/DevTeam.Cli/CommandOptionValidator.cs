namespace DevTeam.Cli;

internal static class CommandOptionValidator
{
    private const string PositionalKey = "__positional";

    private static readonly Dictionary<string, HashSet<string>> CliCommandOptions = CreateCliCommandOptions();
    private static readonly Dictionary<string, HashSet<string>> InteractiveCommandOptions = CreateInteractiveCommandOptions();

    public static void ValidateCli(string command, IReadOnlyDictionary<string, List<string>> options) =>
        Validate(CliCommandOptions, command, options, interactive: false);

    public static void ValidateInteractive(string command, IReadOnlyDictionary<string, List<string>> options) =>
        Validate(InteractiveCommandOptions, command, options, interactive: true);

    private static void Validate(
        IReadOnlyDictionary<string, HashSet<string>> allowedOptionsByCommand,
        string command,
        IReadOnlyDictionary<string, List<string>> options,
        bool interactive)
    {
        if (!allowedOptionsByCommand.TryGetValue(command, out var allowedOptions))
        {
            return;
        }

        var unknownOptions = options.Keys
            .Where(key => !string.Equals(key, PositionalKey, StringComparison.OrdinalIgnoreCase) && !allowedOptions.Contains(key))
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (unknownOptions.Length == 0)
        {
            return;
        }

        var commandLabel = interactive ? $"/{command}" : command;
        if (unknownOptions.Length == 1)
        {
            var unknownOption = unknownOptions[0];
            var suggestion = FindSuggestion(unknownOption, allowedOptions);
            if (suggestion is not null)
            {
                throw new InvalidOperationException($"Unknown option '--{unknownOption}' for {commandLabel}. Did you mean '--{suggestion}'?");
            }

            throw new InvalidOperationException(
                allowedOptions.Count == 0
                    ? $"Unknown option '--{unknownOption}' for {commandLabel}. This command does not accept options."
                    : $"Unknown option '--{unknownOption}' for {commandLabel}. Supported options: {FormatOptions(allowedOptions)}.");
        }

        throw new InvalidOperationException(
            $"Unknown options for {commandLabel}: {string.Join(", ", unknownOptions.Select(option => $"--{option}"))}. " +
            (allowedOptions.Count == 0
                ? "This command does not accept options."
                : $"Supported options: {FormatOptions(allowedOptions)}."));
    }

    private static string? FindSuggestion(string unknownOption, IReadOnlyCollection<string> allowedOptions)
    {
        var bestDistance = int.MaxValue;
        string? bestMatch = null;

        foreach (var allowedOption in allowedOptions)
        {
            var distance = ComputeLevenshteinDistance(unknownOption, allowedOption);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestMatch = allowedOption;
            }
        }

        if (bestMatch is null)
        {
            return null;
        }

        return bestDistance <= Math.Max(2, bestMatch.Length / 3) ? bestMatch : null;
    }

    private static int ComputeLevenshteinDistance(string left, string right)
    {
        if (left.Length == 0)
        {
            return right.Length;
        }

        if (right.Length == 0)
        {
            return left.Length;
        }

        var previousRow = new int[right.Length + 1];
        var currentRow = new int[right.Length + 1];

        for (var column = 0; column <= right.Length; column++)
        {
            previousRow[column] = column;
        }

        for (var row = 0; row < left.Length; row++)
        {
            currentRow[0] = row + 1;
            for (var column = 0; column < right.Length; column++)
            {
                var substitutionCost = char.ToLowerInvariant(left[row]) == char.ToLowerInvariant(right[column]) ? 0 : 1;
                currentRow[column + 1] = Math.Min(
                    Math.Min(currentRow[column] + 1, previousRow[column + 1] + 1),
                    previousRow[column] + substitutionCost);
            }

            (previousRow, currentRow) = (currentRow, previousRow);
        }

        return previousRow[right.Length];
    }

    private static string FormatOptions(IEnumerable<string> options) =>
        string.Join(", ", options.OrderBy(option => option, StringComparer.OrdinalIgnoreCase).Select(option => $"--{option}"));

    private static Dictionary<string, HashSet<string>> CreateCliCommandOptions()
    {
        var globalOptions = new[] { "workspace" };

        return new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["start"] = CreateOptionSet(sharedOptions: globalOptions, "keep-awake", "no-tty", "output-format"),
            ["check-update"] = CreateOptionSet(sharedOptions: globalOptions),
            ["update"] = CreateOptionSet(sharedOptions: globalOptions),
            ["workspace-mcp"] = CreateOptionSet(sharedOptions: globalOptions),
            ["init"] = CreateOptionSet(sharedOptions: globalOptions, "force", "goal", "goal-file", "mode", "provider", "keep-awake", "total-credit-cap", "premium-credit-cap", "workspace-mcp", "pipeline-scheduling", "auto-approve", "recon", "backend", "timeout-seconds"),
            ["customize"] = CreateOptionSet(sharedOptions: globalOptions, "force"),
            ["export"] = CreateOptionSet(sharedOptions: globalOptions, "output"),
            ["import"] = CreateOptionSet(sharedOptions: globalOptions, "input", "force"),
            ["start-here"] = CreateOptionSet(sharedOptions: globalOptions),
            ["bug"] = CreateOptionSet(sharedOptions: globalOptions, "save", "redact-paths", "history-count", "error-count"),
            ["bug-report"] = CreateOptionSet(sharedOptions: globalOptions, "save", "redact-paths", "history-count", "error-count"),
            ["goal"] = CreateOptionSet(sharedOptions: globalOptions, "goal-file"),
            ["set-goal"] = CreateOptionSet(sharedOptions: globalOptions, "goal-file"),
            ["pipeline"] = CreateOptionSet(sharedOptions: globalOptions),
            ["set-pipeline"] = CreateOptionSet(sharedOptions: globalOptions),
            ["provider"] = CreateOptionSet(sharedOptions: globalOptions),
            ["set-provider"] = CreateOptionSet(sharedOptions: globalOptions),
            ["mode"] = CreateOptionSet(sharedOptions: globalOptions),
            ["set-mode"] = CreateOptionSet(sharedOptions: globalOptions),
            ["keep-awake"] = CreateOptionSet(sharedOptions: globalOptions, "enabled"),
            ["set-keep-awake"] = CreateOptionSet(sharedOptions: globalOptions, "enabled"),
            ["add-roadmap"] = CreateOptionSet(sharedOptions: globalOptions, "detail", "priority"),
            ["add-issue"] = CreateOptionSet(sharedOptions: globalOptions, "role", "area", "detail", "priority", "roadmap-item-id", "depends-on"),
            ["edit-issue"] = CreateOptionSet(sharedOptions: globalOptions, "title", "detail", "role", "area", "clear-area", "priority", "status", "depends-on", "clear-depends", "note"),
            ["add-question"] = CreateOptionSet(sharedOptions: globalOptions, "blocking"),
            ["answer"] = CreateOptionSet(sharedOptions: globalOptions),
            ["answer-question"] = CreateOptionSet(sharedOptions: globalOptions),
            ["approve"] = CreateOptionSet(sharedOptions: globalOptions, "note"),
            ["approve-plan"] = CreateOptionSet(sharedOptions: globalOptions, "note"),
            ["set-auto-approve"] = CreateOptionSet(sharedOptions: globalOptions, "enabled"),
            ["feedback"] = CreateOptionSet(sharedOptions: globalOptions),
            ["preview"] = CreateOptionSet(sharedOptions: globalOptions, "max-subagents"),
            ["diff-run"] = CreateOptionSet(sharedOptions: globalOptions),
            ["brownfield-log"] = CreateOptionSet(sharedOptions: globalOptions),
            ["github-sync"] = CreateOptionSet(sharedOptions: globalOptions),
            ["run-once"] = CreateOptionSet(sharedOptions: globalOptions, "max-subagents"),
            ["run"] = CreateOptionSet(sharedOptions: globalOptions, "backend", "provider", "max-iterations", "max-subagents", "timeout-seconds", "verbosity", "keep-awake", "dry-run"),
            ["run-loop"] = CreateOptionSet(sharedOptions: globalOptions, "backend", "provider", "max-iterations", "max-subagents", "timeout-seconds", "verbosity", "keep-awake", "dry-run"),
            ["complete-run"] = CreateOptionSet(sharedOptions: globalOptions, "run-id", "outcome", "summary"),
            ["status"] = CreateOptionSet(sharedOptions: globalOptions),
            ["questions"] = CreateOptionSet(sharedOptions: globalOptions),
            ["plan"] = CreateOptionSet(sharedOptions: globalOptions, "backend", "provider", "max-iterations", "max-subagents", "timeout-seconds", "verbosity", "keep-awake"),
            ["budget"] = CreateOptionSet(sharedOptions: globalOptions, "total", "premium"),
            ["agent-invoke"] = CreateOptionSet(sharedOptions: globalOptions, "backend", "provider", "prompt", "model", "timeout-seconds", "working-directory", "extra-arg", "workspace-mcp"),
            ["ui-harness"] = CreateOptionSet(sharedOptions: globalOptions, "scenario")
        };
    }

    private static Dictionary<string, HashSet<string>> CreateInteractiveCommandOptions() =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["exit"] = CreateOptionSet(),
            ["quit"] = CreateOptionSet(),
            ["help"] = CreateOptionSet(),
            ["check-update"] = CreateOptionSet(),
            ["update"] = CreateOptionSet(),
            ["customize"] = CreateOptionSet("force"),
            ["export"] = CreateOptionSet("output"),
            ["import"] = CreateOptionSet("input", "force"),
            ["start-here"] = CreateOptionSet(),
            ["bug"] = CreateOptionSet("save", "redact-paths", "history-count", "error-count"),
            ["bug-report"] = CreateOptionSet("save", "redact-paths", "history-count", "error-count"),
            ["init"] = CreateOptionSet("force", "goal", "goal-file", "mode", "provider", "keep-awake", "total-credit-cap", "premium-credit-cap", "workspace-mcp", "pipeline-scheduling", "recon", "backend", "timeout-seconds"),
            ["status"] = CreateOptionSet(),
            ["add-issue"] = CreateOptionSet("role", "area", "detail", "priority", "roadmap-item-id", "depends-on"),
            ["edit-issue"] = CreateOptionSet("title", "detail", "role", "area", "clear-area", "priority", "status", "depends-on", "clear-depends", "note"),
            ["questions"] = CreateOptionSet(),
            ["plan"] = CreateOptionSet("backend", "provider", "max-iterations", "max-subagents", "timeout-seconds", "verbosity", "keep-awake"),
            ["goal"] = CreateOptionSet("goal-file"),
            ["set-goal"] = CreateOptionSet("goal-file"),
            ["pipeline"] = CreateOptionSet(),
            ["set-pipeline"] = CreateOptionSet(),
            ["provider"] = CreateOptionSet(),
            ["set-provider"] = CreateOptionSet(),
            ["mode"] = CreateOptionSet(),
            ["set-mode"] = CreateOptionSet(),
            ["keep-awake"] = CreateOptionSet("enabled"),
            ["set-keep-awake"] = CreateOptionSet("enabled"),
            ["approve"] = CreateOptionSet("note"),
            ["approve-plan"] = CreateOptionSet("note"),
            ["auto-approve"] = CreateOptionSet("enabled"),
            ["set-auto-approve"] = CreateOptionSet("enabled"),
            ["feedback"] = CreateOptionSet(),
            ["preview"] = CreateOptionSet("max-subagents"),
            ["diff-run"] = CreateOptionSet(),
            ["brownfield-log"] = CreateOptionSet(),
            ["sync"] = CreateOptionSet(),
            ["github-sync"] = CreateOptionSet(),
            ["answer"] = CreateOptionSet(),
            ["answer-question"] = CreateOptionSet(),
            ["run"] = CreateOptionSet("backend", "provider", "max-iterations", "max-subagents", "timeout-seconds", "verbosity", "keep-awake", "dry-run"),
            ["run-loop"] = CreateOptionSet("backend", "provider", "max-iterations", "max-subagents", "timeout-seconds", "verbosity", "keep-awake", "dry-run"),
            ["run-once"] = CreateOptionSet("max-subagents"),
            ["stop"] = CreateOptionSet(),
            ["wait"] = CreateOptionSet(),
            ["budget"] = CreateOptionSet("total", "premium"),
            ["recon"] = CreateOptionSet("backend", "timeout-seconds"),
            ["worktrees"] = CreateOptionSet()
        };

    private static HashSet<string> CreateOptionSet(params string[] commandOptions) =>
        CreateOptionSet(null, commandOptions);

    private static HashSet<string> CreateOptionSet(IEnumerable<string>? sharedOptions, params string[] commandOptions)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (sharedOptions is not null)
        {
            foreach (var option in sharedOptions)
            {
                values.Add(option);
            }
        }

        foreach (var option in commandOptions)
        {
            values.Add(option);
        }

        return values;
    }
}
