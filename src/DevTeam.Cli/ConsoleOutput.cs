namespace DevTeam.Cli;

public sealed class ConsoleOutput : IConsoleOutput
{
    public void WriteLine(string message = "") => Console.WriteLine(message);
    public void WriteErrorLine(string message) => Console.Error.WriteLine(message);
}
