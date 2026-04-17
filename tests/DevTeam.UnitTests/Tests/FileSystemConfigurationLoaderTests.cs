namespace DevTeam.UnitTests.Tests;

internal static class FileSystemConfigurationLoaderTests
{
    public static IEnumerable<TestCase> GetTests() =>
    [
        new("LoadModels_ReturnsDefaults_WhenFileDoesNotExist", LoadModels_ReturnsDefaults_WhenFileDoesNotExist),
        new("LoadModels_ParsesJsonFile_WhenPresent", LoadModels_ParsesJsonFile_WhenPresent),
        new("LoadProviders_ReturnsEmpty_WhenFileDoesNotExist", LoadProviders_ReturnsEmpty_WhenFileDoesNotExist),
        new("LoadProviders_ParsesJsonFile_WhenPresent", LoadProviders_ParsesJsonFile_WhenPresent),
        new("LoadRoles_ReturnsEmpty_WhenDirectoryDoesNotExist", LoadRoles_ReturnsEmpty_WhenDirectoryDoesNotExist),
        new("LoadRoles_ParsesMarkdownFiles_WhenPresent", LoadRoles_ParsesMarkdownFiles_WhenPresent),
        new("LoadModes_ReturnsDefaults_WhenDirectoryDoesNotExist", LoadModes_ReturnsDefaults_WhenDirectoryDoesNotExist),
        new("LoadMcpServers_ReturnsEmpty_WhenFileDoesNotExist", LoadMcpServers_ReturnsEmpty_WhenFileDoesNotExist),
        new("ParseMarkdownAsset_StripsFrontmatter_AndExtractsTools", ParseMarkdownAsset_StripsFrontmatter_AndExtractsTools),
    ];

    /// <summary>
    /// Finds the path that FileSystemConfigurationLoader.ResolveFirstFile would resolve for a
    /// given candidate by walking up from AppContext.BaseDirectory.
    /// Returns null if no real file is found (the loader will use the default fallback path).
    /// </summary>
    private static string? FindResolvedFilePath(string candidateRelativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, candidateRelativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return null;
    }

