namespace DevTeam.Core;

public enum IssueRefinementState
{
    /// <summary>Just created by architect, planner, or agent — not yet triaged by the PO.</summary>
    Planned,

    /// <summary>
    /// Backlog-manager determined this issue needs scoping before execution.
    /// A refinement sub-issue (navigator, architect, or developer) has been created and must complete first.
    /// </summary>
    NeedsRefinement,

    /// <summary>
    /// Scoped and ready to pick up. FilesInScope and LinkedDecisionIds have been populated.
    /// The executing agent should use get_issue + get_decisions MCP tools rather than reading full workspace context.
    /// </summary>
    ReadyToPickup
}
