# DevTeam Roadmap
## 6 — GitHub mode (major feature)

**Goal:** Run DevTeam against a real GitHub repository. Use GitHub Issues as the issue board, assign issues to Copilot, use PRs for implementation output, reviewer role does the PR review. While keeping the strength of the multi agent role scoped runs.

**Architecture decision point:** This is a parallel mode to the existing local workspace mode. The switch is `--mode github` on init.

**Discover:** options to have the flow work inside github (loop running, user feedback) and have devteam cli communicate with github.

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

**Estimated scope:** Large — probably 6–10 architect issues, 10–15 developer issues across 2–3 runs.

---

## 7 — Agent Personas (fun / polish)

**Inspiration:** [pixel-agents](https://github.com/pablodelucca/pixel-agents) — agents as living characters you can watch work.

**Goal:** Replace the plain "running developer on issue #5…" status lines with a live activity panel where each running agent has a named persona, a role-appropriate avatar, and an animated state. Especially satisfying with parallel runs — you see the whole team at their desks simultaneously.

**Concept:** Each role gets a character. The character has an idle pose, a "thinking" animation (tool calls in flight), and a "done" flash. When multiple agents run in parallel, they all appear in a side-by-side grid. Role suggestion:

| Role | Avatar | Flavour |
|---|---|---|
| planner | `📋` | "sketching the strategy…" |
| architect | `🏗️` | "drawing the blueprints…" |
| developer | `💻` | "pushing code…" |
| backend-developer | `⚙️` | "wiring the pipes…" |
| frontend-developer | `🎨` | "pixel-pushing…" |
| tester | `🧪` | "breaking things on purpose…" |
| reviewer | `🔍` | "reading every line…" |
| orchestrator | `🎼` | "conducting the ensemble…" |

**Rendering approach:** Spectre.Console `Live` display + `Table` or a grid of `Panel`s (one per active agent). The shell's RazorConsole layer already supports `Panel`; the live activity view can slot in as a dedicated `RunningAgentsPanel` component rendered during loop execution, replacing or augmenting the existing `ProgressReporter` callback.

| Step | Detail |
|---|---|
| 7.1 | Add `AgentPersona` record to `Models` — `RoleSlug`, `Avatar`, `DisplayName`, `ActiveFlavour`, `DoneFlavour`. Seed defaults for all known roles. |
| 7.2 | `RunningAgentsPanel` Razor component — accepts `IReadOnlyList<RunProgressSnapshot>`, renders a live grid of persona cards (avatar + name + elapsed + flavour text). Heartbeat ticks animate a spinner on the elapsed time. |
| 7.3 | Wire `ProgressReporter` in `LoopExecutor` to push snapshots to the panel instead of (or alongside) the console text lines. |
| 7.4 | Parallel layout — when >1 agents run simultaneously, cards appear side-by-side in a `Columns` layout. Single agent falls back to a single-panel view. |
| 7.5 | "Done" flash — when a run completes, card briefly shows the outcome emoji (✅ / ❌ / 🔒) and the one-line summary before the card disappears from the live grid. |
| 7.6 | `--personas off` flag (or `Runtime.PersonasEnabled` config) to revert to plain text for automation/CI contexts. |

**Estimated scope:** Small-medium — 1 architect issue, 3–4 developer issues. Entirely additive; no existing behaviour changes.

---

## 8 — Navigator as inline scout (sub-agent preflight)

**Inspiration:** The VS Code Copilot "Explore" subagent pattern — a cheap, read-only reconnaissance call launched *within* an active turn to map the codebase before making changes, rather than a separate, heavyweight planning step.

**Context:** A `navigator` role already exists and works well as a first-class issue on the board. The gap is granularity: Navigator currently requires its own full iteration to run. For complex Execution issues a developer agent would benefit from a *same-turn* scout pass — reading the blast radius, identifying relevant files, flagging merge-conflict candidates — all before writing a single line of code.

**Two related questions the architect should resolve:**
1. **Issue-queue approach (near-term):** Should `LoopExecutor` / the orchestrator role automatically inject a Navigator prerequisite issue when a developer issue exceeds a complexity threshold? This is additive and works today.
2. **Inline subagent approach (future):** Since DevTeam wraps the Copilot SDK, can executor roles spawn a bounded sub-session scoped to read-only tooling? The SDK already supports nested agent calls; the role prompts may just need nudging. A `scout` superpower that any role can invoke could expose this without runtime changes.

| Step | Detail |
|---|---|
| 8.1 | **Architect spike** — decide between issue-queue pre-flight vs. inline sub-session. Document trade-offs: latency (extra iteration), cost (separate model call), context fidelity (inline has fresher state). |
| 8.2 | **Complexity signal** — add a `ComplexityHint` field to `IssueItem` (or derive from `Priority` + `Area` + dependency count). `LoopExecutor` uses it to decide whether to auto-prefix a Navigator issue. |
| 8.3 | **Scout superpower** — if inline path chosen: create `.devteam-source\superpowers\scout.md`. Any role can include `use scout` in its prompt; the superpower describes how to launch a bounded sub-call with read-only tools and return a file manifest into the current context window. |
| 8.4 | **Orchestrator nudge** — update `orchestrator.md` role prompt to explicitly consider prefixing Navigator issues for complex developer issues it queues. |
| 8.5 | **Navigator role update** — add a `lightweight` output mode: just the file manifest + area tags (no full dependency essay), suitable for inline preflight where brevity matters. |
| 8.6 | Smoke tests: verify Navigator issue auto-injection and that a navigator issue's SUMMARY feeds into the dependent developer issue's context. |

**Estimated scope:** Medium — 1 architect issue, 3–5 developer issues. Core loop change in 8.2–8.3 is the risky part; 8.4–8.5 are prompt-only.

---

## 9 — Project hygiene conventions (instructions + role awareness)

**Background:** These conventions emerged from refactoring this repo's own codebase (Program.cs 2155 → 22 lines, SmokeTests 2273 → 111 lines). Two hygiene rules proved high-value and should be codified so both Copilot (working on this repo) *and* DevTeam agents (working on target repos) follow them without being reminded.

**Rule 1 — Keep files small and focused.** No single file should own multiple concerns. When a file grows past ~300–400 lines, split it by theme. Prefer more small files over fewer large ones.

**Rule 2 — Separate presentation from logic.** Specific case: Blazor `.razor` files contain *only* markup and minimal binding glue. All logic lives in the paired `.razor.cs` code-behind file. No `@code { }` blocks with real logic. More generally: any file that mixes rendering and domain logic should be split.

**Three places these rules need to live:**

| Location | Why |
|---|---|
| `.github/copilot-instructions.md` | Copilot follows them when editing this repo's own source |
| Developer / frontend / fullstack role prompts | DevTeam agents follow them when working on target repos |
| Architect role prompt | Architect produces narrowly-scoped issues that respect these boundaries, not "implement the whole feature in one file" issues |

| Step | Detail |
|---|---|
| 9.1 | Add a `## Code hygiene conventions` section to `.github/copilot-instructions.md` — file-size guideline, Blazor code-behind rule, general separation-of-concerns principle. |
| 9.2 | Update `developer.md` role prompt — add a `Constraints` bullet: split files exceeding ~400 lines, never inline `@code` logic in `.razor` files. |
| 9.3 | Update `frontend-developer.md` role prompt — same constraints, plus: always prefer `.razor.cs` over `@code` blocks. |
| 9.4 | Update `fullstack-developer.md` role prompt — same as developer + frontend. |
| 9.5 | Update `architect.md` role prompt — add a sizing guideline for the issues it produces: each issue should touch ≤ ~5 files and produce ≤ ~400 lines of new/changed code. If larger, split into sub-issues. |
| 9.6 | Consider a `hygiene` superpower (`.devteam-source\superpowers\hygiene.md`) that any role can load: describes the full set of hygiene rules in a terse, referenceable format. |
| 9.7 | Smoke test: add a test verifying that a developer role response that would create a 500-line file gets flagged (or that the role prompt addition is present in the loaded role text). |

**Estimated scope:** Small — almost entirely prompt/markdown edits. Step 9.7 is the only runtime touchpoint.

---

## 10 — Orchestrator-driven loop (agents own the iteration)

**The question:** Could the iteration pipeline *just* be the orchestrator spawning subagents — instead of the external `LoopExecutor` picking issues by priority and calling one agent per iteration?

**Current model (external loop):** `LoopExecutor` lives *outside* agents. It picks ready issues by priority/dependency, calls one agent per issue, parses the structured `OUTCOME/SUMMARY/ISSUES/QUESTIONS` response, updates `workspace.json`, and repeats. Agents are stateless per call.

**Proposed model (internal orchestration):** One orchestrator session stays alive per loop batch. It reads the issue board, spawns bounded sub-sessions for each issue *as tool calls* (via a `spawn_agent` tool exposed by `WorkspaceMcpServer`), collects results into its context window, and decides what to queue next. The loop lives inside the agent's own reasoning.

**Why this is better:**
- Orchestrator sees *all* results in context — richer sequencing than a priority-sorted state machine
- Natural fit for the Copilot SDK (nested tool calls → child sessions are a first-class pattern)
- Enables the orchestrator to dynamically adjust what runs next based on what just completed — not possible with the current pre-queued model
- Parallel execution becomes the orchestrator's choice, not a `max-subagents` knob

**Why the external loop must stay as the durability layer:**
- If the orchestrator session crashes, `workspace.json` checkpoints survive
- Long orchestrator sessions hit context window limits; external iteration resets cleanly
- Cost per agent call remains trackable at the runtime level
- `/stop` and `/wait` need a managed `Task` to cancel/await — internal orchestration has no clean save point

**The hybrid architecture:** `LoopExecutor` becomes minimal — boot the orchestrator, wait for it to flag `batch_complete`, persist state, optionally repeat. The orchestrator *owns* what runs in parallel within a batch; the runtime handles child sessions, budget, and persistence around it.

**Relationship to item 8:** The scout/navigator inline sub-call (item 8) is the first instance of this pattern. Generalising `spawn_agent` to all executor roles gives the full orchestrator-driven model. Item 8 is the proof-of-concept; item 10 is the architectural completion.

| Step | Detail |
|---|---|
| 10.1 | **Architect spike** — define the transition: what does `LoopExecutor.RunAsync` look like when the orchestrator drives the batch? What's the contract for `batch_complete` vs the current iteration count? |
| 10.2 | **`spawn_agent` tool** — expose `spawn_agent(role_slug, issue_id, context_hint)` via `WorkspaceMcpServer`. The runtime creates a child session, runs it to completion, writes the result to `workspace.json`, and returns a compact result summary to the caller's context. |
| 10.3 | **Orchestrator role update** — rewrite `orchestrator.md` to use `spawn_agent` as its primary execution mechanism. It reads the board, decides what to spawn (and in parallel), calls `spawn_agent` for each, reviews results, and either queues more work or signals `OUTCOME: completed`. |
| 10.4 | **Durability bridge** — ensure every `spawn_agent` call persists its result to `workspace.json` before returning to the orchestrator. If the orchestrator session is lost, the external loop can reconstruct state from the store and restart cleanly. |
| 10.5 | **Budget propagation** — child sessions spawned via `spawn_agent` must deduct from the same `WorkspaceBudget` as the parent. Add a shared budget context passed through the session factory. |
| 10.6 | **Backwards compatibility** — keep the current priority-queue path as a fallback when no orchestrator role is present (or when `--mode sequential` is set). The issue board remains the source of truth regardless of execution path. |
| 10.7 | Smoke tests for the orchestrator-driven path: verify that a batch started by the orchestrator produces the same `workspace.json` outcome as the current `LoopExecutor` path on identical input. |

**Estimated scope:** Large — significant runtime change. Items 10.2 and 10.4 are the core risk. Requires item 8 as a prerequisite (shared sub-session infrastructure). Likely 1 architect issue + 5–7 developer issues.

---