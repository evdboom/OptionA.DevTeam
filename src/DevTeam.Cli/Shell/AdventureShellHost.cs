using System.Text;
using System.Threading.Channels;
using Spectre.Console;

namespace DevTeam.Cli.Shell;

internal sealed class AdventureSessionState
{
    public AdventurePoint PlayerPosition { get; set; } = AdventureMapRenderer.DefaultSpawn;
    public string? ComposeRole { get; set; }
    public string StatusMessage { get; set; } = "Adventure mode enabled. Walk up to a desk and press Enter.";
    public bool LastModeEnabled { get; set; }
}

internal static class AdventureShellHost
{
    internal const int LeftColumnWidth = 66;
    private const int StatusSize = 10;

    internal static Layout BuildLayoutTree()
    {
        var root = new Layout("Root")
            .SplitRows(
                new Layout("Header").Size(4),
                new Layout("Body"),
                new Layout("Input").Size(6));

        root["Body"].SplitColumns(
            new Layout("Left").Size(LeftColumnWidth),
            new Layout("Right"));

        root["Left"].SplitRows(
            new Layout("Map"),
            new Layout("Status").Size(StatusSize));

        return root;
    }

    internal static void SyncModeState(
        ShellService shell,
        AdventureSessionState session,
        StringBuilder inputBuffer,
        ref int cursorPosition,
        ref int historyCursor,
        ref string savedDraft)
    {
        var enabled = shell.IsAdventureModeEnabled;
        if (enabled == session.LastModeEnabled)
        {
            return;
        }

        if (!enabled)
        {
            session.ComposeRole = null;
            inputBuffer.Clear();
            cursorPosition = 0;
            historyCursor = -1;
            savedDraft = string.Empty;
            session.StatusMessage = "Returned to the normal shell.";
        }
        else
        {
            session.StatusMessage = "Adventure mode enabled. Walk up to a desk and press Enter.";
        }

        session.LastModeEnabled = enabled;
    }

    internal static void UpdateLayout(Layout root, ShellService shell, AdventureSessionState session, string activeInput, int cursorPosition, int scrollOffset)
    {
        var snapshot = shell.AdventureSnapshot;
        session.PlayerPosition = AdventureMapRenderer.EnsureValidPlayer(snapshot, session.PlayerPosition);

        root["Header"].Update(ShellPanelBuilder.BuildHeader(snapshot.Phase, shell.IsLoopRunning));
        root["Map"].Update(AdventureMapRenderer.BuildMapPanel(snapshot, session.PlayerPosition));
        root["Status"].Update(AdventureMapRenderer.BuildStatusPanel(snapshot, session.PlayerPosition, session.StatusMessage, session.ComposeRole));
        root["Input"].Update(AdventureMapRenderer.BuildInputPanel(activeInput, cursorPosition, session.ComposeRole));
        root["Right"].Update(ShellPanelBuilder.BuildProgressPanel(
            shell.Messages,
            scrollOffset,
            termHeightOverride: 0,
            contentWidthOverride: ProgressWidth()));
    }

    internal static void ReadInput(
        AdventureSessionState session,
        StringBuilder inputBuffer,
        ShellService shell,
        ChannelWriter<string> commandWriter,
        ref int cursorPosition,
        ref int historyCursor,
        ref string savedDraft,
        ref int scrollOffset)
    {
        if (Console.IsInputRedirected)
        {
            return;
        }

        while (TerminalMouseScroll.TryReadInputKey(() => Console.KeyAvailable, () => Console.ReadKey(intercept: true), out var key))
        {
            if (TerminalMouseScroll.TryHandleWheel(key, shell.Messages, ref scrollOffset, ProgressWidth()))
            {
                continue;
            }

            if (HandleSharedScrollKeys(key, shell, ref scrollOffset))
            {
                continue;
            }

            if (!shell.IsAdventureModeEnabled)
            {
                return;
            }

            var snapshot = shell.AdventureSnapshot;
            var world = AdventureMapRenderer.BuildWorld(snapshot);
            session.PlayerPosition = AdventureMapRenderer.EnsureValidPlayer(snapshot, session.PlayerPosition);

            if (!string.IsNullOrWhiteSpace(session.ComposeRole))
            {
                HandleComposeInput(session, inputBuffer, commandWriter, key, ref cursorPosition);
                historyCursor = -1;
                savedDraft = string.Empty;
                continue;
            }

            if (key.Key == ConsoleKey.Escape)
            {
                session.StatusMessage = "Returning to the normal shell...";
                commandWriter.TryWrite("/adventure off");
                continue;
            }

            if (key.Key == ConsoleKey.Enter)
            {
                var desk = AdventureMapRenderer.FindAdjacentDesk(world, session.PlayerPosition);
                if (desk is null)
                {
                    session.StatusMessage = "Walk next to a desk before pressing Enter.";
                    continue;
                }

                session.ComposeRole = desk.RoleSlug;
                session.StatusMessage = $"Talking to {desk.DisplayName}.";
                inputBuffer.Clear();
                cursorPosition = 0;
                continue;
            }

            var moved = key.Key switch
            {
                ConsoleKey.UpArrow => new AdventurePoint(0, -1),
                ConsoleKey.DownArrow => new AdventurePoint(0, 1),
                ConsoleKey.LeftArrow => new AdventurePoint(-1, 0),
                ConsoleKey.RightArrow => new AdventurePoint(1, 0),
                _ => default
            };

            if (moved != default)
            {
                session.PlayerPosition = AdventureMapRenderer.MovePlayer(world, session.PlayerPosition, moved.X, moved.Y);
                var nearby = AdventureMapRenderer.FindAdjacentDesk(world, session.PlayerPosition);
                session.StatusMessage = nearby is null
                    ? "Keep exploring."
                    : $"Near {nearby.DisplayName}. Press Enter to talk.";
            }
        }
    }

