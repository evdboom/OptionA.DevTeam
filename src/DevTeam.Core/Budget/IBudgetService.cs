namespace DevTeam.Core;

public interface IBudgetService
{
    /// <summary>
    /// Selects the best affordable model for <paramref name="roleSlug"/>.
    /// When <paramref name="excludeFamily"/> is provided the service prefers a model from a different provider family
    /// (cross-family review). Falls back to any affordable model when no cross-family option is available.
    /// </summary>
    ModelDefinition SelectModelForRole(WorkspaceState state, string roleSlug, string? excludeFamily = null);
    void CommitCredits(WorkspaceState state, ModelDefinition model);
    bool CanAffordModel(WorkspaceState state, ModelDefinition model);
}
