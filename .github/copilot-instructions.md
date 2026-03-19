# DevTeam repository instructions

## Build and test commands

- Build the solution:
  - `dotnet build .\DevTeam.slnx`
- Run the smoke tests:
  - `dotnet run --project .\tests\DevTeam.SmokeTests\DevTeam.SmokeTests.csproj`
- Exercise the default Copilot SDK backend directly:
  - `dotnet run --project .\src\DevTeam.Cli\DevTeam.Cli.csproj -- agent-invoke --prompt "Reply with READY and nothing else."`
- Run the autonomous loop with the default runtime verbosity:
  - `dotnet run --project .\src\DevTeam.Cli\DevTeam.Cli.csproj -- run-loop --workspace .devteam --max-iterations 4 --verbosity normal`

## High-level architecture

- `DevTeam.slnx` contains the new runtime. `src\DevTeam.Core` holds the core domain model, prompt asset loading, model policy, Copilot backends, and the loop executor. `src\DevTeam.Cli` exposes the runtime as a CLI.
- Workspace state is persisted under `.devteam\workspace.json` through `WorkspaceStore`. The runtime also creates `.devteam\runs\`, `.devteam\decisions\`, and `.devteam\artifacts\` for loop outputs and future structured artifacts.
- `WorkspaceStore` also regenerates readable markdown issue mirrors under `.devteam\issues\`, including `_index.md` and per-issue files.
- Prompt assets live in `.devteam-source\`. Roles, superpowers, and `MODELS.json` are loaded from there and drive model selection plus runtime prompting.
- The default agent backend is the GitHub Copilot .NET SDK. `CopilotSdkAgentClient` manages sessions, enables streaming, approves tools, and points the SDK at `.devteam-source\superpowers` via `SkillDirectories`. `CopilotCliAgentClient` remains as a fallback adapter.
- `DevTeamRuntime` manages goal, roadmap, issue, question, run, phase, and decision state. `LoopExecutor` runs ready issues, assigns deterministic Copilot session ids, can execute multiple queued runs concurrently, parses structured responses, marks runs complete, and writes run/decision artifacts.

## Key conventions

- Treat `.devteam-source\roles\` and `.devteam-source\superpowers\` as the editable behavior layer. These are markdown-first assets, not compiled code.
- Roles and superpowers may declare tool expectations in frontmatter, for example `tools: rg, git, dotnet`. The loader strips frontmatter from the prompt body and stores the tool list as metadata.
- The loop expects agent replies in a machine-parsable shape:
  - `OUTCOME: completed|blocked|failed`
  - `SUMMARY:`
  - `ISSUES:`
  - `QUESTIONS:`
- Keep issue scope small. The runtime is designed around narrow issues with explicit dependencies rather than one large task per run.
- Every goal starts in `Planning`. Only planning issues are eligible until the user runs `approve-plan`, which switches the workspace to `Execution`.
- During planning approval, freeform user feedback should revise the plan instead of being ignored.
- Questions are the explicit user-input inbox. Blocking questions can halt the loop; non-blocking questions should not stop other ready work.
- Use `run-loop` for actual execution. `run-once` only queues work; `run-loop` executes queued runs and is the command to use when you want the system to keep moving.
- Planning and architecture roles can propose concrete follow-on issues through the structured `ISSUES:` section.
- Do not rely on `NEXT_ROLE` handoffs. The issue board is the workload queue, and each issue already names the role that should execute it.
- Default runtime verbosity is `normal`. Use `--verbosity detailed` when diagnosing loop behavior and `--verbosity quiet` when embedding the command in automation.
