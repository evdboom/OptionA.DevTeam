---
name: backlog-manager
description: Review, consolidate, and manage the execution backlog. Use when the loop has created many issues/questions and you need to prevent duplicates, merge related work, abandon stale items, and enforce goal alignment. Orchestrator skill for checking goal-drift and backlog health.
allowed-tools: [get_workspace_summary, list_ready_issues, create_issue, get_issue, get_decisions]
---
# Skill: Backlog Manager (Product Owner)

As the orchestrator, use this skill to periodically audit your backlog and prevent wildgrowth—unnecessary duplication, scope creep, and goal-drift that happens when roles create work independently.

## Stack Neutrality

- Apply this process to any stack (Java, Node, Python, .NET, Go, etc.).
- Treat framework names in examples as placeholders for whichever stack the repository actually uses.

## When to Use

- **After run batches complete** (every 5–10 runs)
- **When question count exceeds issue count** (symptom of confused work)
- **When you notice duplicates** (e.g., "Playground" AND "Interactive" projects)
- **Before queuing the next batch of runs**
- **When budget pressure rises** (avoid wasting credits on redundant work)

## Core Pattern: Scan → Identify → Consolidate → Queue

### 1. Scan for Duplicates and Naming Conflicts

**What to look for:**
- Two issues with nearly identical titles (different wording, same intent)
- Multiple issues trying to achieve the same technical goal (e.g., scaffold same project twice)
- Naming ambiguity from goal interpretation (goal said "Interactive"; issues created both "Playground" AND "Interactive")
- Role-generated issues that contradict earlier decisions (architect designed A, developer later proposes B)

**How to spot:**
```
Issues #7, #19, #90:  All about scaffolding a playground/component editor
Issues #66, #70:      Both scoping an "Interactive" package
Issue #115:           "Decide interactive dependency strategy" — suggests conflict emerged
```

**Action:**
- List each duplicate cluster with issue numbers
- Link each cluster to the DECISION entry that justified the original work
- Flag which duplicate(s) can be merged, which should be closed

### 2. Identify Stale Questions and Abandoned Paths

**What to look for:**
- Questions that reference issues now closed as "superseded"
- Questions answered by a decision but never closed
- Blocking questions that don't block anything anymore

**Example from your backlog:**
```
Questions may reference the Playground vs Interactive naming conflict.
If decision #93 settled on "Playground", questions about "should it be interactive?" are now stale.
```

**Action:**
- Close resolved questions
- Update blocking questions if their blocker resolved
- Suggest moving question answers into DECISION artifacts

### 3. Enforce Goal Alignment

**Golden Rule: Goal is read-only after Planning approval.**

Goal stated: **"OptionA.Blazor.Interactive (name to be discussed)"**
What happened: Loop created **both** "Playground" AND "Interactive" projects

**How to detect drift:**
```
Check: Does the CURRENT active issue set match the approved plan?
- Approved plan was about "Interactive" + component editor
- Backlog now includes "Playground" + separate "Interactive"
- This is goal-drift, not goal-execution
```

**Questions to ask:**
1. Did we approve moving from "Interactive" to "Playground" as the implementation target?
2. Does the DECISION log show this choice?
3. If not, which set of issues should we close/merge to stay on goal?

**Action:**
- Find the DECISION that changed direction (if it exists)
- If no decision exists, escalate: "Goal says Interactive, backlog says Playground—need approval"
- Link duplicate issues to the decision so future runs don't repeat the mistake

### 4. Propose Consolidation and Cleanup

**Consolidation Matrix:**

| Scenario | Action | Decision Needed |
|----------|--------|-----------------|
| Issue A and B both implement "Feature X" | Merge B into A, close B, update A scope | Which branch is further along? |
| Question Q answered by Decision D | Close Q, reference D in Q | Is the decision stable? |
| Deprecated issue still in queue | Close, mark superseded | Did replacement issue succeed? |
| Two roles designed conflicting APIs | Create "resolve conflict" issue, block both | Who decides: architect or developer? |

**Template for proposing consolidation:**

```
## Consolidation Proposal

### Cluster 1: Playground vs Interactive Naming
**Issues:** #7, #66, #90, #91 (and others)
**Problem:** Goal asked for "Interactive"; we created both "Playground" + "Interactive"
**Status:** 
  - Playground scaffold: DONE (issue #7)
  - Interactive scaffold: PLANNED (issue #90)
  - Interactive design: IN_PROGRESS (issue #66)
**Recommendation:** 
  Playground became the de facto implementation. Merge #90 into #7, close #66 as superseded.
  Update decision log: "Decision: Use Playground as the implementation vehicle for Interactive requirements"
**Decision:** NEEDS USER APPROVAL
```

### 5. Batch Close Superseded Work

When consolidation happens, batch-close the losers:

