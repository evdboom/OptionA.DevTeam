import { test, expect, Key } from "@microsoft/tui-test";
import { cliArgs } from "./helpers.js";

// All /help commands that must be reachable via scrolling.
const HELP_COMMANDS = [
  "/init",
  "/customize",
  "/start-here",
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

const BASE_ARGS = cliArgs("start", "--workspace", ".devteam-tui-test");

// ── Test 1: Home key reaches oldest content (40×120) ────────────────────────
// Uses a realistic terminal size where long help lines wrap to 2-3 rows each.
// This is the scenario that exposed the MaxScrollOffset bug — without the fix,
// the Home key caps too early and /init is never reachable.
test.describe("help scroll — MaxScrollOffset fix (40×120)", () => {
  test.use({
    program: { file: "dotnet", args: BASE_ARGS },
    rows: 40,
    columns: 120,
  });

  test("all /help commands are visible after scrolling", async ({
    terminal,
  }) => {
    await expect(terminal.getByText("help for commands")).toBeVisible({
      timeout: 120_000,
    });

    terminal.submit("/help");
    await expect(terminal.getByText("@role")).toBeVisible();

    // Jump to oldest content — the key assertion: without the MaxScrollOffset fix
    // the Home key caps too early and /init never appears.
    terminal.keyPress(Key.Home);

    for (const cmd of HELP_COMMANDS.slice(0, 6)) {
      // Use strict:false — some commands (e.g. /init) also appear in the
      // "No workspace found. Use /init..." startup message, causing a strict-mode
      // violation if we require exactly one match.
      await expect(terminal.getByText(cmd, { strict: false })).toBeVisible();
    }

    terminal.keyPress(Key.End);
    await expect(terminal.getByText("@role")).toBeVisible();
  });
});

// ── Test 2: Full coverage via PgUp (30×120) ──────────────────────────────────
// At 40 rows: PageStep(25) > MaxScrollOffset(19) — one PgUp jumps directly to
// the top, leaving lines at the chunk boundary (like /update, /max-iterations)
// in an unreachable gap. At 30 rows: PageStep(15) < MaxScrollOffset(26), so
// three positions (bottom → middle → top) cover the entire help content.
test.describe("help scroll — full command coverage (30×120)", () => {
  test.use({
    program: { file: "dotnet", args: BASE_ARGS },
    rows: 30,
    columns: 120,
  });

  test("scrolling through /help reveals all commands", async ({ terminal }) => {
    await expect(terminal.getByText("help for commands")).toBeVisible({
      timeout: 120_000,
    });

    terminal.submit("/help");
    await expect(terminal.getByText("@role")).toBeVisible();
    // Allow an extra render cycle after the /help response before scrolling.
    await new Promise((r) => setTimeout(r, 300));

    const visible = new Set<string>();

    // Check all commands at the current scroll position.
    // Uses a 500 ms timeout so tui-test has time to poll the rendered rows.
    // Already-found commands are skipped so later iterations are fast.
    const collectVisible = async () => {
      for (const cmd of HELP_COMMANDS) {
        if (visible.has(cmd)) continue;
        try {
          // Use strict:false — /init also appears in the startup message
          // "No workspace found. Use /init…" so it has 2 matches when visible.
          await expect(terminal.getByText(cmd, { strict: false })).toBeVisible({
            timeout: 500,
          });
          visible.add(cmd);
        } catch {
          // not visible at this scroll position — will try again after next PgUp
        }
      }
    };

    // Position 1: bottom (scrollOffset=0) — shows latest content.
    await collectVisible();

    // Scroll up in steps. PageStep = ContentRowCount/2 ≈ 7-12 depending on terminal
    // height. 6 presses are enough to reach MaxScrollOffset from any starting offset.
    for (let i = 0; i < 6; i++) {
      terminal.keyPress(Key.PageUp);
      // Wait for the TUI to render the new scroll position (RefreshMs = 100 ms).
      await new Promise((r) => setTimeout(r, 300));
      await collectVisible();
    }

    // Use Home key as a safety net — guarantees we're at MaxScrollOffset
    // and the very oldest content (/init and first commands) is in view.
    terminal.keyPress(Key.Home);
    await new Promise((r) => setTimeout(r, 300));
    await collectVisible();

    const missing = HELP_COMMANDS.filter((c) => !visible.has(c));
    if (missing.length > 0) {
      const buf = terminal
        .getViewableBuffer()
        .map((row) => row.join("").trimEnd())
        .join("\n");
      throw new Error(
        `The following /help commands were never visible while scrolling: ${missing.join(", ")}\n\nLast terminal frame:\n---\n${buf}\n---`
      );
    }
  });
});

