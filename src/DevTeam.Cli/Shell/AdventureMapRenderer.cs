using System.Text;
using DevTeam.Core;
using Spectre.Console;

namespace DevTeam.Cli.Shell;

internal enum AdventureDeskState
{
    Idle,
    Open,
    Queued,
    Running,
    Blocked,
    Done
}

internal sealed record AdventureDesk(
    string RoleSlug,
    string DisplayName,
    AdventurePoint Position,
    AdventureDeskState State,
    string StatusText,
    string? BubbleText);

internal sealed record AdventureWorld(
    IReadOnlyList<AdventureDesk> Desks,
    IReadOnlyList<string> HiddenRoles);

internal static class AdventureMapRenderer
{
    private const int RoomWidth = 58;
    private const int RoomHeight = 14;
    private const int DeskLabelMax = 13;
    private const int DeskBubbleMax = 15;
    private static readonly AdventurePoint[] DeskPositions =
    [
        new(8, 3),
        new(29, 3),
        new(50, 3),
        new(8, 10),
        new(29, 10),
        new(50, 10)
    ];

    private static readonly string[] RolePriority =
    [
        "planner", "architect", "navigator", "developer", "tester", "security",
        "docs", "auditor", "reviewer", "designer", "researcher"
    ];

    internal static AdventurePoint DefaultSpawn => new(RoomWidth / 2, RoomHeight / 2);

