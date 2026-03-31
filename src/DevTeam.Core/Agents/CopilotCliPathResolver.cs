
namespace DevTeam.Core;

internal static class CopilotCliPathResolver
{
    private static readonly string ExecutableName = OperatingSystem.IsWindows() ? "copilot.exe" : "copilot";

    public static string Resolve()
    {
        return Resolve(Environment.GetEnvironmentVariable("PATH"));
    }

    internal static string Resolve(string? pathValue)
    {
        var cliPath = TryResolveFromPath(pathValue);
        if (!string.IsNullOrWhiteSpace(cliPath))
        {
            return cliPath;
        }

        throw new InvalidOperationException(
            $"GitHub Copilot CLI is required for the SDK backend. Install GitHub Copilot and ensure '{ExecutableName}' is available on PATH.");
    }

    internal static string? TryResolveFromPath(string? pathValue = null)
    {
        pathValue ??= Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return null;
        }

        foreach (var rawSegment in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var segment = rawSegment.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(segment))
            {
                continue;
            }

            var candidate = Path.Combine(segment, ExecutableName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}