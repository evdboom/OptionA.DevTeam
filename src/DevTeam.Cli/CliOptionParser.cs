using System.Text;
using DevTeam.Core;

namespace DevTeam.Cli;

internal static class CliOptionParser
{
    internal const int MaxGoalLength = 100_000;
    internal const int MaxWorkspacePathLength = 512;

    internal static Dictionary<string, List<string>> ParseOptions(string[] tokens)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var positional = new List<string>();

        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index];
            if (token.StartsWith("--", StringComparison.Ordinal))
            {
                var key = token[2..];
                if (!result.TryGetValue(key, out var values))
                {
                    values = [];
                    result[key] = values;
                }

                while (index + 1 < tokens.Length && !tokens[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    values.Add(tokens[++index]);
                }

                if (values.Count == 0)
                {
                    values.Add("true");
                }
            }
            else
            {
                positional.Add(token);
            }
        }

        result["__positional"] = positional;
        return result;
    }

    internal static List<string> TokenizeInput(string input)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var ch in input)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    internal static string NormalizeCommand(string value) =>
        value.Trim().TrimStart('/').ToLowerInvariant();

    internal static string NormalizeWorkspacePathOrThrow(string? workspacePath, string fallback = ".devteam")
    {
        var candidate = string.IsNullOrWhiteSpace(workspacePath) ? fallback : workspacePath.Trim();
        if (candidate.Length > MaxWorkspacePathLength)
        {
            throw new InvalidOperationException($"Workspace path is too long. Maximum length is {MaxWorkspacePathLength} characters.");
        }

        if (candidate.IndexOf('\0') >= 0)
        {
            throw new InvalidOperationException("Workspace path contains invalid control characters.");
        }

        // Keep compatibility with absolute workspace paths used by smoke tests and automation,
        // while still normalizing to a canonical path.
        return Path.GetFullPath(candidate);
    }

    internal static string ValidateGoalTextOrThrow(string goalText)
    {
        var normalized = goalText.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("Goal text cannot be empty.");
        }

        if (normalized.Length > MaxGoalLength)
        {
            throw new InvalidOperationException($"Goal text is too long. Maximum length is {MaxGoalLength} characters.");
        }

        return normalized;
    }

    internal static string? GetOption(Dictionary<string, List<string>> options, string key) =>
        ResolveOptionValues(options, key) is { Count: > 0 } values ? string.Join(" ", values) : null;

    internal static int GetIntOption(Dictionary<string, List<string>> options, string key, int fallback) =>
        int.TryParse(GetOption(options, key), out var value) ? value : fallback;

    internal static double GetDoubleOption(Dictionary<string, List<string>> options, string key, double fallback) =>
        double.TryParse(GetOption(options, key), out var value) ? value : fallback;

    internal static bool GetBoolOption(Dictionary<string, List<string>> options, string key, bool fallback)
    {
        var value = GetOption(options, key);
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "true" or "1" or "yes" or "on" => true,
            "false" or "0" or "no" or "off" => false,
            _ => fallback
        };
    }

    internal static bool? GetNullableBoolOption(Dictionary<string, List<string>> options, string key)
    {
        var value = GetOption(options, key);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return ParseBoolOrThrow(value, $"Invalid boolean value '{value}'. Use true/false, yes/no, or on/off.");
    }

    internal static bool ParseBoolOrThrow(string value, string errorMessage) =>
        value.Trim().ToLowerInvariant() switch
        {
            "true" or "1" or "yes" or "on" => true,
            "false" or "0" or "no" or "off" => false,
            _ => throw new InvalidOperationException(errorMessage)
        };

    internal static int? GetNullableIntOption(Dictionary<string, List<string>> options, string key) =>
        int.TryParse(GetOption(options, key), out var value) ? value : null;

    internal static IReadOnlyList<int> GetMultiIntOption(Dictionary<string, List<string>> options, string key) =>
        ResolveOptionValues(options, key) is { Count: > 0 } values
            ? values.Select(int.Parse).ToList()
            : [];

    internal static List<string>? ResolveOptionValues(Dictionary<string, List<string>> options, string key)
    {
        if (options.TryGetValue(key, out var values))
        {
            return values;
        }

        return key switch
        {
            "max-iterations" when options.TryGetValue("max-iteration", out var aliasValues) => aliasValues,
            _ => null
        };
    }

    internal static string? GetPositionalValue(Dictionary<string, List<string>> options) =>
        options.TryGetValue("__positional", out var values) && values.Count > 0 ? string.Join(" ", values) : null;

    internal static IReadOnlyList<string> GetPositionalValues(Dictionary<string, List<string>> options) =>
        options.TryGetValue("__positional", out var values) ? values : [];

    internal static bool IsApproveIntent(string line) =>
        line.Equals("approve", StringComparison.OrdinalIgnoreCase) ||
        line.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
        line.Equals("y", StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("approve ", StringComparison.OrdinalIgnoreCase);

    internal static LoopVerbosity ParseVerbosity(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "quiet" => LoopVerbosity.Quiet,
            "detailed" => LoopVerbosity.Detailed,
            _ => LoopVerbosity.Normal
        };
}
