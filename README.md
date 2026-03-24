# OptionA.DevTeam

`OptionA.DevTeam` is a .NET global tool that runs a plan-first autonomous dev team from your terminal using GitHub Copilot.

Unlike single-prompt coding agents, DevTeam splits work into narrow issues with explicit dependencies, tracks decisions and questions, and schedules multi-role pipelines (navigator → architect → developer → security → tester) that execute concurrently when areas don't conflict. You stay in control through plan approval and feedback — or enable autopilot and let the agents decide everything.

## How it works

```
 You                          DevTeam                        Copilot SDK
  │                              │                               │
  │  /init --goal "..."          │                               │
  ├─────────────────────────────►│                               │
  │                              │                               │
  │  /plan                       │  planner agent ──────────────►│
  ├─────────────────────────────►│◄──────── high-level plan ─────│
  │◄──── show plan ──────────────│                               │
  │                              │                               │
  │  /approve                    │  architect agent ────────────►│
  ├─────────────────────────────►│◄── tech choices + issues ─────│
  │◄──── show architect plan ────│                               │
  │                              │                               │
  │  /approve                    │  orchestrator selects batch   │
  ├─────────────────────────────►│  developer ──────────────────►│
  │  /run                        │  tester ─────────────────────►│
  ├─────────────────────────────►│◄──── results + new issues ────│
  │                              │                               │
  │                              │  reevaluate ─► next batch     │
  │◄──── status / questions ─────│         ... loop repeats ...  │
```

The workflow has three phases with two approval gates:

1. **Planning** — the `planner` role produces a high-level strategy: milestones, delivery order, risks. It does *not* make technology choices or create implementation-level issues.
2. **Review plan** — you read the plan and either give feedback (which revises it) or approve.
3. **Architecture** — architect issues run and produce technology decisions, concrete execution issues, and ADRs.
4. **Review architect plan** — you review the architect output and approve to move to execution.
5. **Execution** — worker roles (navigator, developer, security, tester, docs, etc.) run the selected issues. Each completed issue can propose follow-on issues, creating multi-role pipelines.
6. **Loop** — the runtime reevaluates dependencies, advances pipelines, and repeats until done or budget is exhausted.

In **autopilot** mode both approval gates are skipped automatically — agents decide everything.

## What it does

- Initialize a workspace for a new or existing repository
- Generate an initial plan and revise it with feedback before execution
- Run an execution loop with specialized agent roles
- Schedule multi-role pipelines (navigator → architect → developer → security → tester) automatically
- Run independent areas concurrently while preventing conflicts
- Track questions, issues, decisions, and budget in a local workspace
- Expose a workspace MCP server so agents can read and write project state
- Connect external MCP servers (e.g., Context7 for library docs) to every spawned agent
- Switch between modes such as `develop` and `creative-writing`
- Generate a sanitized bug report draft with version, workspace state, and recent shell diagnostics

## Installation

Install from NuGet:

```powershell
dotnet tool install --global OptionA.DevTeam
```

Update an existing install:

```powershell
dotnet tool update --global OptionA.DevTeam
```

The CLI can also check for updates and trigger the global tool update for you:

```powershell
devteam check-update
devteam update
```

After installation, use the `devteam` command:

```powershell
devteam /help
```

## Requirements

