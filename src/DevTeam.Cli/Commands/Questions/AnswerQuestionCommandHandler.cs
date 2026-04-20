using DevTeam.Core;
using static DevTeam.Cli.CliOptionParser;

namespace DevTeam.Cli;

internal sealed class AnswerQuestionCommandHandler(WorkspaceStore store, DevTeamRuntime runtime, IConsoleOutput output) : ICliCommandHandler
{
    private readonly WorkspaceStore _store = store;
    private readonly DevTeamRuntime _runtime = runtime;
    private readonly IConsoleOutput _output = output;

    public Task<int> ExecuteAsync(Dictionary<string, List<string>> options)
    {
        var state = _store.Load();
        var values = GetPositionalValues(options);
        if (values.Count < 2)
        {
            throw new InvalidOperationException("Usage: answer-question <id> <answer>");
        }

        _runtime.AnswerQuestion(state, int.Parse(values[0]), string.Join(" ", values.Skip(1)));
        _store.Save(state);
        _output.WriteLine($"Answered question #{values[0]}");
        return Task.FromResult(0);
    }
}
