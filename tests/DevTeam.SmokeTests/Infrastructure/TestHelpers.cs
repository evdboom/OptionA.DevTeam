using DevTeam.Cli;
using DevTeam.Core;
using DevTeam.TestInfrastructure;

namespace DevTeam.SmokeTests;

internal static class TestHelpers
{
    private static readonly string DotnetPath = ResolveDotnetPath();
    private static readonly string GitPath = ResolveGitPath();

    internal static void AssertEqual<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected '{expected}' but got '{actual}'.");
        }
    }
    
    internal static void AssertTrue(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidOperationException(message);
        }
    }
    
    internal static string RunGit(string workingDirectory, params string[] arguments)
    {
        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = GitPath,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
    
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }
    
        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start git in tests.");
        }
    
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr) ? "Git command failed in tests." : stderr.Trim());
        }
    
        return stdout;
    }
    
    internal static CliInvocationResult RunDevTeamCli(string workingDirectory, params string[] arguments)
    {
        var cliAssemblyPath = typeof(GoalInputResolver).Assembly.Location;
        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = DotnetPath,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
    
        process.StartInfo.ArgumentList.Add(cliAssemblyPath);
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }
    
        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start devteam CLI in tests.");
        }
    
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
    
        return new CliInvocationResult
        {
            ExitCode = process.ExitCode,
            StdOut = stdout,
            StdErr = stderr
        };
    }
    
    /// <summary>
    /// Best-effort cleanup for temp git repos. On Windows, .git object locking can cause
    /// flaky failures, so we retry but never fail the test. On Linux/CI, cleanup should
    /// succeed reliably and avoid accumulating temp dirs.
    /// </summary>
    internal static void TryCleanupTempRepo(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }
    
        try
        {
            TestFileSystem.DeleteDirectoryWithRetries(path);
        }
        catch
        {
            // Best-effort: on Windows, .git object files can remain locked.
        }
    }

    private static string ResolveDotnetPath()
    {
        var hostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrWhiteSpace(hostPath) && File.Exists(hostPath))
        {
            return hostPath;
        }

        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(dotnetRoot))
        {
            var candidate = Path.Combine(dotnetRoot, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return "dotnet";
    }

    private static string ResolveGitPath()
    {
        if (OperatingSystem.IsWindows())
        {
            return "git";
        }

        return new[] { "/usr/bin/git", "/usr/local/bin/git" }
            .FirstOrDefault(File.Exists)
            ?? "git";
    }
    
}
