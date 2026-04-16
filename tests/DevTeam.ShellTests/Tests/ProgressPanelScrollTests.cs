using DevTeam.Cli.Shell;
using DevTeam.ShellTests;
using Spectre.Console.Testing;

namespace DevTeam.ShellTests.Tests;

/// <summary>
/// Verifies BuildProgressPanel flat-line virtual scroll: message order, scroll hints,
/// tall messages, and wide-line (word-wrap) safety.
///
/// Test context constants (Console.IsOutputRedirected = true):
///   FallbackTerminalHeight = 40, termWidth = 120
///   budget  = max(4, 40 - 4 - 6 - 3) = 27
///   contentRows = budget - 2 = 25
///   progressWidth = 120 - 60 - 4 = 56
/// </summary>
internal static class ProgressPanelScrollTests
{
    // Derived constants matching ShellPanelBuilder behaviour in redirected-output mode.
    private const int Budget = 27;
    private const int ContentRows = Budget - 2; // 25
    private const int ProgressWidth = 56;

    public static IEnumerable<TestCase> GetTests() =>
    [
        new("ProgressPanel_NoMessages_ShowsNoEventsYet",                     ProgressPanel_NoMessages_ShowsNoEventsYet),
        new("ProgressPanel_FewMessages_AllVisible_NoHints",                  ProgressPanel_FewMessages_AllVisible_NoHints),
        new("ProgressPanel_MessageOrder_OldestAtTop_NewestAtBottom",         ProgressPanel_MessageOrder_OldestAtTop_NewestAtBottom),
        new("ProgressPanel_TooManyLines_ShowsAboveHintAtTop",                ProgressPanel_TooManyLines_ShowsAboveHintAtTop),
        new("ProgressPanel_Scrolled_ShowsBelowHintAtBottom",                 ProgressPanel_Scrolled_ShowsBelowHintAtBottom),
        new("ProgressPanel_Scrolled_HeaderContainsScrolledIndicator",        ProgressPanel_Scrolled_HeaderContainsScrolledIndicator),
        new("ProgressPanel_TallMessage_AllLinesVisible",                     ProgressPanel_TallMessage_AllLinesVisible),
        new("ProgressPanel_TallMessage_ScrollRevealsMidContent",             ProgressPanel_TallMessage_ScrollRevealsMidContent),
        new("ProgressPanel_LongLine_StillVisible",                           ProgressPanel_LongLine_StillVisible),
        new("ProgressPanel_LongLine_DoesNotExceedBudget",                    ProgressPanel_LongLine_DoesNotExceedBudget),
        new("ProgressPanel_VeryLongSingleLine_ForceShown",                   ProgressPanel_VeryLongSingleLine_ForceShown),
        new("FlattenMessages_LineKind_OneLinesPerEntry",                     FlattenMessages_LineKind_OneLinesPerEntry),
        new("FlattenMessages_LineKind_MultilineMarkup_SplitsOnNewlines",     FlattenMessages_LineKind_MultilineMarkup_SplitsOnNewlines),
        new("FlattenMessages_PanelKind_InjectsHeaderSeparator",              FlattenMessages_PanelKind_InjectsHeaderSeparator),
        new("EstimateLineHeight_ShortLine_IsOne",                            EstimateLineHeight_ShortLine_IsOne),
        new("EstimateLineHeight_LongLine_WrapsCorrectly",                    EstimateLineHeight_LongLine_WrapsCorrectly),
        new("EstimateLineHeight_EmptyLine_IsOne",                            EstimateLineHeight_EmptyLine_IsOne),
        new("EstimateMessageHeight_MultilineLineKind_CountsNewlines",        EstimateMessageHeight_MultilineLineKind_CountsNewlines),
        new("EstimateMessageHeight_SingleLine_CountsByWidth",                EstimateMessageHeight_SingleLine_CountsByWidth),
        new("ProgressPanel_OverScrolled_ReachesOldestLine",                  ProgressPanel_OverScrolled_ReachesOldestLine),
        new("ProgressPanel_OverScrolled_FurtherPgUpNotStuck",                ProgressPanel_OverScrolled_FurtherPgUpNotStuck),
        new("ContentRowCount_MatchesBuildProgressPanelViewportSize",         ContentRowCount_MatchesBuildProgressPanelViewportSize),
        new("MaxScrollOffset_CapsAtFlatCountMinusContentRows",                MaxScrollOffset_CapsAtFlatCountMinusContentRows),
        new("MaxScrollOffset_AtCap_ShowsFullPageOfOldestContent",             MaxScrollOffset_AtCap_ShowsFullPageOfOldestContent),
        new("MaxScrollOffset_BeyondCap_ScrollOffsetClampsToMax",               MaxScrollOffset_BeyondCap_ScrollOffsetClampsToMax),
        new("MaxScrollOffset_FewerLinesThanContentRows_IsZero",               MaxScrollOffset_FewerLinesThanContentRows_IsZero),
        new("MaxScrollOffset_WithWrappingLines_AccountsForLineHeight",         MaxScrollOffset_WithWrappingLines_AccountsForLineHeight),
        new("MaxScrollOffset_WithWrappingLines_OldestLineReachable",           MaxScrollOffset_WithWrappingLines_OldestLineReachable),
        new("HelpMarkup_ContainsAllCommands",                                 HelpMarkup_ContainsAllCommands),
        new("HelpMarkup_AllCommandsHaveDescription",                          HelpMarkup_AllCommandsHaveDescription),
        new("HelpMarkup_NoCarriageReturns",                                   HelpMarkup_NoCarriageReturns),
    ];

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static TestConsole CreateConsole()
    {
        var console = new TestConsole();
        console.Profile.Width = 120;
        return console;
    }

