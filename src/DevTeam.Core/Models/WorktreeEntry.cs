using System.Text.Json.Serialization;

namespace DevTeam.Core;

public sealed class WorktreeEntry
{
    public int IssueId { get; set; }
    public int RunId { get; set; }
    public string BranchName { get; set; } = "";
    public string WorktreePath { get; set; } = "";
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public WorktreeStatus Status { get; set; } = WorktreeStatus.Active;
}
