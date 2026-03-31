using DevTeam.Core;
using static DevTeam.Cli.CliOptionParser;

namespace DevTeam.Cli.Shell;

// Option parsing for the interactive shell — delegates to the shared CliOptionParser.
// Each method here is a private-scoped bridge so ShellService call sites need no changes.
internal sealed partial class ShellService
{
    private static Dictionary<string, List<string>> ParseOptions(string[] tokens) => CliOptionParser.ParseOptions(tokens);
    private static List<string> TokenizeInput(string input) => CliOptionParser.TokenizeInput(input);
    private static string NormalizeCommand(string value) => CliOptionParser.NormalizeCommand(value);
    private static string? GetOption(Dictionary<string, List<string>> options, string key) => CliOptionParser.GetOption(options, key);
    private static int GetIntOption(Dictionary<string, List<string>> options, string key, int fallback) => CliOptionParser.GetIntOption(options, key, fallback);
    private static double GetDoubleOption(Dictionary<string, List<string>> options, string key, double fallback) => CliOptionParser.GetDoubleOption(options, key, fallback);
    private static bool GetBoolOption(Dictionary<string, List<string>> options, string key, bool fallback) => CliOptionParser.GetBoolOption(options, key, fallback);
    private static bool? GetNullableBoolOption(Dictionary<string, List<string>> options, string key) => CliOptionParser.GetNullableBoolOption(options, key);
    private static bool ParseBoolOrThrow(string value, string errorMessage) => CliOptionParser.ParseBoolOrThrow(value, errorMessage);
    private static int? GetNullableIntOption(Dictionary<string, List<string>> options, string key) => CliOptionParser.GetNullableIntOption(options, key);
    private static IReadOnlyList<int> GetMultiIntOption(Dictionary<string, List<string>> options, string key) => CliOptionParser.GetMultiIntOption(options, key);
    private static string? GetPositionalValue(Dictionary<string, List<string>> options) => CliOptionParser.GetPositionalValue(options);
    private static IReadOnlyList<string> GetPositionalValues(Dictionary<string, List<string>> options) => CliOptionParser.GetPositionalValues(options);
    private static bool IsApproveIntent(string line) => CliOptionParser.IsApproveIntent(line);
    private static LoopVerbosity ParseVerbosity(string? value) => CliOptionParser.ParseVerbosity(value);
}
