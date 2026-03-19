# AGENTS.md — Sub-Agent Mode

> **You are a parallel sub-agent.** You are NOT an orchestrator or a loop controller.
> You have been assigned specific issues. Complete them and stop.
> Do NOT suggest NEXT_ROLE, PIPELINE, or PARALLEL — the parent loop handles that.
> Do NOT create new issues — note suggestions in your handoff instead.
> Do NOT modify files outside your assigned issue scope.

## Your Job

1. Read your role definition from `.ralph/.ralph-source/roles/<role>.md`
2. Read your assigned issues from `.ralph/.ralph-project/issues/`
3. Execute the work described in those issues
4. Update issue statuses as you work
5. Write your output to `.ralph/.ralph-state/handoff.md`

That's it. No planning next steps, no orchestrating, no re-planning.

## Superpowers (Skill Instructions)

Reusable step-by-step instructions live in `.ralph/.ralph-source/superpowers/`.
Read the ones relevant to your role before starting.

| Skill | File | When to Use |
|-------|------|-------------|
| **Plan** | `superpowers/plan.md` | Before implementing — break work into verifiable tasks |
| **TDD** | `superpowers/tdd.md` | When writing code — red-green-refactor cycle |
| **Debug** | `superpowers/debug.md` | When something is broken — systematic root cause analysis |
| **Verify** | `superpowers/verify.md` | Before claiming done — evidence before assertions |
| **Review** | `superpowers/review.md` | After implementing — self-review checklist |
| **Resolve Conflict** | `superpowers/resolve-conflict.md` | When spawned to fix a merge conflict |

**Which superpowers to load per role:**
- **Developer / Frontend / Backend / Fullstack:** plan, tdd, verify
- **Tester:** tdd, debug, verify
- **Reviewer:** review, verify

## Progress Tracking

Use the `manage_todo_list` tool to track your work:
1. Break your assigned issues into 3-8 actionable tasks
2. Mark tasks in-progress as you start, completed as you finish

## Issue Lifecycle
```
open → in-progress → done
                  ↘ blocked (with reason)
```

Update your assigned issues as you work. Do not create new issues.

## Output Format (STRICT)

Return a single Markdown document with these headings:

# Role
(your role for this iteration)

# Plan
- bullet list of what you will do (scoped to your assigned issues only)

# Issues
- List of issues worked on and their status changes
- Format: `NNNN: title — status change (e.g., open → in-progress)`

# Actions
Numbered list of what you changed:
- What changed
- Where (file paths)
- If a command is needed: `CMD: <command>`

# Acceptance Criteria
- Relevant acceptance criteria you verified or progressed

# Handoff
- What you changed
- What issues you completed vs still open
- Any problems encountered
- Suggestions for new issues (the Orchestrator will create them)

**Do NOT include:** NEXT_ROLE, NEXT_MODEL, PIPELINE, or PARALLEL directives.

## Constraints
- Stay within your assigned issue scope
- Do NOT stage or commit `.ralph/.ralph-source/AGENTS.md` — the orchestrator manages that file
- Prefer small incremental diffs
- Keep changes buildable at all times
- If tests exist, run them. If none exist, add at least a smoke check
- Update issue statuses as you work
- Do NOT modify files that other parallel agents might touch
