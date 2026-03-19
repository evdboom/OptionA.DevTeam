# DevTeam runtime

This repository contains the C#-based `DevTeam` runtime.

It is built for a multi-stage autonomous "dev team" workflow with role-driven execution, issue decomposition, prompt assets in markdown, model budgeting, and GitHub Copilot SDK integration.

## Current state

The new code lives in:

- `DevTeam.slnx`
- `src\DevTeam.Core`
- `src\DevTeam.Cli`
- `tests\DevTeam.SmokeTests`

The runtime already supports:

- project goal storage
- a required planning phase before execution
- roadmap items
- issues with dependencies
- blocking and non-blocking questions
- persisted decision memory and run/session tracking
- role-to-model mapping
- premium credit cap enforcement
- markdown-based roles and superpowers
- GitHub Copilot SDK as the default agent backend
- Copilot CLI as a fallback backend
- an executable loop command with configurable verbosity
- concurrent subagent dispatch up to `--max-subagents`
- structured issue ingestion from agent output so the loop can keep building the backlog
- interactive planning revisions from the `/start` shell
- budget visibility and cap updates from the CLI
- generated markdown issue board and per-issue files for human-readable tracking

## Prompt assets

The new runtime uses `.devteam-source\` as the primary home for prompt assets:

- `.devteam-source\roles\`
- `.devteam-source\superpowers\`
- `.devteam-source\MODELS.json`

Roles and superpowers are still markdown files. They can optionally declare tool requirements with frontmatter:

```md
---
tools: rg, git, dotnet
---
# Superpower: Toolsmith