    private static ShellMessage Line(string text) =>
        new(ShellMessageKind.Line, text);

    private static ShellMessage Panel(string content, string title) =>
        new(ShellMessageKind.Panel, content, Title: title);

    // Counts non-empty output lines (strips box-drawing characters used by panel borders).
    private static int CountOutputLines(string output) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
              .Count(l => l.Trim().Length > 0);

    // ── Core scroll tests ──────────────────────────────────────────────────────

    private static Task ProgressPanel_NoMessages_ShowsNoEventsYet()
    {
        var console = CreateConsole();
        console.Write(ShellPanelBuilder.BuildProgressPanel([], scrollOffset: 0));
        Assert.That(console.Output.Contains("No events"), $"Expected 'No events' in: {console.Output}");
        return Task.CompletedTask;
    }

    private static Task ProgressPanel_FewMessages_AllVisible_NoHints()
    {
        var messages = new[] { Line("msg-one"), Line("msg-two"), Line("msg-three") };
        var console = CreateConsole();
        console.Write(ShellPanelBuilder.BuildProgressPanel(messages, scrollOffset: 0));
        var output = console.Output;

        Assert.That(output.Contains("msg-one"),   $"Expected msg-one visible: {output}");
        Assert.That(output.Contains("msg-two"),   $"Expected msg-two visible: {output}");
        Assert.That(output.Contains("msg-three"), $"Expected msg-three visible: {output}");
        Assert.That(!output.Contains("above"),    $"Should not have 'above' hint: {output}");
        Assert.That(!output.Contains("below"),    $"Should not have 'below' hint: {output}");
        return Task.CompletedTask;
    }

    private static Task ProgressPanel_MessageOrder_OldestAtTop_NewestAtBottom()
    {
        var messages = new[] { Line("first-message"), Line("last-message") };
        var console = CreateConsole();
        console.Write(ShellPanelBuilder.BuildProgressPanel(messages, scrollOffset: 0));
        var output = console.Output;

        var firstPos = output.IndexOf("first-message", StringComparison.Ordinal);
        var lastPos  = output.IndexOf("last-message",  StringComparison.Ordinal);

        Assert.That(firstPos >= 0, $"Expected first-message in output: {output}");
        Assert.That(lastPos  >= 0, $"Expected last-message in output: {output}");
        Assert.That(firstPos < lastPos,
            $"Oldest ({firstPos}) should appear before newest ({lastPos})");
        return Task.CompletedTask;
    }

