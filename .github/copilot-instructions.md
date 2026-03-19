# Ralph repository instructions

## Commands present in this repository

- Initialize a Ralph project scaffold in the current repo:
  - `pwsh -NoProfile -File .\ralph.ps1 -Init`
- Run the Ralph iteration loop from this engine repo:
  - `pwsh -NoProfile -File .\ralph.ps1 -Iterations 25 -Credits 25`
  - Common debugging form: `pwsh -NoProfile -File .\ralph.ps1 -Iterations 40 -Verbose`
  - Continue a previous run: `pwsh -NoProfile -File .\ralph.ps1 -Continue -Iterations +10 -Credits +20`
- Be careful with command paths when copying examples from prompts or comments: this engine repo runs from `.\ralph.ps1`, while generated project workspaces often run Ralph from `.\.ralph\ralph.ps1`.
- Run the repository test harness:
  - `pwsh -NoProfile -File .\.ralph-source\scripts\test-parallel.ps1`
  - This is the only concrete test entrypoint in the repo today; it runs the built-in parallel-job scenarios together rather than exposing a single-test selector.

## High-level architecture

- `ralph.ps1` is the top-level entrypoint. It stays thin and mainly wires together dot-sourced modules from `.ralph-source\scripts\` in dependency order: `utils.ps1`, `copilot.ps1`, `models.ps1`, `parallel.ps1`, `init.ps1`, `preflight.ps1`, then `iteration.ps1`.
- The repo is the Ralph engine itself. Engine code lives in `ralph.ps1` and `.ralph-source\scripts\`. The `.ralph\...` tree referenced throughout the code is runtime project state that gets scaffolded into a target repository by `Invoke-RalphInit`; it is not the source layout of this engine repo.
- `init.ps1` scaffolds the runtime workspace: `.ralph\.ralph-project\` for goal/roadmap/issues/decisions, `.ralph\.ralph-state\` for ephemeral run state, and `src\` for the downstream project Ralph will operate on.
- `preflight.ps1` validates the repo, ensures required runtime files exist, resets or preserves state depending on `-Continue`, rebuilds the issue index, and initializes credit/iteration tracking in `.ralph\.ralph-state\credits.json`.
- `iteration.ps1` is the orchestration core. Each iteration reads `GOAL.md`, `ROADMAP.md`, the role file, issue board, acceptance file, and previous handoff; snapshots the current diff; builds the Copilot prompt; archives full output/result files; optionally executes `CMD:` lines; and updates credits/history.
- Handoff text is not just prose. `models.ps1` parses directives like `NEXT_ROLE:`, `NEXT_MODEL:`, `PIPELINE:`, and `PARALLEL:` from `.ralph\.ralph-state\handoff.md`, and `MODELS.json` is the single source of truth for model names, costs, and the default fallback model.
- Parallel execution infrastructure exists in `parallel.ps1`: it creates isolated git worktrees, starts background Copilot jobs, merges results back, and can invoke a conflict-resolver agent. However, `iteration.ps1` currently short-circuits that path and prints that parallel dispatch is disabled, so `PARALLEL:` is designed for the architecture but not actively executed in the current loop.
- The prompt contract lives under `.ralph-source\AGENTS.md`, `.ralph-source\AGENTS-SUBAGENT.md`, `.ralph-source\roles\`, and `.ralph-source\superpowers\`. Changes there are behavior changes, not just documentation changes, because those files are injected into Copilot prompts at runtime.

## Key conventions

- Keep orchestration logic modular. Reusable behavior belongs in `.ralph-source\scripts\*.ps1`; `ralph.ps1` should remain an entrypoint that wires modules together.
- Preserve the current dot-source order in `ralph.ps1`. Several modules depend on helpers or globals from earlier imports.
- Do not hardcode model defaults or costs in code changes. Read and update `.ralph-source\MODELS.json` instead; `models.ps1` treats it as the authoritative source.
- Be careful about path context when editing prompts or docs. Many prompt strings refer to runtime paths such as `.ralph/.ralph-project/...` and `src/`; those are correct for generated project workspaces even though this engine repo stores its own code under `.ralph-source\scripts\`.
- The issue workflow is file-backed. Issues live at `.ralph\.ralph-project\issues\NNNN-slug.md`, `_index.md` is the summary table, and statuses are expected to move through `open -> in-progress -> done` or `blocked`.
- Output shape is strict. Agent responses are expected to write a single Markdown handoff with the sections defined in `AGENTS.md`; downstream parsing and iteration routing depend on those conventions.
- `CLANCY.md` is a live user-override channel inside generated Ralph workspaces. `iteration.ps1` treats it as highest-priority input, requires a `## CLANCY Response` section in output, and can auto-create a tracked issue when the override is acknowledged.
- `CMD:` lines are meaningful protocol, not just documentation. `iteration.ps1` extracts them from Copilot output and can auto-run only allowlisted commands when `-AutoRunAllowed` is enabled.
- Treat verification guidance in the superpowers files as executable process. In particular, `superpowers\verify.md` is the repository’s explicit rule that completion claims must be backed by a fresh command result.
- If you touch parallel-agent behavior, review both `parallel.ps1` and the preserved parallel block in `iteration.ps1`, plus `AGENTS-SUBAGENT.md`; those pieces are coupled.
