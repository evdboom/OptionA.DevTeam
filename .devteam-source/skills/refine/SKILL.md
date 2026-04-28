---
name: refine
description: Scope and refine ambiguous issues into executable work packets with explicit what/why/how, files-in-scope, acceptance criteria, and linked decisions.
---
# Skill: Refine

Turn a broad or ambiguous issue into a precise execution issue that a scoped role can complete safely.

## When to Use

- Issue detail is vague, overloaded, or missing acceptance criteria.
- Scope is too large for one focused run.
- Files impacted are unclear.
- Work appears to contradict an existing decision.
- Orchestrator marks issue as `NeedsRefinement`.

## Goal

Produce an exhaustive refinement outcome with:
- what: exact deliverable(s)
- why: business/technical rationale and decision alignment
- how: implementation approach and constraints
- files in scope: concrete candidate file paths
- linked decisions: explicit decision ids that must be honored
- acceptance criteria: objective, testable checks

## Required Flow

1. Fetch the issue using MCP: `get_issue(issueId)`.
2. Read linked decisions using MCP: `get_decisions(linkedDecisionIds)`.
3. Identify ambiguity, missing constraints, and potential conflicts.
4. Produce refined notes and, if needed, create split follow-up issues.
5. Keep implementation roles scoped to the refined file set and decisions.

## Refinement Output Template

Use this structure in `SUMMARY` or parent-issue notes:

```markdown
REFINEMENT:
- what: <specific deliverable>
- why: <reason + expected impact>
- how: <approach and constraints>

FILES_IN_SCOPE:
- path/to/fileA
- path/to/fileB

LINKED_DECISIONS:
- #12: <title>
- #19: <title>

ACCEPTANCE_CRITERIA:
- [ ] <testable criterion 1>
- [ ] <testable criterion 2>

RISKS:
- <risk>

OUT_OF_SCOPE:
- <explicitly excluded work>
```

## Rules

- Do not implement production code during refinement.
- Prefer one refined issue per cohesive change set.
- If an issue is too broad, split into dependency-ordered follow-up issues.
- If no linked decision exists for a contested direction, raise a decision issue instead of guessing.
