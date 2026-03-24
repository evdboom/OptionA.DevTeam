# DevTeam Roadmap

Stepwise plan for the remaining tracked work. Each section is self-contained — items within a section can be worked on independently. Sections are roughly ordered by dependency and value.

---

## 0 — Housekeeping (do this first)

| Step | Action |
|---|---|
| 0.1 | `git commit` the base state of `OptionA.DevTeam` before any loop runs against it |
| 0.2 | For each clone workspace, `git init` and commit the initial snapshot so the diff is clean after a loop run |
| 0.3 | Set `max-subagents` default: for UX work use 2–3; for GitHub integration use 1 (it's exploratory) |

---

## 1 — Chat layout: RazorConsole migration

**Goal:** Fixed input line at the bottom, scrolling chat history above — the Squad UX. Removes the mid-line heartbeat problem permanently.

**Why it needs its own section:** Requires adding `Microsoft.NET.Sdk.Razor` and `RazorConsole` as dependencies to `DevTeam.Cli.csproj`, which is a one-way door. Do this after the polish work (item 2) is merged so there's a clean baseline.

| Step | Owner | Detail |
|---|---|---|
| 1.1 | architect | ✅ Evaluate `RazorConsole` — specifically `LLMAgentTUI` example. Define the component map: chat pane (scrollable), input pane (fixed), status bar (1-line). Produce an issue breakdown. |
| 1.2 | developer | ✅ Add `RazorConsole` package to `DevTeam.Cli.csproj`. Wire up the shell host and a minimal skeleton that boots without crashing. |
| 1.3 | developer | ✅ Migrate `ChatConsole.WriteAgent`, `WriteSystem`, `WriteQuestion` to Razor components rendered into the chat pane. |
| 1.4 | developer | ✅ Migrate `WriteLoopLog`, heartbeat, and background-task output to the chat pane — this replaces the `ConsoleOutputLock` workaround entirely. |
| 1.5 | developer | ✅ Migrate the input line: `ReadLine`-based prompt → Razor input component pinned to the bottom. Commands, tab-complete, and `/` prefix all stay. |
| 1.6 | tester | ✅ Verify: (a) heartbeat never overwrites the input line, (b) all existing `/` commands work, (c) 74 smoke tests still pass (smoke tests use headless path, not Razor). |

**Goal file:** `devteam-razor-migration.md` (create when step 1.1 is ready to run)

---

## 2 — Near-term polish (run the existing goal file on the clone)

The `devteam-ux-improvements.md` goal file covers these. Let the clone loop run them. After the loop finishes, review diffs and port any clean changes back to main.

| Step | Detail |
|---|---|
| 2.1 | ✅ `/plan` output as a formatted panel (same style as question/plan-ready panels) |
| 2.2 | ✅ `@role` echo — "You → developer: …" line before the agent response |
| 2.3 | ✅ `/status` rendered as a Spectre.Console Table with pipeline column |
| 2.4 | ✅ `ConsoleOutputLock` — `ConcurrentQueue<string>` buffers background log messages; main thread drains before each prompt and on loop complete |
| 2.5 | ✅ `/history` — in-memory session log, last 50 entries rendered as a Spectre Table with elapsed time |

**Note:** Item 2.4 (`ConsoleOutputLock`) is a stopgap until item 1 (RazorConsole) is complete. Keep it simple.

---

## 3 — Architect writes plan.md (not just issues)

**Root cause:** `WritePlanArtifact` in `ExecutionLoop.cs` is only called when `issue.IsPlanningIssue == true`. Architect issues don't have this flag, so their summary never updates `plan.md` even though they produce the detailed execution breakdown.

**Why it matters:** After architect approval the plan file still shows the high-level planner output, not the detailed architect design. `/plan` shows stale information.

| Step | Detail |
|---|---|
| 3.1 | ✅ In `ExecutionLoop.cs`, also call `WritePlanArtifact` after any architect issue completes (`issue.RoleSlug == "architect" && !issue.IsPlanningIssue`). |
| 3.2 | ✅ `WritePlanArtifact` accepts a `string header` — planner: "High-level plan", architect: "Detailed execution plan (architect)". |
| 3.3 | ✅ Architect role prompt updated to state SUMMARY goes to `plan.md`. |
| 3.4 | ✅ Smoke test added: `Architect run updates plan artifact with execution details` — verifies that a completed architect run writes `plan.md` with the architect header and summary. |

**Estimated scope:** Small — 2–3 files, no new dependencies.

---

## 4 — Pipeline visibility (current role + next role)

**Problem:** When the loop is running, it's not clear which pipeline stage is active or what comes next.

| Step | Detail |
|---|---|
| 4.1 | ✅ `/status` table shows per-issue pipeline column with color-coded stage progression (done=green, current=bold cyan, upcoming=dim). |
| 4.2 | ✅ Execution loop logs `pipeline #P · [role]` context when starting an issue with a pipeline. |
| 4.3 | ✅ Combined with 4.1 — pipeline column in `/status` table. |
| 4.4 | ✅ Execution loop logs `Pipeline handoff → #N role: title` on stage completion and `Pipeline #P completed` on final stage. |

**Estimated scope:** Medium — touches `ExecutionLoop.cs`, `DevTeamRuntime.cs`, `ChatConsole.cs`, and `Program.cs`.

---

## 5 — Parallel subagent cap (Squad-style)

**Problem:** Default is 1 (sequential). Squad runs everything in parallel. We want a configurable sweet spot — probably 3–4 for most projects.

| Step | Detail |
|---|---|
| 5.1 | **Done (this session):** `DefaultMaxSubagents` is persisted in `RuntimeConfiguration` and settable via `/max-subagents N`. |
| 5.2 | ✅ Hint logged on iteration 1 when max-subagents==1 and 4+ issues ready. |
| 5.3 | ✅ Smoke test added: `Conflict prevention holds at max-subagents 4` — four-area scenario with two same-area issues; verifies at most 3 run concurrently. |
| 5.4 | ✅ Documented in `README.md` — new `## Parallel subagents` section with recommended settings table, credit burn-rate guidance, and conflict prevention note. |

---

## 6 — GitHub mode (major feature)

**Goal:** Run DevTeam against a real GitHub repository. Use GitHub Issues as the issue board, assign issues to Copilot, use PRs for implementation output, reviewer role does the PR review.

**Architecture decision point:** This is a parallel mode to the existing local workspace mode. The switch is `--mode github` on init.

| Step | Detail |
|---|---|
| 6.1 | **Architect spike** — define the data model: `GitHubWorkspaceAdapter` wraps `WorkspaceStore` but reads/writes issues from/to the GitHub API via `gh` CLI or Octokit. What `WorkspaceState` fields map to GitHub concepts? (Issues → GitHub Issues, Questions → GitHub Issues with `devteam:question` label, Decisions → GitHub Discussions or PR comments.) |
| 6.2 | **Auth** — `gh auth status` check on init. If not authenticated, prompt with instructions. |
| 6.3 | **Issue sync** — on each loop iteration, pull open GitHub Issues labelled `devteam:ready` and map them to `IssueItem`. Push status updates back as label changes + comments. |
| 6.4 | **Role assignment** — GitHub Issues don't have a `roleSlug` field. Options: (a) use a label `role:developer`, (b) parse from issue body frontmatter `role: developer`, (c) use a `devteam.json` in the repo root as a routing table. Recommendation: (b) with fallback to (a). |
| 6.5 | **PR output** — when a developer issue completes, instead of just logging the summary, produce a branch + PR via `git` + `gh pr create`. PR body = run summary. |
| 6.6 | **Reviewer role** — add a `reviewer` role that is triggered when a PR is open. It reads the diff (via `gh pr diff`), runs the review superpower, and posts a review comment. |
| 6.7 | **Approve flow** — `gh pr merge --squash` when reviewer marks done. |
| 6.8 | **Interactive shell** — `/sync` command to pull latest GitHub state into the local workspace snapshot. `/pr <id>` to attach to an open PR. |
| 6.9 | Smoke tests for the GitHub adapter (can use a mock `IGitHubClient` to avoid hitting the real API in CI). |

**Pre-requisites:** Items 3 (plan.md), 4 (pipeline visibility), and 5 (parallel cap) should be done first — they make the GitHub mode easier to use once it's live.

**Estimated scope:** Large — probably 6–10 architect issues, 10–15 developer issues across 2–3 runs.

---

## Quick-reference: recommended run order

```
1. git commit current state (do manually)
2. Run clone on devteam-ux-improvements.md  → ships items 2.1–2.5
3. Implement item 3 (architect plan.md)      → small, do directly
4. Implement item 4 (pipeline visibility)    → medium, use devteam on itself
5. Validate item 5.3 (parallel cap tests)    → small
6. Plan RazorConsole migration (item 1)      → architect spike first
7. Start GitHub mode (item 6)               → architect spike, then phased
```
