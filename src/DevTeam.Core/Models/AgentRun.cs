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
    public List<string> SuperpowersUsed { get; set; } = [];
    public List<string> ToolsUsed { get; set; } = [];
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}