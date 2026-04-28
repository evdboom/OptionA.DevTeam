---
name: diagnose
description: "Diagnose a pasted .devteam workspace from another repository by reviewing issues, decisions, questions, runs, and artifacts to find bad assumptions, weak questions, timeout/weird run patterns, and concrete DevTeam runtime improvements. Use when the user asks to analyze .devteam state, loop quality, orchestration mistakes, or agent execution failures."
---

# Diagnose Skill

## Purpose

Use this skill when a user pastes or provides a `.devteam` folder (or a subset) and wants a post-mortem of agent behavior quality.

Primary goals:
- Detect wrong assumptions made by agents and orchestrators.
- Detect weak, missing, or mis-prioritized user questions.
- Detect weird runs, repeated retries, timeouts, or blocked loops.
- Turn evidence into concrete, implementable improvements for DevTeam.

## Inputs To Collect

Read whatever exists from the pasted workspace. Typical high-value inputs:
- `.devteam/workspace.json`
- `.devteam/issues/_index.md` and per-issue files in `.devteam/issues/`
- `.devteam/decisions/` artifacts
- `.devteam/runs/` artifacts and run summaries
- `.devteam/questions/` artifacts
- `.devteam/artifacts/` outputs tied to runs
- Any CLI logs, crash traces, and diagnostics provided by the user

If some folders are missing, continue with what is available and explicitly list gaps.

## Diagnostic Workflow

1. Build timeline
- Reconstruct sequence: plan -> architect plan -> execution loops -> blocked/completed outcomes.
- Identify phase transitions and stalls.

2. Inspect issue quality
- Check issue scope size and dependency clarity.
- Check whether issue role assignment matches the work type.
- Check whether refinements (`FilesInScope`, `LinkedDecisionIds`, `RefinementState`) are present and useful.

3. Inspect decisions
- Verify each major change has a corresponding decision with rationale.
- Flag contradictory or stale decisions reused after context changed.
- Flag decisions with no downstream issue linkage.

4. Inspect questions
- Classify questions as blocking vs non-blocking.
- Flag missing critical questions that should have been asked.
- Flag low-value or repetitive questions that slowed progress.

5. Inspect run behavior
- Track run outcomes: completed, blocked, failed.
- Identify timeout extension usage and repeated extension requests.
- Detect retry loops, handoff confusion, role thrashing, or no-op runs.
- Detect malformed structured replies (`OUTCOME`, `SUMMARY`, `ISSUES`, `QUESTIONS`).

6. Detect assumption failures
- Compare assumptions to available workspace evidence.
- Label each as: valid, weakly-supported, or unsupported.
- Highlight assumptions that caused rework or user-facing confusion.

7. Synthesize improvements
- Convert findings into specific DevTeam improvements, grouped by layer:
  - Prompt/role/skill changes
  - Runtime loop logic and guardrails
  - Issue triage/refinement policy
  - Question policy
  - Telemetry and artifact completeness
  - CLI UX and diagnostics

## Output Format

Always produce findings first, ordered by severity.

### 1) Findings
For each finding include:
- Severity: Critical | High | Medium | Low
- Evidence: exact file(s) and key excerpt summary
- Impact: why this harmed quality, speed, or correctness
- Likely root cause: prompt, policy, runtime logic, or missing telemetry

### 2) Question Audit
- Questions that should have been asked but were not
- Questions that were asked but low-value/wrongly timed
- Suggested improved question wording for top misses

### 3) Assumption Audit
- Assumptions table: assumption | evidence for | evidence against | verdict

### 4) Run Stability Audit
- Timeout and retry pattern summary
- Weird/abnormal run signatures and probable causes

### 5) Improvement Backlog
Provide a prioritized, implementation-ready backlog:
- P0: must-fix correctness/stability issues
- P1: major quality/productivity improvements
- P2: polish and observability enhancements

Each item should include:
- Proposed change
- Target files/components (runtime, skill, role, CLI, MCP tool, etc.)
- Expected benefit
- How to validate (unit/smoke/integration signal)

## Heuristics

Use these guardrails while diagnosing:
- Prefer evidence over speculation. If uncertain, mark as a hypothesis.
- Avoid blaming a single run; prioritize repeated patterns.
- Distinguish user-input gaps from agent reasoning failures.
- Distinguish orchestration bugs from role prompt quality issues.
- Treat missing artifacts as a telemetry finding, not proof of correctness.

## Fast Checks

If time is limited, run this minimum pass:
- Count outcomes by status from run artifacts.
- List top 5 repeated blockers/failures.
- Identify 3 highest-cost wrong assumptions.
- Identify 3 missing questions that would have prevented rework.
- Propose top 5 DevTeam improvements with owners.

## Done Criteria

The diagnosis is complete when:
- Findings are evidence-backed and severity-ranked.
- Wrong assumptions are explicitly identified and verified.
- Question quality gaps are identified with better alternatives.
- Weird/time-out run patterns are explained with probable causes.
- A prioritized improvement backlog is ready for implementation planning.
