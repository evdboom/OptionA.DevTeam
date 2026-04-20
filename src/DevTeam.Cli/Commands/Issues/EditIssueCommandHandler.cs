using DevTeam.Core;
using static DevTeam.Cli.CliOptionParser;

namespace DevTeam.Cli;

internal sealed class EditIssueCommandHandler(WorkspaceStore store, DevTeamRuntime runtime, IConsoleOutput output) : ICliCommandHandler
{
    private readonly WorkspaceStore _store = store;
    private readonly DevTeamRuntime _runtime = runtime;
    private readonly IConsoleOutput _output = output;

    public Task<int> ExecuteAsync(Dictionary<string, List<string>> options)
    {
        var state = _store.Load();
        var request = IssueEditRequestParser.Parse(_runtime, state, options);
        var issue = _runtime.EditIssue(state, request);
        _store.Save(state);
        _output.WriteLine($"Updated issue #{issue.Id}: {issue.Title} ({issue.RoleSlug}{(string.IsNullOrWhiteSpace(issue.Area) ? "" : $", area {issue.Area}")}, priority {issue.Priority}, status {issue.Status.ToString().ToLowerInvariant()})");
        return Task.FromResult(0);
    }
}
