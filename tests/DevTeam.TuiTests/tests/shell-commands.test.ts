import { test, expect, Key } from "@microsoft/tui-test";

const BUILD_TIMEOUT = 120_000;

test.use({
  program: {
    file: "dotnet",
    args: [
      "run",
      "--project",
      "../../src/DevTeam.Cli/DevTeam.Cli.csproj",
      "--",
      "start",
      "--workspace",
      ".devteam-e2e-commands",
    ],
  },
  rows: 40,
  columns: 120,
});

test("unknown command shows error with /help hint", async ({ terminal }) => {
  await expect(terminal.getByText("help for commands")).toBeVisible({
    timeout: BUILD_TIMEOUT,
  });

  terminal.submit("/xyzzy-not-a-real-command");

  // "Unknown command" appears in the error line. Allow time for the async
  // command processor (Task.Run in SpectreShellHost) to complete and re-render.
  await expect(terminal.getByText("Unknown command")).toBeVisible({
    timeout: 10_000,
  });
  // "Type /help." is unique to the error message (unlike "/help" which also
  // appears in the startup banner "· /help for commands ·").
  await expect(terminal.getByText("Type /help.")).toBeVisible({
    timeout: 5_000,
  });
});

test("history shows previously submitted commands", async ({ terminal }) => {
  await expect(terminal.getByText("help for commands")).toBeVisible({
    timeout: BUILD_TIMEOUT,
  });

  // Submit a couple of commands that will land in history
  terminal.submit("/help");
  await expect(terminal.getByText("@role")).toBeVisible();

  terminal.submit("/history");

  // /help must appear in the history output
  await expect(terminal.getByText("/help")).toBeVisible();
});

test("bug command produces a report in the progress panel", async ({
  terminal,
}) => {
  await expect(terminal.getByText("help for commands")).toBeVisible({
    timeout: BUILD_TIMEOUT,
  });

  terminal.submit("/bug");

  // The bug report is added as a panel titled "bug report".
  // Its separator line "── bug report ──" is rendered in the progress panel.
  // The report content is long and ## Environment is near the top (scrolled off at
  // follow-latest); instead verify the last section header which stays in the viewport.
  await expect(terminal.getByText("Recent agent runs")).toBeVisible({
    timeout: 15_000,
  });
});

test("exit command terminates the shell", async ({ terminal }) => {
  await expect(terminal.getByText("help for commands")).toBeVisible({
    timeout: BUILD_TIMEOUT,
  });

  terminal.submit("/exit");

  // After /exit the underlying process should terminate within a few seconds
  await new Promise<void>((resolve, reject) => {
    const timeout = setTimeout(
      () => reject(new Error("Shell did not exit within 10 seconds")),
      10_000
    );
    terminal.onExit(() => {
      clearTimeout(timeout);
      resolve();
    });
    // Also resolve immediately if already exited
    if (terminal.exitResult !== null) {
      clearTimeout(timeout);
      resolve();
    }
  });
});

test("PageUp shows scroll hint when history is long", async ({ terminal }) => {
  await expect(terminal.getByText("help for commands")).toBeVisible({
    timeout: BUILD_TIMEOUT,
  });

  // Generate enough output that the progress panel overflows
  terminal.submit("/help");
  await expect(terminal.getByText("@role")).toBeVisible();

  terminal.keyPress(Key.PageUp);

  // After scrolling up the panel header should show the scrolled indicator
  await expect(terminal.getByText("scrolled")).toBeVisible();
});

test("End key returns to follow-latest mode after scrolling", async ({
  terminal,
}) => {
  await expect(terminal.getByText("help for commands")).toBeVisible({
    timeout: BUILD_TIMEOUT,
  });

  terminal.submit("/help");
  await expect(terminal.getByText("@role")).toBeVisible();

  terminal.keyPress(Key.PageUp);
  await expect(terminal.getByText("scrolled")).toBeVisible();

  terminal.keyPress(Key.End);
  // Progress header goes back to non-scrolled title
  await expect(terminal.getByText("Progress")).toBeVisible();
  // The scrolled indicator must be gone
  await expect(terminal.getByText("scrolled")).not.toBeVisible();
});

// ── /worktrees command (requires a workspace — use ui-harness) ──────────────
// The /worktrees command needs an existing workspace state to read/write.
// Run these tests using the ui-harness execution scenario to provide state.

test.describe("worktrees command (ui-harness)", () => {
  test.use({
    program: {
      file: "dotnet",
      args: [
        "run",
        "--project",
        "../../src/DevTeam.Cli/DevTeam.Cli.csproj",
        "--",
        "ui-harness",
        "--scenario",
        "execution",
      ],
    },
    rows: 40,
    columns: 120,
  });

  test("worktrees on shows enabled confirmation", async ({ terminal }) => {
    // Wait for Phase: header which signals the harness is ready
    await expect(terminal.getByText("Phase:")).toBeVisible({
      timeout: BUILD_TIMEOUT,
    });

    terminal.submit("/worktrees on");

    await expect(terminal.getByText("Worktree mode enabled")).toBeVisible({
      timeout: 10_000,
    });
  });

  test("worktrees off shows disabled confirmation", async ({ terminal }) => {
    await expect(terminal.getByText("Phase:")).toBeVisible({
      timeout: BUILD_TIMEOUT,
    });

    terminal.submit("/worktrees off");

    await expect(terminal.getByText("Worktree mode disabled")).toBeVisible({
      timeout: 10_000,
    });
  });
});
