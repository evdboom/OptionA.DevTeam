# Goal Usage Implementation Guide

Reference document for implementing goal-access constraints (Option B: Hard Constraint) in DevTeamRuntime.

## Current State

All roles receive the full workspace context including `ActiveGoal`. This allows independent interpretation and causes goal-drift (Playground vs Interactive duplication).

## Proposed Architecture

```csharp
// Current: AgentContext includes Goal
class AgentContext {
    public Goal Goal { get; set; }           // ← All roles see this
    public Plan Plan { get; set; }
    public List<Decision> Decisions { get; set; }
    public Issue CurrentIssue { get; set; }
}

// Proposed: Goal only visible to Planner and Orchestrator
class PlanningContext {
    public Goal Goal { get; set; }           // ← Only Planner sees this
    public string GoalText { get; set; }
}

class ExecutionContext {
    // Goal explicitly excluded
    public Plan Plan { get; set; }           // Approved plan + roadmap
    public List<Decision> Decisions { get; set; }
    public Issue CurrentIssue { get; set; }
}

class OrchestratorContext {
    public Goal Goal { get; set; }           // ← Orchestrator sees goal (tiebreaker)
    public Plan Plan { get; set; }
    public List<Decision> Decisions { get; set; }
    public List<Issue> BacklogIssues { get; set; }
    public List<Question> BacklogQuestions { get; set; }
}
```

## Files to Modify

### 1. `src/DevTeam.Core/AgentInteractionModels/AgentContext.cs`
- Add role-aware context classes
- Keep backward compatibility for now (pass full context, but mark fields as deprecated)
- Document: "Goal should only be read by Planner role"

### 2. `src/DevTeam.Core/ExecutionLoop/LoopExecutor.cs`
- Identify where context is built
- Find lines that assign `Goal` to agent context
- Add conditional: if role != "planner" and role != "orchestrator", exclude goal

**Search for:**
```
var context = new AgentContext { Goal = workspace.ActiveGoal, ... };
```

**Change to:**
```
var context = new AgentContext {
    Goal = (role == "planner" || role == "orchestrator") ? workspace.ActiveGoal : null,
    ...
};
```

### 3. `.devteam-source/roles/<role>/ROLE.md`
- Add preamble to non-planner roles:
  ```markdown
  > **Goal Access:** You cannot read the original goal directly. 
  > Instead, use:
  > - **Plan**: The approved high-level strategy
  > - **Decisions**: Architectural and naming choices (e.g., "Decision #42: Use Playground as implementation vehicle")
  > - **Issue**: Your assigned work unit
  >
  > If you need to make a choice that contradicts an earlier decision, escalate in your handoff.
  ```

### 4. `src/DevTeam.Core/Models.cs`
- Check `AgentContext` definition
- Add comments documenting which roles should access which fields
- Example:
  ```csharp
  public class AgentContext {
      /// <summary>
      /// Original goal (read-only after planning). 
      /// Only Planner and Orchestrator should read this.
      /// Other roles should use Plan, Decisions, and Issue instead.
      /// </summary>
      public Goal Goal { get; set; }
  }
  ```

## Validation Logic (Option C Alternative)

If you don't want to exclude Goal entirely, add validation instead:

```csharp
class IssueValidator {
    public ValidationResult ValidateNewIssue(Issue issue, List<Decision> decisions, Plan plan) {
        // Fail if issue contradicts an earlier decision without referencing it
        if (!issue.Description.Contains("Decision #") && 
            issue.Description.Contains("instead of") || 
            issue.Description.Contains("alternative")) {
            return Fail("New architectural choice must link to Decision #N");
        }
        
        // Fail if issue scope is outside roadmap without justification
        if (!plan.RoadmapItems.Any(r => r.IssueId == issue.Id) &&
            !issue.Justification.Contains("escalation")) {
            return Fail("Issue not in approved roadmap; needs escalation");
        }
        
        return OK;
    }
}
```

Then in `LoopExecutor`:
```csharp
var validation = validator.ValidateNewIssue(newIssue, decisions, plan);
if (!validation.IsValid) {
    logger.LogError($"Issue rejected: {validation.Message}");
    // Either: break the loop, or mark issue as "needs user approval"
}
```

## Testing Implications

### Unit Tests

Add tests to verify role context isolation:

```csharp
[Fact]
public void NonPlanner_DoesNotReceiveGoal() {
    var context = BuildContextForRole("developer");
    Assert.Null(context.Goal);
}

[Fact]
public void Planner_ReceivesGoal() {
    var context = BuildContextForRole("planner");
    Assert.NotNull(context.Goal);
}

[Fact]
public void DecisionLinkageRequired_OnNewIssue() {
    var issue = new Issue { 
        Description = "Use Interactive instead of Playground" // Missing Decision link
    };
    var result = validator.Validate(issue, decisions);
    Assert.False(result.IsValid);
}
```

### Smoke Tests

Add an integration test:
```
Scenario: Goal-drift detection
1. Create workspace with Goal="Interactive"
2. Run planner → Plan says "use Interactive"
3. Run architect → Decision #1: "Playground is implementation"
4. Verify: Later issues link to Decision #1, not reinterpreting Goal
5. If an issue contradicts Decision #1, it's flagged or rejected
```

## Rollout Plan

1. **Phase 1 (Current):** Backlog-manager skill + documentation (orchestrator-side)
2. **Phase 2 (Future):** Add role-specific context classes (backward-compatible)
3. **Phase 3 (Future):** Validation logic in LoopExecutor
4. **Phase 4 (Future):** Make Goal truly inaccessible to non-planners (breaking change)

## Monitoring

Track these metrics to validate the fix works:

```json
{
  "backlog_health": {
    "duplicate_issues_per_batch": 0,  // Was: 5-10, target: 0-1
    "goal_drift_escalations": 0,      // New metric, target: 0
    "decision_linkage_pct": 100,      // % of issues referencing a Decision
    "goal_reinterpretations": 0       // Roles making different architectural choices
  }
}
```

## Related Decision Records

- Decision #93 (assumed): "Playground is implementation vehicle for Interactive requirements"
- ROADMAP #8: Navigator as inline scout (detects conflicts early)
- ROADMAP #10: Orchestrator-driven loop (external checkpoints + spawn_agent)
