using System.Text.Json.Serialization;

namespace DevTeam.Core;

public sealed class AgentRun
{
    public int Id { get; set; }
    public int IssueId { get; set; }
    public string RoleSlug { get; set; } = "";
    public string ModelName { get; set; } = "";
    public string SessionId { get; set; } = "";
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AgentRunStatus Status { get; set; } = AgentRunStatus.Queued;
    public string Summary { get; set; } = "";
    public double CreditsUsed { get; set; }
    public double PremiumCreditsUsed { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public double? EstimatedCostUsd { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ItemStatus? ResultingIssueStatus { get; set; }
    public List<string> SuperpowersUsed { get; set; } = [];
    public List<string> ToolsUsed { get; set; } = [];
    public List<string> ChangedPaths { get; set; } = [];
    public List<int> CreatedIssueIds { get; set; } = [];
    public List<int> CreatedQuestionIds { get; set; } = [];
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