Use these tools when the skill is active.
```

The runtime strips the frontmatter before sending markdown content to agents and keeps the declared tool list as metadata.

## Requirements

- .NET SDK 10
- GitHub Copilot CLI installed and authenticated

## Build and test

Build the solution:

```powershell
dotnet build .\DevTeam.slnx
```

Run the smoke tests:

```powershell
dotnet run --project .\tests\DevTeam.SmokeTests\DevTeam.SmokeTests.csproj
```

Pack the CLI as a .NET tool:

```powershell
dotnet pack .\src\DevTeam.Cli\DevTeam.Cli.csproj -c Release -o .\nupkg
```

Install it from the local package output:

```powershell
dotnet tool install --global --add-source .\nupkg DevTeam.Cli --version 0.1.10
```

Update an existing install:

```powershell
dotnet tool update --global --add-source .\nupkg DevTeam.Cli --version 0.1.10
```

## Basic usage

Initialize a workspace and set the project goal:

```powershell
dotnet run --project .\src\DevTeam.Cli\DevTeam.Cli.csproj -- init --workspace .devteam --goal "Build an autonomous dev team runtime"
```

After installing the tool, the same command becomes:

```powershell
devteam /init --workspace .devteam --goal "Build an autonomous dev team runtime"
```

Start the interactive shell:

```powershell
devteam /start --workspace .devteam
```

Inside the shell, use slash commands:

```text
/status
/run --max-iterations 1
/plan
/questions
/budget
/answer 1 Use pixel art.
/feedback Make the first milestone only the playable scaffold.
/approve Start building.
/exit
```

Run one queueing step:

```powershell
dotnet run --project .\src\DevTeam.Cli\DevTeam.Cli.csproj -- run-once --workspace .devteam
```

Approve the current plan and allow execution work:

```powershell
dotnet run --project .\src\DevTeam.Cli\DevTeam.Cli.csproj -- approve-plan --workspace .devteam --note "The initial plan is good. Start building."
```

Run the loop end-to-end with the default verbosity (`normal`):

```powershell
dotnet run --project .\src\DevTeam.Cli\DevTeam.Cli.csproj -- run-loop --workspace .devteam --max-iterations 4 --verbosity normal
```

Use more detail while debugging:

```powershell
dotnet run --project .\src\DevTeam.Cli\DevTeam.Cli.csproj -- run-loop --workspace .devteam --max-iterations 4 --verbosity detailed
```

Show current state:

```powershell
dotnet run --project .\src\DevTeam.Cli\DevTeam.Cli.csproj -- status --workspace .devteam
```

Add a roadmap item manually:

```powershell
dotnet run --project .\src\DevTeam.Cli\DevTeam.Cli.csproj -- add-roadmap "Implement agent execution"
```

Add an issue manually:

```powershell
dotnet run --project .\src\DevTeam.Cli\DevTeam.Cli.csproj -- add-issue "Build Roslyn analysis tool" --role architect --area tooling
```

`add-issue` validates the role against the workspace role catalog. Friendly names like `Front-end developer` are normalized to canonical slugs like `frontend-developer`. If the role is unknown, or if you pass an alias like `engineer`, the CLI prints the canonical valid roles and known aliases so the caller can correct the request. You can also set `--area` to mark likely file/subsystem overlap so parallel scheduling can avoid collisions.

Add a blocking question:

```powershell
dotnet run --project .\src\DevTeam.Cli\DevTeam.Cli.csproj -- add-question "Should Roslyn tooling run in-process or over MCP?" --blocking
```

Answer a question:

```powershell
dotnet run --project .\src\DevTeam.Cli\DevTeam.Cli.csproj -- answer-question 1 "Use MCP so tools are reusable outside the loop."
```

Complete a queued run:

```powershell
dotnet run --project .\src\DevTeam.Cli\DevTeam.Cli.csproj -- complete-run --run-id 1 --outcome completed --summary "Planning phase finished."
```

## Agent invocation

Use the default GitHub Copilot SDK backend:

```powershell
dotnet run --project .\src\DevTeam.Cli\DevTeam.Cli.csproj -- agent-invoke --prompt "Reply with READY and nothing else."
```

Force the CLI fallback backend:

```powershell
dotnet run --project .\src\DevTeam.Cli\DevTeam.Cli.csproj -- agent-invoke --backend cli --prompt "Reply with READY and nothing else."
```

## How the loop behaves today

- `init` creates a persistent workspace file at `.devteam\workspace.json`
- `.devteam\workspace.json` is the canonical state store
- `.devteam\issues\_index.md` and `.devteam\issues\0001-*.md` are generated readable mirrors of that state
- every new goal starts in the `Planning` phase
- `run-once` bootstraps the first roadmap item and starter issues from the active goal if needed
- while the workspace is in `Planning`, only planning issues are eligible to run
- after the planning issue is complete, the loop stops at `awaiting-plan-approval` until the user runs `approve-plan`
- after approval, the workspace moves to `Execution` and delivery issues can run
- `run-loop` dispatches up to `--max-subagents` ready issues concurrently, invokes the configured backend, and records run artifacts under `.devteam\runs\`
- conflict prevention is area-based: issues with the same `area` do not get scheduled in parallel, while disjoint areas can run together
- planning and architecture runs can emit structured `ISSUES:` entries, which the runtime adds to the backlog automatically
- the CLI `add-issue` command is the strict manual intake path: it validates canonical roles, accepts optional `--area`, and gives correction hints for aliases or invalid role names
- agent session ids are persisted on runs, and durable decision records are written under `.devteam\decisions\`
- run artifacts now separate the parsed summary from raw output and also record `SUPERPOWERS_USED` and `TOOLS_USED` when the agent reports them
- older workspaces are hydrated on load if role/superpower/model metadata is missing, so legacy `workspace.json` files recover their prompt asset metadata automatically
- when a target repo does not have its own `.devteam-source`, the CLI falls back to the prompt assets bundled with the tool
- questions are the user-input inbox: blocking questions can halt the loop, while non-blocking questions still allow other ready work to continue
- open questions are written to `.devteam\questions.md` so the shell and batch CLI both have a file-backed inbox
- `normal` verbosity now emits a heartbeat during long-running agent turns
- `/run` prints current budget usage after the loop finishes

## Issue tracking model

The open workload is the issue board.

- each issue has a role, status, priority, and dependency list
- each issue can also carry an optional `area` used for conflict prevention during parallel scheduling
- the orchestrator should create and refine issues, not route work through `NEXT_ROLE`
- the runtime chooses ready issues directly from the board based on status and dependencies
- the latest run summary for an issue is mirrored into the generated issue markdown file

That means the new runtime does **not** depend on the old Ralph-style `NEXT_ROLE` handoff chain. The handoff is effectively stored on the issue itself and in the latest run/decision artifacts.

## How user input works

User input is file-backed and explicit rather than hidden in an agent session.

- the project goal enters through `init --goal` or `set-goal`
- agents or humans can add open questions with `add-question`
- the user answers them with `answer-question`
- while a plan is awaiting approval, freeform text in `/start` is treated as planning feedback and triggers a planning revision
- the user explicitly moves the system from planning into execution with `approve-plan`
- the interactive shell at `devteam /start` exposes the same flow over stdin/stdout with slash commands

## Budget controls

Inspect current usage and caps:

```powershell
devteam /budget --workspace .devteam
```

Update caps:

```powershell
devteam /budget --workspace .devteam --total 25 --premium 6
```

That means Copilot session history is useful for one run, but the durable project memory lives in `.devteam\workspace.json` plus the artifacts under `.devteam\decisions\` and `.devteam\runs\`.

## Recommended next steps

The current scaffold is ready for the next layer of implementation:

- enrich role prompts so orchestrator and architect runs create or refine issues automatically
- map role and superpower metadata into session configuration and tool availability
- add first-class tool registration for local C# tools and MCP servers
- add durable iteration history and richer structured handoffs
- build Roslyn-based analysis tools or an MCP server for C#/.NET repos
