import { test, expect, Key } from "@microsoft/tui-test";

// All /help commands that must be reachable via scrolling.
const HELP_COMMANDS = [
  "/init",
  "/customize",
  "/bug",
  "/status",
  "/history",
  "/mode",
  "/keep-awake",
  "/add-issue",
  "/plan",
  "/questions",
  "/budget",
  "/check-update",
  "/update",
  "/max-iterations",
  "/max-subagents",
  "/run",
  "/stop",
  "/wait",
  "/feedback",
  "/approve",
  "/answer",
  "/goal",
  "/exit",
  "@role",
];

// Use a realistic terminal size: 120 cols × 40 rows.
// The Progress panel is 120 - 60 - 4 = 56 chars wide, which causes long help lines to
// wrap to 2-3 rows each — exactly the scenario that exposed the MaxScrollOffset bug.
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
      ".devteam-tui-test",
    ],
  },
  rows: 40,
  columns: 120,
});

test("all /help commands are visible after scrolling", async ({ terminal }) => {
  // The shell compiles and starts; wait for the banner hint that signals it is ready.
  // Generous timeout — first run needs dotnet to restore and build.
  await expect(terminal.getByText("help for commands")).toBeVisible({
    timeout: 120_000,
  });

  // Submit /help.
  terminal.submit("/help");

  // The newest part of the help (bottom) should be immediately visible.
  await expect(terminal.getByText("@role")).toBeVisible();

  // Jump to the oldest content — this is the key assertion that verifies the
  // MaxScrollOffset fix: without it, the Home key caps too early and /init never appears.
  terminal.keyPress(Key.Home);

  // Every command from the top of the help must now be on-screen.
  for (const cmd of HELP_COMMANDS.slice(0, 6)) {
    await expect(terminal.getByText(cmd)).toBeVisible();
  }

  // Return to follow-latest mode and verify bottom commands are visible again.
  terminal.keyPress(Key.End);
  await expect(terminal.getByText("@role")).toBeVisible();
});

test("scrolling through /help reveals all commands", async ({ terminal }) => {
  await expect(terminal.getByText("help for commands")).toBeVisible({
    timeout: 120_000,
  });

  terminal.submit("/help");
  await expect(terminal.getByText("@role")).toBeVisible();

  // Verify every command is visible somewhere in the scroll range.
  // Strategy: jump to top, then page down collecting visible commands.
  terminal.keyPress(Key.Home);

  const visible = new Set<string>();

  // Collect what is on screen at the top.
  for (const cmd of HELP_COMMANDS) {
    try {
      await expect(terminal.getByText(cmd)).toBeVisible({ timeout: 500 });
      visible.add(cmd);
    } catch {
      // not visible at this scroll position — will check after paging down
    }
  }

  // Page down until we reach the bottom (End key), collecting as we go.
  for (let pages = 0; pages < 5; pages++) {
    terminal.keyPress(Key.PageDown);
    for (const cmd of HELP_COMMANDS) {
      if (visible.has(cmd)) continue;
      try {
        await expect(terminal.getByText(cmd)).toBeVisible({ timeout: 500 });
        visible.add(cmd);
      } catch {
        // not visible yet
      }
    }
    if (visible.size === HELP_COMMANDS.length) break;
  }

  const missing = HELP_COMMANDS.filter((c) => !visible.has(c));
  if (missing.length > 0) {
    throw new Error(
      `The following /help commands were never visible while scrolling: ${missing.join(", ")}`
    );
  }
});
