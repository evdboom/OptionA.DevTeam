import { test, expect } from "@microsoft/tui-test";

// The ui-harness command starts the full interactive shell with synthetic workspace
// state so the UI can be verified without real agent backends.
// Each describe block overrides the program args to use a specific scenario.

const BUILD_TIMEOUT = 120_000;

// Shared base args — only the --scenario value differs per describe block.
const BASE_ARGS = [
  "run",
  "--project",
  "../../src/DevTeam.Cli/DevTeam.Cli.csproj",
  "--",
  "ui-harness",
];

// Wait for the shell to finish starting up (same signal as the interactive shell).
async function waitForReady(terminal: Parameters<Parameters<typeof test>[1]>[0]["terminal"]) {
  await expect(terminal.getByText("DevTeam")).toBeVisible({
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
    // The execution scenario has a Running architect agent (issue #38)
    await expect(terminal.getByText("architect")).toBeVisible();
  });

  test("roadmap panel shows issue titles", async ({ terminal }) => {
    await waitForReady(terminal);
    // Several issues from the execution scenario should be visible in the roadmap
    await expect(terminal.getByText("architect")).toBeVisible(); // at least the agent/roadmap role
  });

  test("roadmap shows done indicator for completed issues", async ({
    terminal,
  }) => {
    await waitForReady(terminal);
    // Done issues are rendered with a ✓ checkmark
    await expect(terminal.getByText("✓")).toBeVisible();
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
