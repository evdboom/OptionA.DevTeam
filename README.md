# DevTeam runtime

This repository now contains two things:

- the legacy `ralph.ps1` PowerShell loop
- a new C#-based `DevTeam` runtime intended to replace Ralph over time

The new runtime is built for a multi-stage autonomous "dev team" workflow with role-driven execution, issue decomposition, prompt assets in markdown, model budgeting, and GitHub Copilot SDK integration.

## Current state

The new code lives in:

- `DevTeam.slnx`
- `src\DevTeam.Core`
- `src\DevTeam.Cli`
- `tests\DevTeam.SmokeTests`

The runtime already supports:

- project goal storage
- roadmap items
- issues with dependencies
- blocking and non-blocking questions
- role-to-model mapping
- premium credit cap enforcement
- markdown-based roles and superpowers
- GitHub Copilot SDK as the default agent backend
- Copilot CLI as a fallback backend

## Prompt assets

The new runtime uses `.devteam-source\` as the primary home for prompt assets:

- `.devteam-source\roles\`
- `.devteam-source\superpowers\`
- `.devteam-source\MODELS.json`

If an asset is missing there, the runtime falls back to `.ralph-source\`.

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

The legacy Ralph test harness still exists:

```powershell
pwsh -NoProfile -File .\.ralph-source\scripts\test-parallel.ps1
```

## Basic usage

Initialize a workspace and set the project goal:

```powershell
dotnet run --project .\src\DevTeam.Cli\DevTeam.Cli.csproj -- init --workspace .devteam --goal "Build an autonomous dev team runtime"
```

Run one loop step:

```powershell
dotnet run --project .\src\DevTeam.Cli\DevTeam.Cli.csproj -- run-once --workspace .devteam
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
dotnet run --project .\src\DevTeam.Cli\DevTeam.Cli.csproj -- add-issue "Build Roslyn analysis tool" --role architect
```

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
- `run-once` bootstraps the first roadmap item and starter issues from the active goal if needed
- ready issues are selected by dependency order and queued for role-specific execution
- if only blocking questions remain, the loop returns a waiting state instead of inventing work
- if non-blocking questions exist, the loop can still continue with ready work

## Recommended next steps

The current scaffold is ready for the next layer of implementation:

- connect queued runs to real role execution through the SDK session flow
- map role and superpower metadata into session configuration and tool availability
- add first-class tool registration for local C# tools and MCP servers
- add durable iteration history and richer structured handoffs
- build Roslyn-based analysis tools or an MCP server for C#/.NET repos

## Legacy Ralph

`ralph.ps1` and `.ralph-source\` remain in the repository as the reference implementation and prompt source from which the new runtime is evolving. The intended direction is for `DevTeam` to become the primary runtime.
