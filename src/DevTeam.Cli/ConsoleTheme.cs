namespace DevTeam.Cli;

internal static class ConsoleTheme
{
    private static readonly bool _supportsAnsi = !Console.IsOutputRedirected && Environment.GetEnvironmentVariable("NO_COLOR") is null;

    // ANSI color codes
    private const string Reset = "\x1b[0m";
    private const string Bold = "\x1b[1m";
    private const string Dim = "\x1b[2m";
    private const string Cyan = "\x1b[36m";
    private const string Green = "\x1b[32m";
    private const string Yellow = "\x1b[33m";
    private const string Red = "\x1b[31m";
    private const string Magenta = "\x1b[35m";
    private const string BrightWhite = "\x1b[97m";
    private const string BrightCyan = "\x1b[96m";
    private const string BrightGreen = "\x1b[92m";
    private const string BrightYellow = "\x1b[93m";

    private static string Wrap(string code, string text) => _supportsAnsi ? $"{code}{text}{Reset}" : text;

    // Semantic color helpers
    public static string Command(string text) => Wrap(BrightCyan, text);
    public static string Phase(string text) => Wrap(BrightYellow, text);
    public static string Success(string text) => Wrap(BrightGreen, text);
    public static string Warning(string text) => Wrap(Yellow, text);
    public static string Error(string text) => Wrap(Red, text);
    public static string Label(string text) => Wrap(Bold, text);
    public static string Accent(string text) => Wrap(Cyan, text);
    public static string Muted(string text) => Wrap(Dim, text);
    public static string Role(string text) => Wrap(Magenta, text);
    public static string Number(string text) => Wrap(BrightWhite, text);

    public static string Outcome(string outcome) => outcome switch
    {
        "completed" => Success(outcome),
        "blocked" => Warning(outcome),
        "failed" => Error(outcome),
        _ => outcome
    };

    public static string BudgetUsage(double used, double cap)
    {
        var ratio = cap > 0 ? used / cap : 0;
        var text = $"{used:0.##}/{cap:0.##}";
        return ratio switch
        {
            >= 0.9 => Wrap(Red, text),
            >= 0.7 => Wrap(Yellow, text),
            _ => Wrap(Green, text)
        };
    }

    public static void WritePrompt(string prompt)
    {
        if (_supportsAnsi)
        {
            Console.Write($"{BrightCyan}{prompt}{Reset}");
        }
        else
        {
            Console.Write(prompt);
        }
    }
}
