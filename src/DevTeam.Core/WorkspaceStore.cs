using System.Text.Json;
using System.Text.Json.Serialization;

namespace DevTeam.Core;

public sealed class WorkspaceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public WorkspaceStore(string workspacePath)
    {
        WorkspacePath = Path.GetFullPath(workspacePath);
        StatePath = Path.Combine(WorkspacePath, "workspace.json");
    }

    public string WorkspacePath { get; }
    public string StatePath { get; }

    public WorkspaceState Initialize(string repoRoot, double totalCreditCap, double premiumCreditCap)
    {
        Directory.CreateDirectory(WorkspacePath);
        var state = SeedData.BuildInitialState(repoRoot, totalCreditCap, premiumCreditCap);
        Save(state);
        return state;
    }

    public WorkspaceState Load()
    {
        if (!File.Exists(StatePath))
        {
            throw new InvalidOperationException(
                $"Workspace state not found at '{StatePath}'. Run 'init' first.");
        }

        var json = File.ReadAllText(StatePath);
        var state = JsonSerializer.Deserialize<WorkspaceState>(json, JsonOptions);
        if (state is null)
        {
            throw new InvalidOperationException("Failed to deserialize workspace state.");
        }

        return state;
    }

    public void Save(WorkspaceState state)
    {
        Directory.CreateDirectory(WorkspacePath);
        File.WriteAllText(StatePath, JsonSerializer.Serialize(state, JsonOptions));
    }
}