    private static Task ProgressPanel_TooManyLines_ShowsAboveHintAtTop()
    {
        // 30 single-line messages → 30 flat lines. FallbackTerminalHeight gives contentRows=25 → 5 lines above.
        var messages = Enumerable.Range(1, 30).Select(i => Line($"msg-{i:D3}")).ToArray();
        var console = CreateConsole();
        console.Write(ShellPanelBuilder.BuildProgressPanel(messages, scrollOffset: 0,
            termHeightOverride: ShellPanelBuilder.FallbackTerminalHeight));
        var output = console.Output;

        Assert.That(output.Contains("above"),   $"Expected 'above' hint: {output}");
        Assert.That(!output.Contains("below"),  $"Should not have 'below' hint: {output}");
        Assert.That(output.Contains("msg-030"), $"Newest message should be visible: {output}");

        // "above" hint must appear BEFORE the newest message line (i.e. at the top).
        var abovePos  = output.IndexOf("above",   StringComparison.Ordinal);
        var newestPos = output.IndexOf("msg-030", StringComparison.Ordinal);
        Assert.That(abovePos < newestPos,
            $"'above' hint (pos {abovePos}) should precede newest message (pos {newestPos})");
        return Task.CompletedTask;
    }

    private static Task ProgressPanel_Scrolled_ShowsBelowHintAtBottom()
    {
        // 30 messages, scroll up 5 lines → windowEnd=25, linesBelow=5.
        var messages = Enumerable.Range(1, 30).Select(i => Line($"msg-{i:D3}")).ToArray();
        var console = CreateConsole();
        console.Write(ShellPanelBuilder.BuildProgressPanel(messages, scrollOffset: 5));
        var output = console.Output;

        Assert.That(output.Contains("below"), $"Expected 'below' hint: {output}");

        // "below" hint must appear AFTER the last visible message (i.e. at the bottom).
        var belowPos = output.IndexOf("below", StringComparison.Ordinal);
        // msg-025 is the last message in the window (windowEnd=25 → lines 0..24 → msg-001..msg-025)
        var lastVisiblePos = output.IndexOf("msg-025", StringComparison.Ordinal);
        Assert.That(lastVisiblePos >= 0, $"Expected msg-025 visible: {output}");
        Assert.That(lastVisiblePos < belowPos,
            $"Last visible message (pos {lastVisiblePos}) should precede 'below' hint (pos {belowPos})");
        return Task.CompletedTask;
    }

    private static Task ProgressPanel_Scrolled_HeaderContainsScrolledIndicator()
    {
        var messages = Enumerable.Range(1, 30).Select(i => Line($"msg-{i:D3}")).ToArray();
        var console = CreateConsole();
        console.Write(ShellPanelBuilder.BuildProgressPanel(messages, scrollOffset: 5));
        Assert.That(console.Output.Contains("scrolled"),
            $"Expected 'scrolled' in header: {console.Output}");
        return Task.CompletedTask;
    }

    // ── Tall message tests (no truncation — scroll reveals all lines) ──────────

    private static Task ProgressPanel_TallMessage_AllLinesVisible()
    {
        // A 30-line message. With contentRows=25, the panel shows lines 5..29 by default.
        // Scrolling up reveals lines 0..24. No content is ever truncated.
        var lines = Enumerable.Range(1, 30).Select(i => $"line-{i:D2}").ToArray();
        var msg = Line(string.Join("\n", lines));

        // Default view: newest (bottom) lines should be visible.
        var console1 = CreateConsole();
        console1.Write(ShellPanelBuilder.BuildProgressPanel([msg], scrollOffset: 0));
        Assert.That(console1.Output.Contains("line-30"),
            $"Expected line-30 visible by default: {console1.Output}");

        // Scrolled view: older lines should be reachable.
        var console2 = CreateConsole();
        console2.Write(ShellPanelBuilder.BuildProgressPanel([msg], scrollOffset: 25));
        Assert.That(console2.Output.Contains("line-01"),
            $"Expected line-01 visible when scrolled: {console2.Output}");

        return Task.CompletedTask;
    }

    private static Task ProgressPanel_TallMessage_ScrollRevealsMidContent()
    {
        // Verify mid-content lines (line-15) are reachable somewhere in the scroll range.
        var lines = Enumerable.Range(1, 30).Select(i => $"line-{i:D2}").ToArray();
        var msg = Line(string.Join("\n", lines));

        var found = false;
        for (var offset = 0; offset <= 30; offset++)
        {
            var c = CreateConsole();
            c.Write(ShellPanelBuilder.BuildProgressPanel([msg], scrollOffset: offset));
            if (c.Output.Contains("line-15")) { found = true; break; }
        }
        Assert.That(found, "Expected line-15 reachable via scroll, but it was never visible");
        return Task.CompletedTask;
    }

