using DevTeam.Core;

var tests = new List<(string Name, Action Run)>
{
    ("RunOnce bootstraps and queues orchestrator work", TestRunOnceBootstrapsAndQueues),
    ("Completed run unlocks dependent architect work", TestCompleteRunUnlocksDependency),
    ("Blocking questions only halt when no ready work exists", TestBlockingQuestionWaitState),
    ("Premium cap forces fallback model", TestPremiumCapFallback),
    ("CLI agent client builds a copilot invocation", TestCliAgentClientInvocationShape),
    ("SDK agent client is the default integration backend", TestSdkAgentClientFactory),
    ("DevTeam assets load from .devteam-source first", TestPromptAssetsPreferDevTeamSource)
};

var failures = new List<string>();
foreach (var (name, run) in tests)
{
    try
    {
        run();
        Console.WriteLine($"PASS: {name}");
    }
    catch (Exception ex)
    {
        failures.Add($"{name}: {ex.Message}");
        Console.WriteLine($"FAIL: {name}");
    }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine("Smoke tests failed:");
    foreach (var failure in failures)
    {
        Console.Error.WriteLine($"  - {failure}");
    }
    return 1;
}

Console.WriteLine($"All {tests.Count} smoke tests passed.");
return 0;

static void TestRunOnceBootstrapsAndQueues()
{
    using var harness = new TestHarness();
    harness.Runtime.SetGoal(harness.State, "Build a multi-stage autonomous dev-team loop.");

    var result = harness.Runtime.RunOnce(harness.State, 2);

    AssertEqual("queued", result.State, "Loop state");
    AssertEqual(1, result.QueuedRuns.Count, "Queued run count");
    AssertEqual("orchestrator", result.QueuedRuns[0].RoleSlug, "Queued role");
    AssertEqual(1, harness.State.Roadmap.Count, "Roadmap count");
    AssertEqual(2, harness.State.Issues.Count, "Issue count");
}

static void TestCompleteRunUnlocksDependency()
{
    using var harness = new TestHarness();
    harness.Runtime.SetGoal(harness.State, "Build a multi-stage autonomous dev-team loop.");

    var first = harness.Runtime.RunOnce(harness.State, 1);
    harness.Runtime.CompleteRun(harness.State, first.QueuedRuns[0].RunId, "completed", "Planning finished.");
    var second = harness.Runtime.RunOnce(harness.State, 2);

    AssertEqual("architect", second.QueuedRuns[0].RoleSlug, "Dependent role");
}

static void TestBlockingQuestionWaitState()
{
    using var harness = new TestHarness();
    harness.Runtime.AddQuestion(harness.State, "Need production API key?", true);

    var result = harness.Runtime.RunOnce(harness.State, 2);

    AssertEqual("waiting-for-user", result.State, "Loop state");
}

static void TestPremiumCapFallback()
{
    using var harness = new TestHarness();
    harness.State.Budget.PremiumCreditCap = 0;
    harness.Runtime.AddIssue(harness.State, "Review architecture", "", "reviewer", 100, null, []);

    var result = harness.Runtime.RunOnce(harness.State, 1);

    AssertEqual("gpt-5.4", result.QueuedRuns[0].ModelName, "Fallback model");
}

static void TestCliAgentClientInvocationShape()
{
    var fakeRunner = new FakeCommandRunner();
    var client = new CopilotCliAgentClient(fakeRunner);
    var result = client.InvokeAsync(new AgentInvocationRequest
    {
        Prompt = "Summarize this repository",
        Model = "gpt-5.4",
        WorkingDirectory = @"C:\repo\Ralph",
        Timeout = TimeSpan.FromSeconds(15),
        ExtraArguments = ["--allow-all"]
    }).GetAwaiter().GetResult();

    AssertEqual("copilot-cli", result.BackendName, "Backend name");
    AssertEqual("copilot", fakeRunner.LastSpec?.FileName, "Process file name");
    AssertTrue(fakeRunner.LastSpec?.Arguments.SequenceEqual(
        ["--allow-all", "--model", "gpt-5.4", "--no-ask-user", "-p", "Summarize this repository"]) == true,
        "Argument list should match the CLI invocation.");
}

static void TestSdkAgentClientFactory()
{
    var client = AgentClientFactory.Create("sdk");
    AssertEqual("copilot-sdk", client.Name, "SDK backend name");
}

static void TestPromptAssetsPreferDevTeamSource()
{
    using var harness = new TestHarness();
    var role = harness.State.Roles.FirstOrDefault(item => item.Slug == "developer")
        ?? throw new InvalidOperationException("Developer role should be present.");
    var superpower = harness.State.Superpowers.FirstOrDefault(item => item.Slug == "verify")
        ?? throw new InvalidOperationException("Verify superpower should be present.");

    AssertTrue(role.SourcePath.StartsWith(".devteam-source", StringComparison.OrdinalIgnoreCase),
        "Roles should load from .devteam-source when present.");
    AssertTrue(superpower.SourcePath.StartsWith(".devteam-source", StringComparison.OrdinalIgnoreCase),
        "Superpowers should load from .devteam-source when present.");
}

static void AssertEqual<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"{label}: expected '{expected}' but got '{actual}'.");
    }
}

static void AssertTrue(bool value, string message)
{
    if (!value)
    {
        throw new InvalidOperationException(message);
    }
}

file sealed class TestHarness : IDisposable
{
    public TestHarness()
    {
        TempRoot = Path.Combine(Path.GetTempPath(), "devteam-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(TempRoot);
        RepoRoot = FindRepoRoot();
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
            Directory.Delete(TempRoot, true);
        }
    }

    private static string FindRepoRoot()
    {
        var directory = AppContext.BaseDirectory;
        var current = new DirectoryInfo(directory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".ralph-source")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new InvalidOperationException("Could not locate repository root.");
    }
}

file sealed class FakeCommandRunner : ICommandRunner
{
    public CommandExecutionSpec? LastSpec { get; private set; }

    public Task<CommandExecutionResult> RunAsync(CommandExecutionSpec spec, CancellationToken cancellationToken = default)
    {
        LastSpec = spec;
        return Task.FromResult(new CommandExecutionResult
        {
            ExitCode = 0,
            StdOut = "ok"
        });
    }
}
