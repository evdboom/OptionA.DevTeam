using DevTeam.Core;

namespace DevTeam.Cli.Shell;

internal sealed partial class ShellService
{
    private const int MaxCompletedCycleItems = 3;
    private readonly Dictionary<string, CycleSlot> _activeCycle = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CycleSlot> _completedCycle = [];

    private void ResetCycleState()
    {
        lock (_gate)
        {
            _activeCycle.Clear();
            _completedCycle.Clear();
        }
    }

    private IReadOnlyList<CycleSlot> GetCycleSnapshot()
    {
        lock (_gate)
        {
            var running = _activeCycle.Values
                .Where(slot => slot.IsRunning)
                .OrderBy(slot => slot.RoleSlug.Equals("orchestrator", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenByDescending(slot => slot.Elapsed)
                .ToList();

            var completed = _completedCycle
                .OrderByDescending(slot => slot.UpdatedAtUtc)
                .ToList();

            return [.. running, .. completed];
        }
    }

    private void ReportLoopProgress(IReadOnlyList<RunProgressSnapshot> snapshots)
    {
        var now = _clock.UtcNow;
        lock (_gate)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var snapshot in snapshots)
            {
                var key = BuildCycleKey(snapshot.RoleSlug, snapshot.IssueId);
                seen.Add(key);

                var updated = new CycleSlot(
                    snapshot.RoleSlug,
                    snapshot.IssueId,
                    snapshot.Title,
                    snapshot.Elapsed,
                    IsRunning: true,
                    IsCompleted: false,
                    UpdatedAtUtc: now);

                _activeCycle[key] = updated;
                _completedCycle.RemoveAll(slot => BuildCycleKey(slot.RoleSlug, slot.IssueId).Equals(key, StringComparison.OrdinalIgnoreCase));
            }

            var finishedKeys = _activeCycle.Keys
                .Where(key => !seen.Contains(key))
                .ToList();

            foreach (var key in finishedKeys)
            {
                var finished = _activeCycle[key] with { IsRunning = false, IsCompleted = true, UpdatedAtUtc = now };
                _completedCycle.RemoveAll(slot => BuildCycleKey(slot.RoleSlug, slot.IssueId).Equals(key, StringComparison.OrdinalIgnoreCase));
                _completedCycle.Insert(0, finished);
                _activeCycle.Remove(key);
            }

            if (_completedCycle.Count > MaxCompletedCycleItems)
            {
                _completedCycle.RemoveRange(MaxCompletedCycleItems, _completedCycle.Count - MaxCompletedCycleItems);
            }
        }

        if (TryLoadState(out var state) && state is not null)
        {
            RefreshLayoutSnapshot(state);
        }
        else
        {
            NotifyStateChanged();
        }
    }

    private static string BuildCycleKey(string roleSlug, int? issueId)
    {
        if (issueId is int id)
        {
            return $"issue:{id}";
        }

        return $"role:{roleSlug}";
    }
}
