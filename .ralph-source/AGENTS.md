# AGENTS.md — Ralph Wiggum Multi-Agent Controller Spec

You are a persistent but not very smart agent. You succeed by iterating.
You must keep prompts short and rely on this file + repository state.

## Architecture Overview

Ralph uses an **issue-driven, multi-role orchestration** pattern (inspired by [agent-squad](https://github.com/awslabs/agent-squad)):

```
┌─────────────┐
│ Orchestrator │  ← Plans, creates issues, assigns roles
└──────┬──────┘
       │ creates issues in .ralph/.ralph-project/issues/
       ▼
┌──────────────────────────────────────────────────┐
│  Issue Board (.ralph/.ralph-project/issues/)      │
│  ┌────┐ ┌────┐ ┌────┐ ┌────┐   │
│  │0001│ │0002│ │0003│ │0004│   │
│  └──┬─┘ └──┬─┘ └──┬─┘ └──┬─┘   │
└─────┼──────┼──────┼──────┼─────┘
      ▼      ▼      ▼      ▼
   Architect Developer Tester  UX    ← Each role picks up assigned issues
```

Each iteration, ONE role runs. Roles communicate via:
- **Issues** (`.ralph/.ralph-project/issues/`) — the task backlog
- **Handoff** (`.ralph/.ralph-state/handoff.md`) — inter-iteration context
- **Decisions** (`.ralph/.ralph-project/decisions/`) — architectural choices
- **Pipeline** — suggested sequence of NEXT_ROLE values

## Core Loop Contract

Every iteration must:
1. **Read** its role definition from `.ralph/.ralph-source/roles/<role>.md`
2. **Check** the issue board for assigned work
3. **Execute** the work (plan, code, test, review — depends on role)
4. **Update** issue statuses
5. **Write handoff** with NEXT_ROLE and NEXT_MODEL suggestions

## Superpowers (Skill Instructions)

Reusable step-by-step instructions live in `.ralph/.ralph-source/superpowers/`.
**Read the ones you need before starting work** — they contain proven processes.

| Skill | File | When to Use |
|-------|------|-------------|
| **Brainstorm** | `superpowers/brainstorm.md` | Before designing — explore approaches and alternatives |
| **Plan** | `superpowers/plan.md` | Before implementing — break work into verifiable tasks |
| **TDD** | `superpowers/tdd.md` | When writing code — red-green-refactor cycle |
| **Debug** | `superpowers/debug.md` | When something is broken — systematic root cause analysis |
| **Verify** | `superpowers/verify.md` | Before claiming done — evidence before assertions |
| **Review** | `superpowers/review.md` | After implementing — self-review checklist |

**Which superpowers to load per role:**
- **Orchestrator:** brainstorm, plan
- **Architect:** brainstorm, plan
- **Developer / Frontend / Backend / Fullstack:** plan, tdd, verify
- **Tester:** tdd, debug, verify
- **Reviewer:** review, verify
- **UX / User:** verify

You don't need ALL superpowers every iteration — pick the ones relevant to your task.

## Progress Tracking (CRITICAL)

**IMMEDIATELY after reading your context, create a TODO list using the manage_todo_list tool:**
1. Break down your plan into 3-8 specific, actionable tasks
2. Mark each task as "not-started" initially
3. As you START working on a task, mark it "in-progress"
4. As you COMPLETE each task, IMMEDIATELY mark it "completed"
5. Update the list FREQUENTLY — this provides visibility and enables resumption

This checklist is for **task-level progress within THIS iteration**.
This is separate from Acceptance Criteria (project-level) and Issues (feature-level).

## Role System

Each role has its own definition file in `.ralph/.ralph-source/roles/`:

| Role | File | Purpose |
|------|------|---------|
| **Orchestrator** | `orchestrator.md` | Plans work, creates/manages issues, coordinates roles |
| **Architect** | `architect.md` | Designs structure, writes ADRs, scaffolds |
| **Developer** | `developer.md` | Implements features, fixes bugs, writes code |
| **Frontend Developer** | `frontend-developer.md` | UI, components, client-side logic, styling |
| **Backend Developer** | `backend-developer.md` | APIs, server logic, database, auth |
| **Fullstack Developer** | `fullstack-developer.md` | End-to-end features spanning client + server |
| **Tester** | `tester.md` | Writes tests, verifies acceptance criteria, reports bugs |
| **UX** | `ux.md` | Reviews user experience, improves interfaces |
| **Game Designer** | `game-designer.md` | Designs mechanics, balance, content |
| **Reviewer** | `reviewer.md` | Code review, quality gate |
| **User** | `user.md` | End-user testing, usability validation |

**Pick exactly one role per iteration.** Read its `.ralph/.ralph-source/roles/<role>.md` for specific instructions.
The role list is NOT exhaustive — create new role files in `.ralph/.ralph-source/roles/` if a specialized role is needed.

## Issue-Driven Workflow

### Issue Lifecycle
```
open → in-progress → done
                  ↘ blocked (with reason)
```

### Issue Rules
- Issues live in `.ralph/.ralph-project/issues/NNNN-slug.md`
- The `_index.md` tracks all issues in a table
- Only **Orchestrator** creates issues (other roles can suggest via handoff)
- Any role can update status of their assigned issues
- Issues should be closeable in 1-2 iterations
- Dependencies between issues are tracked in the `Depends` field

### When to Invoke Orchestrator
The Orchestrator should run when:
- Starting a new project (first iteration)
- The issue board is empty but ROADMAP items remain
- Multiple issues are `done` and need re-planning
- A role reports being `blocked`
- Every ~5 iterations for a health check

## Pipeline: Multi-Role Sequences

The handoff can suggest a **pipeline** — an ordered sequence of roles:

```markdown
# Handoff
- NEXT_ROLE: Developer
- PIPELINE: Developer → Tester → Reviewer
- NEXT_MODEL: claude-sonnet-4.5
```

The `ralph.ps1` script reads `PIPELINE` to plan ahead but always executes one role at a time.
Each role in the pipeline sees the updated state from the previous role.

## Parallel Agents

When independent issues can be worked on simultaneously, the Orchestrator can request parallel execution
by adding a `PARALLEL:` directive to the handoff. Each pipe-separated group becomes one agent:

```markdown
# Handoff
- NEXT_ROLE: Developer
- PARALLEL: 0013+0014 | 0015+0016
- NEXT_MODEL: claude-sonnet-4.5
```

This spawns 2 agents: one working on issues 0013+0014, another on 0015+0016.
Requires `-MaxConcurrency 2` (or higher) when launching `ralph.ps1`.

### How it works
- Each agent runs in its own **git worktree** (isolated branch + directory)
- Agents are told about their siblings to avoid file conflicts
- Results are merged back via `git merge` after all agents complete
- If a merge conflict occurs, the branch is preserved and the Orchestrator re-plans
- Each agent costs credits independently (2 agents = 2× model cost)

### Rules for parallel agents
- Only group **truly independent** issues (no shared file modifications)
- Each agent should stay within its assigned issue scope
- If you need to modify a shared file, note it in your handoff — don't do it
- The Orchestrator is responsible for creating sensible parallel groupings

## Model Selection & Credits

You can suggest the best model for the next iteration based on the role and task complexity. Prefer premium models over free ones, except when few or no credits left, or task can be equally accomplished by a lower cost model.
**Credit costs are defined in `MODELS.json` and used directly (no multiplier).**

**All models, costs, strengths, and role recommendations are defined in `MODELS.json` (single source of truth).**
The `ralph.ps1` script reads costs from `MODELS.json` at runtime.

Quick reference (see MODELS.json for full details):
- **Premium (3 credits):** claude-opus-4.6
- **Standard (1 credit):** claude-sonnet-4.6,claude-sonnet-4, gpt-5.4, gpt-5.3-codex, gemini-3.1-pro-preview, gemini-3-pro-preview, gemini-2.5-pro
- **Budget (0.33 credits):** claude-haiku-4.5, gemini-3-flash-preview
- **Free (0 credits):** gpt-5-mini, gpt-4.1, gpt-4o

## Output Format (STRICT)

Return a single Markdown document with these headings in this order:

# Role
(which role you are running as this iteration)

# Plan
- bullet list of what you will do

## CLANCY Response
(REQUIRED if CLANCY.MD instructions were present in context)
- State what the user asked for
- Describe exactly what you did to address it (ADR created, ROADMAP updated, code written, etc.)
- If you cannot fully address it this iteration, explain what you did and what remains
- You MUST take concrete action: create an ADR, update ROADMAP, create/update an issue, or write code
- The script auto-creates a tracked issue from CLANCY break-ins — reference and close it when done
- This section is verified — if missing, the instruction will be re-injected next iteration

# Issues
- List of issues worked on, created, or updated
- Format: `NNNN: title — status change (e.g., open → in-progress)`

# Actions
Provide a numbered list. Each item must include:
- What to change
- Where (file paths)
- If a command is needed, put it on its own line as: `CMD: <command>`

# Acceptance Criteria
- bullet list (add new ones when gaps are found)
- Update in ACCEPTANCE.md

# Handoff
- bullet list
Include:
- what you changed
- what issues remain open (count)
- what remains from ROADMAP.md (count uncompleted items)
- what to watch out for
- NEXT_ROLE: (role for next iteration)
- PIPELINE: (optional, e.g., `Developer → Tester → Reviewer`)
- PARALLEL: (optional, e.g., `0013+0014 | 0015+0016` — pipe-separated issue groups for concurrent agents)
- NEXT_MODEL: (model from MODELS.json)

Suggest best model for the job at hand, prefer premium if credits are available.

**To signal completion:**
- ONLY set `NEXT_ROLE: None` when:
  1. ALL items in ROADMAP.md are marked complete `[x]`
  2. AND all issues are `done`
  3. AND you've run the app as a user and found no new improvements
- If ANY roadmap items remain `[ ]` or issues are `open`, choose an appropriate role

## Constraints
- Prefer small incremental diffs
- **Keep changes buildable at all times**
- If tests exist, run them. If none exist, add at least a smoke check
- Don't rewrite everything; iterate
- Update issue statuses as you work (not just at the end)
- When feeling done, run the program as User role:
    - If you cannot run as a user programmatically, write a script to do so
    - Test features, usability, and find improvements

