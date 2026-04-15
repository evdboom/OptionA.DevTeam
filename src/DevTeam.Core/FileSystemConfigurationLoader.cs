using System.Text.Json;
using System.Text.RegularExpressions;

namespace DevTeam.Core;

public sealed partial class FileSystemConfigurationLoader : IConfigurationLoader
{
    private static readonly string[] RoleDirectoryCandidates = [Path.Combine(".devteam-source", "roles")];
    private static readonly string[] ModeDirectoryCandidates = [Path.Combine(".devteam-source", "modes")];
    private static readonly string[] SuperpowerDirectoryCandidates = [Path.Combine(".devteam-source", "superpowers")];
    private static readonly string[] ModelFileCandidates = [Path.Combine(".devteam-source", "MODELS.json")];
    private static readonly string[] McpServerFileCandidates = [Path.Combine(".devteam-source", "MCP_SERVERS.json")];

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
                Cost = cost,
                IsDefault = element.TryGetProperty("Default", out var isDefault) && isDefault.GetBoolean(),
                IsPremium = cost > 1
            });
        }

        return models;
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
                    SourcePath = Path.Combine(".devteam-source", "modes", "develop.md"),
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
                    SourcePath = Path.Combine(".devteam-source", "modes", "creative-writing.md"),
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

    public List<SuperpowerDefinition> LoadSuperpowers(string repoRoot)
    {
        var directory = ResolveFirstDirectory(repoRoot, SuperpowerDirectoryCandidates);
        if (!_fs.DirectoryExists(directory))
        {
            return [];
        }

        var items = new List<SuperpowerDefinition>();
        foreach (var path in _fs.GetFiles(directory, "*.md").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var asset = ParseMarkdownAsset(path);
            var firstLine = asset.Body.Split('\n', StringSplitOptions.None).FirstOrDefault()?.Trim() ?? "";
            items.Add(new SuperpowerDefinition
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

    public List<McpServerDefinition> LoadMcpServers(string repoRoot)
    {
        var filePath = ResolveFirstFile(repoRoot, McpServerFileCandidates);
        if (!_fs.FileExists(filePath))
        {
            return [];
        }

        using var doc = JsonDocument.Parse(_fs.ReadAllText(filePath));
        var servers = new List<McpServerDefinition>();
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            servers.Add(new McpServerDefinition
            {
                Name = element.GetProperty("Name").GetString() ?? "",
                Command = element.GetProperty("Command").GetString() ?? "",
                Args = element.TryGetProperty("Args", out var args)
                    ? args.EnumerateArray().Select(a => a.GetString() ?? "").ToList()
                    : [],
                Cwd = element.TryGetProperty("Cwd", out var cwd) ? cwd.GetString() : null,
                Description = element.TryGetProperty("Description", out var desc) ? desc.GetString() ?? "" : "",
                Enabled = !element.TryGetProperty("Enabled", out var enabled) || enabled.GetBoolean()
            });
        }

        return servers;
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
            if (line.StartsWith("tools:", StringComparison.OrdinalIgnoreCase))
            {
                var inline = line["tools:".Length..].Trim();
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

    private static IEnumerable<string> ParseToolList(string inline) =>
        inline.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

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
