using System.Text.Json;
using System.Text.RegularExpressions;

namespace DevTeam.Core;

public sealed partial class FileSystemConfigurationLoader : IConfigurationLoader
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
    private static readonly string[] RoleDirectoryCandidates = [Path.Combine(CoreConstants.Paths.DevTeamSource, CoreConstants.Paths.Roles)];
    private static readonly string[] ModeDirectoryCandidates = [Path.Combine(CoreConstants.Paths.DevTeamSource, CoreConstants.Paths.Modes)];
    private static readonly string[] SkillDirectoryCandidates = [Path.Combine(CoreConstants.Paths.DevTeamSource, CoreConstants.Paths.Skills)];
    private static readonly string[] LegacySuperpowerDirectoryCandidates = [Path.Combine(CoreConstants.Paths.DevTeamSource, CoreConstants.Paths.Superpowers)];
    private static readonly string[] ModelFileCandidates = [Path.Combine(CoreConstants.Paths.DevTeamSource, CoreConstants.Paths.ModelsFile)];
    private static readonly string[] ProviderFileCandidates = [Path.Combine(CoreConstants.Paths.DevTeamSource, CoreConstants.Paths.ProvidersFile)];
    private static readonly string[] McpServerFileCandidates = [Path.Combine(CoreConstants.Paths.DevTeamSource, CoreConstants.Paths.McpServersFile)];

    private readonly IFileSystem _fs;

    public FileSystemConfigurationLoader(IFileSystem? fileSystem = null)
    {
        _fs = fileSystem ?? new PhysicalFileSystem();
    }

    public List<ModelDefinition> LoadModels(string repoRoot)
    {
        var modelsPath = ResolveFirstFile(repoRoot, ModelFileCandidates);
        if (!_fs.FileExists(modelsPath))
        {
            return
            [
                new ModelDefinition { Name = "claude-opus-4.6", Cost = 3, IsPremium = true },
                new ModelDefinition { Name = "claude-sonnet-4.6", Cost = 1 },
                new ModelDefinition { Name = "gpt-5.4", Cost = 1 },
                new ModelDefinition { Name = "gpt-5.4-mini", Cost = 0.33 },
                new ModelDefinition { Name = "gpt-5-mini", Cost = 0, IsDefault = true }
            ];
        }

        using var doc = JsonDocument.Parse(_fs.ReadAllText(modelsPath));
        var models = new List<ModelDefinition>();
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            var cost = element.GetProperty("Cost").GetDouble();
            models.Add(new ModelDefinition
            {
                Name = element.GetProperty("Name").GetString() ?? "",
                ProviderName = GetString(element, "ProviderName"),
                Cost = cost,
                InputCostPer1kTokens = GetNullableDouble(element, "InputCostPer1kTokens"),
                OutputCostPer1kTokens = GetNullableDouble(element, "OutputCostPer1kTokens"),
                Family = GetModelFamily(element),
                IsDefault = element.TryGetProperty("Default", out var isDefault) && isDefault.GetBoolean(),
                IsPremium = cost > 1
            });
        }

        return models;
    }

    public List<ProviderDefinition> LoadProviders(string repoRoot)
    {
        var providersPath = ResolveFirstFile(repoRoot, ProviderFileCandidates);
        if (!_fs.FileExists(providersPath))
        {
            return [];
        }

        using var doc = JsonDocument.Parse(_fs.ReadAllText(providersPath));
        var providers = new List<ProviderDefinition>();
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            providers.Add(new ProviderDefinition
            {
                Name = GetString(element, "Name"),
                Type = GetString(element, "Type"),
                BaseUrl = GetString(element, "BaseUrl"),
                WireApi = GetString(element, "WireApi"),
                ApiKeyEnvVar = GetString(element, "ApiKeyEnvVar"),
                BearerTokenEnvVar = GetString(element, "BearerTokenEnvVar"),
                AzureApiVersion = GetString(element, "AzureApiVersion")
            });
        }

        return providers;
    }

    public List<RoleDefinition> LoadRoles(string repoRoot)
    {
        var rolesDir = ResolveFirstDirectory(repoRoot, RoleDirectoryCandidates);
        if (!_fs.DirectoryExists(rolesDir))
        {
            return [];
        }

        var roles = new List<RoleDefinition>();
        foreach (var path in _fs.GetFiles(rolesDir, "*.md").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var asset = ParseMarkdownAsset(path);
            var firstLine = asset.Body.Split('\n', StringSplitOptions.None).FirstOrDefault()?.Trim() ?? "";
            var suggestedMatch = SuggestedModelRegex().Match(asset.Body);
            roles.Add(new RoleDefinition
            {
                Slug = Path.GetFileNameWithoutExtension(path),
                Name = firstLine.Replace("# Role:", "", StringComparison.Ordinal).Trim(),
                SuggestedModel = suggestedMatch.Success ? suggestedMatch.Groups[1].Value : "",
                SourcePath = Path.GetRelativePath(repoRoot, path),
                Body = asset.Body,
                RequiredTools = asset.RequiredTools
            });
        }

        return roles;
    }

    public List<ModeDefinition> LoadModes(string repoRoot)
    {
        var modesDir = ResolveFirstDirectory(repoRoot, ModeDirectoryCandidates);
        if (!_fs.DirectoryExists(modesDir))
        {
            return
            [
                new ModeDefinition
                {
                    Slug = "develop",
                    Name = "Develop",
                    SourcePath = Path.Combine(CoreConstants.Paths.DevTeamSource, CoreConstants.Paths.Modes, "develop.md"),
                    Body = """
                    # Mode: Develop

                    Deliver working software, not just plausible code.

                    Guardrails:
                    - Always build the changed project or solution before declaring the work done.
                    - Add thorough tests for the delivered behavior: unit tests, integration tests when relevant, and end-to-end tests when the user-facing flow matters.
                    - If the repository cannot currently test the behavior, create the minimum missing test harness or automation needed so the behavior can be verified safely.
                    - Prefer closing the loop on actual runtime behavior instead of stopping at static implementation.
                    - Update user-facing or maintainer-facing documentation when the feature, workflow, or validation story changes.
                    """
                },
                new ModeDefinition
                {
                    Slug = "creative-writing",
                    Name = "Creative Writing",
                    SourcePath = Path.Combine(CoreConstants.Paths.DevTeamSource, CoreConstants.Paths.Modes, "creative-writing.md"),
                    Body = """
                    # Mode: Creative Writing

                    Optimize for voice, coherence, revision quality, and reader experience.

                    Guardrails:
                    - Preserve tone, point of view, and narrative continuity.
                    - Revise in deliberate passes: structure first, then language, then polish.
                    - Surface gaps in character motivation, pacing, or clarity instead of glossing over them.
                    - When useful, propose follow-on editorial work as focused issues rather than broad rewrites.
                    - Keep supporting notes and documentation aligned with the latest draft direction.
                    """
                },
                new ModeDefinition
                {
                    Slug = "github",
                    Name = "GitHub",
                    SourcePath = Path.Combine(CoreConstants.Paths.DevTeamSource, CoreConstants.Paths.Modes, "github.md"),
                    Body = """
                    # Mode: GitHub

                    Optimize for repository-native teamwork where GitHub Issues act as the shared work queue.

                    Guardrails:
                    - Treat GitHub Issues as the source of truth for incoming execution work when the team is using issue sync.
                    - Sync the GitHub work queue before running a batch so local execution reflects the latest labelled issues.
                    - Keep execution conservative by default: prefer smaller batches, clear summaries, and review-friendly issue scope.
                    - Preserve the intent of the originating GitHub issue. If you narrow or reinterpret the work, explain that clearly in the summary.
                    - When work extends an existing GitHub thread, keep the resulting local issue, run summary, and audit trail easy to map back to the original issue reference.
                    """
                }
            ];
        }

        var modes = new List<ModeDefinition>();
        foreach (var path in _fs.GetFiles(modesDir, "*.md").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var asset = ParseMarkdownAsset(path);
            var firstLine = asset.Body.Split('\n', StringSplitOptions.None).FirstOrDefault()?.Trim() ?? "";
            modes.Add(new ModeDefinition
            {
                Slug = Path.GetFileNameWithoutExtension(path),
                Name = firstLine.Replace("# Mode:", "", StringComparison.Ordinal).Trim(),
                SourcePath = Path.GetRelativePath(repoRoot, path),
                Body = asset.Body
            });
        }

        return modes;
    }

    public List<SkillDefinition> LoadSkills(string repoRoot)
    {
        var directory = ResolveFirstDirectory(repoRoot, SkillDirectoryCandidates);
        if (_fs.DirectoryExists(directory))
        {
            return LoadStructuredSkills(repoRoot, directory);
        }

        var legacyDirectory = ResolveFirstDirectory(repoRoot, LegacySuperpowerDirectoryCandidates);
        if (!_fs.DirectoryExists(legacyDirectory))
        {
            return [];
        }

        var items = new List<SkillDefinition>();
        foreach (var path in _fs.GetFiles(legacyDirectory, "*.md").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var asset = ParseMarkdownAsset(path);
            var firstLine = asset.Body.Split('\n', StringSplitOptions.None).FirstOrDefault()?.Trim() ?? "";
            items.Add(new SkillDefinition
            {
                Slug = Path.GetFileNameWithoutExtension(path),
                Name = firstLine.TrimStart('#', ' ').Trim(),
                SourcePath = Path.GetRelativePath(repoRoot, path),
                Body = asset.Body,
                RequiredTools = asset.RequiredTools
            });
        }

        return items;
    }

    private List<SkillDefinition> LoadStructuredSkills(string repoRoot, string skillsRoot)
    {
        var items = new List<SkillDefinition>();
        foreach (var skillPath in _fs.EnumerateFiles(skillsRoot, "SKILL.md", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var skillDirectory = Path.GetDirectoryName(skillPath);
            if (string.IsNullOrWhiteSpace(skillDirectory))
            {
                continue;
            }

            var asset = ParseMarkdownAsset(skillPath);
            var firstLine = asset.Body.Split('\n', StringSplitOptions.None).FirstOrDefault()?.Trim() ?? "";
            items.Add(new SkillDefinition
            {
                Slug = Path.GetFileName(skillDirectory),
                Name = firstLine.TrimStart('#', ' ').Trim(),
                SourcePath = Path.GetRelativePath(repoRoot, skillPath),
                Body = asset.Body,
                RequiredTools = asset.RequiredTools
            });
        }

        return items;
    }

    public List<McpServerDefinition> LoadMcpServers(string repoRoot)
    {
        var filePath = ResolveFirstFile(repoRoot, McpServerFileCandidates);
        if (!_fs.FileExists(filePath))
        {
            return [];
        }

        var repoRootFullPath = Path.GetFullPath(repoRoot);
        using var doc = JsonDocument.Parse(_fs.ReadAllText(filePath));
        var servers = new List<McpServerDefinition>();
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            var definition = new McpServerDefinition
            {
                Name = element.GetProperty("Name").GetString() ?? "",
                Command = element.GetProperty("Command").GetString() ?? "",
                Args = element.TryGetProperty("Args", out var args)
                    ? args.EnumerateArray().Select(a => a.GetString() ?? "").ToList()
                    : [],
                Cwd = element.TryGetProperty("Cwd", out var cwd) ? cwd.GetString() : null,
                Description = element.TryGetProperty("Description", out var desc) ? desc.GetString() ?? "" : "",
                Enabled = !element.TryGetProperty("Enabled", out var enabled) || enabled.GetBoolean()
            };
            ValidateMcpServerDefinition(definition, repoRootFullPath);
            servers.Add(definition);
        }

        return servers;
    }

    private static void ValidateMcpServerDefinition(McpServerDefinition definition, string repoRootFullPath)
    {
        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            throw new InvalidOperationException("MCP server name is required.");
        }

        if (string.IsNullOrWhiteSpace(definition.Command))
        {
            throw new InvalidOperationException($"MCP server '{definition.Name}' must define a command.");
        }

        if (definition.Command.IndexOf('\0') >= 0)
        {
            throw new InvalidOperationException($"MCP server '{definition.Name}' contains an invalid command value.");
        }

        if (!Path.IsPathRooted(definition.Command) && definition.Command.Contains(Path.DirectorySeparatorChar))
        {
            throw new InvalidOperationException($"MCP server '{definition.Name}' command must be a bare executable name or an absolute path.");
        }

        if (!Path.IsPathRooted(definition.Command) && definition.Command.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new InvalidOperationException($"MCP server '{definition.Name}' command must be a bare executable name or an absolute path.");
        }

        if (definition.Args.Count > 64)
        {
            throw new InvalidOperationException($"MCP server '{definition.Name}' has too many command arguments (max 64).");
        }

        foreach (var arg in definition.Args)
        {
            if (arg.IndexOf('\0') >= 0)
            {
                throw new InvalidOperationException($"MCP server '{definition.Name}' contains an invalid command argument.");
            }
        }

        if (string.IsNullOrWhiteSpace(definition.Cwd))
        {
            return;
        }

        var resolvedCwd = Path.GetFullPath(
            Path.IsPathRooted(definition.Cwd)
                ? definition.Cwd
                : Path.Combine(repoRootFullPath, definition.Cwd));
        var normalizedRepoRoot = EnsureTrailingSeparator(Path.GetFullPath(repoRootFullPath));
        var normalizedCwd = EnsureTrailingSeparator(resolvedCwd);
        if (!normalizedCwd.StartsWith(normalizedRepoRoot, PathComparison))
        {
            throw new InvalidOperationException($"MCP server '{definition.Name}' cwd must remain inside the repository root.");
        }

        definition.Cwd = resolvedCwd;
    }

    private static string EnsureTrailingSeparator(string path)
    {
        var normalized = Path.GetFullPath(path);
        if (!normalized.EndsWith(Path.DirectorySeparatorChar) && !normalized.EndsWith(Path.AltDirectorySeparatorChar))
        {
            normalized += Path.DirectorySeparatorChar;
        }

        return normalized;
    }

    private MarkdownAsset ParseMarkdownAsset(string path)
    {
        var raw = _fs.ReadAllText(path);
        var requiredTools = new List<string>();
        if (!raw.StartsWith("---", StringComparison.Ordinal))
        {
            return new MarkdownAsset(raw, requiredTools);
        }

        var lines = raw.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (lines.Length < 3 || lines[0] != "---")
        {
            return new MarkdownAsset(raw, requiredTools);
        }

        var endIndex = Array.FindIndex(lines, 1, line => line == "---");
        if (endIndex < 0)
        {
            return new MarkdownAsset(raw, requiredTools);
        }

        for (var index = 1; index < endIndex; index++)
        {
            var line = lines[index].Trim();
            if (TryGetToolsLineValue(line, out var inline))
            {
                if (!string.IsNullOrWhiteSpace(inline))
                {
                    requiredTools.AddRange(ParseToolList(inline));
                }
                continue;
            }

            if (line.StartsWith("-", StringComparison.Ordinal))
            {
                requiredTools.Add(line.TrimStart('-', ' ').Trim());
            }
        }

        var body = string.Join('\n', lines.Skip(endIndex + 1)).TrimStart();
        return new MarkdownAsset(body, requiredTools.Where(tool => !string.IsNullOrWhiteSpace(tool)).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static bool TryGetToolsLineValue(string line, out string value)
    {
        const string toolsPrefix = "tools:";
        const string allowedToolsPrefix = "allowed-tools:";

        if (line.StartsWith(toolsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            value = line[toolsPrefix.Length..].Trim();
            return true;
        }

        if (line.StartsWith(allowedToolsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            value = line[allowedToolsPrefix.Length..].Trim();
            return true;
        }

        value = "";
        return false;
    }

    private static IEnumerable<string> ParseToolList(string inline)
    {
        var normalized = inline.Trim();
        if (normalized.StartsWith("[", StringComparison.Ordinal) &&
            normalized.EndsWith("]", StringComparison.Ordinal) &&
            normalized.Length >= 2)
        {
            normalized = normalized[1..^1];
        }

        return normalized.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static double? GetNullableDouble(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;

    private static string GetModelFamily(JsonElement element)
    {
        var family = GetString(element, "Family");
        if (!string.IsNullOrWhiteSpace(family))
        {
            return NormalizeModelFamily(family);
        }

        return NormalizeModelFamily(GetString(element, "Provider"));
    }

    private static string NormalizeModelFamily(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "openai" or "azure" or "azure-openai" or "azure ai" or "azure ai foundry" => "openai",
            "anthropic" => "anthropic",
            "google" or "gemini" => "google",
            _ => value.Trim().ToLowerInvariant()
        };
    }

    private static string ResolveFirstDirectory(string repoRoot, IEnumerable<string> candidates)
    {
        foreach (var root in EnumerateAssetRoots(repoRoot))
        {
            foreach (var candidate in candidates)
            {
                var fullPath = Path.Combine(root, candidate);
                if (Directory.Exists(fullPath))
                {
                    return fullPath;
                }
            }
        }
        return Path.Combine(repoRoot, candidates.First());
    }

    private static string ResolveFirstFile(string repoRoot, IEnumerable<string> candidates)
    {
        foreach (var root in EnumerateAssetRoots(repoRoot))
        {
            foreach (var candidate in candidates)
            {
                var fullPath = Path.Combine(root, candidate);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
        }
        return Path.Combine(repoRoot, candidates.First());
    }

    private static IEnumerable<string> EnumerateAssetRoots(string repoRoot)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in new[] { repoRoot, AppContext.BaseDirectory })
        {
            var current = new DirectoryInfo(Path.GetFullPath(root));
            while (current is not null)
            {
                if (seen.Add(current.FullName))
                {
                    yield return current.FullName;
                }

                current = current.Parent;
            }
        }
    }

    private sealed record MarkdownAsset(string Body, List<string> RequiredTools);

    [GeneratedRegex(@"(?m)^## Suggested Model\s*\r?\n\s*`([a-zA-Z0-9][\w.\-]*)`")]
    private static partial Regex SuggestedModelRegex();
}
