namespace DevTeam.Core;

public interface IBudgetService
{
    ModelDefinition SelectModelForRole(WorkspaceState state, string roleSlug);
    void CommitCredits(WorkspaceState state, ModelDefinition model);
    bool CanAffordModel(WorkspaceState state, ModelDefinition model);
}
