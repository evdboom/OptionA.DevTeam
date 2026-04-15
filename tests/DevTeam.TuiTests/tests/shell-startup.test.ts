import { test, expect } from "@microsoft/tui-test";
import { cliArgs } from "./helpers.js";

// Generous timeout for first run: dotnet needs to restore + compile the CLI project.
const BUILD_TIMEOUT = 120_000;

test.use({
  program: {
    file: "dotnet",
    args: cliArgs("start", "--workspace", ".devteam-e2e-startup"),
  },
  rows: 40,
  // 90 cols ensures the "· /help for commands …" hint starts near the end of
  // line 1 (Windows path = 78 chars → hint starts at col 81), causing it to
  // wrap mid-phrase at the same split the CI runner sees with its longer path.
  columns: 90,
});

test("banner shows help hint on startup", async ({ terminal }) => {
  await expect(terminal.getByText("help for commands")).toBeVisible({
    timeout: BUILD_TIMEOUT,
  });
});

test("header panel shows current phase on startup", async ({ terminal }) => {
  // "Phase:" is the content of the header panel body — reliably rendered in the
  // terminal buffer. The panel border title ("DevTeam") uses Spectre box-drawing
  // characters that tui-test does not expose via getByText.
  await expect(terminal.getByText("Phase:")).toBeVisible({
    timeout: BUILD_TIMEOUT,
  });
});

test("header shows Planning phase on fresh start", async ({ terminal }) => {
  await expect(terminal.getByText("help for commands")).toBeVisible({
    timeout: BUILD_TIMEOUT,
  });
  await expect(terminal.getByText("Planning")).toBeVisible();
});

test("banner shows exit hint on startup", async ({ terminal }) => {
  await expect(terminal.getByText("exit to quit")).toBeVisible({
    timeout: BUILD_TIMEOUT,
  });
});
