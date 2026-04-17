using System.Text.Json.Serialization;

namespace DevTeam.Core;

public sealed class IssueItem
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    public string Area { get; set; } = "";
    public string FamilyKey { get; set; } = "";
    public bool IsPlanningIssue { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ItemStatus Status { get; set; } = ItemStatus.Open;
    public string RoleSlug { get; set; } = "developer";
    public int Priority { get; set; } = 50;
    public int? RoadmapItemId { get; set; }
    public List<int> DependsOnIssueIds { get; set; } = [];
    public int? ParentIssueId { get; set; }
    public int? PipelineId { get; set; }
    public int? PipelineStageIndex { get; set; }
    public string Notes { get; set; } = "";
    public string ExternalReference { get; set; } = "";
    /// <summary>
    /// Optional 0–100 complexity signal. 0 = trivial, 100 = very complex / cross-cutting.
    /// Used by the orchestrator to decide whether to inject a navigator preflight issue.
    /// </summary>
    public int? ComplexityHint { get; set; }
}
