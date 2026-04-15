using DevTeam.Cli;
using DevTeam.Core;
using DevTeam.TestInfrastructure;

namespace DevTeam.SmokeTests;

internal sealed class TestHarness : IDisposable
{
    public TestHarness()
    {
        TempRoot = Path.Combine(Path.GetTempPath(), "devteam-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(TempRoot);
        RepoRoot = FindRepoRootForTests();
        Store = new WorkspaceStore(Path.Combine(TempRoot, ".devteam"));
        State = Store.Initialize(RepoRoot, 25, 6);
        Runtime = new DevTeamRuntime();
    }

    public string TempRoot { get; }
    public string RepoRoot { get; }
    public WorkspaceStore Store { get; }
    public WorkspaceState State { get; }
    public DevTeamRuntime Runtime { get; }

    public void Dispose()
    {
        if (Directory.Exists(TempRoot))
        {
            TestFileSystem.DeleteDirectoryWithRetries(TempRoot);
        }
    }

    internal static string FindRepoRootForTests()
    {
        var directory = AppContext.BaseDirectory;
        var current = new DirectoryInfo(directory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".devteam-source")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new InvalidOperationException("Could not locate repository root.");
    }
}

internal sealed class CliInvocationResult
{
    public int ExitCode { get; init; }
    public string StdOut { get; init; } = "";
    public string StdErr { get; init; } = "";
}
