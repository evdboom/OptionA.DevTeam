namespace DevTeam.UnitTests;

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
            throw new Exception($"Expected string to contain '{expected}' but it did not.");
    }

    public static void DoesNotContain(string unexpected, string actual)
    {
        if (actual.Contains(unexpected, StringComparison.Ordinal))
            throw new Exception($"Expected string NOT to contain '{unexpected}' but it did.");
    }

    public static void Throws<T>(Action action, string? message = null) where T : Exception
    {
        try
        {
            action();
            throw new Exception(message ?? $"Expected {typeof(T).Name} but no exception was thrown.");
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
            throw new Exception(message ?? $"Expected {typeof(T).Name} but no exception was thrown.");
        }
        catch (T)
        {
            // expected
        }
    }
}