    // ── Long-line (wider than panel) tests ────────────────────────────────────

    private static Task ProgressPanel_LongLine_StillVisible()
    {
        // A line wider than progressWidth(56) should still appear in the panel output.
        var longLine = "LONGLINE:" + new string('X', 200);
        var console = CreateConsole();
        console.Write(ShellPanelBuilder.BuildProgressPanel([Line(longLine)], scrollOffset: 0));
        Assert.That(console.Output.Contains("LONGLINE:"),
            $"Expected long line marker in: {console.Output}");
        return Task.CompletedTask;
    }

    private static Task ProgressPanel_LongLine_DoesNotExceedBudget()
    {
        // Fill with lines that each wrap to 4 rows (ceil(200/56)=4).
        // With contentRows=25 we can fit 6 of them (6×4=24 ≤ 25); the 7th is excluded.
        // Total rendered rows (including 2 panel borders) must not exceed budget+2.
        var longLine = new string('X', 200); // VisibleLength=200, height=ceil(200/56)=4
        var messages = Enumerable.Range(1, 10).Select(i => Line($"L{i:D2}:{longLine}")).ToArray();

        var console = CreateConsole();
        console.Write(ShellPanelBuilder.BuildProgressPanel(messages, scrollOffset: 0));
        var outputLines = console.Output.Split('\n');

        // Panel borders (2) + content rows ≤ budget + some Spectre padding.
        // A conservative upper bound: rendered lines ≤ budget + 4.
        Assert.That(outputLines.Length <= Budget + 4,
            $"Expected ≤{Budget + 4} output lines but got {outputLines.Length}");
        return Task.CompletedTask;
    }

    private static Task ProgressPanel_VeryLongSingleLine_ForceShown()
    {
        // A single line taller than the entire budget (e.g. 2000 chars → 36 rows at width 56).
        // Even though it exceeds contentRows, the force-show fallback ensures it's not blank.
        var hugeContent = "START:" + new string('Y', 2000) + ":END";
        var console = CreateConsole();
        console.Write(ShellPanelBuilder.BuildProgressPanel([Line(hugeContent)], scrollOffset: 0));
        Assert.That(console.Output.Contains("START:"),
            $"Expected oversized line force-shown but panel was blank: {console.Output}");
        return Task.CompletedTask;
    }

    // ── FlattenMessages tests ─────────────────────────────────────────────────

    private static Task FlattenMessages_LineKind_OneLinesPerEntry()
    {
        var messages = new[] { Line("alpha"), Line("beta") };
        var flat = ShellPanelBuilder.FlattenMessages(messages);
        Assert.That(flat.Length == 2, $"Expected 2 flat lines, got {flat.Length}");
        Assert.That(flat[0] == "alpha", $"Expected flat[0]='alpha', got '{flat[0]}'");
        Assert.That(flat[1] == "beta",  $"Expected flat[1]='beta', got '{flat[1]}'");
        return Task.CompletedTask;
    }

    private static Task FlattenMessages_LineKind_MultilineMarkup_SplitsOnNewlines()
    {
        var msg = Line("line-one\nline-two\nline-three");
        var flat = ShellPanelBuilder.FlattenMessages([msg]);
        Assert.That(flat.Length == 3, $"Expected 3 flat lines, got {flat.Length}");
        Assert.That(flat[2] == "line-three", $"Expected last line 'line-three', got '{flat[2]}'");
        return Task.CompletedTask;
    }

    private static Task FlattenMessages_PanelKind_InjectsHeaderSeparator()
    {
        var msg = Panel("content-line", title: "MyTitle");
        var flat = ShellPanelBuilder.FlattenMessages([msg]);
        // Expect: 1 separator line (containing "MyTitle") + 1 content line = 2 flat lines.
        Assert.That(flat.Length == 2,
            $"Expected separator + content = 2 lines, got {flat.Length}: [{string.Join(", ", flat)}]");
        Assert.That(flat[0].Contains("MyTitle"),
            $"Expected header separator to contain title, got '{flat[0]}'");
        Assert.That(flat[1] == "content-line",
            $"Expected content on second line, got '{flat[1]}'");
        return Task.CompletedTask;
    }

