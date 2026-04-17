using DevTeam.Core;

namespace DevTeam.Cli.Shell;

internal sealed partial class ShellService
{
    private bool _adventureModeEnabled;
    private List<AdventureRoleSlot> _adventureRoles = [];
    private readonly Dictionary<string, string> _adventureSpeechBubbles = new(StringComparer.OrdinalIgnoreCase);

    public bool IsAdventureModeEnabled
    {
        get { lock (_gate) return _adventureModeEnabled; }
    }

    public AdventureShellSnapshot AdventureSnapshot
    {
        get
        {
            lock (_gate)
            {
                return new AdventureShellSnapshot(
                    _adventureModeEnabled,
                    _layoutSnapshot.Phase,
                    [.. _adventureRoles],
                    [.. _layoutSnapshot.Agents],
                    [.. _layoutSnapshot.Roadmap],
                    new Dictionary<string, string>(_adventureSpeechBubbles, StringComparer.OrdinalIgnoreCase));
            }
        }
    }

    private static string BuildAdventureBubbleText(string text)
    {
        var firstLine = text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "(no reply)";
        return firstLine.Length <= 40 ? firstLine : firstLine[..37] + "...";
    }

    private void UpdateAdventureRoles(WorkspaceState state)
    {
        var roles = state.Roles
            .Select(role => new AdventureRoleSlot(role.Slug, string.IsNullOrWhiteSpace(role.Name) ? role.Slug : role.Name))
            .ToList();

        lock (_gate)
        {
            _adventureRoles = roles;
        }
    }

    private void SetAdventureMode(bool enabled)
    {
        lock (_gate)
        {
            _adventureModeEnabled = enabled;
        }

        NotifyStateChanged();
    }
}
