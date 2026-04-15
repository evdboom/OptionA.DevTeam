import { defineConfig } from "@microsoft/tui-test";

export default defineConfig({
  // Retry flaky tests once before reporting failure.
  retries: 1,
  // Capture traces so failures can be replayed with: npx @microsoft/tui-test show-trace
  trace: true,
  // Increase timeout for the help-scroll coverage test which scrolls through all commands.
  // Most tests finish in ~6 s; the scroll test needs up to ~30 s.
  timeout: 90_000,
});
