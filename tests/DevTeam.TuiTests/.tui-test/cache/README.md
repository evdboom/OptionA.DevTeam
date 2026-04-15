# DevTeam.TuiTests

End-to-end terminal UI tests for the DevTeam interactive shell, using [Microsoft's tui-test](https://github.com/microsoft/tui-test) framework.

## Prerequisites

- Node.js 18+ (LTS recommended)
- `dotnet` in PATH (the tests compile and run the CLI via `dotnet run`)

## Setup

From this directory (`tests/DevTeam.TuiTests/`):

```sh
npm install
```

## Running tests manually

```sh
npm test
```

The first run compiles the CLI project — allow ~30 s. Subsequent runs are fast because `dotnet` reuses the build cache.

## Viewing test output

### Traces (always recorded)

Every test run writes a trace to `tui-traces/`. Traces capture a full replay of everything the terminal received — useful for diagnosing flaky failures or seeing exactly what the UI looked like at any moment.

Replay a trace:

```sh
npx @microsoft/tui-test show-trace tui-traces/<file>.tuitrace
```

### Terminal snapshots

Tests can assert the full terminal state visually:

```ts
await expect(terminal).toMatchSnapshot();
```

Snapshots are saved in `tests/__snapshots__/` alongside the test file. On first run they are written; subsequent runs diff against them. This is how to add a screenshot-style assertion to any test.

## What is tested

| File | Tests | What it covers |
|------|-------|----------------|
| `tests/shell-startup.test.ts` | 4 | Shell starts, banner/header visible, Planning phase on fresh start |
| `tests/shell-commands.test.ts` | 6 | Unknown command error, `/history`, `/bug` output, `/exit`, scroll hints (PgUp/End) |
| `tests/help-scroll.test.ts` | 2 | `/help` scroll: Home reveals oldest commands, all commands reachable by paging |
| `tests/ui-harness-scenarios.test.ts` | 6 | Scenario-based panel rendering: phase labels, running-agent indicator, roadmap issue titles, done `✓` indicators |

## Workspace cleanup

Each test suite creates a temporary workspace directory (e.g. `.devteam-e2e-startup`) in the current working directory. These are listed in `.gitignore`. To clean them up manually:

```sh
Remove-Item -Recurse -Force .devteam-e2e-* 2>$null   # PowerShell
# or
rm -rf .devteam-e2e-*                                  # bash/zsh
```
