import { defineConfig } from "@microsoft/tui-test";

export default defineConfig({
  // Retry flaky tests once before reporting failure.
  retries: 1,
  // Capture traces so failures can be replayed with: npx @microsoft/tui-test show-trace
  trace: true,
  // Increase timeout for the help-scroll coverage test which scrolls through all commands.
  // Most tests finish in ~6 s; the scroll test needs up to ~40 s.
  // Tests use --no-build so the project must be pre-built (npm pretest runs dotnet build).
  timeout: 90_000,
  // Cap workers so parallel dotnet processes don't overwhelm CI runners.
  // dotnet run --no-build starts fast; 4 concurrent processes is a safe limit.
  workers: 4,
});
