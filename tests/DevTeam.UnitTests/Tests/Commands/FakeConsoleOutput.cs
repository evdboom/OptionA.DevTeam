using DevTeam.Cli;

namespace DevTeam.UnitTests.Tests.Commands;

internal sealed class FakeConsoleOutput : IConsoleOutput
{
    private readonly List<string> _lines = [];
    private readonly List<string> _errorLines = [];

    public List<string> Lines => _lines;
    public List<string> ErrorLines => _errorLines;

    public void WriteLine(string message = "")
    {
        _lines.Add(message);
    }

    public void WriteErrorLine(string message)
    {
        _errorLines.Add(message);
    }
}
