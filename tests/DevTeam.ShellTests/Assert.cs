namespace DevTeam.ShellTests;

internal static class Assert
{
    public static void That(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }

    public static void Contains(string expected, string actual)
    {
        if (!actual.Contains(expected, StringComparison.Ordinal))
            throw new Exception($"Expected string to contain '{expected}' but it did not.\nActual: {actual}");
    }

    public static void DoesNotContain(string unexpected, string actual)
    {
        if (actual.Contains(unexpected, StringComparison.Ordinal))
            throw new Exception($"Expected string NOT to contain '{unexpected}' but it did.");
    }
}
