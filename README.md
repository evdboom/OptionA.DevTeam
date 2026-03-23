# OptionA.DevTeam

`OptionA.DevTeam` is a .NET global tool that runs a plan-first autonomous dev team from your terminal using Github Copilot.

It helps you turn a goal into a reviewed plan, then executes the work through specialized roles such as planner, orchestrator, architect, developer, and tester. The CLI keeps a local workspace, tracks questions and decisions, and iterates through execution from the command line.

## What it does

With `OptionA.DevTeam`, you can:

- initialize a workspace for a new or existing repository
- generate an initial plan and revise it with feedback
- approve the plan before execution starts
- run an execution loop with multiple agent roles
- track questions, issues, decisions, and runs in a local workspace
- switch between modes such as `develop` and `creative-writing`

The default experience is built around software delivery. In `develop` mode, the runtime pushes agents toward building working software, validating it, and documenting what changed.

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

If you want to install a specific version:

```powershell
dotnet tool install --global OptionA.DevTeam --version 0.1.18
```

For local testing from a package folder:

```powershell
dotnet tool install --global --add-source .\nupkg OptionA.DevTeam --version 0.1.18
```

## Requirements

- .NET SDK 10
- GitHub Copilot CLI installed and authenticated

## Quick start

Create or open a repository, then initialize DevTeam:

```powershell
devteam /init --workspace .devteam --goal "Build a Flappy Bird game"
```

For longer goals or markdown-based project briefs, load the goal from a file:

```powershell
devteam /init --workspace .devteam --goal-file .\goal.md
```

This creates a local workspace and, if needed, initializes a git repository first.

Start the interactive shell:

```powershell
devteam /start --workspace .devteam
```

If you want DevTeam to keep Windows awake during long shell or loop sessions,
enable it explicitly:

```powershell
devteam /set-keep-awake true --workspace .devteam
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

## Typical CLI flow

Initialize a workspace:

```powershell
devteam /init --workspace .devteam --goal "Build an autonomous dev team runtime" --mode develop
```

Open the shell:

```powershell
devteam /start --workspace .devteam
```

Common shell commands:

```text
/status
/plan
/questions
/budget
/keep-awake on
/check-update
/update
/feedback Narrow the first milestone.
/approve Start building.
/run --max-iterations 5 --max-subagents 3
/exit
```

If no plan exists yet, `/plan` runs the planner for you, writes `.devteam\plan.md`, shows it in the shell, and waits for feedback or approval.

## Non-interactive usage

Initialize a workspace:

```powershell
devteam /init --workspace .devteam --goal "Build a CLI Tetris clone"
```

You can also update the active goal from a markdown file later:

```powershell
devteam /set-goal --workspace .devteam --goal-file .\goal.md
```

Generate and inspect a plan:

```powershell
devteam /plan --workspace .devteam
```

Approve the plan:

```powershell
devteam /approve-plan --workspace .devteam --note "Looks good. Start building."
```

Run the loop:

```powershell
devteam /run --workspace .devteam --max-iterations 5 --max-subagents 3
```

One-off keep-awake override for a long run:

```powershell
devteam /run --workspace .devteam --max-iterations 5 --max-subagents 3 --keep-awake true
```

Check status:

```powershell
devteam /status --workspace .devteam
```

List open questions:

```powershell
devteam /questions --workspace .devteam
```

Answer a question:

```powershell
devteam /answer-question 1 "Use keyboard controls only for the first version."
```

## Modes

The runtime supports mode-specific guardrails.

Switch modes with:

```powershell
devteam /set-mode creative-writing --workspace .devteam
```

Current packaged modes:

- `develop`
- `creative-writing`

`develop` is the default mode for software delivery.

## Customizing roles, modes, and superpowers

DevTeam ships with built-in roles, modes, superpowers, and model policies. To
customize them for your project, copy the defaults into your repo:

```powershell
devteam /customize
```

This creates a `.devteam-source/` directory in the current folder containing
all packaged assets. Edit the markdown files to adjust behaviour:

- `roles/` — agent personas (architect, developer, tester, …)
- `modes/` — mode-specific guardrails (develop, creative-writing, …)
- `superpowers/` — reusable skill prompts (tdd, review, debug, …)
- `MODELS.json` — model selection policies per role

Project-level assets always override the packaged defaults. To reset a file,
delete it and it will fall back to the built-in version.

Use `--force` to overwrite existing files with the latest packaged versions.

## How DevTeam works

At a high level:

1. `planner` creates the initial plan.
2. You review it and approve it.
3. `orchestrator` chooses the next execution batch.
4. Worker roles execute the selected work.
5. The loop reevaluates the workspace and repeats.

The runtime stores its local state under the workspace directory, typically `.devteam`.

## Workspace files

The CLI writes its local runtime state to the workspace, including:

- `workspace.json`
- `state\issues.json`
- `state\runs.json`
- `state\decisions.json`
- `questions.md`
- `plan.md`
- `issues\_index.md`

You do not need to edit these files manually to use the tool, but they are there for visibility.

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

## License

MIT