    internal static AdventureWorld BuildWorld(AdventureShellSnapshot snapshot)
    {
        var knownRoles = snapshot.Roles
            .GroupBy(role => role.RoleSlug, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToDictionary(
                role => role.RoleSlug,
                role => string.IsNullOrWhiteSpace(role.DisplayName) ? role.RoleSlug : role.DisplayName,
                StringComparer.OrdinalIgnoreCase);

        foreach (var slug in snapshot.Agents.Select(agent => agent.RoleSlug)
                     .Concat(snapshot.Roadmap.Select(item => item.RoleSlug))
                     .Concat(snapshot.SpeechBubbles.Keys))
        {
            if (!knownRoles.ContainsKey(slug))
            {
                knownRoles[slug] = slug;
            }
        }

        var focusRoles = snapshot.Agents.Select(agent => agent.RoleSlug)
            .Concat(snapshot.Roadmap.Where(item => item.Status != ItemStatus.Done).Select(item => item.RoleSlug))
            .Concat(snapshot.SpeechBubbles.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(GetRolePriority)
            .ThenBy(slug => slug, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var remainingRoles = knownRoles.Keys
            .Except(focusRoles, StringComparer.OrdinalIgnoreCase)
            .OrderBy(GetRolePriority)
            .ThenBy(slug => slug, StringComparer.OrdinalIgnoreCase);

        var orderedRoles = focusRoles
            .Concat(remainingRoles)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var visibleRoles = orderedRoles.Take(DeskPositions.Length).ToList();
        var hiddenRoles = orderedRoles.Skip(DeskPositions.Length).ToList();

        var desks = new List<AdventureDesk>(visibleRoles.Count);
        for (var index = 0; index < visibleRoles.Count; index++)
        {
            var slug = visibleRoles[index];
            var displayName = knownRoles.TryGetValue(slug, out var name) ? name : slug;
            desks.Add(new AdventureDesk(
                slug,
                displayName,
                DeskPositions[index],
                ResolveState(snapshot, slug),
                ResolveStatusText(snapshot, slug),
                snapshot.SpeechBubbles.TryGetValue(slug, out var bubble) ? bubble : null));
        }

        return new AdventureWorld(desks, hiddenRoles);
    }

    internal static AdventurePoint EnsureValidPlayer(AdventureShellSnapshot snapshot, AdventurePoint player)
    {
        var world = BuildWorld(snapshot);
        var clamped = ClampToRoom(player);
        if (!IsDeskTile(world, clamped))
        {
            return clamped;
        }

        var spawn = ClampToRoom(DefaultSpawn);
        if (!IsDeskTile(world, spawn))
        {
            return spawn;
        }

        for (var y = 1; y < RoomHeight - 1; y++)
        {
            for (var x = 1; x < RoomWidth - 1; x++)
            {
                var candidate = new AdventurePoint(x, y);
                if (!IsDeskTile(world, candidate))
                {
                    return candidate;
                }
            }
        }

        return spawn;
    }

    internal static AdventurePoint MovePlayer(AdventureWorld world, AdventurePoint player, int dx, int dy)
    {
        var next = ClampToRoom(new AdventurePoint(player.X + dx, player.Y + dy));
        return IsDeskTile(world, next) ? player : next;
    }

    internal static AdventureDesk? FindAdjacentDesk(AdventureWorld world, AdventurePoint player) =>
        world.Desks
            .Where(desk => ManhattanDistance(desk.Position, player) <= 2)
            .OrderBy(desk => ManhattanDistance(desk.Position, player))
            .ThenBy(desk => desk.RoleSlug, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

    internal static Panel BuildMapPanel(AdventureShellSnapshot snapshot, AdventurePoint player)
    {
        var world = BuildWorld(snapshot);
        var safePlayer = EnsureValidPlayer(snapshot, player);
        var buffer = CreateBuffer(RoomWidth, RoomHeight);

        foreach (var desk in world.Desks)
        {
            WriteCentered(buffer, desk.Position.Y - 2, desk.Position.X, $"<{Truncate(desk.BubbleText ?? desk.StatusText, DeskBubbleMax)}>");
            WriteCentered(buffer, desk.Position.Y - 1, desk.Position.X, Truncate(desk.DisplayName, DeskLabelMax));
            WriteCentered(buffer, desk.Position.Y, desk.Position.X, DeskGlyph(desk.State));
        }

        Write(buffer, safePlayer.X, safePlayer.Y, '@');

        var lines = new List<string>(RoomHeight + 2)
        {
            $"┌{new string('─', RoomWidth)}┐"
        };

        foreach (var row in buffer)
        {
            lines.Add($"│{new string(row)}│");
        }

        lines.Add($"└{new string('─', RoomWidth)}┘");

        return new Panel(new Markup(Markup.Escape(string.Join("\n", lines))))
            .Header("- [teal bold]Adventure[/] -")
            .BorderColor(Color.Purple3)
            .Expand();
    }

    internal static Panel BuildStatusPanel(AdventureShellSnapshot snapshot, AdventurePoint player, string statusMessage, string? composeRole)
    {
        var world = BuildWorld(snapshot);
        var nearby = FindAdjacentDesk(world, EnsureValidPlayer(snapshot, player));
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(composeRole))
        {
            sb.AppendLine($"[bold]Chatting with[/] [cyan]{Markup.Escape(composeRole)}[/]");
            sb.AppendLine("[dim]Enter sends the message as @role. Esc cancels.[/]");
        }
        else if (nearby is not null)
        {
            sb.AppendLine($"[bold]Near[/] [cyan]{Markup.Escape(nearby.DisplayName)}[/]");
            sb.AppendLine($"[bold]Status:[/] {Markup.Escape(nearby.StatusText)}");
            sb.AppendLine("[dim]Press Enter to talk.[/]");
        }
        else
        {
            sb.AppendLine("[bold]Explore the office[/]");
            sb.AppendLine("[dim]Arrow keys move. Walk next to a desk and press Enter.[/]");
        }

        sb.AppendLine($"[dim]{Markup.Escape(statusMessage)}[/]");

        if (world.HiddenRoles.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"[dim]+{world.HiddenRoles.Count} role(s) off-screen:[/] {Markup.Escape(string.Join(", ", world.HiddenRoles.Take(4)))}");
        }

        sb.AppendLine();
        sb.AppendLine("[dim]Esc returns to the normal shell.[/]");
        sb.AppendLine("[dim]PgUp/PgDn still scroll the event log.[/]");

        return new Panel(new Markup(sb.ToString().TrimEnd()))
            .Header("- [teal bold]Nearby[/] -")
            .BorderColor(Color.Purple3)
            .Expand();
    }

    internal static Panel BuildInputPanel(string activeInput, int cursorPosition, string? composeRole)
    {
        if (string.IsNullOrWhiteSpace(composeRole))
        {
            return new Panel(new Markup("[dim]Movement mode — use Enter beside a desk to start chatting.[/]"))
                .Header("- [teal bold]adventure[/] -")
                .BorderColor(Color.Purple3)
                .Expand();
        }

        return ShellPanelBuilder.BuildInput($"@{composeRole}> ", activeInput, cursorPosition);
    }

    private static AdventureDeskState ResolveState(AdventureShellSnapshot snapshot, string roleSlug)
    {
        var activeRun = snapshot.Agents
            .Where(agent => string.Equals(agent.RoleSlug, roleSlug, StringComparison.OrdinalIgnoreCase))
            .OrderBy(agent => agent.Status == AgentRunStatus.Running ? 0 : 1)
            .FirstOrDefault();
        if (activeRun is not null)
        {
            return activeRun.Status == AgentRunStatus.Running ? AdventureDeskState.Running : AdventureDeskState.Queued;
        }

        var roadmapItem = snapshot.Roadmap
            .Where(item => string.Equals(item.RoleSlug, roleSlug, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Status switch
            {
                ItemStatus.InProgress => 0,
                ItemStatus.Open => 1,
                ItemStatus.Blocked => 2,
                ItemStatus.Done => 3,
                _ => 4
            })
            .ThenBy(item => item.Id)
            .FirstOrDefault();

        return roadmapItem?.Status switch
        {
            ItemStatus.InProgress => AdventureDeskState.Running,
            ItemStatus.Open => AdventureDeskState.Open,
            ItemStatus.Blocked => AdventureDeskState.Blocked,
            ItemStatus.Done => AdventureDeskState.Done,
            _ => AdventureDeskState.Idle
        };
    }

    private static string ResolveStatusText(AdventureShellSnapshot snapshot, string roleSlug)
    {
        var activeRun = snapshot.Agents
            .Where(agent => string.Equals(agent.RoleSlug, roleSlug, StringComparison.OrdinalIgnoreCase))
            .OrderBy(agent => agent.Status == AgentRunStatus.Running ? 0 : 1)
            .FirstOrDefault();
        if (activeRun is not null)
        {
            var verb = activeRun.Status == AgentRunStatus.Running ? "running" : "queued";
            return $"{verb} #{activeRun.IssueId}";
        }

        var roadmapItem = snapshot.Roadmap
            .Where(item => string.Equals(item.RoleSlug, roleSlug, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Status switch
            {
                ItemStatus.InProgress => 0,
                ItemStatus.Open => 1,
                ItemStatus.Blocked => 2,
                ItemStatus.Done => 3,
                _ => 4
            })
            .ThenBy(item => item.Id)
            .FirstOrDefault();

        return roadmapItem?.Status switch
        {
            ItemStatus.InProgress => $"issue #{roadmapItem.Id} in progress",
            ItemStatus.Open => $"issue #{roadmapItem.Id} ready",
            ItemStatus.Blocked => $"issue #{roadmapItem.Id} blocked",
            ItemStatus.Done => $"issue #{roadmapItem.Id} done",
            _ => "idle"
        };
    }

    private static char[][] CreateBuffer(int width, int height)
    {
        var buffer = new char[height][];
        for (var y = 0; y < height; y++)
        {
            buffer[y] = Enumerable.Repeat(' ', width).ToArray();
        }

        return buffer;
    }

    private static void WriteCentered(char[][] buffer, int y, int centerX, string text)
    {
        if (y < 0 || y >= buffer.Length)
        {
            return;
        }

        var startX = centerX - (text.Length / 2);
        for (var index = 0; index < text.Length; index++)
        {
            Write(buffer, startX + index, y, text[index]);
        }
    }

    private static void Write(char[][] buffer, int x, int y, char value)
    {
        if (y < 0 || y >= buffer.Length || x < 0 || x >= buffer[y].Length)
        {
            return;
        }

        buffer[y][x] = value;
    }

    private static string DeskGlyph(AdventureDeskState state) =>
        state switch
        {
            AdventureDeskState.Running => "[!]",
            AdventureDeskState.Queued => "[?]",
            AdventureDeskState.Open => "[+]",
            AdventureDeskState.Blocked => "[x]",
            AdventureDeskState.Done => "[=]",
            _ => "[ ]"
        };

    private static int GetRolePriority(string roleSlug)
    {
        for (var index = 0; index < RolePriority.Length; index++)
        {
            if (string.Equals(RolePriority[index], roleSlug, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return RolePriority.Length;
    }

    private static AdventurePoint ClampToRoom(AdventurePoint point) =>
        new(Math.Clamp(point.X, 1, RoomWidth - 2), Math.Clamp(point.Y, 1, RoomHeight - 2));

    private static bool IsDeskTile(AdventureWorld world, AdventurePoint point) =>
        world.Desks.Any(desk => desk.Position == point);

    private static int ManhattanDistance(AdventurePoint left, AdventurePoint right) =>
        Math.Abs(left.X - right.X) + Math.Abs(left.Y - right.Y);

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..(maxLength - 3)] + "...";
}
