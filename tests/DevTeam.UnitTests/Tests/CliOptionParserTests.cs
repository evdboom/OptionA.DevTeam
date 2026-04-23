namespace DevTeam.UnitTests.Tests;

internal static class CliOptionParserTests
{
    public static IEnumerable<TestCase> GetTests() =>
    [
        new("NormalizeWorkspacePathOrThrow_RejectsTooLongPath", NormalizeWorkspacePathOrThrow_RejectsTooLongPath),
        new("NormalizeWorkspacePathOrThrow_RejectsNulCharacter", NormalizeWorkspacePathOrThrow_RejectsNulCharacter),
        new("NormalizeWorkspacePathOrThrow_NormalizesRelativePath", NormalizeWorkspacePathOrThrow_NormalizesRelativePath),
    ];

    private static Task NormalizeWorkspacePathOrThrow_RejectsTooLongPath()
    {
        var tooLongPath = new string('a', DevTeam.Cli.CliOptionParser.MaxWorkspacePathLength + 1);

        try
        {
            DevTeam.Cli.CliOptionParser.NormalizeWorkspacePathOrThrow(tooLongPath);
            throw new Exception("Expected InvalidOperationException for overlong workspace path.");
        }
        catch (InvalidOperationException ex)
        {
            Assert.That(ex.Message.Contains("Workspace path is too long", StringComparison.Ordinal),
                $"Expected too-long validation message but got: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    private static Task NormalizeWorkspacePathOrThrow_RejectsNulCharacter()
    {
        var invalidPath = "workspace" + '\0' + "name";

        try
        {
            DevTeam.Cli.CliOptionParser.NormalizeWorkspacePathOrThrow(invalidPath);
            throw new Exception("Expected InvalidOperationException for workspace path containing NUL.");
        }
        catch (InvalidOperationException ex)
        {
            Assert.That(ex.Message.Contains("invalid control characters", StringComparison.OrdinalIgnoreCase),
                $"Expected invalid-character validation message but got: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    private static Task NormalizeWorkspacePathOrThrow_NormalizesRelativePath()
    {
        var normalized = DevTeam.Cli.CliOptionParser.NormalizeWorkspacePathOrThrow(".devteam");

        Assert.That(Path.IsPathRooted(normalized),
            $"Expected normalized workspace path to be rooted but got: {normalized}");
        Assert.That(string.Equals(normalized, Path.GetFullPath(".devteam"), StringComparison.Ordinal),
            $"Expected normalized path to equal Path.GetFullPath('.devteam') but got: {normalized}");

        return Task.CompletedTask;
    }
}
