using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading.Channels;
using DevTeam.Core;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace DevTeam.Cli.Shell;

/// <summary>
/// Hosts the interactive shell using Spectre.Console LiveDisplay with a
/// single stacked frame (header, cycle, progress, input).
/// </summary>
internal static class SpectreShellHost
{
    private const int RefreshMs = 120;

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
        var adventureSession = new AdventureSessionState();
        var normalLayout = BuildLayoutTree();
        var adventureLayout = AdventureShellHost.BuildLayoutTree();
        var lastUiVersion = -1L;
        var lastCursor = -1;
        var lastScrollOffset = -1;
        var lastAdventureMode = false;

        await shell.InitializeAsync();

        // Switch to the alternate screen buffer so the shell feels like a proper TUI app:
        // pre-launch terminal content is hidden and fully restored on exit.
        // This is the same mechanism used by vim, less, htop, etc.
        var useAltScreen = !Console.IsOutputRedirected && !Console.IsInputRedirected;
        if (useAltScreen)
        {
            Console.Write("\x1b[?1049h"); // enter alternate screen
            TerminalMouseScroll.EnableTracking();
        }

        var commandChannel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
        var consumerTask = ConsumeCommandsAsync(commandChannel.Reader, shell, cancellationToken);

        try
        {
            UpdateLayout(normalLayout, shell, string.Empty, 0, scrollOffset);
            IRenderable liveFrame = normalLayout;
            lastUiVersion = -1L;  // Force rebuild on first loop iteration once terminal is measured
            lastCursor = 0;
            lastScrollOffset = scrollOffset;

            await console.Live(liveFrame)
                .Overflow(VerticalOverflow.Crop)
                .Cropping(VerticalOverflowCropping.Top)
                .StartAsync(async context =>
                {
                    context.UpdateTarget(liveFrame);
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var inputText = inputBuffer.ToString();
                        var beforeScrollOffset = scrollOffset;
                        AdventureShellHost.SyncModeState(shell, adventureSession, inputBuffer, ref cursorPosition, ref historyCursor, ref savedDraft);

                        if (shell.IsAdventureModeEnabled)
                        {
                            AdventureShellHost.ReadInput(adventureSession, inputBuffer, shell, commandChannel.Writer, ref cursorPosition, ref historyCursor, ref savedDraft, ref scrollOffset);
                        }
                        else
                        {
                            ReadInput(inputBuffer, shell, commandChannel.Writer, ref cursorPosition, ref historyCursor, ref savedDraft, ref scrollOffset);
                        }

                        var inputChanged = !string.Equals(inputText, inputBuffer.ToString(), StringComparison.Ordinal)
                            || cursorPosition != lastCursor
                            || beforeScrollOffset != scrollOffset;

                        var uiVersion = shell.UiVersion;
                        var adventureMode = shell.IsAdventureModeEnabled;
                        var shouldRender = uiVersion != lastUiVersion
                            || inputChanged
                            || scrollOffset != lastScrollOffset
                            || adventureMode != lastAdventureMode;

                        if (shouldRender)
                        {
                            if (adventureMode)
                            {
                                AdventureShellHost.UpdateLayout(adventureLayout, shell, adventureSession, inputBuffer.ToString(), cursorPosition, scrollOffset);
                                liveFrame = adventureLayout;
                            }
                            else
                            {
                                UpdateLayout(normalLayout, shell, inputBuffer.ToString(), cursorPosition, scrollOffset);
                                liveFrame = normalLayout;
                            }

                            context.UpdateTarget(liveFrame);

                            lastUiVersion = uiVersion;
                            lastCursor = cursorPosition;
                            lastScrollOffset = scrollOffset;
                            lastAdventureMode = adventureMode;
                        }

                        try { await Task.Delay(RefreshMs, cancellationToken); }
                        catch (OperationCanceledException) { break; }
                    }
                });
        }
        finally
        {
            commandChannel.Writer.Complete();
            if (useAltScreen)
            {
                TerminalMouseScroll.DisableTracking();
                Console.Write("\x1b[?1049l"); // restore original screen
            }

            try
            {
                await consumerTask;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                /* normal exit via cancellation */
            }
        }
    }

    private static async Task ConsumeCommandsAsync(ChannelReader<string> reader, ShellService shell, CancellationToken cancellationToken)
    {
        try
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            /* normal exit via cancellation */
        }
    }

    private static Layout BuildLayoutTree() =>
        new Layout("Root")
            .SplitRows(
                new Layout("Header").Size(ShellPanelBuilder.HeaderSize),
                new Layout("Body"),
                new Layout("Input").Size(ShellPanelBuilder.InputSize));

    private static void UpdateLayout(Layout root, ShellService shell, string activeInput, int cursorPosition, int scrollOffset)
    {
        var snapshot = shell.LayoutSnapshot;

        root["Header"].Update(ShellPanelBuilder.BuildHeader(snapshot.Phase, shell.IsLoopRunning, snapshot.CurrentCycle));
        root["Body"].Update(ShellPanelBuilder.BuildProgressPanel(shell.Messages, scrollOffset));
        root["Input"].Update(ShellPanelBuilder.BuildInput(shell.PromptText, activeInput, cursorPosition));
    }

    // ── Input handling ─────────────────────────────────────────────────────────

    [SuppressMessage("Major Code Smell", "S3776", Justification = "Interactive key-handling loop necessarily branches on many key combinations.")]
    private static void ReadInput(StringBuilder inputBuffer, ShellService shell, ChannelWriter<string> commandWriter, ref int cursorPosition, ref int historyCursor, ref string savedDraft, ref int scrollOffset)
    {
        if (Console.IsInputRedirected) return;

        while (TerminalMouseScroll.TryReadInputKey(() => Console.KeyAvailable, () => Console.ReadKey(intercept: true), out var key))
        {
            if (TerminalMouseScroll.TryHandleWheel(key, shell.Messages, ref scrollOffset, ProgressWidth()))
            {
                continue;
            }

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
                var history = shell.GetCommandHistory();
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
                var history = shell.GetCommandHistory();
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
        return Math.Max(20, termWidth - 4);
    }
}
