---
name: spawn-subagent
description: Route work to the right delegation strategy — inline agents for fast bounded tasks, spawn_agent for full issue execution.
---
# Skill: Spawn Subagent

## Purpose
Choose the right delegation strategy when you need to hand off work.
Two paths are available in every session; picking the wrong one wastes tokens or blocks progress.

## Available inline agents

Every session has a set of pre-registered inline sub-agents based on its role.
These share the parent session's MCP servers and skill directories but have a restricted tool surface.

| Agent | Available to | What it does |
|---|---|---|
| `navigator` | All roles except `navigator` | Read-only file explorer: locate files, map deps, trace imports |
| `backlog-manager` | `orchestrator` | Audit backlog via MCP: triage, close duplicates, resolve stale questions |
| `refiner` | `architect` | Scope an ambiguous issue: read files, produce REFINEMENT notes, update via MCP |
| `analyst` | `architect`, `auditor`, `security` | ATM health check on a file set: size, mixed concerns, static I/O, missing seams |
| `inline-reviewer` | `developer`/*-developer, `refactorer`, `reviewer` | Pre-handoff diff review: correctness, scope violations, missing tests |
| `security-scanner` | `developer`/*-developer | OWASP scan of changed files: injection, secrets, path traversal |
| `verifier` | `developer`/*-developer, `refactorer`, `tester` | Run build/test command, return pass/fail evidence |

## Two paths

### 1. Inline agent (fast, bounded, isolated tool surface)
Inline agents run within your current session as a separate focused sub-session.
They can use only the tools registered for them (see table above) and produce a structured output block.

Use an inline agent when:
- The task is auxiliary — it supports your main work, not the deliverable itself.
- The scope is bounded and well-defined (a file set, a recent diff, a single issue).
- The task is read-mostly with at most targeted MCP writes.
- You need a sandboxed tool surface (e.g., restrict a reviewer to `git diff` only).

**Do not use** an inline agent for tasks that write source files, run large multi-step
tool chains, or need a fresh context window to avoid pollution.

### 2. `spawn_agent(issueId)` (full session, write access, isolated context)
`spawn_agent` starts a separate agent session for a workspace issue. That session has
the full skill set, workspace MCP tools, and its own context window. Its result is
persisted as a run outcome.

Use `spawn_agent` when:
- The task writes or modifies source files.
- The task must record architectural decisions via `remember_decision`.
- The task needs a long, independent chain of tool calls.
- Isolation matters (e.g., architect, reviewer with broad scope, auditor).

## Decision table

| Situation | Strategy |
|---|---|
| "Where does X class live?" | `navigator` inline |
| "What files does this subsystem touch?" | `navigator` inline |
| "Triage and deduplicate the backlog" | `backlog-manager` inline (orchestrator only) |
| "Scope this ambiguous issue" | `refiner` inline (architect) |
| "Check health of these files before designing" | `analyst` inline |
| "Review my changes before I claim done" | `inline-reviewer` inline |
| "Quick OWASP scan of what I just changed" | `security-scanner` inline |
| "Did my changes break the tests?" | `verifier` inline |
| "Implement the feature in issue #N" | `spawn_agent(issueId)` |
| "Design the architecture for issue #N" | `spawn_agent(issueId)` |
| "Full review of a multi-file change" | `spawn_agent(issueId)` |
| "Architect needs scope clarity first, then implementation" | `navigator` → then `spawn_agent` |

## Using an inline agent

Invoke an inline agent with a focused prompt. The agent name must match the registered name:

```
Use the navigator to: locate all files that implement IXxx and trace their call chain.
```

```
Use the backlog-manager to: audit the backlog, triage all Planned issues, and close any duplicates.
```

```
Use the verifier to: run the project tests and return evidence.
```

Keep inline prompts short and targeted. Inline agents do not call workspace MCP tools
unless the session's workspace MCP is configured (which it always is in a DevTeam run).

## Using spawn_agent

Call `spawn_agent(issueId)` with the issue id of the ready issue you want executed.
Optionally pass `contextHint` — a short string (≤ 200 chars) of supplemental context
not yet captured in the issue or linked decisions.

```
spawn_agent(issueId: 42, contextHint: "Existing cache uses LRU; new cache must be LFU.")
```

After each `spawn_agent` call, inspect the result summary. If the spawned agent created
new ready issues, spawn those too before returning `OUTCOME: completed`.

## Recursive spawning
Spawned sessions also have their inline agents available:
- An architect session can use `navigator` + `analyst` + `refiner` inline.
- A developer session can use `navigator` + `inline-reviewer` + `security-scanner` + `verifier` inline.
- Any session can call `spawn_agent` to delegate further sub-issues.

The runtime tracks budget and prevents runaway spawning through the configured credit cap.
Each spawned session starts a fresh context window.

## Typical flows

**Orchestrator flow:**
1. Use `backlog-manager` inline: audit and triage the backlog.
2. Review BACKLOG_AUDIT output for any remaining items.
3. Call `spawn_agent(issueId)` for each ready issue in the chosen batch.
4. After all complete, check for new ready issues and continue.

**Architect flow:**
1. Use `navigator` inline: locate the relevant files for the design area.
2. Use `analyst` inline: check health of those files before designing.
3. Design and record decision via `remember_decision` MCP tool.
4. Call `spawn_agent(issueId)` for each follow-on developer issue.

**Developer flow:**
1. Use `navigator` inline: confirm which files are in scope if unclear.
2. Implement the changes.
3. Use `inline-reviewer` inline: check the diff for issues.
4. Use `security-scanner` inline: scan for OWASP risks in changed files.
5. Use `verifier` inline: run tests and capture evidence.
6. Only claim `OUTCOME: completed` if verifier says RESULT: pass.
