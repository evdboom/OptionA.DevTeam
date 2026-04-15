using System.Text;
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

        try
        {
            // Build the Layout tree ONCE. Reuse the same instance and only
            // call .Update() on the leaf nodes each tick. This guarantees
            // the tree shape and rendered height never change between frames,
            // which is the prerequisite for Live display to overwrite correctly.
            var layout = BuildLayoutTree();
            UpdateLayout(layout, shell, string.Empty, scrollOffset);

            await console.Live(layout)
                .Overflow(VerticalOverflow.Crop)
                .Cropping(VerticalOverflowCropping.Top)
                .StartAsync(async context =>
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        ReadInput(inputBuffer, shell, ref historyCursor, ref savedDraft, ref scrollOffset);
                        UpdateLayout(layout, shell, inputBuffer.ToString(), scrollOffset);
                        context.UpdateTarget(layout);

                        try { await Task.Delay(RefreshMs, cancellationToken); }
                        catch (OperationCanceledException) { break; }
                    }
                });
        }
        finally
        {
            if (useAltScreen)
                Console.Write("\x1b[?1049l"); // restore original screen
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
    private static void UpdateLayout(Layout root, ShellService shell, string activeInput, int scrollOffset)
    {
        var snapshot = shell.LayoutSnapshot;
        var messages = shell.Messages;

        root["Header"].Update(ShellPanelBuilder.BuildHeader(snapshot.Phase, shell.IsLoopRunning));
        root["Input"].Update(ShellPanelBuilder.BuildInput(shell.PromptText, activeInput));

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

    private static void ReadInput(StringBuilder inputBuffer, ShellService shell, ref int historyCursor, ref string savedDraft, ref int scrollOffset)
    {
        if (Console.IsInputRedirected) return;

        while (Console.KeyAvailable)
        {
            var key = Console.ReadKey(intercept: true);

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

            // End → jump back to auto-follow latest
            if (key.Key == ConsoleKey.End)
            {
                scrollOffset = 0;
                continue;
            }

            // Home → jump to oldest visible content (exactly fills panel, no blank space)
            if (key.Key == ConsoleKey.Home)
            {
                scrollOffset = ShellPanelBuilder.MaxScrollOffset(shell.Messages, Math.Max(20, Console.WindowHeight), ProgressWidth());
                continue;
            }

            // Shift+Enter or Ctrl+J → insert newline into the buffer
            // (Ctrl+J is more reliable across terminal emulators than Shift+Enter)
            if ((key.Key == ConsoleKey.Enter && key.Modifiers.HasFlag(ConsoleModifiers.Shift))
                || (key.Key == ConsoleKey.J && key.Modifiers.HasFlag(ConsoleModifiers.Control)))
            {
                inputBuffer.Append('\n');
                historyCursor = -1;
                continue;
            }

            // Enter → submit
            if (key.Key == ConsoleKey.Enter)
            {
                var command = inputBuffer.ToString().Trim();
                inputBuffer.Clear();
                historyCursor = -1;
                savedDraft = string.Empty;
                if (!string.IsNullOrWhiteSpace(command))
                    _ = Task.Run(() => shell.ProcessInputAsync(command));
                continue;
            }

            // Up arrow → older history entry
            if (key.Key == ConsoleKey.UpArrow)
            {
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
                continue;
            }

            // Down arrow → newer history entry, or restore draft
            if (key.Key == ConsoleKey.DownArrow)
            {
                if (historyCursor == -1) continue;
                var history = shell.CommandHistory;
                if (historyCursor < history.Count - 1)
                {
                    historyCursor++;
                    inputBuffer.Clear();
                    inputBuffer.Append(history[historyCursor]);
                }
                else
                {
                    historyCursor = -1;
                    inputBuffer.Clear();
                    inputBuffer.Append(savedDraft);
                    savedDraft = string.Empty;
                }
                continue;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (inputBuffer.Length > 0) inputBuffer.Length--;
                historyCursor = -1;
                continue;
            }

            if (key.Key == ConsoleKey.Escape)
            {
                inputBuffer.Clear();
                historyCursor = -1;
                savedDraft = string.Empty;
                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                inputBuffer.Append(key.KeyChar);
                historyCursor = -1;
            }
        }
    }

    /// <summary>
    /// One page = the visible content rows in the progress panel (budget minus 2 hint rows).
    /// Computed dynamically so it matches the actual rendered viewport height.
    /// </summary>
    private static int PageStep()
    {
        var th = Console.IsOutputRedirected ? ShellPanelBuilder.FallbackTerminalHeight : Math.Max(20, Console.WindowHeight);
        return ShellPanelBuilder.ContentRowCount(th);
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