**Before closing:**
- [ ] Link the closed issue to the winner (cross-reference)
- [ ] Add a note explaining why it was superseded
- [ ] Check if any QUESTIONS reference this issue (close/update those too)
- [ ] Verify no in-progress runs depend on it

**Closure template:**
```
OUTCOME: completed (merged into issue #X)
SUMMARY: This work was superseded by issue #X. Playground became the implementation vehicle for Interactive requirements.
```

## Backlog Health Metrics

Track these numbers each cycle. **If they're rising, backlog is growing faster than it's shrinking:**

| Metric | Target | Warning |
|--------|--------|---------|
| Issue count | Steady or shrinking | > 100 and rising |
| Duplicates per batch | 0–2 | > 5 |
| Stale questions | 0 | > 10% of total |
| Decision-to-issue ratio | 1:3 | > 1:5 (too many decisions, not enough execution) |
| Runs-to-issue ratio | 1:1.5 | > 1:2 (more runs than output; debugging swamp) |

## Orchestrator Checklist (After Run Batch)

Use this before queuing the next round:

- [ ] **Scan**: Any new duplicates created in the last 10 runs? (Read issues #N through #M titles)
- [ ] **Goal Check**: Do active issues still align with approved goal + decisions?
- [ ] **Stale Q Check**: Any blocking questions waiting > 3 cycles? Escalate or close.
- [ ] **Naming Conflicts**: Did this batch use inconsistent naming vs. earlier decisions? (e.g., "Interactive" vs "Playground")
- [ ] **Decision Log**: Any key architectural or naming choices made by roles? Add them to DECISIONS.
- [ ] **Budget**: If > 80% spent, propose cleanup issues instead of new features.
- [ ] **Consolidation**: Found duplicates? Draft a "consolidate X and Y" issue or approve batch close.

## Refinement Triage (PO Role: Decide What Gets Refined)

Before queuing issues for execution, triage them for refinement. Not every issue needs refining, but fuzzy or large ones will waste credits if agents guess at scope.

### When Issues Need Refinement

**Decision Tree:**

```
New Issue Created
├─ Is it clear? (title + detail fully scoped, acceptance criteria testable)
│  ├─ YES → Check Complexity (see below)
│  └─ NO → Create refinement sub-issue (Navigator)
│
Complexity Assessment (use complexityHint 0–100):
├─ Small (0–30): Clear + straight ahead (add parameter, fix bug)
│  └─ NO REFINEMENT NEEDED
│     Status: ReadyToPickup (agent can start immediately)
│
├─ Medium (30–60): Moderate scope, familiar area
│  ├─ Touches architecture / new abstraction?
│  │  └─ Create refinement: Architect scopes (15–30 min issue)
│  │     Sub-task: "Refine #X: scope API surface & decisions"
│  │
│  └─ Straightforward implementation in known area?
│     └─ Create refinement: Developer quick-scopes (10–15 min issue)
│        Sub-task: "Refine #X: map files & linked decisions"
│
└─ Large (60+): Cross-cutting or unfamiliar
   └─ CREATE REFINEMENT: Navigator scouts (20–40 min issue)
      Sub-task: "Scout #X: codebase, related issues, decisions"
```

### Refinement Sub-Issue Pattern

When you decide an issue needs refining, create a **sub-issue** linked to the parent:

**Parent Issue #42:** "Implement OptaPlayground container component" (Status: NeedsRefinement)

**Sub-Issue #42.R1** (Refinement Sub-Task):
```
Title: Scout #42: Playground container patterns & dependencies
Role: navigator
Priority: URGENT (blocks parent #42)
Detail: Scout the Playground codebase:
  1. Which files define OptaPlayground and its dependencies?
  2. What existing components/patterns should this container follow?
  3. Are there related decisions (naming, styling, composition)?
  4. What tests already exist?
Produce a scope document with:
  - FilesInScope: list of files the implementation should touch
  - LinkedDecisions: decision #s that apply
  - AcceptanceCriteria: testable definition of done
  - RelatedIssues: similar work already done or in progress
DependsOn: [none]
```

**Why sub-issues are better than separate tasks:**
- Clear parent-child hierarchy (dependency visible in backlog)
- Don't clutter main backlog (refinement sub-task is "support work")
- Refinement output is attached to the issue it refines
- Budget tracking visible (refinement cost vs execution cost)

### Triage Roles for Refinement

| Scenario | Role | Time | Output |
|----------|------|------|--------|
| **Fuzzy/unclear** | Navigator | 20–40 min | Scope doc, file list, decisions |
| **Medium + architectural** | Architect | 15–30 min | API surface, design notes, decisions |
| **Medium + code** | Developer | 10–15 min | File mapping, imports, test patterns |
| **Small + clear** | (none) | 0 | ReadyToPickup |
| **Decision-heavy** | Architect | 15–30 min | Linked decisions, trade-off analysis |
| **Cross-cutting** | Navigator | 20–40 min | Full scope document |

**Note on Architect Issues:** Architect-generated issues are often fuzzier than developer issues ("implement feature X in component Y"). Navigator scouts are especially valuable early in projects. As the project matures and patterns stabilize, architect issues become clearer and may skip refinement.

### Refinement Checklist (PO Decision)

For each new issue, ask:

- [ ] **Clarity**: Can an agent understand "done" from the title + detail without asking?
  - No → Refinement needed
  - Yes → Continue
  
- [ ] **Scope**: Are the files/modules involved clearly bounded?
  - No → Refinement needed
  - Yes → Continue
  
- [ ] **Complexity**: Is complexityHint ≤ 30?
  - Yes → ReadyToPickup (no refinement)
  - No → Continue
  
- [ ] **Familiarity**: Are you confident the agent knows this area?
  - For **Small (0–30)**: If yes → ReadyToPickup
  - For **Medium (30–60)**: If no → Refinement needed (developer or architect)
  - For **Large (60+)**: Always refinement (navigator)
  
- [ ] **Architecture**: Does this touch API design, naming, or module boundaries?
  - Yes → Architect refinement
  - No → Continue
  
- [ ] **Dependencies**: Are linked decisions clear from issue text?
  - No → Add to refinement scope (architect/navigator will link them)
  - Yes → ReadyToPickup

### Cost-Benefit Rule

**Refinement pays for itself when:**
- **Medium issue + refinement**: 0.5 credit (15 min) + 1.5 credit execution = 2 credits
- **Without refinement**: Agent guesses wrong scope, re-runs = 3+ credits
- **Savings per medium issue**: 1 credit (if 20% of issues are medium, saves ~10 credits per 50 issues)

**Rule of thumb:**
- **Small (< 30):** Skip refinement (not worth 0.5 credit)
- **Medium (30–60):** Refinement if fuzzy, cross-cutting, or unfamiliar
- **Large (60+):** Always refine (prevents catastrophic rework)

### Workflow: After Refinement Sub-Task Completes

Once refinement sub-issue is **Done**:
1. Read the refinement output (scope doc, file list, decisions)
2. Update parent issue:
   - [ ] Set `FilesInScope` field (if your system supports it)
   - [ ] Link `LinkedDecisions` (e.g., "Decision #7, #15 apply")
   - [ ] Update acceptance criteria if refinement clarified it
3. Mark parent: `Status: ReadyToPickup`
4. Parent issue is now ready to queue for execution

### Questions During Refinement

If a refinement sub-task uncovers a question that blocks the parent:
- [ ] **Non-blocking**: Record in workspace, move parent to ReadyToPickup
- [ ] **Blocking** (e.g., "Should we use X or Y?"): Create a blocking question
  - Parent stays `NeedsRefinement` until question is answered
  - Once answered, update parent scope and mark ReadyToPickup

## Common Pitfalls

| Pitfall | Why It Happens | Fix |
|---------|----------------|-----|
| Duplicates keep accumulating | No one reads backlog before proposing | Make BACKLOG SCAN a mandatory orchestrator step |
| Goal-drift undetected | Roles never read goal+decisions, just prior work | Add this skill as a checkpoint; block runs that don't link to decisions |
| Questions become trash | Answered questions never closed | Add "close stale questions" to consolidation proposals |
| Roles revert earlier decisions | No immutable decision record | Require roles to link to DECISION #N before proposing conflicting work |
| Impossible to prioritize | 100+ issues, no sense of clusters/themes | Use consolidation to group by theme, close low-priority clusters |

## Reference: Goal Hierarchy

To prevent future goal-drift, establish this rule:

```
GOAL (read-only after planning)
├─ PLAN (approved roadmap + strategy)
│  └─ DECISION #1, #2, ... (immutable architectural/naming choices)
│     └─ ROADMAP (concrete execution issues derived from plan + decisions)
│        └─ ISSUE (work assigned to a role)
│           └─ RUN (a single agent session on that issue)
```

**Key constraint:** Roles use ISSUE + DECISION, never read GOAL directly. Orchestrator uses GOAL as tiebreaker only.

If a role is creating issues that conflict with an earlier DECISION, that's a red flag:
- Either the decision needs to be revisited (escalate to you)
- Or the role misread the decision (clarify it)

## Next Steps

After consolidation:
1. Queue a "consolidate and batch-close" issue (or run it directly if simple)
2. Update DECISION log with any choices made during consolidation
3. Re-run this skill after next batch to confirm duplicates didn't reappear
4. If duplicates keep appearing, consider adding a "validate against decisions" check to the PLANNER role

