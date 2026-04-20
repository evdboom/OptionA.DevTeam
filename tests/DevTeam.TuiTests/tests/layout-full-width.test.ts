import { test, expect } from "@microsoft/tui-test";
import { cliArgs } from "./helpers.js";

const BUILD_TIMEOUT = 120_000;
const COLUMNS = 120;

test.use({
  program: {
    file: "dotnet",
    args: cliArgs("start", "--workspace", ".devteam-e2e-layout"),
  },
  rows: 40,
  columns: COLUMNS,
});

function rowToString(row: string[]): string {
  return row.join("");
}

function lastNonSpaceIndex(text: string): number {
  for (let i = text.length - 1; i >= 0; i--) {
    if (text[i] !== " ") {
      return i;
    }
  }

  return -1;
}

function assertRowEndsAtTerminalEdge(buffer: string[][], needle: string): void {
  const renderedRows = buffer.map(rowToString);
  const row = renderedRows.find((line) => line.includes(needle));

  if (!row) {
    throw new Error(`Could not find a rendered row containing '${needle}'.`);
  }

  const rightEdge = COLUMNS - 1;
  const lastChar = lastNonSpaceIndex(row);
  if (lastChar !== rightEdge) {
    throw new Error(
      `Row containing '${needle}' did not reach full width. Expected right edge at column ${rightEdge}, got ${lastChar}.\nRow='${row}'`
    );
  }
}

test("shell renders 3 full-width panels", async ({ terminal }) => {
  await expect(terminal.getByText("Phase:")).toBeVisible({ timeout: BUILD_TIMEOUT });
  await expect(terminal.getByText("Workspace:")).toBeVisible();
  await expect(terminal.getByText("help for commands")).toBeVisible();

  const buffer = terminal.getViewableBuffer();

  // Header panel body line
  assertRowEndsAtTerminalEdge(buffer, "Phase:");
  // Progress panel body line
  assertRowEndsAtTerminalEdge(buffer, "Workspace:");
  // Input panel line
  assertRowEndsAtTerminalEdge(buffer, "> ");

  // Visual regression snapshot: catches panel frame shape regressions.
  await expect(terminal).toMatchSnapshot();
});
