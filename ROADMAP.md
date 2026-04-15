# DevTeam Roadmap

## Priority order

Items below are ordered by execution priority. Each item builds on what came before.

| Priority | Item | Scope | Rationale |
|---|---|---|---|
| 1 | **#8 — Navigator scout** | Medium | Proof-of-concept for the `spawn_agent` sub-session pattern. Required before item 10. |
| 2 | **#10 — Orchestrator-driven loop** | Large | Foundational architectural change: the orchestrator owns what runs and in what order. Affects #14, #15, and all future loop behavior. Do this before adding more loop features. |
| 3 | **#16 — Console cursor + multiline** | Small | Self-contained UX fix, no architectural coupling. Good to have before adding more interactive features. |
| 4 | **#15 — Cross-family AI review** | Small-Medium | Small model-selection change. Gains more value once the orchestrator drives role assignment (item 10). |
| 5 | **#14 — Git worktrees** | Large | Parallel conflict-safety. Orchestrator-driven parallelism (#10) should land first so the worktree lifecycle aligns with how batches are actually formed. |
| 6 | **#12 — Brownfield init** | Medium | Reconnaissance for real existing codebases. Independent of items above. |
| 7 | **#13 — Container/CI mode** | Medium | Deployment and automation path. Lower priority until core loop is solid. |
| 8 | **#6 — GitHub mode** | Major | Issues/PRs as the work queue. Major feature; build after orchestrator loop stabilizes. |

> Items 9 (hygiene conventions) and ATM fixes are **complete** as of April 2026.
> Item 7b (Adventure mode) is deliberately deprioritized until core loop is solid.

---

## 14 — Git worktree support for parallel agents

**Goal:** When multiple agents run in parallel, each agent currently works in the same working directory. This causes silent file-level conflicts: two agents editing the same file produce only one agent's changes. Git worktrees give each parallel agent its own isolated branch + working directory, making parallel execution truly conflict-safe.

**How it works today:** `LoopExecutor` runs up to `MaxSubagents` issues concurrently. They all write to `state.RepoRoot`. If two developer agents both edit `src/Api.cs`, one wins and the other's changes are lost with no error.

**Proposed model:**
1. Before spawning a parallel batch, `LoopExecutor` creates a git worktree per agent: `git worktree add .devteam/worktrees/issue-{id} -b devteam/issue-{id}`
2. Each agent runs with `WorkingDirectory` set to its worktree path instead of `RepoRoot`
3. After an agent completes successfully, its branch is merged back into the main branch
4. Conflicts are surfaced as a new `ConflictResolution` issue assigned to the `developer` or `architect` role with the diff attached
5. After all agents complete (including conflict resolution), the worktrees are removed

**Conflict resolution strategy:** Rather than auto-merge (which produces corrupt output), create a first-class `ConflictResolution` issue. The assigned role sees: the conflict markers, both versions, the context of what each agent was trying to do. It resolves and commits. This keeps the human-readable issue board as the conflict audit trail.

| Step | Detail |
|---|---|
| 14.1 | Architect spike — define the worktree lifecycle: when to create, when to merge, when to clean up. What happens if the orchestrator crashes mid-batch? (Answer: worktrees survive and are re-attached on next run.) |
| 14.2 | Add `WorktreeMode` flag to `RuntimeConfiguration`. Default off. Enable with `--worktrees` on `run-loop` or via `/worktrees on` in the shell. |
| 14.3 | `IGitRepository` extension — add `CreateWorktree(repoRoot, path, branch)`, `RemoveWorktree(repoRoot, path)`, `MergeWorktree(repoRoot, branch)`, `GetConflictedFiles(repoRoot)`. Implement in `ProcessGitRepository`. |
| 14.4 | `LoopExecutor` worktree allocation — before spawning parallel runs, create one worktree per run. Pass the worktree path as `WorkingDirectory` to `AgentInvocationRequest`. |
| 14.5 | Post-merge conflict detection — after each worktree merge, check for conflicts. If found, create a `ConflictResolution` issue with the conflicted diff as the issue detail. Cancel the conflicted worktree merge and leave the worktree until the resolution issue completes. |
| 14.6 | Shell `/worktrees` command — list active worktrees, their associated issue, and status (running/merged/conflict). |
| 14.7 | Smoke tests for worktree lifecycle: create, parallel run, merge, conflict detection, cleanup. |

**Estimated scope:** Large — touches `IGitRepository`, `LoopExecutor`, `WorkspaceState` (worktree registry), and the shell. 1 architect issue, 6–8 developer issues.

---

## 15 — AI family cross-pollination for review roles

**Goal:** When an OpenAI model builds something, an Anthropic model reviews it (and vice versa). Independent AI families have different blind spots; cross-family review catches errors that same-family review misses.

