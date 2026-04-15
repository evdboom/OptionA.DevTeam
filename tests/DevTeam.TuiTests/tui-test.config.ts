import { defineConfig } from "@microsoft/tui-test";

export default defineConfig({
  // Retry flaky tests once before reporting failure.
  retries: 1,
  // Capture traces so failures can be replayed with: npx @microsoft/tui-test show-trace
  trace: true,
});
