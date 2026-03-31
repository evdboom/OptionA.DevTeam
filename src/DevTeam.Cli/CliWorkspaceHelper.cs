using DevTeam.Core;

namespace DevTeam.Cli;

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
        string? sourceRoot = null;
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, ".devteam-source");
            if (Directory.Exists(candidate) &&
                !string.Equals(Path.GetFullPath(candidate), Path.GetFullPath(targetRoot), StringComparison.OrdinalIgnoreCase))
            {
                sourceRoot = candidate;
                break;
            }
            current = current.Parent;
        }

        if (sourceRoot is null)
        {
            throw new InvalidOperationException(
                "No packaged assets found. " +
                "This command copies the built-in roles, modes, and superpowers so you can customize them.");
        }

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

        Console.WriteLine($"Copied assets to {Path.GetFullPath(targetRoot)}");
        Console.WriteLine($"  {created} created, {overwritten} overwritten, {skipped} skipped (use --force to overwrite)");
        Console.WriteLine("Edit these files to customize roles, modes, superpowers, and model policies.");
    }

    internal static void EmitBugReport(
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
    }
}