**Current model selection:** `RoleModelPolicy.ModelPool` is a list of models. `LoopExecutor` picks randomly from affordable models in the pool. There is no awareness of what model built the artifact being reviewed.

**Proposed model:**
1. Add a `Family` field to `ModelDefinition`: `"openai"`, `"anthropic"`, `"google"`, `"other"`
2. `AgentRun` already records `ModelName` — derive `ModelFamily` from it
3. When a review/test role is selecting from its model pool, it checks the family of the model that completed the *predecessor issue* and prefers a different family
4. Falls back to random selection if no cross-family model is affordable

**Family tagging (initial defaults):**

| Family | Models |
|---|---|
| `openai` | gpt-5.4, gpt-5.4-mini, gpt-5-mini, gpt-5.1, gpt-5.2, gpt-5.3-codex |
| `anthropic` | claude-sonnet-4.6, claude-haiku-4.5, claude-opus-4.6, claude-sonnet-4.5 |
| `google` | gemini-3.1-pro-preview, gemini-3-flash-preview |

**Which roles benefit:** `reviewer`, `tester`, `security`, `analyst` — roles whose value is in finding what the builder missed.

| Step | Detail |
|---|---|
| 15.1 | Add `Family` field to `ModelDefinition`. Seed defaults from the table above. If `MODELS.json` specifies `"Family"`, use that; otherwise infer from the model name prefix. |
| 15.2 | Add `ModelFamily` (string) to `AgentRun` so we record which family was used per run. Populate from `ModelDefinition.Family` when the run is created. |
| 15.3 | Add `CrossFamilyReviewEnabled` flag to `RuntimeConfiguration` (default: `true`). |
| 15.4 | In model selection logic (currently in `DevTeamRuntime` / `BudgetService`): when `CrossFamilyReviewEnabled` and the issue depends on a predecessor run, filter the model pool to prefer a different family than the predecessor's `ModelFamily`. Fall back to full pool if no cross-family model is affordable. |
| 15.5 | Add `/cross-family <on\|off>` shell command. |
| 15.6 | Smoke tests: verify that a reviewer issue whose predecessor used an `openai` model selects an `anthropic` model from its pool when both are affordable. |

**Estimated scope:** Small-medium — mostly model selection logic + a new field. 1 architect issue, 2–3 developer issues.

---

## 16 — Console input: full cursor and multiline navigation

**Goal:** Left/right arrows move the cursor within typed text. Up/down arrows behave *contextually*: in a single-line input they navigate command history (current behaviour); in a multiline input they move the cursor vertically within the buffer (like any modern text editor or chat interface). Currently Left/Right are ignored, and Up/Down always trigger history regardless of context.

**Current state:** `SpectreShellHost.ReadInput` uses `Console.ReadKey(intercept: true)` and a `StringBuilder inputBuffer`. Left/Right `ConsoleKey` values have no handler. Up/Down always navigate `_history` — even when the user is mid-edit on line 3 of a 5-line prompt.

**What needs to change — horizontal navigation:**
- Track a `cursorPosition` integer (0 = before first char, `inputBuffer.Length` = after last)
- Left: decrement cursor (clamped to 0); wraps to end of previous line at column 0
- Right: increment cursor (clamped to length); wraps to start of next line at end of line
- Backspace: delete char at `cursorPosition - 1`, decrement cursor
- Delete: delete char at `cursorPosition` (no cursor movement)
- Character input: insert at `cursorPosition`, increment cursor
- Ctrl+Left / Ctrl+Right: word-by-word jumping (next/previous whitespace boundary)
- Home key: move cursor to start of current line (within multiline buffer)
- End key: move cursor to end of current line

**What needs to change — vertical navigation (the tricky part):**

The correct behaviour depends on the current input context:

| Condition | Up arrow | Down arrow |
|---|---|---|
| Single-line input OR cursor is on line 1 | Navigate history (current behaviour) | Navigate history (current behaviour) |
| Multiline input AND cursor row > 0 | Move cursor up one logical line, same column | Move cursor down one logical line, same column |
| Multiline input AND cursor on last line | Move cursor down one line if possible, else navigate history forward | Navigate history forward |

This matches the behaviour of VS Code's terminal input, GitHub's comment box, and this chat interface.

**Design decision required (step 16.0):** The boundary case — "cursor on line 1 of a multiline input" — should trigger history navigation or do nothing? Options:
- (A) Always prefer history on line 1 regardless of multiline state — consistent with current muscle memory
- (B) Never trigger history while multiline content exists — prevents accidental history overwrite of a long prompt
- (C) Require Ctrl+Up to navigate history when in multiline mode — explicit, never ambiguous

This is a UX judgement call that should be an explicit **architect review issue** before implementation. The table above uses option (A) as the safe default, but the architect issue should evaluate all three with a recommendation.

