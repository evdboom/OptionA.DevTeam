using System.IO.Compression;
using System.Text.Json;

namespace DevTeam.Cli;

internal static class WorkspaceArchiveService
{
    private static readonly string[] ExportFiles = ["workspace.json", "questions.md", "plan.md"];
    private static readonly string[] ExportDirectories = ["state", "issues", "runs", "decisions", "artifacts"];

    public static string Export(string workspacePath, string? outputPath)
    {
        var fullWorkspacePath = Path.GetFullPath(workspacePath);
        if (!File.Exists(Path.Combine(fullWorkspacePath, "workspace.json")))
        {
            throw new InvalidOperationException($"Workspace state not found at '{fullWorkspacePath}'.");
        }

        var destination = Path.GetFullPath(string.IsNullOrWhiteSpace(outputPath)
            ? Path.Combine(Environment.CurrentDirectory, "devteam-workspace.zip")
            : outputPath);
        var destinationDir = Path.GetDirectoryName(destination);
        if (!string.IsNullOrWhiteSpace(destinationDir))
        {
            Directory.CreateDirectory(destinationDir);
        }

        if (File.Exists(destination))
        {
            File.Delete(destination);
        }

        using var archive = ZipFile.Open(destination, ZipArchiveMode.Create);
        WriteManifest(archive, fullWorkspacePath);

        foreach (var file in ExportFiles)
        {
            var path = Path.Combine(fullWorkspacePath, file);
            if (File.Exists(path))
            {
                archive.CreateEntryFromFile(path, file, CompressionLevel.Optimal);
            }
        }

        foreach (var directory in ExportDirectories)
        {
            var root = Path.Combine(fullWorkspacePath, directory);
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(fullWorkspacePath, file);
                archive.CreateEntryFromFile(file, relativePath, CompressionLevel.Optimal);
            }
        }

        return destination;
    }

    public static string Import(string inputPath, string workspacePath, bool force)
    {
        var source = Path.GetFullPath(inputPath);
        if (!File.Exists(source))
        {
            throw new InvalidOperationException($"Archive not found at '{source}'.");
        }

        var destination = Path.GetFullPath(workspacePath);
        if (HasExistingWorkspace(destination))
        {
            if (!force)
            {
                throw new InvalidOperationException($"Workspace already exists at '{destination}'. Use --force to overwrite it.");
            }

            Directory.Delete(destination, recursive: true);
        }

        Directory.CreateDirectory(destination);

        using var archive = ZipFile.OpenRead(source);
        var manifestEntry = archive.GetEntry("devteam-export.json")
            ?? throw new InvalidOperationException("Workspace archive is missing devteam-export.json.");
        using (var manifestStream = manifestEntry.Open())
        {
            JsonDocument.Parse(manifestStream);
        }

        foreach (var entry in archive.Entries.Where(entry => !string.Equals(entry.FullName, "devteam-export.json", StringComparison.OrdinalIgnoreCase)))
        {
            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            var destinationPath = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!destinationPath.StartsWith(destination, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Archive contains an invalid path outside the workspace root.");
            }

            var parent = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            entry.ExtractToFile(destinationPath, overwrite: true);
        }

        return destination;
    }

    private static bool HasExistingWorkspace(string workspacePath)
    {
        if (!Directory.Exists(workspacePath))
        {
            return false;
        }

        if (File.Exists(Path.Combine(workspacePath, "workspace.json")))
        {
            return true;
        }

        return ExportDirectories.Any(directory => Directory.Exists(Path.Combine(workspacePath, directory)));
    }

    private static void WriteManifest(ZipArchive archive, string workspacePath)
    {
        var manifest = new
        {
            format = 1,
            exportedAtUtc = DateTimeOffset.UtcNow,
            workspaceName = Path.GetFileName(workspacePath),
            includedFiles = ExportFiles,
            includedDirectories = ExportDirectories
        };

        var entry = archive.CreateEntry("devteam-export.json", CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }
}
