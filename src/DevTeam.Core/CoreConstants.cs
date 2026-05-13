namespace DevTeam.Core;

public static class CoreConstants
{
    public static class Paths
    {
        public const string DevTeamRepo = ".devteam-repo";
        public const string DevTeamSource = ".devteam-source";
        public const string Roles = "roles";
        public const string Modes = "modes";
        public const string Skills = "skills";
        public const string Superpowers = "superpowers";
        public const string ModelsFile = "MODELS.json";
        public const string ProvidersFile = "PROVIDERS.json";
        public const string McpServersFile = "MCP_SERVERS.json";
    }

    public static class Roles
    {
        public const string Architect = "architect";
    }

    public static class LoopStates
    {
        public const string Queued = "queued";
        public const string Idle = "idle";
        public const string WaitingForUser = "waiting-for-user";
        public const string AwaitingPlanApproval = "awaiting-plan-approval";
        public const string AwaitingArchitectApproval = "awaiting-architect-approval";
    }

    public static class DecisionSources
    {
        public const string Runtime = "runtime";
        public const string Pipeline = "pipeline";
    }

    public static class Models
    {
        public const string ClaudeHaiku45 = "claude-haiku-4.5";
        public const string ClaudeSonnet46 = "claude-sonnet-4.6";
        public const string ClaudeOpus46 = "claude-opus-4.6";
        public const string ClaudeOpus47 = "claude-opus-4.7";
        public const string Gpt55 = "gpt-5.5";
        public const string Gpt54 = "gpt-5.4";
        public const string Gpt54Mini = "gpt-5.4-mini";
        public const string Gpt5Mini = "gpt-5-mini";
    }
}
