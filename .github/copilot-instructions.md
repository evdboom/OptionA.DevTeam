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
- After meaningful changes, pack the tool locally with an explicit version override (the csproj uses `0.0.0` as a placeholder):
  - `dotnet pack .\src\DevTeam.Cli\DevTeam.Cli.csproj -c Release -o .\nupkg /p:Version=0.5.40`

## High-level architecture

- `DevTeam.slnx` contains the new runtime. `src\DevTeam.Core` holds the core domain model, prompt asset loading, model policy, Copilot backends, and the loop executor. `src\DevTeam.Cli` exposes the runtime as a CLI.
- Workspace state is persisted under `.devteam\workspace.json` through `WorkspaceStore`. The runtime also creates `.devteam\runs\`, `.devteam\decisions\`, and `.devteam\artifacts\` for loop outputs and future structured artifacts.
- `WorkspaceStore` also regenerates readable markdown issue mirrors under `.devteam\issues\`, including `_index.md` and per-issue files.
- Prompt assets live in `.devteam-source\`. Roles, superpowers, `MODELS.json`, and `MCP_SERVERS.json` are loaded from there and drive model selection, external tool access, and runtime prompting.
- External MCP servers are declared in `.devteam-source\MCP_SERVERS.json`. Each entry specifies a `Name`, `Command`, `Args`, optional `Cwd`, `Description`, and `Enabled` flag. Enabled servers are registered with every Copilot SDK session so spawned agents can call their tools.
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
- Every goal starts in `Planning`. The planner produces a high-level strategy without making technology choices. After the user approves (`approve-plan`), the workspace transitions to `ArchitectPlanning` where architect issues run and produce detailed execution issues. A second approval moves the workspace to `Execution`. If no architect issues exist, approval goes directly to `Execution`.
- Auto-approve: when `RuntimeConfiguration.AutoApproveEnabled` is true, the loop automatically approves both the plan and the architect plan. The `autopilot` mode enables auto-approve and is the intended "agents decide everything" workflow.
- During planning approval, freeform user feedback should revise the plan instead of being ignored.
- Questions are the explicit user-input inbox. Blocking questions can halt the loop; non-blocking questions should not stop other ready work.
- Use `run-loop` for actual execution. `run-once` only queues work; `run-loop` executes queued runs and is the command to use when you want the system to keep moving.
- Planning and architecture roles can propose concrete follow-on issues through the structured `ISSUES:` section.
- Do not rely on `NEXT_ROLE` handoffs. The issue board is the workload queue, and each issue already names the role that should execute it.
- Default runtime verbosity is `normal`. Use `--verbosity detailed` when diagnosing loop behavior and `--verbosity quiet` when embedding the command in automation.
- Keep user-facing CLI documentation in sync with features. When adding or changing commands such as `/bug` / `bug-report`, update `README.md` and this `.github\copilot-instructions.md` file in the same change unless there is a strong reason not to.
- `bug-report` is the non-interactive command and `/bug` is the interactive-shell alias for generating a GitHub-issue-ready bug report draft with version, environment, workspace snapshot, and recent shell diagnostics.

## Code hygiene conventions

These apply when editing any source in this repo. DevTeam role prompts should mirror them for target repos.

- **Keep files small and focused.** No file should own multiple concerns. When a file grows past ~400 lines, split it by theme into separate files. Prefer more small files over fewer large ones.
- **Separate presentation from logic.** Blazor `.razor` files contain markup and minimal binding glue only. All logic lives in the paired `.razor.cs` code-behind file. Never put real logic inside `@code { }` blocks. The same principle applies broadly: a file that mixes rendering and domain logic should be split.
- **Entry points are bootstrap only.** `Program.cs` (or equivalent top-level file) should be ≤ ~30 lines: wire DI, resolve the dispatcher, call it. All logic belongs in focused service classes.
- **When adding a feature**, check whether an existing file is already close to the size limit before adding to it. If so, extract first, then add.

## Testability conventions

These apply to all production code in `src\` and to tests in `tests\`. Testability is a first-class design constraint, not an afterthought.

- **Inject all dependencies via constructor.** Never use `new` inside business logic for non-trivial collaborators. Use interfaces, not concrete types, as constructor parameters. This is the single most important rule — it makes every class independently testable.
- **No static I/O.** Do not call `File.*`, `Directory.*`, `Process.Start`, or `Console.*` directly in `DevTeam.Core`. These must be behind injected interfaces (`IFileSystem`, `IGitRepository`, `IConsoleOutput`, etc.) so tests can substitute them without touching the real file system or spawning processes.
- **No static clocks.** Do not call `DateTimeOffset.UtcNow` or `DateTime.Now` directly. Use an injected `ISystemClock`. Tests control time via a fake implementation.
- **No static factory methods as hidden dependencies.** If a class needs a factory, accept `IXxxFactory` via constructor. Nullable parameters that fall back to a static default hide dependencies and break test isolation.
- **Do not seal service and infrastructure classes.** Reserve `sealed` for value objects, DTOs, and records. Sealing infrastructure classes prevents test doubles and proxy-based mocking frameworks from working.
- **Test project structure.** The `tests\` folder contains two projects: `DevTeam.SmokeTests` (end-to-end, integration-style, slow) and `DevTeam.UnitTests` (fast, isolated, one test class per production class). Shared test infrastructure (fakes, builders, helpers) lives in `DevTeam.TestInfrastructure` and is referenced by both test projects.
- **Unit test file size.** Apply the same ~400-line limit to test files. One test class per file. When a test class grows large, extract nested test classes or split by scenario group into separate files.
- **One assert per concept.** Each test method should verify one logical outcome. Prefer multiple focused tests over one test with many unrelated assertions.
- **Smoke tests are the regression guard, not the only guard.** Smoke tests cover the happy-path end-to-end flow. Unit tests cover edge cases, error paths, boundary conditions, and concurrency scenarios. Do not rely solely on smoke tests as evidence of correctness.

## ATM prime directive

Every codebase DevTeam builds or modifies must be **Auditable, Testable, and Maintainable (ATM)**. This is the prime directive for all agent output — it applies to this repo *and* to any target repo an agent works on.

**Auditable** — every significant action leaves a trace:
- Commands that fail must surface errors visibly. Never swallow exceptions silently (`catch {}`, `_ = Task.Run(...)`, discarded `Task` results).
- State mutations must be logged or written to an artifact so the cause of any workspace state can be reconstructed.
- Use awaited tasks or queued consumers, not fire-and-forget, so failures are always observable.

**Testable** — every class can be tested in isolation:
- No `File.*`, `Directory.*`, `Process.Start`, or `Console.*` in core/domain logic. Inject `IFileSystem`, `IGitRepository`, or equivalent interfaces.
- No `DateTime.Now` / `DateTimeOffset.UtcNow` in core logic. Inject `ISystemClock`; tests control time through a fake.
- No hidden static dependencies. All collaborators via constructor injection.
- Tests must assert on *state and values*, not just *existence*. Verify field values and state transitions, not merely that a count is non-zero.
- Cover error paths and boundary conditions, not only the happy path.

**Maintainable** — any developer can understand and extend the code:
- Files ≤ ~400 lines, one concern per file.
- Constructor injection throughout. No `new` for non-trivial collaborators in business logic.
- Follow the style patterns already established in the target codebase. Don't introduce new patterns without justification.