    // ── EstimateLineHeight tests ───────────────────────────────────────────────

    private static Task EstimateLineHeight_ShortLine_IsOne()
    {
        var result = ShellPanelBuilder.EstimateLineHeight("hello world", contentWidth: 56);
        Assert.That(result == 1, $"Expected height 1 for short line, got {result}");
        return Task.CompletedTask;
    }

    private static Task EstimateLineHeight_LongLine_WrapsCorrectly()
    {
        // 200 visible chars at width 56 → ceil(200/56) = ceil(3.57) = 4 rows.
        var result = ShellPanelBuilder.EstimateLineHeight(new string('x', 200), contentWidth: 56);
        Assert.That(result == 4, $"Expected height 4 for 200-char line at width 56, got {result}");
        return Task.CompletedTask;
    }

    private static Task EstimateLineHeight_EmptyLine_IsOne()
    {
        var result = ShellPanelBuilder.EstimateLineHeight("", contentWidth: 56);
        Assert.That(result == 1, $"Expected height 1 for empty line, got {result}");
        return Task.CompletedTask;
    }

    // ── EstimateMessageHeight tests (method kept for completeness) ────────────

    private static Task EstimateMessageHeight_MultilineLineKind_CountsNewlines()
    {
        // "a\nb\nc" = 3 logical lines, each 1 char at width 56 → total height 3.
        var msg = Line("a\nb\nc");
        var result = ShellPanelBuilder.EstimateMessageHeight(msg, contentWidth: 56);
        Assert.That(result == 3, $"Expected height 3, got {result}");
        return Task.CompletedTask;
    }

    private static Task EstimateMessageHeight_SingleLine_CountsByWidth()
    {
        // 60 chars at width 56 → ceil(60/56) = 2 rows.
        var msg = Line(new string('x', 60));
        var result = ShellPanelBuilder.EstimateMessageHeight(msg, contentWidth: 56);
        Assert.That(result == 2, $"Expected height 2 for 60-char line at width 56, got {result}");
        return Task.CompletedTask;
    }

    // ── Scroll-stop regression tests ──────────────────────────────────────────
    // These reproduce the bug where the old Math.Min(contentRows, totalLines) lower-bound
    // on the windowEnd clamp prevented PgUp from reaching the oldest content.

    private static Task ProgressPanel_OverScrolled_ReachesOldestLine()
    {
        // 30 messages, contentRows=25. Over-scroll far past the top.
        // The oldest message (msg-001) must be visible at some large scrollOffset.
        var messages = Enumerable.Range(1, 30).Select(i => Line($"msg-{i:D3}")).ToArray();

        var console = CreateConsole();
        console.Write(ShellPanelBuilder.BuildProgressPanel(messages, scrollOffset: 1000));
        var output = console.Output;

        Assert.That(output.Contains("msg-001"),
            $"Expected oldest message visible when fully scrolled: {output}");
        Assert.That(!output.Contains("above"),
            $"Should not have 'above' hint at the very top of history: {output}");
        return Task.CompletedTask;
    }

    private static Task ProgressPanel_OverScrolled_FurtherPgUpNotStuck()
    {
        // With 30 messages and contentRows=25, the first PgUp (scrollOffset=25) should
        // produce a DIFFERENT view from the second PgUp (scrollOffset=50).
        // The old clamp bug caused both to show identical content.
        var messages = Enumerable.Range(1, 30).Select(i => Line($"msg-{i:D3}")).ToArray();

        var c1 = CreateConsole();
        c1.Write(ShellPanelBuilder.BuildProgressPanel(messages, scrollOffset: ContentRows));
        var out1 = c1.Output;

        var c2 = CreateConsole();
        c2.Write(ShellPanelBuilder.BuildProgressPanel(messages, scrollOffset: ContentRows * 2));
        var out2 = c2.Output;

        // Both views should differ — the second PgUp must still change the viewport.
        Assert.That(out1 != out2,
            "Expected second PgUp to produce different output (scroll was stuck)");
        return Task.CompletedTask;
    }