    private static string? FindResolvedDirectoryPath(string candidateRelativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, candidateRelativePath);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return null;
    }

    private static Task LoadModels_ReturnsDefaults_WhenFileDoesNotExist()
    {
        var fs = new InMemoryFileSystem(); // empty — no MODELS.json seeded
        var loader = new FileSystemConfigurationLoader(fs);

        // InMemoryFileSystem has no models file, so defaults are returned
        var models = loader.LoadModels("C:\\nonexistent-test-root");

        Assert.That(models.Count > 0, "Expected default models to be returned");
        Assert.That(models.Any(m => m.IsDefault), "Expected at least one default model");
        return Task.CompletedTask;
    }

    private static Task LoadModels_ParsesJsonFile_WhenPresent()
    {
        var fs = new InMemoryFileSystem();
        var loader = new FileSystemConfigurationLoader(fs);

        // The loader's path resolution will find the real .devteam-source\MODELS.json
        // via the AppContext walk. We seed InMemoryFileSystem with that resolved path
        // so the loader reads our custom content instead of returning defaults.
        var resolvedPath = FindResolvedFilePath(Path.Combine(".devteam-source", "MODELS.json"))
            ?? Path.Combine("C:\\nonexistent-test-root", ".devteam-source", "MODELS.json");

        var customJson = """
            [
                { "Name": "test-model-a", "Cost": 0.5, "ProviderName": "azure-foundry", "Provider": "Azure", "InputCostPer1kTokens": 0.01, "OutputCostPer1kTokens": 0.02 },
                { "Name": "test-model-b", "Cost": 2.0, "Default": true, "Family": "anthropic" }
            ]
            """;
        fs.WriteAllText(resolvedPath, customJson);

        var models = loader.LoadModels("C:\\nonexistent-test-root");

        Assert.That(models.Count == 2, $"Expected 2 models but got {models.Count}");
        var modelA = models.FirstOrDefault(m => m.Name == "test-model-a");
        Assert.That(modelA is not null, "Expected test-model-a");
        Assert.That(modelA!.ProviderName == "azure-foundry", $"Expected provider name azure-foundry but got '{modelA.ProviderName}'");
        Assert.That(modelA.Family == "openai", $"Expected azure provider to normalize to openai family but got '{modelA.Family}'");
        Assert.That(modelA.InputCostPer1kTokens == 0.01, $"Expected input rate 0.01 but got {modelA.InputCostPer1kTokens}");
        Assert.That(modelA.OutputCostPer1kTokens == 0.02, $"Expected output rate 0.02 but got {modelA.OutputCostPer1kTokens}");
        Assert.That(models.Any(m => m.Name == "test-model-b" && m.IsDefault), "Expected test-model-b as default");
        return Task.CompletedTask;
    }

    private static Task LoadProviders_ReturnsEmpty_WhenFileDoesNotExist()
    {
        var fs = new InMemoryFileSystem();
        var loader = new FileSystemConfigurationLoader(fs);

        var providers = loader.LoadProviders("C:\\nonexistent-test-root");

        Assert.That(providers.Count == 0, $"Expected 0 providers but got {providers.Count}");
        return Task.CompletedTask;
    }

    private static Task LoadProviders_ParsesJsonFile_WhenPresent()
    {
        var fs = new InMemoryFileSystem();
        var loader = new FileSystemConfigurationLoader(fs);
        var resolvedPath = FindResolvedFilePath(Path.Combine(".devteam-source", "PROVIDERS.json"))
            ?? Path.Combine("C:\\nonexistent-test-root", ".devteam-source", "PROVIDERS.json");

        var customJson = """
            [
                { "Name": "azure-foundry", "Type": "azure", "BaseUrl": "https://example.openai.azure.com/openai", "ApiKeyEnvVar": "AZURE_OPENAI_KEY", "AzureApiVersion": "2024-10-21" },
                { "Name": "ollama-local", "Type": "openai", "BaseUrl": "http://localhost:11434/v1", "BearerTokenEnvVar": "OLLAMA_TOKEN", "WireApi": "responses" }
            ]
            """;
        fs.WriteAllText(resolvedPath, customJson);

        var providers = loader.LoadProviders("C:\\nonexistent-test-root");

        Assert.That(providers.Count == 2, $"Expected 2 providers but got {providers.Count}");
        Assert.That(providers.Any(p => p.Name == "azure-foundry" && p.Type == "azure" && p.AzureApiVersion == "2024-10-21"),
            "Expected Azure provider definition.");
        Assert.That(providers.Any(p => p.Name == "ollama-local" && p.WireApi == "responses" && p.BearerTokenEnvVar == "OLLAMA_TOKEN"),
            "Expected Ollama provider definition.");
        return Task.CompletedTask;
    }

    private static Task LoadRoles_ReturnsEmpty_WhenDirectoryDoesNotExist()
    {
        var fs = new InMemoryFileSystem(); // no directories seeded
        var loader = new FileSystemConfigurationLoader(fs);

        var roles = loader.LoadRoles("C:\\nonexistent-test-root");

        Assert.That(roles.Count == 0, $"Expected 0 roles but got {roles.Count}");
        return Task.CompletedTask;
    }

    private static Task LoadRoles_ParsesMarkdownFiles_WhenPresent()
    {
        var fs = new InMemoryFileSystem();
        var loader = new FileSystemConfigurationLoader(fs);

        // Seed InMemoryFileSystem with the path that the loader will resolve to
        var resolvedDir = FindResolvedDirectoryPath(Path.Combine(".devteam-source", "roles"))
            ?? Path.Combine("C:\\nonexistent-test-root", ".devteam-source", "roles");

        fs.CreateDirectory(resolvedDir);
        var rolePath = Path.Combine(resolvedDir, "my-role.md");
        fs.WriteAllText(rolePath, "# Role: My Role\n\nDo great things.\n");

        var roles = loader.LoadRoles("C:\\nonexistent-test-root");

        Assert.That(roles.Count == 1, $"Expected 1 role but got {roles.Count}");
        Assert.That(roles[0].Slug == "my-role", $"Expected slug 'my-role' but got '{roles[0].Slug}'");
        Assert.That(roles[0].Name == "My Role", $"Expected name 'My Role' but got '{roles[0].Name}'");
        return Task.CompletedTask;
    }

    private static Task LoadModes_ReturnsDefaults_WhenDirectoryDoesNotExist()
    {
        var fs = new InMemoryFileSystem(); // no mode directory seeded
        var loader = new FileSystemConfigurationLoader(fs);

        var modes = loader.LoadModes("C:\\nonexistent-test-root");

        // Returns built-in defaults when directory missing
        Assert.That(modes.Count > 0, "Expected default modes to be returned");
        Assert.That(modes.Any(m => m.Slug == "develop"), "Expected 'develop' default mode");
        Assert.That(modes.Any(m => m.Slug == "github"), "Expected 'github' default mode");
        return Task.CompletedTask;
    }

    private static Task LoadMcpServers_ReturnsEmpty_WhenFileDoesNotExist()
    {
        var fs = new InMemoryFileSystem();
        var loader = new FileSystemConfigurationLoader(fs);

        var servers = loader.LoadMcpServers("C:\\nonexistent-test-root");

        Assert.That(servers.Count == 0, $"Expected 0 MCP servers but got {servers.Count}");
        return Task.CompletedTask;
    }

    private static Task ParseMarkdownAsset_StripsFrontmatter_AndExtractsTools()
    {
        var fs = new InMemoryFileSystem();
        var loader = new FileSystemConfigurationLoader(fs);

        var resolvedDir = FindResolvedDirectoryPath(Path.Combine(".devteam-source", "roles"))
            ?? Path.Combine("C:\\nonexistent-test-root", ".devteam-source", "roles");

        fs.CreateDirectory(resolvedDir);
        var rolePath = Path.Combine(resolvedDir, "tooled-role.md");
        var content = """
            ---
            tools: rg, git, dotnet
            ---
            # Role: Tooled Role

            Uses tools to get things done.
            """;
        fs.WriteAllText(rolePath, content);

        var roles = loader.LoadRoles("C:\\nonexistent-test-root");

        Assert.That(roles.Count == 1, $"Expected 1 role but got {roles.Count}");
        Assert.That(roles[0].Slug == "tooled-role", $"Expected slug 'tooled-role' but got '{roles[0].Slug}'");
        Assert.That(roles[0].RequiredTools.Count == 3, $"Expected 3 tools but got {roles[0].RequiredTools.Count}");
        Assert.That(roles[0].RequiredTools.Contains("rg"), "Expected tool 'rg'");
        Assert.That(roles[0].RequiredTools.Contains("git"), "Expected tool 'git'");
        Assert.That(roles[0].RequiredTools.Contains("dotnet"), "Expected tool 'dotnet'");
        Assert.That(!roles[0].Body.Contains("---"), "Expected frontmatter stripped from body");
        return Task.CompletedTask;
    }
}
