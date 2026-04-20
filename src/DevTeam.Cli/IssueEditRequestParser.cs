using DevTeam.Core;
using static DevTeam.Cli.CliOptionParser;

namespace DevTeam.Cli;

internal static class IssueEditRequestParser
{
    internal const string Usage = "Usage: edit-issue <id> [--title TEXT] [--detail TEXT] [--role ROLE] [--area AREA | --clear-area] [--priority N] [--status open|in-progress|done|blocked] [--depends-on N ... | --clear-depends] [--note TEXT]";

    internal static IssueEditRequest Parse(DevTeamRuntime runtime, WorkspaceState state, Dictionary<string, List<string>> options)
    {
        var positional = GetPositionalValues(options);
        if (positional.Count == 0 || !int.TryParse(positional[0], out var issueId))
        {
            throw new InvalidOperationException($"{Usage}\nMissing or invalid issue id.");
        }

        if (positional.Count > 1)
        {
            throw new InvalidOperationException($"{Usage}\nUse --title to change the title. Extra positional values are not supported.");
        }

        var clearArea = GetBoolOption(options, "clear-area", false);
        var clearDepends = GetBoolOption(options, "clear-depends", false);
        var area = GetOption(options, "area");
        var dependsValues = ResolveOptionValues(options, "depends-on");
        if (clearArea && area is not null)
        {
            throw new InvalidOperationException("Use either --area or --clear-area, not both.");
        }

        if (clearDepends && dependsValues is { Count: > 0 })
        {
            throw new InvalidOperationException("Use either --depends-on or --clear-depends, not both.");
        }

        IReadOnlyList<int>? dependsOn = null;
        if (dependsValues is { Count: > 0 })
        {
            var parsed = new List<int>(dependsValues.Count);
            foreach (var value in dependsValues)
            {
                if (!int.TryParse(value, out var dependencyId))
                {
                    throw new InvalidOperationException($"Invalid dependency id '{value}'. Dependency ids must be integers.");
                }

                parsed.Add(dependencyId);
            }

            dependsOn = parsed;
        }

        var role = GetOption(options, "role");
        if (!string.IsNullOrWhiteSpace(role))
        {
            runtime.TryResolveRoleSlug(state, role, out var resolvedRole);
            if (!DevTeamRuntime.GetKnownRoleSlugs(state).Contains(resolvedRole, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Unknown role '{role}'. Valid roles: {string.Join(", ", DevTeamRuntime.GetKnownRoleSlugs(state))}");
            }
        }

        return new IssueEditRequest
        {
            IssueId = issueId,
            Title = GetOption(options, "title"),
            Detail = GetOption(options, "detail"),
            RoleSlug = role,
            Area = area,
            ClearArea = clearArea,
            Priority = GetNullableIntOption(options, "priority"),
            Status = GetOption(options, "status"),
            DependsOnIssueIds = dependsOn,
            ClearDependencies = clearDepends,
            NotesToAppend = GetOption(options, "note")
        };
    }
}
