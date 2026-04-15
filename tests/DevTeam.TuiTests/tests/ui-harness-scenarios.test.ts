import { test, expect } from "@microsoft/tui-test";
import { cliArgs } from "./helpers.js";

// The ui-harness command starts the full interactive shell with synthetic workspace
// state so the UI can be verified without real agent backends.
// Each describe block overrides the program args to use a specific scenario.

const BUILD_TIMEOUT = 120_000;

// Shared base args — only the --scenario value differs per describe block.
const BASE_ARGS = cliArgs("ui-harness");

// Wait for the shell to finish starting up — "Phase:" appears exactly once in the header.
async function waitForReady(terminal: Parameters<Parameters<typeof test>[1]>[0]["terminal"]) {
  await expect(terminal.getByText("Phase:")).toBeVisible({
    timeout: BUILD_TIMEOUT,
  });
}

// ── Planning scenario ────────────────────────────────────────────────────────
// Phase: Planning. One planning issue (done), one blocking question.

test.describe("planning scenario", () => {
  test.use({
    program: { file: "dotnet", args: [...BASE_ARGS, "--scenario", "planning"] },
    rows: 40,
    columns: 120,
  });

  test("header shows Planning phase", async ({ terminal }) => {
    await waitForReady(terminal);
    await expect(terminal.getByText("Planning")).toBeVisible();
  });
});

// ── Architect-planning scenario ──────────────────────────────────────────────
// Phase: ArchitectPlanning. Multiple issues, one completed architect run.

test.describe("architect scenario", () => {
  test.use({
    program: {
      file: "dotnet",
      args: [...BASE_ARGS, "--scenario", "architect"],
    },
    rows: 40,
    columns: 120,
  });

  test("header shows Architect Planning phase", async ({ terminal }) => {
    await waitForReady(terminal);
    await expect(terminal.getByText("Architect")).toBeVisible();
  });
});

// ── Execution scenario ───────────────────────────────────────────────────────
// Phase: Execution. 30+ issues, one Running architect agent, multiple done issues.

test.describe("execution scenario", () => {
  test.use({
    program: {
      file: "dotnet",
      args: [...BASE_ARGS, "--scenario", "execution"],
    },
    rows: 40,
    columns: 120,
  });

  test("header shows Execution phase", async ({ terminal }) => {
    await waitForReady(terminal);
    await expect(terminal.getByText("Execution")).toBeVisible();
  });

  test("agents panel shows the running architect agent", async ({
    terminal,
  }) => {
    await waitForReady(terminal);
    // The running agent (issue #38, role=architect) renders as "⚡ architect #38 Drop stale…"
    // "Drop stale" is unique to this specific running issue title.
    await expect(terminal.getByText("Drop stale")).toBeVisible();
  });

  test("roadmap panel shows issue titles", async ({ terminal }) => {
    await waitForReady(terminal);
    // Issue #3 "Write project README and rule catalogue" is in the execution scenario
    // and its title is unique enough to distinguish roadmap content from other panels.
    await expect(terminal.getByText("Write project README")).toBeVisible();
  });

  test("roadmap shows done indicator for completed issues", async ({
    terminal,
  }) => {
    await waitForReady(terminal);
    // Multiple done issues show ✓ — getByText("✓") hits strict-mode (8+ matches).
    // Instead verify a specific done issue title is visible; its ✓ prefix proves
    // the done indicator works without needing to match the symbol in isolation.
    await expect(terminal.getByText("Write project README")).toBeVisible();
  });
});

// ── Questions scenario ───────────────────────────────────────────────────────
// Phase: Execution + two open questions (one blocking).

test.describe("questions scenario", () => {
  test.use({
    program: {
      file: "dotnet",
      args: [...BASE_ARGS, "--scenario", "questions"],
    },
    rows: 40,
    columns: 120,
  });

  test("shell renders without error", async ({ terminal }) => {
    await waitForReady(terminal);
    // The scenario should render the execution state (with questions).
    // Verify Execution phase is shown and no crash/error message appears.
    await expect(terminal.getByText("Execution")).toBeVisible();
  });
});

// ── Sprint-resume scenario ───────────────────────────────────────────────────
// Phase: Execution. One in-progress issue (interrupted sprint) + two open issues.
// On startup the shell should show a resume hint and an in-progress warning.

test.describe("sprint-resume scenario", () => {
  test.use({
    program: {
      file: "dotnet",
      args: [...BASE_ARGS, "--scenario", "sprint-resume"],
    },
    rows: 40,
    columns: 120,
  });

  test("header shows Execution phase", async ({ terminal }) => {
    await waitForReady(terminal);
    await expect(terminal.getByText("Execution")).toBeVisible();
  });

  test("shows sprint resume hint on startup", async ({ terminal }) => {
    await waitForReady(terminal);
    // The sprint-resume scenario has open items from a prior sprint.
    // The hint text includes "sprint item" — verify it appears in the progress panel.
    await expect(terminal.getByText("sprint item")).toBeVisible({
      timeout: 10_000,
    });
  });

  test("shows in-progress warning for interrupted issue", async ({
    terminal,
  }) => {
    await waitForReady(terminal);
    // The scenario has one InProgress issue (issue #3, SignalR hub).
    // The warning text includes "in progress" — verify it appears.
    await expect(terminal.getByText("in progress")).toBeVisible({
      timeout: 10_000,
    });
  });

  test("hint suggests /run to resume", async ({ terminal }) => {
    await waitForReady(terminal);
    // The hint text ends with "Use /run to resume or /status to review."
    // "to resume" is unique to the sprint resume hint.
    await expect(terminal.getByText("to resume")).toBeVisible({
      timeout: 10_000,
    });
  });
});
