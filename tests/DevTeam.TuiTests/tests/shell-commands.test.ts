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

  await expect(terminal.getByText("Unknown command")).toBeVisible();
  // The error line should point users back to /help
  await expect(terminal.getByText("/help")).toBeVisible();
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

  // Bug report always contains a "Version" section header
  await expect(terminal.getByText("Version")).toBeVisible();
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
