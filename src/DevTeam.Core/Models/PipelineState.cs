using System.Text.Json.Serialization;

namespace DevTeam.Core;

public sealed class PipelineState
{
    public int Id { get; set; }
    public int RootIssueId { get; set; }
    public string FamilyKey { get; set; } = "";
    public string Area { get; set; } = "";
    public List<string> RoleSequence { get; set; } = [];
    public List<int> IssueIds { get; set; } = [];
    public int? ActiveIssueId { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PipelineStatus Status { get; set; } = PipelineStatus.Open;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
