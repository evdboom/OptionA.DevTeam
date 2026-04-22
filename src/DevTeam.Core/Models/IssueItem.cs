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

    /// <summary>
    /// Tracks whether this issue has been scoped by the backlog-manager (PO triage).
    /// Planned = just created; NeedsRefinement = requires a scout/refine sub-issue;
    /// ReadyToPickup = scoped, FilesInScope and LinkedDecisionIds populated.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public IssueRefinementState RefinementState { get; set; } = IssueRefinementState.Planned;

    /// <summary>
    /// File paths that the executing agent should focus on. Populated during refinement.
    /// Advisory: agents are expected to stay within this list unless they discover new dependencies.
    /// </summary>
    public List<string> FilesInScope { get; set; } = [];

    /// <summary>
    /// Decision record IDs that are relevant to this issue. Populated during refinement.
    /// Agents call get_decisions(LinkedDecisionIds) to retrieve context without reading all decisions.
    /// </summary>
    public List<int> LinkedDecisionIds { get; set; } = [];
}
