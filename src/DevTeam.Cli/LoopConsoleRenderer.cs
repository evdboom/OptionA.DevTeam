using DevTeam.Core;
using System.Text.RegularExpressions;

namespace DevTeam.Cli;

internal sealed class LoopConsoleRenderer : IDisposable
{
    private static readonly Regex SecretLikeKeyValueRegex = new(
        @"(?ix)(?<key>api[_-]?key|bearer[_-]?token|token|secret|password)\s*[:=]\s*(?<value>[^\s,;]+)",
        RegexOptions.Compiled);
    private static readonly Regex GitHubTokenRegex = new(
        @"\bgh[pousr]_[A-Za-z0-9_]{20,}\b",
        RegexOptions.Compiled);

    private readonly object _gate = new();
    private int _progressLineCount;
    private bool _disposed;

    public void Log(string message)
    {
        lock (_gate)
        {
            ClearProgressBlock();
            Console.WriteLine(RedactSecrets(message));
        }
    }

    public void ReportProgress(IReadOnlyList<RunProgressSnapshot> snapshots)
    {
        lock (_gate)
        {
            ClearProgressBlock();
            foreach (var snapshot in snapshots.OrderBy(item => item.IssueId))
            {
                var scope = snapshot.IssueId is null
                    ? $"{snapshot.RoleSlug,-12} [{Truncate(snapshot.Title, 48)}]"
                    : $"{snapshot.RoleSlug,-12} issue #{snapshot.IssueId,-3} [{Truncate(snapshot.Title, 48)}]";
                Console.WriteLine($"Running {scope} {snapshot.Elapsed.TotalSeconds,4:0}s");
            }
            _progressLineCount = snapshots.Count;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            ClearProgressBlock();
            _disposed = true;
        }
    }

    private void ClearProgressBlock()
    {
        if (_progressLineCount <= 0)
        {
            return;
        }

        for (var index = 0; index < _progressLineCount; index++)
        {
            var targetTop = Console.CursorTop - 1;
            if (targetTop < 0)
            {
                break;
            }

            Console.SetCursorPosition(0, targetTop);
            Console.Write(new string(' ', Math.Max(1, Console.BufferWidth - 1)));
            Console.SetCursorPosition(0, targetTop);
        }

        _progressLineCount = 0;
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..Math.Max(0, maxLength - 1)] + "…";
    }

    private static string RedactSecrets(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var redacted = SecretLikeKeyValueRegex.Replace(value, "${key}: [REDACTED]");
        redacted = GitHubTokenRegex.Replace(redacted, "[REDACTED_GITHUB_TOKEN]");
        return redacted;
    }
}
