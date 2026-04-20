using DevTeam.Core;
using static DevTeam.Cli.CliOptionParser;
using static DevTeam.Cli.CliWorkspaceHelper;

namespace DevTeam.Cli;

internal sealed class AddIssueCommandHandler(WorkspaceStore store, DevTeamRuntime runtime, IConsoleOutput output) : ICliCommandHandler
{
    private readonly WorkspaceStore _store = store;
    private readonly DevTeamRuntime _runtime = runtime;
    private readonly IConsoleOutput _output = output;

    public Task<int> ExecuteAsync(Dictionary<string, List<string>> options)
    {
        var state = _store.Load();

        var request = new IssueRequest
        {
            Title = GetPositionalValue(options) ?? throw new InvalidOperationException("Missing issue title."),
            RoleSlug = GetOption(options, "role") ?? throw new InvalidOperationException(BuildMissingRoleMessage(_runtime, state)),
            Detail = GetOption(options, "detail") ?? "",
            Area = GetOption(options, "area"),
            Priority = GetIntOption(options, "priority", 50),
            RoadmapItemId = GetNullableIntOption(options, "roadmap-item-id"),
            DependsOn = GetMultiIntOption(options, "depends-on")
        };

        ValidateRoleOrThrow(_runtime, state, request.RoleSlug);
        var issue = _runtime.AddIssue(state, request);
        _store.Save(state);
        _output.WriteLine($"Created issue #{issue.Id}: {issue.Title} ({issue.RoleSlug}{(string.IsNullOrWhiteSpace(issue.Area) ? "" : $", area {issue.Area}")})");
        return Task.FromResult(0);
    }
}