**Rendering:**
- `BuildInput` / `ShellPanelBuilder.BuildInput` receives `cursorPosition` and inserts a `▌` marker at the cursor offset
- In multiline mode, the marker appears mid-line, not just at the end
- The `▌` approach is frame-based (100ms Live display tick) — no flicker concern

| Step | Detail |
|---|---|
| 16.0 | **Architect review issue** — evaluate Up/Down behaviour options (A/B/C above) for the multiline boundary case. Produce a recommendation and update this step before development starts. |
| 16.1 | Add `cursorPosition` tracking to `ReadInput`. Update character input and backspace to use cursor position (insert-at-cursor, not always append). |
| 16.2 | Implement Left/Right, Ctrl+Left/Right, Delete, line-aware Home/End. |
| 16.3 | Implement context-aware Up/Down per the architect-approved option. Track `cursorRow` derived from `inputBuffer` newline count and `cursorPosition` offset. |
| 16.4 | Update `BuildInput` / `ShellPanelBuilder.BuildInput` to accept `cursorPosition`, insert `▌` at the right offset within the rendered lines. |
| 16.5 | Thread `cursorPosition` through `SpectreShellHost.RunAsync` → `UpdateLayout`. |
| 16.6 | Shell tests (item 11): verify `BuildInput` places `▌` at position 0, mid-string, end-of-string, and mid-multiline. Verify Up/Down dispatch logic for single-line vs multiline with cursor on different rows. |

**Estimated scope:** Small-medium — contained in `SpectreShellHost` and `ShellPanelBuilder`, plus the architect review. Steps 16.1–16.2 are straightforward; 16.3 is the interesting part.

---

## 11 — Terminal UI snapshot tests

**Goal:** Make the SpectrConsole shell testable. Currently any change to `SpectreShellHost` or `ShellService` can silently break rendering with no automated signal.

**Architecture insight:** `ShellService` is already separated from rendering — it's pure state (`Messages`, `LayoutSnapshot`). `SpectreShellHost` is the renderer. This gives two clean test layers without needing Playwright/Cypress or browser tooling.

**Layer 1 — ShellService logic tests (no console):**
Call `ProcessInputAsync` with command strings, assert on `Messages` and `LayoutSnapshot`. Covers: question flow, plan approval, loop-running state transitions, `/status`, `/plan`, `@role` invocation, error handling.

**Layer 2 — Panel rendering tests (TestConsole):**
Spectre.Console ships a `TestConsole` in `Spectre.Console.Testing` that captures all rendered output as a string. Extract `SpectreShellHost`'s private `Build*Panel` methods into an `internal ShellPanelBuilder` class, then render each panel in tests and assert on visible content.

**Layer 3 — Scenario regression tests:**
Use the existing `UiHarness.BuildScenarioState` scenarios (empty, planning, architect, execution, questions) as test fixtures. Render the full layout snapshot, save output as a snapshot file, fail if it changes unexpectedly.

