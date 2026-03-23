using System.Diagnostics;
using System.Text;

namespace DevTeam.Core;

public sealed class GitStatusSnapshot
{
    public string RepositoryRoot { get; init; } = "";
    public Dictionary<string, string> Entries { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public static class GitWorkspace
{
    public static bool IsGitRepository(string workingDirectory)
    {
        try
        {
            RunGit(workingDirectory, "rev-parse", "--show-toplevel");
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool EnsureRepository(string workingDirectory)
    {
        if (IsGitRepository(workingDirectory))
        {
            return false;
        }

        RunGit(workingDirectory, "init");
        return true;
    }

    public static GitStatusSnapshot? TryCaptureStatus(string workingDirectory)
    {
        if (!IsGitRepository(workingDirectory))
        {
            return null;
        }

        var repoRoot = RunGit(workingDirectory, "rev-parse", "--show-toplevel").StdOut.Trim();
        var status = RunGit(repoRoot, "status", "--porcelain=v1");
        return new GitStatusSnapshot
        {
            RepositoryRoot = repoRoot,
            Entries = ParsePorcelain(status.StdOut)
        };
    }

    public static IReadOnlyList<string> StagePathsChangedSince(
        string workingDirectory,
        GitStatusSnapshot? beforeSnapshot)
    {
        if (beforeSnapshot is null || !IsGitRepository(workingDirectory))
        {
            return [];
        }

        var afterSnapshot = TryCaptureStatus(workingDirectory);
        if (afterSnapshot is null)
        {
            return [];
        }

        var pathsToStage = afterSnapshot.Entries
            .Where(pair =>
                !beforeSnapshot.Entries.TryGetValue(pair.Key, out var previousStatus)
                || !string.Equals(previousStatus, pair.Value, StringComparison.Ordinal))
            .Select(pair => pair.Key)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (pathsToStage.Count == 0)
        {
            return [];
        }

        RunGit(afterSnapshot.RepositoryRoot, ["add", "--all", "--", .. pathsToStage]);
        return pathsToStage;
    }

    private static Dictionary<string, string> ParsePorcelain(string stdout)
    {
        var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = stdout.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            if (line.Length < 4)
            {
                continue;
            }

            var status = line[..2];
            var pathPart = line[3..];
            var path = pathPart.Contains(" -> ", StringComparison.Ordinal)
                ? pathPart.Split(" -> ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? pathPart
                : pathPart.Trim();
            if (!string.IsNullOrWhiteSpace(path))
            {
                entries[path] = status;
            }
        }

        return entries;
    }

    private static GitCommandResult RunGit(string workingDirectory, params string[] arguments) =>
        RunGit(workingDirectory, (IReadOnlyList<string>)arguments);

    private static GitCommandResult RunGit(string workingDirectory, IReadOnlyList<string> arguments)
    {
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = Path.GetFullPath(workingDirectory),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                stdout.AppendLine(args.Data);
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                stderr.AppendLine(args.Data);
            }
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start git.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        if (!process.WaitForExit(30_000))
        {
            try { process.Kill(); } catch { }
            throw new InvalidOperationException("Git command timed out after 30 seconds.");
        }
        process.WaitForExit();
        var stdoutText = stdout.ToString();
        var stderrText = stderr.ToString();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderrText) ? "Git command failed." : stderrText.Trim());
        }

        return new GitCommandResult(process.ExitCode, stdoutText, stderrText);
    }

    private sealed record GitCommandResult(int ExitCode, string StdOut, string StdErr);
}
