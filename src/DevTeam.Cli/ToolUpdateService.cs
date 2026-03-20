using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DevTeam.Cli;

public sealed class ToolUpdateService(HttpClient? httpClient = null) : IDisposable
{
    public const string PackageId = "OptionA.DevTeam";
    public const string PackageIndexUrl = "https://api.nuget.org/v3-flatcontainer/optiona.devteam/index.json";

    private readonly HttpClient _httpClient = httpClient ?? new HttpClient();
    private readonly bool _ownsHttpClient = httpClient is null;

    public async Task<ToolUpdateStatus> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(PackageIndexUrl, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new ToolUpdateUnavailableException($"{PackageId} is not published to NuGet yet.");
        }

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<NuGetFlatContainerIndex>(stream, cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("NuGet returned an empty package version feed.");

        return EvaluateVersions(GetInstalledVersion(), payload.Versions);
    }

    public ToolUpdateLaunchResult ScheduleGlobalUpdate(string version)
    {
        var scriptPath = CreateUpdateScript(version);
        StartDetachedProcess(scriptPath);
        return new ToolUpdateLaunchResult
        {
            ScriptPath = scriptPath,
            ManualCommand = BuildManualUpdateCommand(version)
        };
    }

    public static ToolUpdateStatus EvaluateVersions(string currentVersion, IEnumerable<string> versions)
    {
        var current = ParseStableVersion(currentVersion)
            ?? throw new InvalidOperationException($"Current tool version '{currentVersion}' is not a supported stable version.");

        var latest = versions
            .Select(ParseStableVersion)
            .Where(version => version is not null)
            .Cast<ParsedStableVersion>()
            .OrderByDescending(version => version.SemanticVersion)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("NuGet did not return any stable package versions.");

        return new ToolUpdateStatus
        {
            CurrentVersion = current.Normalized,
            LatestVersion = latest.Normalized,
            IsUpdateAvailable = latest.SemanticVersion > current.SemanticVersion
        };
    }

    public static string GetInstalledVersion()
    {
        var informationalVersion = typeof(ToolUpdateService).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            throw new InvalidOperationException("Unable to determine the installed OptionA.DevTeam version.");
        }

        return informationalVersion.Split('+', 2)[0];
    }

    public static IReadOnlyList<string> BuildGlobalUpdateArguments(string version) =>
        ["tool", "update", "--global", PackageId, "--version", version];

    public static string BuildManualUpdateCommand(string version) =>
        $"dotnet {string.Join(" ", BuildGlobalUpdateArguments(version))}";

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private static ParsedStableVersion? ParseStableVersion(string version)
    {
        var normalized = version.Split('+', 2)[0].Trim();
        if (normalized.Length == 0 || normalized.Contains('-', StringComparison.Ordinal))
        {
            return null;
        }

        return Version.TryParse(normalized, out var semanticVersion)
            ? new ParsedStableVersion(normalized, semanticVersion)
            : null;
    }

    private static string CreateUpdateScript(string version)
    {
        var extension = OperatingSystem.IsWindows() ? ".cmd" : ".sh";
        var scriptPath = Path.Combine(Path.GetTempPath(), $"optiona-devteam-update-{Guid.NewGuid():N}{extension}");
        File.WriteAllText(scriptPath, BuildUpdateScriptContent(version));
        return scriptPath;
    }

    private static string BuildUpdateScriptContent(string version)
    {
        var command = BuildManualUpdateCommand(version);
        return OperatingSystem.IsWindows()
            ? string.Join(Environment.NewLine,
            [
                "@echo off",
                "ping 127.0.0.1 -n 3 > nul",
                command,
                "del \"%~f0\""
            ])
            : string.Join('\n',
            [
                "#!/bin/sh",
                "sleep 2",
                command,
                "rm -- \"$0\""
            ]);
    }

    private static void StartDetachedProcess(string scriptPath)
    {
        var startInfo = OperatingSystem.IsWindows()
            ? new ProcessStartInfo
            {
                FileName = "cmd.exe",
                WorkingDirectory = Directory.GetCurrentDirectory(),
                UseShellExecute = false,
                CreateNoWindow = true
            }
            : new ProcessStartInfo
            {
                FileName = "/bin/sh",
                WorkingDirectory = Directory.GetCurrentDirectory(),
                UseShellExecute = false,
                CreateNoWindow = true
            };

        startInfo.ArgumentList.Add(OperatingSystem.IsWindows() ? "/c" : scriptPath);
        if (OperatingSystem.IsWindows())
        {
            startInfo.ArgumentList.Add(scriptPath);
        }

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to launch the updater process.");
        process.Dispose();
    }

    private sealed record ParsedStableVersion(string Normalized, Version SemanticVersion);

    private sealed class NuGetFlatContainerIndex
    {
        [JsonPropertyName("versions")]
        public List<string> Versions { get; init; } = [];
    }
}

public sealed class ToolUpdateStatus
{
    public string CurrentVersion { get; init; } = "";
    public string LatestVersion { get; init; } = "";
    public bool IsUpdateAvailable { get; init; }
}

public sealed class ToolUpdateLaunchResult
{
    public string ScriptPath { get; init; } = "";
    public string ManualCommand { get; init; } = "";
}

public sealed class ToolUpdateUnavailableException(string message) : InvalidOperationException(message);