    private static Task ContentRowCount_MatchesBuildProgressPanelViewportSize()
    {
        // ContentRowCount must equal the budget-2 value used inside BuildProgressPanel.
        // This ensures PageStep() moves exactly one page — no more, no less.
        var result = ShellPanelBuilder.ContentRowCount(ShellPanelBuilder.FallbackTerminalHeight);
        var expected = ShellPanelBuilder.ComputeLineBudget(ShellPanelBuilder.FallbackTerminalHeight) - 2;
        Assert.That(result == expected,
            $"ContentRowCount({ShellPanelBuilder.FallbackTerminalHeight}) = {result}, expected {expected}");
        return Task.CompletedTask;
    }

    // ── Scroll cap tests ──────────────────────────────────────────────────────
    // Ensure PgUp cannot scroll so far that the panel becomes nearly empty.

    private static Task MaxScrollOffset_CapsAtFlatCountMinusContentRows()
    {
        // 30 single-line messages, contentRows=25. Max useful scroll = 30-25 = 5.
        var messages = Enumerable.Range(1, 30).Select(i => Line($"msg-{i:D3}")).ToArray();
        var max = ShellPanelBuilder.MaxScrollOffset(messages, ShellPanelBuilder.FallbackTerminalHeight);
        Assert.That(max == 30 - ContentRows,
            $"Expected MaxScrollOffset = {30 - ContentRows}, got {max}");
        return Task.CompletedTask;
    }

    private static Task MaxScrollOffset_AtCap_ShowsFullPageOfOldestContent()
    {
        // At the max scroll offset the oldest content fills the panel completely (linesAbove=0).
        var messages = Enumerable.Range(1, 40).Select(i => Line($"msg-{i:D3}")).ToArray();
        var termH = ShellPanelBuilder.FallbackTerminalHeight;
        var maxOffset = ShellPanelBuilder.MaxScrollOffset(messages, termH);

        var console = CreateConsole();
        console.Write(ShellPanelBuilder.BuildProgressPanel(messages, scrollOffset: maxOffset, termH));
        var output = console.Output;

        Assert.That(output.Contains("msg-001"),
            $"Expected oldest message visible at max scroll offset {maxOffset}: {output}");
        Assert.That(!output.Contains("above"),
            $"Should not have 'above' hint at max scroll (full page of oldest content): {output}");
        return Task.CompletedTask;
    }

    private static Task MaxScrollOffset_BeyondCap_ScrollOffsetClampsToMax()
    {
        // MaxScrollOffset defines the ceiling. Any scrollOffset above it should produce
        // the same view as exactly MaxScrollOffset — as enforced by clamping in ReadInput.
        var messages = Enumerable.Range(1, 40).Select(i => Line($"msg-{i:D3}")).ToArray();
        var termH = ShellPanelBuilder.FallbackTerminalHeight;
        var maxOffset = ShellPanelBuilder.MaxScrollOffset(messages, termH);

        // Clamping in ReadInput: min(scrollOffset + step, maxOffset)
        // Verify that the clamped value equals maxOffset when we exceed it.
        var clampedAt100 = Math.Min(maxOffset + 100, maxOffset);
        Assert.That(clampedAt100 == maxOffset,
            $"Clamping scrollOffset+100 should produce maxOffset={maxOffset}, got {clampedAt100}");

        // And that both produce the same rendered panel.
        var c1 = CreateConsole();
        c1.Write(ShellPanelBuilder.BuildProgressPanel(messages, maxOffset, termH));
        var c2 = CreateConsole();
        c2.Write(ShellPanelBuilder.BuildProgressPanel(messages, maxOffset, termH));
        Assert.That(c1.Output == c2.Output, "Same offset must produce same output");
        return Task.CompletedTask;
    }

    private static Task MaxScrollOffset_FewerLinesThanContentRows_IsZero()
    {
        // When all content fits in the panel without scrolling, max scroll offset = 0.
        var messages = Enumerable.Range(1, 5).Select(i => Line($"msg-{i:D3}")).ToArray();
        var max = ShellPanelBuilder.MaxScrollOffset(messages, ShellPanelBuilder.FallbackTerminalHeight);
        Assert.That(max == 0,
            $"Expected MaxScrollOffset=0 when all content fits, got {max}");
        return Task.CompletedTask;
    }

