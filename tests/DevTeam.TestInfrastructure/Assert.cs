namespace DevTeam.TestInfrastructure;

public static class Assert
{
    public static void That(bool condition, string message)
    {
        if (!condition)
            throw new AssertException(message, "true", "false");
    }

    public static void Contains(string expected, string actual)
    {
        if (!actual.Contains(expected, StringComparison.Ordinal))
            throw new AssertException($"Expected string to contain '{expected}' but it did not.", expected, actual);
    }

    public static void DoesNotContain(string unexpected, string actual)
    {
        if (actual.Contains(unexpected, StringComparison.Ordinal))
            throw new AssertException($"Expected string NOT to contain '{unexpected}' but it did.", unexpected, actual);
    }

    public static void Throws<T>(Action action, string? message = null) where T : Exception
    {
        try
        {
            action();
            throw new AssertException(message ?? $"Expected {typeof(T).Name} but no exception was thrown.", typeof(T).Name, "no exception");
        }
        catch (T)
        {
            // expected
        }
    }

    public static async Task ThrowsAsync<T>(Func<Task> action, string? message = null) where T : Exception
    {
        try
        {
            await action();
            throw new AssertException(message ?? $"Expected {typeof(T).Name} but no exception was thrown.", typeof(T).Name, "no exception");
        }
        catch (T)
        {
            // expected
        }
    }
}
