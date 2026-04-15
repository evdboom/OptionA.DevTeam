namespace DevTeam.Cli;

public interface IConsoleOutput
{
    void WriteLine(string message = "");
    void WriteErrorLine(string message);
}
