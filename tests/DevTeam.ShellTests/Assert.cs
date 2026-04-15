namespace DevTeam.ShellTests;

internal static class Assert
{
    public static void That(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }
}
