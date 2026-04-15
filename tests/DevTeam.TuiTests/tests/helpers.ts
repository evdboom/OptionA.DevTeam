// Pre-built CLI binary path (relative to tests/DevTeam.TuiTests/).
// Built once by 'npm pretest' (dotnet build) before the test suite runs.
// Using the DLL directly avoids dotnet run's MSBuild overhead, which causes
// timeouts when 4 workers all invoke MSBuild concurrently on Linux CI.
export const CLI_DLL =
  "../../src/DevTeam.Cli/bin/Debug/net10.0/DevTeam.Cli.dll";

/** Returns dotnet program args for the given CLI subcommand. */
export function cliArgs(...args: string[]): string[] {
  return [CLI_DLL, ...args];
}
