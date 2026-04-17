using System.Text.RegularExpressions;

namespace DevTeam.Core;

internal static partial class UsageTelemetryExtractor
{
    [GeneratedRegex("""["']?(?:prompt_tokens|input_tokens|promptTokens|inputTokens)["']?\s*[:=]\s*(\d+)""", RegexOptions.IgnoreCase)]
    private static partial Regex InputTokensRegex();

    [GeneratedRegex("""["']?(?:completion_tokens|output_tokens|completionTokens|outputTokens)["']?\s*[:=]\s*(\d+)""", RegexOptions.IgnoreCase)]
    private static partial Regex OutputTokensRegex();

    internal static (int? InputTokens, int? OutputTokens, double? EstimatedCostUsd) Extract(ModelDefinition? model, AgentInvocationResult response)
    {
        var combined = string.Join(
            Environment.NewLine,
            new[] { response.StdOut, response.StdErr }
                .Where(value => !string.IsNullOrWhiteSpace(value)));

        var inputTokens = response.InputTokens ?? MatchValue(InputTokensRegex(), combined);
        var outputTokens = response.OutputTokens ?? MatchValue(OutputTokensRegex(), combined);
        var estimatedCostUsd = model?.EstimateCostUsd(inputTokens, outputTokens);
        return (inputTokens, outputTokens, estimatedCostUsd);
    }

    private static int? MatchValue(Regex regex, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = regex.Match(text);
        return match.Success && int.TryParse(match.Groups[1].Value, out var value)
            ? value
            : null;
    }
}