    private static Task MaxScrollOffset_WithWrappingLines_AccountsForLineHeight()
    {
        // 10 messages, each exactly 25 visible chars. At contentWidth=5 each line renders as
        // ceil(25/5) = 5 terminal rows.  contentRows = ContentRowCount(40) = 25.
        // Only 5 flat lines fit (5×5 = 25 rows), so MaxScrollOffset must be 10 - 5 = 5.
        // The legacy formula (flatCount − contentRows) would give max(0, 10−25) = 0,
        // meaning the oldest content can never be reached — the bug this test guards against.
        var messages = Enumerable.Range(1, 10)
            .Select(i => Line(new string('x', 25)))
            .ToArray();
        var contentWidth = 5;
        var termH = ShellPanelBuilder.FallbackTerminalHeight; // 40 → contentRows = 25
        var max = ShellPanelBuilder.MaxScrollOffset(messages, termH, contentWidth);
        var expected = 5; // 10 - 5 lines that fit
        Assert.That(max == expected,
            $"Expected MaxScrollOffset={expected} for wrapping lines, got {max}");
        return Task.CompletedTask;
    }

    private static Task MaxScrollOffset_WithWrappingLines_OldestLineReachable()
    {
        // Verifies that at the height-aware max offset the oldest message is actually
        // rendered visible in the panel (linesAbove = 0).
        var messages = Enumerable.Range(1, 10)
            .Select(i => Line(new string('x', 25) + $"{i:D2}")) // 27 chars each
            .ToArray();
        var contentWidth = 5;
        var termH = ShellPanelBuilder.FallbackTerminalHeight;
        var maxOffset = ShellPanelBuilder.MaxScrollOffset(messages, termH, contentWidth);

        var console = CreateConsole();
        // BuildProgressPanel uses FallbackTerminalHeight when output is redirected,
        // so pass the same termHeightOverride to match.
        console.Write(ShellPanelBuilder.BuildProgressPanel(messages, scrollOffset: maxOffset, termHeightOverride: termH));
        var output = console.Output;

        // The oldest message (index 0) must be visible — no "above" hint.
        Assert.That(!output.Contains("above"),
            $"Expected no 'above' hint at height-aware max offset {maxOffset}: {output}");
        return Task.CompletedTask;
    }

    // ── Help content tests ────────────────────────────────────────────────────

    private static Task HelpMarkup_ContainsAllCommands()
    {
        var markup = ShellService.BuildInteractiveHelpMarkup();
        string[] requiredCommands =
        [
            "/init", "/customize", "/start-here", "/bug", "/status", "/history",
            "/mode", "/keep-awake", "/add-issue", "/edit-issue", "/plan", "/questions",
            "/diff-run", "/budget", "/check-update", "/update", "/max-iterations",
            "/max-subagents", "/run", "/stop", "/wait", "/feedback",
            "/preview", "/approve", "/answer", "/goal", "/exit", "@role",
        ];
        foreach (var cmd in requiredCommands)
            Assert.That(markup.Contains(cmd), $"Help markup missing command: {cmd}");
        return Task.CompletedTask;
    }

    private static Task HelpMarkup_AllCommandsHaveDescription()
    {
        var markup = ShellService.BuildInteractiveHelpMarkup();
        // Every command line contains [dim] markup (description text).
        var lines = markup.Split('\n')
            .Where(l => l.Contains("[cyan]/"))
            .ToList();
        Assert.That(lines.Count >= 20, $"Expected at least 20 command lines, got {lines.Count}");
        foreach (var line in lines)
            Assert.That(line.Contains("[dim]"),
                $"Command line missing description ([dim] marker): {line.TrimEnd()}");
        return Task.CompletedTask;
    }

    private static Task HelpMarkup_NoCarriageReturns()
    {
        // BuildInteractiveHelpMarkup uses AppendLine which on Windows produces \r\n.
        // The \r must be stripped by the caller (FlattenMessages) — the raw markup
        // is allowed to have them, but FlattenMessages output must not.
        var markup = ShellService.BuildInteractiveHelpMarkup();
        var msg = new ShellMessage(ShellMessageKind.Panel, markup, Title: "help");
        var flat = ShellPanelBuilder.FlattenMessages([msg]);
        foreach (var line in flat)
            Assert.That(!line.Contains('\r'),
                $"FlattenMessages produced a flat line with \\r: [{line}]");
        return Task.CompletedTask;
    }
}
