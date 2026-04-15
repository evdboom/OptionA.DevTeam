# DevTeam.TuiTests

End-to-end terminal UI tests for the DevTeam interactive shell, using [Microsoft's tui-test](https://github.com/microsoft/tui-test) framework.

## Prerequisites

- Node.js 18+
- `dotnet` in PATH (the tests compile and run the CLI via `dotnet run`)

## Setup

```sh
npm install
```

## Running tests

From this directory:

```sh
npm test
```

With traces (for debugging failures):

```sh
npm run test:trace
```

Replay a trace:

```sh
npx @microsoft/tui-test show-trace tui-traces/<file>.tuitrace
```

## What is tested

| File | What it covers |
|------|----------------|
| `tests/shell-startup.test.ts` | Shell starts, banner/header visible, Planning phase on fresh start |
| `tests/shell-commands.test.ts` | Unknown command error, `/history`, `/bug` output, `/exit`, scroll hints (PgUp/End) |
| `tests/help-scroll.test.ts` | `/help` scroll: Home reveals oldest commands, all commands reachable by paging |
| `tests/ui-harness-scenarios.test.ts` | Scenario-based panel rendering: phase labels, running-agent indicator, roadmap issue titles, done `✓` indicators |

> **Note**: the first test run compiles the CLI project (`dotnet run`). This takes ~30 s. Subsequent runs are fast.
> Traces are written to `tui-traces/` on failure and can be replayed with `npx @microsoft/tui-test show-trace`.
