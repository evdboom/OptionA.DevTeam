namespace DevTeam.Core;

public interface ISessionManager
{
    AgentSession GetOrCreateAgentSession(WorkspaceState state, int runId);
    AgentSession GetOrCreateExecutionOrchestratorSession(WorkspaceState state);
}
