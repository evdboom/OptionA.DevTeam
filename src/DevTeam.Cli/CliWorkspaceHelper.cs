using DevTeam.Core;

namespace DevTeam.Cli;

internal sealed record AssetCopyReport(int Created, int Overwritten, int Skipped);

internal static class CliWorkspaceHelper
{
    internal static void ValidateRoleOrThrow(DevTeamRuntime runtime, WorkspaceState state, string role)
    {
        if (runtime.TryResolveRoleSlug(state, role, out var resolvedRole) && string.Equals(role.Trim(), resolvedRole, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var aliasMap = runtime.GetKnownRoleAliases(state);
        if (aliasMap.TryGetValue(role.Trim(), out var aliasTarget))
        {
            throw new InvalidOperationException(
                $"Role '{role.Trim()}' is an alias. Use the canonical role '{aliasTarget}'.\n{BuildRoleCatalog(runtime, state)}");
        }

        throw new InvalidOperationException(
            $"Unknown role '{role.Trim()}'.\n{BuildRoleCatalog(runtime, state)}");
    }

    internal static string BuildMissingRoleMessage(DevTeamRuntime runtime, WorkspaceState state) =>
        $"Missing --role.\n{BuildRoleCatalog(runtime, state)}";

    internal static string BuildRoleCatalog(DevTeamRuntime runtime, WorkspaceState state)
    {
        var roles = string.Join(", ", runtime.GetKnownRoleSlugs(state));
        var aliases = runtime.GetKnownRoleAliases(state);
        if (aliases.Count == 0)
        {
            return $"Valid roles: {roles}";
        }

        var aliasText = string.Join(", ", aliases.Select(pair => $"{pair.Key} -> {pair.Value}"));
        return $"Valid roles: {roles}\nKnown aliases: {aliasText}";
    }

    internal static bool TryLoadState(WorkspaceStore store, out WorkspaceState? state)
    {
        try
        {
            state = store.Load();
            return true;
        }
        catch (InvalidOperationException)
        {
            state = null;
            return false;
        }
        catch (IOException)
        {
            state = null;
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            state = null;
            return false;
        }
        catch (System.Text.Json.JsonException)
        {
            state = null;
            return false;
        }
    }

    internal static void ApplyKeepAwakeSetting(
        KeepAwakeController controller,
        bool enabled,
        bool interactiveShell,
        Action<string>? log = null)
    {
        if (!enabled)
        {
            controller.SetEnabled(false);
            return;
        }

        try
        {
            controller.SetEnabled(true);
            log?.Invoke(interactiveShell
                ? "Keep-awake enabled for this session."
                : "Keep-awake enabled for this run.");
        }
        catch (InvalidOperationException ex)
        {
            log?.Invoke(ex.Message);
        }
    }

    internal static bool ResolveKeepAwakeEnabled(WorkspaceState state, Dictionary<string, List<string>> options) =>
        CliOptionParser.GetNullableBoolOption(options, "keep-awake") ?? state.Runtime.KeepAwakeEnabled;

    internal static void UpdateBudget(WorkspaceState state, Dictionary<string, List<string>> options)
    {
        var total = CliOptionParser.GetDoubleOption(options, "total", state.Budget.TotalCreditCap);
        var premium = CliOptionParser.GetDoubleOption(options, "premium", state.Budget.PremiumCreditCap);
        state.Budget.TotalCreditCap = total;
        state.Budget.PremiumCreditCap = premium;
    }

    internal static void CopyPackagedAssets(string targetRoot, bool force)
    {
        var sourceRoot = FindPackagedAssetsRoot(targetRoot);

        if (sourceRoot is null)
        {
            throw new InvalidOperationException(
                "No packaged assets found. " +
                "This command copies the built-in roles, modes, and skills so you can customize them.");
        }

        var report = CopyDirectoryContents(sourceRoot, targetRoot, force);

        Console.WriteLine($"Copied assets to {Path.GetFullPath(targetRoot)}");
        Console.WriteLine($"  {report.Created} created, {report.Overwritten} overwritten, {report.Skipped} skipped (use --force to overwrite)");
        Console.WriteLine("Edit these files to customize roles, modes, skills, and model policies.");
    }

    internal static void ExportGitHubSkills(string repoRoot, bool force, Action<string>? log = null)
    {
        var sourceRoot = FindPackagedAssetsRoot(repoRoot);
        if (sourceRoot is null)
        {
            return;
        }

        var skillsSource = Path.Combine(sourceRoot, "skills");
        if (!Directory.Exists(skillsSource))
        {
            return;
        }

        var targetRoot = Path.Combine(repoRoot, ".github", "skills");
        var created = 0;
        var overwritten = 0;
        var skipped = 0;

        foreach (var sourceFile in Directory.EnumerateFiles(skillsSource, "SKILL.md", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var skillDirectory = Path.GetDirectoryName(sourceFile);
            if (string.IsNullOrWhiteSpace(skillDirectory))
            {
                continue;
            }

            var slug = Path.GetFileName(skillDirectory);
            var targetDirectory = Path.Combine(targetRoot, slug);
            var targetFile = Path.Combine(targetDirectory, "SKILL.md");
            Directory.CreateDirectory(targetDirectory);

            if (File.Exists(targetFile) && !force)
            {
                skipped++;
                continue;
            }

            if (File.Exists(targetFile))
            {
                overwritten++;
            }
            else
            {
                created++;
            }

            File.Copy(sourceFile, targetFile, overwrite: true);
        }

        if (created == 0 && overwritten == 0 && skipped == 0)
        {
            return;
        }

        log?.Invoke($"Exported GitHub Copilot skills to {Path.GetFullPath(targetRoot)} ({created} created, {overwritten} overwritten, {skipped} skipped). Use /plan or /tdd style skill names in Copilot when needed.");
    }

    private static string? FindPackagedAssetsRoot(string targetRoot)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, ".devteam-source");
            if (Directory.Exists(candidate) &&
                !string.Equals(Path.GetFullPath(candidate), Path.GetFullPath(targetRoot), StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }

    private static AssetCopyReport CopyDirectoryContents(string sourceRoot, string targetRoot, bool force)
    {
        var created = 0;
        var skipped = 0;
        var overwritten = 0;

        foreach (var sourceFile in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceRoot, sourceFile);
            var targetFile = Path.Combine(targetRoot, relativePath);
            var targetDir = Path.GetDirectoryName(targetFile)!;

            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            if (File.Exists(targetFile) && !force)
            {
                skipped++;
                continue;
            }

            if (File.Exists(targetFile))
            {
                overwritten++;
            }
            else
            {
                created++;
            }

            File.Copy(sourceFile, targetFile, overwrite: true);
        }

        return new AssetCopyReport(created, overwritten, skipped);
    }

    internal static Task<int> EmitBugReport(
        WorkspaceStore store,
        DevTeamRuntime runtime,
        Dictionary<string, List<string>> options,
        ShellSessionDiagnostics? shellDiagnostics)
    {
        var redactPaths = CliOptionParser.GetBoolOption(options, "redact-paths", true);
        var historyCount = CliOptionParser.GetIntOption(options, "history-count", 8);
        var errorCount = CliOptionParser.GetIntOption(options, "error-count", 5);
        var reportText = BugReportBuilder.Build(store, runtime, shellDiagnostics, redactPaths, historyCount, errorCount);
        var savePath = CliOptionParser.GetOption(options, "save");

        if (!string.IsNullOrWhiteSpace(savePath))
        {
            var fullPath = Path.GetFullPath(
                Path.IsPathRooted(savePath)
                    ? savePath
                    : Path.Combine(Environment.CurrentDirectory, savePath));
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(fullPath, reportText);
            Console.WriteLine($"Saved bug report draft to {fullPath}");
            Console.WriteLine();
        }

        Console.WriteLine(reportText.TrimEnd());
        return Task.FromResult(0);
    }
}
