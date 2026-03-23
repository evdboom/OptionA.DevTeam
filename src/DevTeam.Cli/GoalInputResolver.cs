namespace DevTeam.Cli;

public static class GoalInputResolver
{
    public static string? Resolve(string? inlineGoal, string? goalFilePath, string workingDirectory)
    {
        var hasInline = !string.IsNullOrWhiteSpace(inlineGoal);
        var hasFile = !string.IsNullOrWhiteSpace(goalFilePath);
        if (hasInline && hasFile)
        {
            throw new InvalidOperationException("Specify either inline goal text or --goal-file, not both.");
        }

        if (hasFile)
        {
            return LoadGoalFile(goalFilePath!, workingDirectory);
        }

        return hasInline ? inlineGoal!.Trim() : null;
    }

    public static string LoadGoalFile(string goalFilePath, string workingDirectory)
    {
        var fullPath = Path.GetFullPath(
            Path.IsPathRooted(goalFilePath)
                ? goalFilePath
                : Path.Combine(workingDirectory, goalFilePath));
        if (!File.Exists(fullPath))
        {
            throw new InvalidOperationException($"Goal file not found: {fullPath}");
        }

        var content = File.ReadAllText(fullPath).Trim();
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException($"Goal file is empty: {fullPath}");
        }

        return content;
    }
}
