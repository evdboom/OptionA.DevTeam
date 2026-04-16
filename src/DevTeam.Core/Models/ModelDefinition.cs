namespace DevTeam.Core;

public sealed class ModelDefinition
{
    public string Name { get; set; } = "";
    public string ProviderName { get; set; } = "";
    public double Cost { get; set; }
    public double? InputCostPer1kTokens { get; set; }
    public double? OutputCostPer1kTokens { get; set; }
    public bool IsDefault { get; set; }
    public bool IsPremium { get; set; }
    /// <summary>
    /// AI provider family. If not set explicitly, inferred from <see cref="Name"/>.
    /// Known families: "openai", "anthropic", "google". Anything else = "other".
    /// </summary>
    public string Family { get; set; } = "";

    /// <summary>Returns the effective family for this model, inferring from <see cref="Name"/> when <see cref="Family"/> is blank.</summary>
    public string EffectiveFamily => string.IsNullOrWhiteSpace(Family) ? InferFamily(Name) : Family;

    /// <summary>
    /// Infers the AI-provider family from a model name prefix.
    /// Rules: claude-* → anthropic, gpt-*/o1*/o3*/o4* → openai, gemini-* → google, everything else → other.
    /// </summary>
    public static string InferFamily(string modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName)) return "other";
        var lower = modelName.ToLowerInvariant();
        if (lower.StartsWith("claude-")) return "anthropic";
        if (lower.StartsWith("gpt-") || lower.StartsWith("o1") || lower.StartsWith("o3") || lower.StartsWith("o4")) return "openai";
        if (lower.StartsWith("gemini-")) return "google";
        return "other";
    }

    public double? EstimateCostUsd(int? inputTokens, int? outputTokens)
    {
        double total = 0;
        var hasEstimate = false;

        if (inputTokens is int input && InputCostPer1kTokens is double inputRate)
        {
            total += input / 1000d * inputRate;
            hasEstimate = true;
        }

        if (outputTokens is int output && OutputCostPer1kTokens is double outputRate)
        {
            total += output / 1000d * outputRate;
            hasEstimate = true;
        }

        return hasEstimate ? Math.Round(total, 6) : null;
    }
}
