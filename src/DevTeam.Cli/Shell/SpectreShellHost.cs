using System.Text;
using System.Threading.Channels;
using DevTeam.Core;
using Spectre.Console;

namespace DevTeam.Cli.Shell;

/// <summary>
/// Hosts the interactive shell using Spectre.Console LiveDisplay with a
/// Layout-based dashboard for fixed-region rendering.
/// </summary>
internal static class SpectreShellHost
{
    private const int MaxRoadmapLines = 10;
    private const int RefreshMs = 100;
    private const int HeaderSize = 4;
    private const int InputSize = 6; // 4 content lines max (border top + 4 + border bottom)
    private const int AgentsSize = 8;

    internal static async Task RunAsync(ShellService shell, CancellationToken cancellationToken)
    {
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.TrueColor,
            Interactive = InteractionSupport.Yes,
        });

        var inputBuffer = new StringBuilder();
        var cursorPosition = 0;       // offset within inputBuffer (0 = before first char)
        var historyCursor = -1; // -1 = not navigating history
        var savedDraft = string.Empty; // preserves unsent input while browsing history
        var scrollOffset = 0;  // 0 = auto-follow latest; N = scrolled N lines up

        await shell.InitializeAsync();

        // Switch to the alternate screen buffer so the shell feels like a proper TUI app:
        // pre-launch terminal content is hidden and fully restored on exit.
        // This is the same mechanism used by vim, less, htop, etc.
        var useAltScreen = !Console.IsOutputRedirected && !Console.IsInputRedirected;
        if (useAltScreen)
            Console.Write("\x1b[?1049h"); // enter alternate screen

        var commandChannel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
        var consumerTask = ConsumeCommandsAsync(commandChannel.Reader, shell, cancellationToken);

        try
        {
            // Build the Layout tree ONCE. Reuse the same instance and only
            // call .Update() on the leaf nodes each tick. This guarantees
            // the tree shape and rendered height never change between frames,
            // which is the prerequisite for Live display to overwrite correctly.
            var layout = BuildLayoutTree();
            UpdateLayout(layout, shell, string.Empty, 0, scrollOffset);

            await console.Live(layout)
                .Overflow(VerticalOverflow.Crop)
                .Cropping(VerticalOverflowCropping.Top)
                .StartAsync(async context =>
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        ReadInput(inputBuffer, shell, commandChannel.Writer, ref cursorPosition, ref historyCursor, ref savedDraft, ref scrollOffset);
                        UpdateLayout(layout, shell, inputBuffer.ToString(), cursorPosition, scrollOffset);
                        context.UpdateTarget(layout);

                        try { await Task.Delay(RefreshMs, cancellationToken); }
                        catch (OperationCanceledException) { break; }
                    }
                });
        }
        finally
        {
            commandChannel.Writer.Complete();
            await consumerTask;
            if (useAltScreen)
                Console.Write("\x1b[?1049l"); // restore original screen
        }
    }

    private static async Task ConsumeCommandsAsync(ChannelReader<string> reader, ShellService shell, CancellationToken cancellationToken)
    {
        await foreach (var command in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                await shell.ProcessInputAsync(command).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                shell.AddError($"Command failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Creates the Layout tree once. The same instance is reused every frame.
    /// Only leaf .Update() calls change — never the tree structure.
    /// </summary>
    private static Layout BuildLayoutTree()
    {
        var root = new Layout("Root")
            .SplitRows(
                new Layout("Header").Size(HeaderSize),
                new Layout("Body"),
                new Layout("Input").Size(InputSize));

        root["Body"].SplitColumns(
            new Layout("Left").Size(ShellPanelBuilder.LeftColumnWidth),
            new Layout("Right"));

        root["Left"].SplitRows(
            new Layout("Agents").Size(AgentsSize),
            new Layout("Roadmap"));

        return root;
    }

    /// <summary>Updates every leaf panel in the pre-built layout tree.</summary>
    private static void UpdateLayout(Layout root, ShellService shell, string activeInput, int cursorPosition, int scrollOffset)
    {
        var snapshot = shell.LayoutSnapshot;
        var messages = shell.Messages;

        root["Header"].Update(ShellPanelBuilder.BuildHeader(snapshot.Phase, shell.IsLoopRunning));
        root["Input"].Update(ShellPanelBuilder.BuildInput(shell.PromptText, activeInput, cursorPosition));

        if (snapshot.ShowMiddleRow)
        {
            root["Agents"].Update(ShellPanelBuilder.BuildAgentsPanel(snapshot));

            // Compute how many roadmap lines can fit in the remaining left-column height
            // without overflowing: termHeight - header - input - agents slot - panel borders.
            var th = Console.IsOutputRedirected ? ShellPanelBuilder.FallbackTerminalHeight : Math.Max(20, Console.WindowHeight);
            var roadmapBudget = Math.Max(2, th - HeaderSize - InputSize - AgentsSize - 3);
            root["Roadmap"].Update(ShellPanelBuilder.BuildRoadmapPanel(snapshot, Math.Min(MaxRoadmapLines, roadmapBudget)));
        }
        else
        {
            root["Agents"].Update(ShellPanelBuilder.BuildEmptyPanel("Agents"));
            root["Roadmap"].Update(ShellPanelBuilder.BuildEmptyPanel("Roadmap"));
        }

        root["Right"].Update(ShellPanelBuilder.BuildProgressPanel(messages, scrollOffset));
    }

    // ── Input handling ─────────────────────────────────────────────────────────

    private static void ReadInput(StringBuilder inputBuffer, ShellService shell, ChannelWriter<string> commandWriter, ref int cursorPosition, ref int historyCursor, ref string savedDraft, ref int scrollOffset)
    {
        if (Console.IsInputRedirected) return;

        while (Console.KeyAvailable)
        {
            var key = Console.ReadKey(intercept: true);
            var text = inputBuffer.ToString();

            // PageUp → scroll progress pane up (older lines)
            if (key.Key == ConsoleKey.PageUp)
            {
                scrollOffset = Math.Min(
                    scrollOffset + PageStep(),
                    ShellPanelBuilder.MaxScrollOffset(shell.Messages, Math.Max(20, Console.WindowHeight), ProgressWidth()));
                continue;
            }

            // PageDown → scroll progress pane down (newer lines)
            if (key.Key == ConsoleKey.PageDown)
            {
                scrollOffset = Math.Max(0, scrollOffset - PageStep());
                continue;
            }

            // Home → cursor to start of current line (or scroll to top when buffer empty)
            if (key.Key == ConsoleKey.Home)
            {
                if (text.Length > 0)
                {
                    cursorPosition = InputCursorNavigation.GetLineStart(text, cursorPosition);
                }
                else
                {
                    scrollOffset = ShellPanelBuilder.MaxScrollOffset(shell.Messages, Math.Max(20, Console.WindowHeight), ProgressWidth());
                }
                continue;
            }

            // End → cursor to end of current line (or scroll to bottom when buffer empty)
            if (key.Key == ConsoleKey.End)
            {
                if (text.Length > 0)
                {
                    cursorPosition = InputCursorNavigation.GetLineEnd(text, cursorPosition);
                }
                else
                {
                    scrollOffset = 0;
                }
                continue;
            }

            // Shift+Enter or Ctrl+J → insert newline at cursor
            if ((key.Key == ConsoleKey.Enter && key.Modifiers.HasFlag(ConsoleModifiers.Shift))
                || (key.Key == ConsoleKey.J && key.Modifiers.HasFlag(ConsoleModifiers.Control)))
            {
                inputBuffer.Insert(cursorPosition, '\n');
                cursorPosition++;
                historyCursor = -1;
                continue;
            }

            // Enter → submit
            if (key.Key == ConsoleKey.Enter)
            {
                var command = inputBuffer.ToString().Trim();
                inputBuffer.Clear();
                cursorPosition = 0;
                historyCursor = -1;
                savedDraft = string.Empty;
                if (!string.IsNullOrWhiteSpace(command))
                    commandWriter.TryWrite(command);
                continue;
            }

            // Up arrow — context-aware: line 0 = history, deeper rows = move cursor up
            if (key.Key == ConsoleKey.UpArrow)
            {
                var (cursorRow, cursorCol) = InputCursorNavigation.GetCursorRowCol(text, cursorPosition);
                if (cursorRow > 0)
                {
                    cursorPosition = InputCursorNavigation.GetPositionAtRowCol(text, cursorRow - 1, cursorCol);
                    continue;
                }

                // On line 0: navigate history (option A)
                var history = shell.CommandHistory;
                if (history.Count == 0) continue;
                if (historyCursor == -1)
                {
                    savedDraft = inputBuffer.ToString();
                    historyCursor = history.Count - 1;
                }
                else if (historyCursor > 0)
                {
                    historyCursor--;
                }
                inputBuffer.Clear();
                inputBuffer.Append(history[historyCursor]);
                cursorPosition = inputBuffer.Length;
                continue;
            }

            // Down arrow — context-aware: below last line = history, otherwise move cursor down
            if (key.Key == ConsoleKey.DownArrow)
            {
                var (cursorRow, cursorCol) = InputCursorNavigation.GetCursorRowCol(text, cursorPosition);
                var totalRows = InputCursorNavigation.CountRows(text);
                if (cursorRow < totalRows - 1)
                {
                    cursorPosition = InputCursorNavigation.GetPositionAtRowCol(text, cursorRow + 1, cursorCol);
                    continue;
                }

                // On last line: navigate history forward
                if (historyCursor == -1) continue;
                var history = shell.CommandHistory;
                if (historyCursor < history.Count - 1)
                {
                    historyCursor++;
                    inputBuffer.Clear();
                    inputBuffer.Append(history[historyCursor]);
                    cursorPosition = inputBuffer.Length;
                }
                else
                {
                    historyCursor = -1;
                    inputBuffer.Clear();
                    inputBuffer.Append(savedDraft);
                    cursorPosition = inputBuffer.Length;
                    savedDraft = string.Empty;
                }
                continue;
            }

            // Left arrow → move cursor left (no wrapping across lines)
            if (key.Key == ConsoleKey.LeftArrow)
            {
                if (key.Modifiers.HasFlag(ConsoleModifiers.Control))
                    cursorPosition = InputCursorNavigation.WordJumpLeft(text, cursorPosition);
                else
                    cursorPosition = Math.Max(0, cursorPosition - 1);
                continue;
            }

            // Right arrow → move cursor right (no wrapping across lines)
            if (key.Key == ConsoleKey.RightArrow)
            {
                if (key.Modifiers.HasFlag(ConsoleModifiers.Control))
                    cursorPosition = InputCursorNavigation.WordJumpRight(text, cursorPosition);
                else
                    cursorPosition = Math.Min(inputBuffer.Length, cursorPosition + 1);
                continue;
            }

            // Delete → remove char at cursor (no cursor movement)
            if (key.Key == ConsoleKey.Delete)
            {
                if (cursorPosition < inputBuffer.Length)
                    inputBuffer.Remove(cursorPosition, 1);
                continue;
            }

            // Backspace → remove char before cursor
            if (key.Key == ConsoleKey.Backspace)
            {
                if (cursorPosition > 0)
                {
                    inputBuffer.Remove(cursorPosition - 1, 1);
                    cursorPosition--;
                }
                historyCursor = -1;
                continue;
            }

            if (key.Key == ConsoleKey.Escape)
            {
                inputBuffer.Clear();
                cursorPosition = 0;
                historyCursor = -1;
                savedDraft = string.Empty;
                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                inputBuffer.Insert(cursorPosition, key.KeyChar);
                cursorPosition++;
                historyCursor = -1;
            }
        }
    }

    /// <summary>
    /// One page = half the visible content rows in the progress panel.
    /// Using half (rather than full ContentRowCount) guarantees consecutive page positions
    /// overlap in flat-line space even when many lines wrap to 2–3 terminal rows each —
    /// without overlap, wrapped-line content at panel-chunk boundaries would be unreachable.
    /// Minimum of 3 prevents degenerate step size at very small terminals.
    /// </summary>
    private static int PageStep()
    {
        var th = Console.IsOutputRedirected ? ShellPanelBuilder.FallbackTerminalHeight : Math.Max(20, Console.WindowHeight);
        return Math.Max(3, ShellPanelBuilder.ContentRowCount(th) / 2);
    }

    /// <summary>
    /// The usable content width of the Progress panel — same formula used in
    /// <see cref="ShellPanelBuilder.BuildProgressPanel"/> to estimate line heights.
    /// </summary>
    private static int ProgressWidth()
    {
        var termWidth = Console.IsOutputRedirected ? 120 : Math.Max(40, Console.WindowWidth);
        return Math.Max(20, termWidth - ShellPanelBuilder.LeftColumnWidth - 4);
    }
}