- [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0)
- [GitHub Copilot SDK](https://github.com/features/copilot) — the runtime uses the Copilot .NET SDK as its default backend.
- [GitHub Copilot CLI](https://github.com/github/copilot-cli) installed and available on `PATH` as `copilot`. DevTeam uses your installed Copilot CLI instead of packaging its own copy, so Copilot updates do not require a DevTeam republish.
- You must be authenticated with GitHub Copilot (via `gh auth login` or an active Copilot subscription).

## Quick start

Create or open a repository, then initialize DevTeam:

```powershell
devteam /init --workspace .devteam --goal "Build a Flappy Bird game"
```

For longer goals or markdown-based project briefs, load the goal from a file:

```powershell
devteam /init --workspace .devteam --goal-file .\goal.md
```

This creates a local workspace and, if needed, initializes a git repository.

Start the interactive shell:

```powershell
devteam /start --workspace .devteam
```

Generate the first plan:

```text
/plan
```

Review the plan, then either give feedback as plain text or approve it:

```text
/approve Start building.
```

Run the execution loop:

```text
/run --max-iterations 5 --max-subagents 3
```

Capture a bug report draft you can paste into a GitHub issue:

```text
/bug
/bug --save .\bugreport.md
```

### Example session

```
> devteam /init --workspace .devteam --goal "Build a CLI Tetris clone"
  Workspace initialized at .devteam
  Goal set: Build a CLI Tetris clone

> devteam /start --workspace .devteam
  DevTeam shell (type /help for commands)

devteam> /plan
  Running planner...
  Plan written to .devteam\plan.md

devteam> /approve Looks good.
  Plan approved. Workspace moved to ArchitectPlanning phase.
  Running architect issues...

devteam> /approve Ship it.
  Architect plan approved. Workspace moved to Execution phase.

devteam> /run --max-iterations 5 --max-subagents 3
  Iteration 1: orchestrator selected 2 issues
    [ISS-3] developer: Implement game loop (pipeline 1)
    [ISS-4] developer: Implement renderer (pipeline 2)
  Budget: 4/25 credits used (2/6 premium)
  Iteration 2: pipelines advanced
    [ISS-5] tester: Test game loop (pipeline 1)
    [ISS-6] tester: Test renderer (pipeline 2)
  Budget: 8/25 credits used (2/6 premium)
  ...
  Iteration 5: 3 pipelines completed, 1 question pending
  Loop finished: waiting-for-input
```

## Interactive shell commands

```text
/status                                   Show workspace state
/plan                                     Generate or show the plan
/feedback <text>                          Revise the plan with feedback
/approve [note]                           Approve the plan and move to execution
/run [--max-iterations N] [--max-subagents N]  Run the execution loop
/questions                                List open questions
/answer-question <ID> <answer>            Answer a question
/budget [--total N] [--premium N]         Show or adjust budget
/bug [--save PATH] [--redact-paths true|false]  Generate a bug report draft
/keep-awake on|off                        Prevent Windows sleep during runs
/check-update                             Check for newer versions
/update                                   Update the global tool
/exit                                     Exit the shell
```

## Non-interactive usage

Every shell command also works as a standalone CLI invocation:

```powershell
devteam /init --workspace .devteam --goal "Build a CLI Tetris clone"
devteam /plan --workspace .devteam
devteam /approve-plan --workspace .devteam --note "Looks good. Start building."
devteam /run --workspace .devteam --max-iterations 5 --max-subagents 3
devteam /status --workspace .devteam
devteam /questions --workspace .devteam
devteam /answer-question 1 "Use keyboard controls only." --workspace .devteam
devteam bug-report --workspace .devteam --save .\bugreport.md
```

Update the goal later:

```powershell
devteam /set-goal --workspace .devteam --goal-file .\goal.md
```

Keep Windows awake during a long run:

```powershell
devteam /run --workspace .devteam --max-iterations 10 --keep-awake true
```

Generate a GitHub-issue-ready bug report draft from the CLI:

```powershell
devteam bug-report --workspace .devteam
devteam bug-report --workspace .devteam --save .\bugreport.md
```

By default the report redacts common local paths and includes the current DevTeam version, environment details, workspace phase, active goal, recent runs, and any recent interactive shell commands or errors captured in the current shell session.

## Modes

The runtime supports mode-specific guardrails that shape how agents approach work.

Switch modes with:

```powershell
devteam /set-mode creative-writing --workspace .devteam
```

Packaged modes:

| Mode | Description |
|------|-------------|
| `develop` (default) | Build working software, add tests, validate builds |
| `creative-writing` | Preserve voice, revise in passes, surface narrative gaps |
| `autopilot` | Full autonomy — agents decide everything without approval gates |

Mode guardrails are injected into every agent prompt so all roles follow the active mode's rules.

Autopilot mode automatically approves both the plan and architect plan, so the loop runs end-to-end without pausing for human input. Enable it at init time or switch later:

```powershell
devteam /init --workspace .devteam --goal "Build a Flappy Bird game" --mode autopilot
devteam /set-mode autopilot --workspace .devteam
```

## Budget

Every agent invocation costs credits. The budget is a local cost cap that prevents runaway iteration — when credits are exhausted, roles fall back to free models (like `gpt-5-mini`) instead of stopping.

Credits are a simple abstraction over model cost tiers:

| Tier | Cost | Examples |
|------|------|----------|
| Premium | 3 credits | `claude-opus-4.6` |
| Standard | 1 credit | `claude-sonnet-4.6`, `gpt-5.4`, `gemini-3.1-pro-preview` |
| Light | 0.33 credits | `claude-haiku-4.5`, `gemini-3-flash-preview` |
| Free | 0 credits | `gpt-5-mini`, `gpt-4.1`, `gpt-4o` |

The budget has two caps:

- **Total** (default 25) — maximum credits across all models. When exhausted, every role falls back to a free model.
- **Premium** (default 6) — maximum credits for premium models only. When exhausted, premium roles fall back to their configured fallback model while standard/free models continue normally.

View or adjust the budget:

```text
/budget
/budget --total 50 --premium 12
```

The budget is displayed after each loop iteration so you can see spend in real time.

## Pipelines

When work is approved, the runtime automatically creates multi-role pipelines. For example, a feature issue assigned to `architect` will, on completion, generate a follow-up for `developer`, and then `tester`. Each stage must complete before the next starts.

Independent pipelines (different `area` values) run concurrently. Pipelines in the same area are serialized to avoid merge conflicts.

## Extending with MCP servers

Spawned agents can access external tools through MCP (Model Context Protocol) servers. Two types are supported:

### Workspace MCP server

The runtime automatically exposes a local `devteam-workspace` MCP server that lets agents read and write workspace state (issues, questions, decisions). This is enabled by default and can be toggled:

```powershell
devteam /init --workspace .devteam --goal "..." --workspace-mcp true
```

### External MCP servers

Declare additional MCP servers in `.devteam-source/MCP_SERVERS.json`. Every enabled server is registered with every Copilot SDK session, so all spawned agents can call their tools.

```json
[
  {
    "Name": "context7",
    "Command": "npx",
    "Args": ["-y", "@upstash/context7-mcp@latest"],
    "Description": "Library documentation lookup via Context7.",
    "Enabled": true
  }
]
```

Each entry supports:

| Field | Required | Description |
|-------|----------|-------------|
| `Name` | yes | Unique server name (used as the MCP server key) |
| `Command` | yes | Executable to launch (e.g., `npx`, `node`, `dotnet`) |
| `Args` | yes | Command-line arguments |
| `Cwd` | no | Working directory (defaults to repo root) |
| `Description` | no | Human-readable description |
| `Enabled` | no | `true` by default; set `false` to disable without removing |

Context7 ships as a default entry, giving every agent access to up-to-date library documentation.

## Customizing roles, modes, and superpowers

DevTeam ships with built-in roles, modes, superpowers, and model policies. To customize them for your project, copy the defaults into your repo:

```powershell
devteam /customize
```

This creates a `.devteam-source/` directory containing all packaged assets:

```
.devteam-source/
├── roles/              Agent personas
│   ├── architect.md
│   ├── developer.md
│   ├── tester.md
│   ├── navigator.md
│   ├── analyst.md
│   ├── security.md
│   ├── docs.md
│   ├── devops.md
│   ├── refactorer.md
│   └── ...
├── modes/              Mode-specific guardrails
│   ├── develop.md
│   └── creative-writing.md
├── superpowers/        Reusable skill prompts
│   ├── tdd.md
│   ├── review.md
│   ├── debug.md
│   └── ...
├── MODELS.json         Model selection per role
└── MCP_SERVERS.json    External MCP servers for agents
```

Edit the markdown files to adjust behavior. Roles and superpowers can declare tool expectations in frontmatter:

```markdown
---
tools: rg, git, dotnet
---
# Role: Architect
...
```

Project-level assets always override the packaged defaults. To reset a file, delete it and the built-in version takes over. Use `--force` to overwrite existing files with the latest packaged versions.

## Workspace files

The CLI writes its local runtime state under the workspace directory (typically `.devteam/`):

```
.devteam/
├── workspace.json          Main manifest (phase, budget, runtime config)
├── plan.md                 Generated plan (readable, versioned)
├── questions.md            Open questions for the user
├── state/
│   ├── issues.json         Issue board
│   ├── runs.json           Agent run history
│   ├── sessions.json       Copilot session tracking
│   ├── decisions.json      Architectural decisions
│   ├── pipelines.json      Multi-role pipeline state
│   └── ...
├── issues/                 Markdown mirrors of the issue board
│   ├── _index.md
│   └── ISS-*.md
├── runs/                   Per-run artifacts
└── decisions/              Per-decision artifacts
```

You do not need to edit these files manually. They are there for visibility and version control.

## Packaging and local development

Build the solution:

```powershell
dotnet build .\DevTeam.slnx
```

Run the smoke tests:

```powershell
dotnet run --project .\tests\DevTeam.SmokeTests\DevTeam.SmokeTests.csproj
```

Pack the tool locally:

```powershell
dotnet pack .\src\DevTeam.Cli\DevTeam.Cli.csproj -c Release -o .\nupkg
```

Install a local build:

```powershell
dotnet tool update --global --add-source .\nupkg OptionA.DevTeam
```

## License

MIT
