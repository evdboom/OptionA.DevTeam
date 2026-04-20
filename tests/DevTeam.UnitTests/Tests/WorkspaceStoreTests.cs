namespace DevTeam.UnitTests.Tests;

internal static class WorkspaceStoreTests
{
    private const string RepoRoot = "C:\\test-repo";
    private const string DefaultModelName = "gpt-5-mini";
    private const double Tolerance = 0.0001;

    private sealed class EmptyConfigurationLoader : IConfigurationLoader
    {
        public List<ModelDefinition> LoadModels(string repoRoot) => [];
        public List<ProviderDefinition> LoadProviders(string repoRoot) => [];
        public List<RoleDefinition> LoadRoles(string repoRoot) => [];
        public List<ModeDefinition> LoadModes(string repoRoot) => [];
        public List<SkillDefinition> LoadSkills(string repoRoot) => [];
        public List<McpServerDefinition> LoadMcpServers(string repoRoot) => [];
    }

    public static IEnumerable<TestCase> GetTests() =>
    [
        new("Constructor_ThrowsArgumentException_WhenPathIsEmpty", Constructor_ThrowsArgumentException_WhenPathIsEmpty),
        new("Save_ThenLoad_RoundTrips_IssueList", Save_ThenLoad_RoundTrips_IssueList),
        new("Save_ThenLoad_RoundTrips_Questions", Save_ThenLoad_RoundTrips_Questions),
        new("Save_WritesExternalReferences_ToMirrors", Save_WritesExternalReferences_ToMirrors),
        new("Save_ThenLoad_RoundTrips_Budget", Save_ThenLoad_RoundTrips_Budget),
        new("Load_ThrowsInvalidOperation_WhenNoFile", Load_ThrowsInvalidOperation_WhenNoFile),
        new("Initialize_CreatesWorkspaceJson", Initialize_CreatesWorkspaceJson),
    ];

    private static WorkspaceStore CreateStore(InMemoryFileSystem fs) =>
        new("test-workspace", fs, new EmptyConfigurationLoader());

    private static Task Constructor_ThrowsArgumentException_WhenPathIsEmpty()
    {
        Assert.Throws<ArgumentException>(
            () => _ = new WorkspaceStore("   "),
            "Expected ArgumentException for empty workspace path");
        return Task.CompletedTask;
    }

    private static Task Save_ThenLoad_RoundTrips_IssueList()
    {
        var fs = new InMemoryFileSystem();
        var store = CreateStore(fs);
        var state = new WorkspaceState
        {
            RepoRoot = RepoRoot,
            Models = [new ModelDefinition { Name = DefaultModelName, Cost = 0, IsDefault = true }]
        };
        state.Issues.Add(new IssueItem { Id = state.NextIssueId++, Title = "Issue Alpha", RoleSlug = "developer" });
        state.Issues.Add(new IssueItem { Id = state.NextIssueId++, Title = "Issue Beta", RoleSlug = "tester" });

        store.Save(state);
        var loaded = store.Load();

        Assert.That(loaded.Issues.Count == 2, $"Expected 2 issues but got {loaded.Issues.Count}");
        Assert.That(loaded.Issues.Any(i => i.Title == "Issue Alpha"), "Expected Issue Alpha");
        Assert.That(loaded.Issues.Any(i => i.Title == "Issue Beta"), "Expected Issue Beta");
        return Task.CompletedTask;
    }

    private static Task Save_ThenLoad_RoundTrips_Questions()
    {
        var fs = new InMemoryFileSystem();
        var store = CreateStore(fs);
        var state = new WorkspaceState
        {
            RepoRoot = RepoRoot,
            Models = [new ModelDefinition { Name = DefaultModelName, Cost = 0, IsDefault = true }]
        };
        state.Questions.Add(new QuestionItem { Id = state.NextQuestionId++, Text = "What is the plan?", IsBlocking = true });
        state.Questions.Add(new QuestionItem { Id = state.NextQuestionId++, Text = "Which library?", IsBlocking = false });

        store.Save(state);
        var loaded = store.Load();

        Assert.That(loaded.Questions.Count == 2, $"Expected 2 questions but got {loaded.Questions.Count}");
        Assert.That(loaded.Questions.Any(q => q.Text == "What is the plan?"), "Expected first question");
        Assert.That(loaded.Questions.Any(q => !q.IsBlocking), "Expected non-blocking question");
        return Task.CompletedTask;
    }

    private static Task Save_ThenLoad_RoundTrips_Budget()
    {
        var fs = new InMemoryFileSystem();
        var store = CreateStore(fs);
        var state = new WorkspaceState
        {
            RepoRoot = RepoRoot,
            Models = [new ModelDefinition { Name = DefaultModelName, Cost = 0, IsDefault = true }],
            Budget = new BudgetState
            {
                TotalCreditCap = 50.0,
                PremiumCreditCap = 10.0,
                CreditsCommitted = 3.5
            }
        };

        store.Save(state);
        var loaded = store.Load();

        Assert.That(Math.Abs(loaded.Budget.TotalCreditCap - 50.0) <= Tolerance, $"Expected TotalCreditCap=50 but got {loaded.Budget.TotalCreditCap}");
        Assert.That(Math.Abs(loaded.Budget.PremiumCreditCap - 10.0) <= Tolerance, $"Expected PremiumCreditCap=10 but got {loaded.Budget.PremiumCreditCap}");
        Assert.That(Math.Abs(loaded.Budget.CreditsCommitted - 3.5) <= Tolerance, $"Expected CreditsCommitted=3.5 but got {loaded.Budget.CreditsCommitted}");
        return Task.CompletedTask;
    }

    private static Task Save_WritesExternalReferences_ToMirrors()
    {
        var fs = new InMemoryFileSystem();
        var store = CreateStore(fs);
        var state = new WorkspaceState
        {
            RepoRoot = RepoRoot,
            Models = [new ModelDefinition { Name = DefaultModelName, Cost = 0, IsDefault = true }]
        };
        state.Issues.Add(new IssueItem
        {
            Id = state.NextIssueId++,
            Title = "Issue Alpha",
            RoleSlug = "developer",
            ExternalReference = "github#101"
        });
        state.Questions.Add(new QuestionItem
        {
            Id = state.NextQuestionId++,
            Text = "Question Alpha?",
            IsBlocking = true,
            ExternalReference = "github#202"
        });

        store.Save(state);

        var issueMirror = fs.Files.Values.FirstOrDefault(content => content.Contains("# Issue 0001: Issue Alpha", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Expected issue mirror content to be written.");
        var questionsMirror = fs.Files.Values.FirstOrDefault(content => content.Contains("# Open questions", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Expected questions mirror content to be written.");

        Assert.Contains("- External: github#101", issueMirror);
        Assert.Contains("- External: github#202", questionsMirror);
        return Task.CompletedTask;
    }

    private static Task Load_ThrowsInvalidOperation_WhenNoFile()
    {
        var fs = new InMemoryFileSystem();
        var store = CreateStore(fs);

        Assert.Throws<InvalidOperationException>(
            () => store.Load(),
            "Expected InvalidOperationException when no workspace file exists");
        return Task.CompletedTask;
    }

    private static Task Initialize_CreatesWorkspaceJson()
    {
        var fs = new InMemoryFileSystem();
        var store = CreateStore(fs);

        store.Initialize(RepoRoot, totalCreditCap: 25, premiumCreditCap: 6);

        Assert.That(fs.FileExists(store.StatePath),
            $"Expected workspace.json to exist at '{store.StatePath}'");
        return Task.CompletedTask;
    }
}