| Step | Detail |
|---|---|
| 11.1 | Create `tests\DevTeam.ShellTests\` project. Reference `DevTeam.Cli` and `DevTeam.Core`. Add `Spectre.Console.Testing` package. |
| 11.2 | Extract `SpectreShellHost`'s `Build*Panel` private static methods into `internal static class ShellPanelBuilder`. Mark `internal` members visible to the test project via `InternalsVisibleTo`. |
| 11.3 | Add `ShellServiceTests` — test `ShellService` state management: commands, question and approval flow, loop-running guard, `@role` parsing. All state-level, no console. |
| 11.4 | Add `ShellPanelRenderTests` — render each panel type through `TestConsole`. Assert that role names, issue IDs, phase labels, budget numbers appear correctly. |
| 11.5 | Add `UiHarnessScenarioTests` — render the full layout for each `UiHarness` scenario. Snapshot the output. These are the regression tests — any layout change that changes visible text fails here first. |
| 11.6 | Add `--no-tty` flag to `SpectreShellHost.RunAsync` (and wire from CLI args): when set, disable `LiveDisplay` and fall back to plain `Console.WriteLine` log lines. This makes CI/container runs clean without ANSI escape codes. |

**Prerequisite:** `IConsoleOutput` abstraction from Phase 3 (`hygiene-console`) unlocks testing of `CliDispatcher` output too, but isn't required for Phase 11 steps 11.1–11.5. Step 11.6 is independent.

**Estimated scope:** Small-medium — entirely additive, no existing behaviour changes. 1 architect issue, 3–4 developer issues.

---

## 12 — Brownfield init: automatic codebase reconnaissance

**Goal:** When targeting a large existing codebase, `devteam init` should automatically run a one-shot Navigator pass and produce a `CODEBASE_CONTEXT.md` that gets injected into every subsequent planner and architect prompt. This makes DevTeam dramatically less likely to break existing conventions or duplicate existing patterns.

**What the recon covers:** tech stack, folder structure, existing test framework and coverage patterns, key entry points, coding conventions (detected from existing files), known fragile areas (large files, circular deps, no tests).

| Step | Detail |
|---|---|
| 12.1 | Architect spike: define the recon prompt and the `CODEBASE_CONTEXT.md` schema. What's the minimum context a planner needs to not repeat existing work? |
| 12.2 | Add `--recon` flag to `init` command (default: on for non-empty repos). When set, run a single Navigator-style agent call before writing `workspace.json`. |
| 12.3 | Store the recon output as `state.CodebaseContext` in `WorkspaceState`. Persist to `.devteam/codebase-context.md`. |
| 12.4 | Inject `CodebaseContext` into the planner and architect prompt builders in `AgentPromptBuilder` (or equivalent). |
| 12.5 | Add `/recon` shell command to re-run the recon on demand (e.g., after major refactors). |
| 12.6 | Smoke tests: verify that a workspace initialized with `--recon` has a non-empty `CodebaseContext` and that it appears in the generated planner prompt. |

**Estimated scope:** Medium — new init flow, new state field, prompt injection. 1 architect issue, 3–4 developer issues.

---

## 13 — Container and CI mode

**Goal:** Run DevTeam inside a Docker container or GitHub Actions pipeline with zero TTY assumptions. The headless mode already exists (`--verbosity quiet`) but `SpectreShellHost`'s `LiveDisplay` throws or produces corrupt output in non-interactive terminals.

| Step | Detail |
|---|---|
| 13.1 | Auto-detect non-interactive terminal: if `Console.IsOutputRedirected` or `--no-tty` is set, skip `SpectreShellHost` entirely and use plain log output. |
| 13.2 | Add `Dockerfile` to the repo root: installs dotnet, installs the DevTeam tool, copies workspace config. Entrypoint: `devteam run-loop`. |
| 13.3 | Add `.github/workflows/devteam-run.yml` template: checkout → install DevTeam tool → `devteam init --goal "..."` → `devteam run-loop --max-iterations 10 --verbosity quiet`. |
| 13.4 | Non-TTY output format: structured JSON lines (`{"timestamp": ..., "event": ..., "detail": ...}`) as an alternative to plain text, for CI log parsers. Flag: `--output-format jsonl`. |
| 13.5 | Smoke test: run `devteam start` with `Console.IsInputRedirected = true` (simulated) and verify it falls back to non-Live output without throwing. |

**Estimated scope:** Small — mostly infrastructure/config. One developer issue for the TTY detection + fallback, one for the Dockerfile and workflow template.

---

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

## 7b — Adventure mode 🎮 (easter egg — do not prioritise until core loop is solid)

**Inspiration:** Same pixel-agents inspiration, taken all the way.

**Concept:** A hidden `--adventure` flag (or `/adventure` shell command) that replaces the standard Live display with a top-down 2D ASCII map. Each agent role occupies a fixed desk on the map. The user controls a `@` character that can walk around using arrow keys. Walking up to an agent's desk and pressing Enter opens a chat dialog — mechanically equivalent to `@architect <message>` but wrapped in the spatial metaphor.

```
┌─────────────────────────────────────────┐
│  📋 planner       🏗️ architect          │
│  [ done ]         [ thinking... ]       │
│                                         │
│  💻 developer     🧪 tester             │
│  [ running #7 ]   [ idle ]              │
│                                         │
│              @                          │  ← you
└─────────────────────────────────────────┘
  > Walking toward architect...
```

When adjacent to an agent:
- Press **Enter** → opens inline chat, input is sent as `@{role} <text>` to the running session
- The agent's response appears as a speech bubble above their desk tile
- The loop continues running in the background while the user explores

**Why this is cool:** It makes the multi-agent parallel loop *tangible*. You can see all your agents working simultaneously, walk over to the one that's blocked, ask it a question, and watch it unblock in real time. It's a genuine mental model shift from "I submitted a command" to "I talked to someone."

**Prerequisites:** Item 7 (personas) should land first — the persona metadata (`Avatar`, `DisplayName`, `ActiveFlavour`) is reused directly. Item 10 (orchestrator-driven loop) makes the background execution more robust for this interaction pattern.

**Notes:**
- This is explicitly an easter egg — off by default, no documentation in the main README, discoverable via `/help --all` or word of mouth
- Spectre.Console can render the map as a `Canvas` or a grid of `Markup` strings redrawn each tick
- Arrow key navigation conflicts with the existing history navigation — needs a modal input state (normal shell mode vs. adventure mode)

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