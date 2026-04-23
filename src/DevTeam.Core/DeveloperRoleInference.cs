using System.Text;

namespace DevTeam.Core;

internal static class DeveloperRoleInference
{
    private const string RoleDeveloper = "developer";
    private const string RoleBackendDeveloper = "backend-developer";
    private const string RoleFrontendDeveloper = "frontend-developer";
    private const string RoleFullstackDeveloper = "fullstack-developer";

    private static readonly string[] FrontendRoleHints =
    [
        "blazor", "razor", "ui", "ux", "frontend", "front-end", "component", "page", "layout", "css", "html", "browser", "client"
    ];

    private static readonly string[] BackendRoleHints =
    [
        "backend", "back-end", "api", "endpoint", "controller", "service", "repository", "database", "sql", "migration", "auth", "middleware", "server"
    ];

    public static string InferSpecializedRole(
        WorkspaceState state,
        string requestedRole,
        string normalizedRole,
        string title,
        string detail,
        string area)
    {
        if (!string.Equals(normalizedRole, RoleDeveloper, StringComparison.OrdinalIgnoreCase))
        {
            return normalizedRole;
        }

        if (!CanSpecialize(requestedRole))
        {
            return normalizedRole;
        }

        var tokens = BuildHintTokens(title, detail, area);
        if (tokens.Count == 0)
        {
            return normalizedRole;
        }

        var frontendSignal = FrontendRoleHints.Any(tokens.Contains);
        var backendSignal = BackendRoleHints.Any(tokens.Contains);

        if (frontendSignal && backendSignal && RoleExists(state, RoleFullstackDeveloper))
        {
            return RoleFullstackDeveloper;
        }

        if (frontendSignal && RoleExists(state, RoleFrontendDeveloper))
        {
            return RoleFrontendDeveloper;
        }

        if (backendSignal && RoleExists(state, RoleBackendDeveloper))
        {
            return RoleBackendDeveloper;
        }

        return normalizedRole;
    }

    private static bool CanSpecialize(string requestedRole)
    {
        var normalizedRequestedRole = requestedRole.Trim().ToLowerInvariant();
        return normalizedRequestedRole.Length == 0 || normalizedRequestedRole == RoleDeveloper;
    }

    private static HashSet<string> BuildHintTokens(string title, string detail, string area)
    {
        var corpus = string.Join(" ", title, detail, area).ToLowerInvariant();
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(corpus))
        {
            return tokens;
        }

        var builder = new StringBuilder();
        foreach (var character in corpus)
        {
            if (char.IsLetterOrDigit(character) || character == '-')
            {
                builder.Append(character);
                continue;
            }

            AddToken(tokens, builder);
        }

        AddToken(tokens, builder);
        return tokens;
    }

    private static void AddToken(HashSet<string> tokens, StringBuilder builder)
    {
        if (builder.Length == 0)
        {
            return;
        }

        var token = builder.ToString();
        builder.Clear();
        tokens.Add(token);
        if (!token.Contains('-', StringComparison.Ordinal))
        {
            return;
        }

        foreach (var part in token.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            tokens.Add(part);
        }
    }

    private static bool RoleExists(WorkspaceState state, string roleSlug) =>
        state.Roles.Any(role => string.Equals(role.Slug, roleSlug, StringComparison.OrdinalIgnoreCase));
}