    private static bool HandleSharedScrollKeys(ConsoleKeyInfo key, ShellService shell, ref int scrollOffset)
    {
        if (key.Key == ConsoleKey.UpArrow && key.Modifiers.HasFlag(ConsoleModifiers.Control))
        {
            scrollOffset = Math.Min(
                scrollOffset + 1,
                ShellPanelBuilder.MaxScrollOffset(shell.Messages, Math.Max(20, Console.WindowHeight), ProgressWidth()));
            return true;
        }

        if (key.Key == ConsoleKey.DownArrow && key.Modifiers.HasFlag(ConsoleModifiers.Control))
        {
            scrollOffset = Math.Max(0, scrollOffset - 1);
            return true;
        }

        if (key.Key == ConsoleKey.PageUp)
        {
            scrollOffset = Math.Min(
                scrollOffset + PageStep(),
                ShellPanelBuilder.MaxScrollOffset(shell.Messages, Math.Max(20, Console.WindowHeight), ProgressWidth()));
            return true;
        }

        if (key.Key == ConsoleKey.PageDown)
        {
            scrollOffset = Math.Max(0, scrollOffset - PageStep());
            return true;
        }

        if (key.Key == ConsoleKey.Home)
        {
            scrollOffset = ShellPanelBuilder.MaxScrollOffset(shell.Messages, Math.Max(20, Console.WindowHeight), ProgressWidth());
            return true;
        }

        if (key.Key == ConsoleKey.End)
        {
            scrollOffset = 0;
            return true;
        }

        return false;
    }

    private static void HandleComposeInput(
        AdventureSessionState session,
        StringBuilder inputBuffer,
        ChannelWriter<string> commandWriter,
        ConsoleKeyInfo key,
        ref int cursorPosition)
    {
        if (key.Key == ConsoleKey.Enter)
        {
            var message = inputBuffer.ToString().Trim();
            if (string.IsNullOrWhiteSpace(message))
            {
                session.StatusMessage = "Type a message first, or press Esc to cancel.";
                return;
            }

            commandWriter.TryWrite($"@{session.ComposeRole} {message}");
            session.StatusMessage = $"Asked {session.ComposeRole}. Watch the desk for the reply.";
            session.ComposeRole = null;
            inputBuffer.Clear();
            cursorPosition = 0;
            return;
        }

        if (key.Key == ConsoleKey.Escape)
        {
            if (inputBuffer.Length > 0)
            {
                inputBuffer.Clear();
                cursorPosition = 0;
                session.StatusMessage = "Draft cleared. Press Esc again to leave the chat.";
                return;
            }

            session.ComposeRole = null;
            session.StatusMessage = "Back to walking.";
            return;
        }

        if (key.Key == ConsoleKey.LeftArrow)
        {
            cursorPosition = Math.Max(0, cursorPosition - 1);
            return;
        }

        if (key.Key == ConsoleKey.RightArrow)
        {
            cursorPosition = Math.Min(inputBuffer.Length, cursorPosition + 1);
            return;
        }

        if (key.Key == ConsoleKey.Backspace)
        {
            if (cursorPosition > 0)
            {
                inputBuffer.Remove(cursorPosition - 1, 1);
                cursorPosition--;
            }

            return;
        }

        if (key.Key == ConsoleKey.Delete)
        {
            if (cursorPosition < inputBuffer.Length)
            {
                inputBuffer.Remove(cursorPosition, 1);
            }

            return;
        }

        if (!char.IsControl(key.KeyChar))
        {
            inputBuffer.Insert(cursorPosition, key.KeyChar);
            cursorPosition++;
        }
    }

    private static int PageStep()
    {
        var terminalHeight = Console.IsOutputRedirected ? ShellPanelBuilder.FallbackTerminalHeight : Math.Max(20, Console.WindowHeight);
        return Math.Max(3, ShellPanelBuilder.ContentRowCount(terminalHeight) / 2);
    }

    private static int ProgressWidth()
    {
        var terminalWidth = Console.IsOutputRedirected ? 120 : Math.Max(40, Console.WindowWidth);
        return Math.Max(20, terminalWidth - LeftColumnWidth - 4);
    }
}
