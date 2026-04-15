import { test, expect } from "@microsoft/tui-test";

// Generous timeout for first run: dotnet needs to restore + compile the CLI project.
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
      ".devteam-e2e-startup",
    ],
  },
  rows: 40,
  columns: 120,
});

test("banner shows help hint on startup", async ({ terminal }) => {
  await expect(terminal.getByText("help for commands")).toBeVisible({
    timeout: BUILD_TIMEOUT,
  });
});

test("header shows DevTeam title on startup", async ({ terminal }) => {
  await expect(terminal.getByText("DevTeam")).toBeVisible({
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
