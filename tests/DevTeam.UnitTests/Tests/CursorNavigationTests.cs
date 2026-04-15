using DevTeam.Cli.Shell;
using Spectre.Console;

namespace DevTeam.UnitTests.Tests;

internal static class CursorNavigationTests
{
    public static IEnumerable<TestCase> GetTests() =>
    [
        // BuildInput cursor rendering
        new("BuildInput_PlacesCursor_AtStart", BuildInput_PlacesCursor_AtStart),
        new("BuildInput_PlacesCursor_AtEnd", BuildInput_PlacesCursor_AtEnd),
        new("BuildInput_PlacesCursor_MidString", BuildInput_PlacesCursor_MidString),
        new("BuildInput_PlacesCursor_MidMultiline", BuildInput_PlacesCursor_MidMultiline),
        new("BuildInput_NoCursor_WhenPositionNegative", BuildInput_NoCursor_WhenPositionNegative),

        // GetCursorRowCol
        new("GetCursorRowCol_SingleLine_Row0", GetCursorRowCol_SingleLine_Row0),
        new("GetCursorRowCol_MultiLine_AfterNewline", GetCursorRowCol_MultiLine_AfterNewline),
        new("GetCursorRowCol_AtNewline_EndsOnPrevRow", GetCursorRowCol_AtNewline_EndsOnPrevRow),

        // Up/Down dispatch logic
        new("CountRows_SingleLine_Returns1", CountRows_SingleLine_Returns1),
        new("CountRows_TwoLines_Returns2", CountRows_TwoLines_Returns2),

        // GetPositionAtRowCol
        new("GetPositionAtRowCol_Row0Col0_ReturnsZero", GetPositionAtRowCol_Row0Col0_ReturnsZero),
        new("GetPositionAtRowCol_Row1Col2_CorrectOffset", GetPositionAtRowCol_Row1Col2_CorrectOffset),
        new("GetPositionAtRowCol_ColBeyondLineEnd_ClampsToEnd", GetPositionAtRowCol_ColBeyondLineEnd_ClampsToEnd),

        // Word jump
        new("WordJumpRight_JumpsToNextWord", WordJumpRight_JumpsToNextWord),
        new("WordJumpLeft_JumpsToPreviousWord", WordJumpLeft_JumpsToPreviousWord),

        // Line start/end
        new("GetLineStart_MidLine_ReturnsLineStart", GetLineStart_MidLine_ReturnsLineStart),
        new("GetLineEnd_MidLine_ReturnsLineEnd", GetLineEnd_MidLine_ReturnsLineEnd),
    ];

    // ── BuildInput cursor rendering ───────────────────────────────────────────

    private static Task BuildInput_PlacesCursor_AtStart()
    {
        var panel = ShellPanelBuilder.BuildInput("shell", "hello", 0);
        var text = panel.ToString() ?? "";
        // The rendered output should contain ▌ before 'hello'
        Assert.That(ContainsCursorBeforeText(panel, "▌hello"), $"Expected ▌ at position 0 (before 'hello')");
        return Task.CompletedTask;
    }

    private static Task BuildInput_PlacesCursor_AtEnd()
    {
        var panel = ShellPanelBuilder.BuildInput("shell", "hello", 5);
        Assert.That(ContainsCursorBeforeText(panel, "hello▌"), $"Expected ▌ at end (after 'hello')");
        return Task.CompletedTask;
    }

    private static Task BuildInput_PlacesCursor_MidString()
    {
        var panel = ShellPanelBuilder.BuildInput("shell", "hello world", 5);
        Assert.That(ContainsCursorBeforeText(panel, "hello▌ world"), $"Expected ▌ at position 5");
        return Task.CompletedTask;
    }

    private static Task BuildInput_PlacesCursor_MidMultiline()
    {
        // "line1\nline2", cursor at position 7 (start of 'line2')
        var panel = ShellPanelBuilder.BuildInput("shell", "line1\nline2", 6);
        Assert.That(ContainsCursorBeforeText(panel, "▌line2"), $"Expected ▌ at start of second line");
        return Task.CompletedTask;
    }

    private static Task BuildInput_NoCursor_WhenPositionNegative()
    {
        var panel = ShellPanelBuilder.BuildInput("shell", "hello", -1);
        Assert.That(!ContainsCursorBeforeText(panel, "▌"), $"Expected no cursor marker when position is -1");
        return Task.CompletedTask;
    }

    // ── GetCursorRowCol ───────────────────────────────────────────────────────

    private static Task GetCursorRowCol_SingleLine_Row0()
    {
        var (row, col) = InputCursorNavigation.GetCursorRowCol("hello", 3);
        Assert.That(row == 0, $"Expected row 0 but got {row}");
        Assert.That(col == 3, $"Expected col 3 but got {col}");
        return Task.CompletedTask;
    }

    private static Task GetCursorRowCol_MultiLine_AfterNewline()
    {
        // "hello\nworld", pos=7 → row 1, col 1
        var (row, col) = InputCursorNavigation.GetCursorRowCol("hello\nworld", 7);
        Assert.That(row == 1, $"Expected row 1 but got {row}");
        Assert.That(col == 1, $"Expected col 1 but got {col}");
        return Task.CompletedTask;
    }

    private static Task GetCursorRowCol_AtNewline_EndsOnPrevRow()
    {
        // "hello\nworld", pos=5 → at the \n → row 0, col 5
        var (row, col) = InputCursorNavigation.GetCursorRowCol("hello\nworld", 5);
        Assert.That(row == 0, $"Expected row 0 but got {row}");
        Assert.That(col == 5, $"Expected col 5 but got {col}");
        return Task.CompletedTask;
    }

    // ── CountRows ─────────────────────────────────────────────────────────────

    private static Task CountRows_SingleLine_Returns1()
    {
        Assert.That(InputCursorNavigation.CountRows("hello") == 1, "Expected 1 row for single-line text");
        Assert.That(InputCursorNavigation.CountRows("") == 1, "Expected 1 row for empty string");
        return Task.CompletedTask;
    }

    private static Task CountRows_TwoLines_Returns2()
    {
        Assert.That(InputCursorNavigation.CountRows("hello\nworld") == 2, "Expected 2 rows for two-line text");
        Assert.That(InputCursorNavigation.CountRows("a\nb\nc") == 3, "Expected 3 rows for three-line text");
        return Task.CompletedTask;
    }

    // ── GetPositionAtRowCol ───────────────────────────────────────────────────

    private static Task GetPositionAtRowCol_Row0Col0_ReturnsZero()
    {
        var pos = InputCursorNavigation.GetPositionAtRowCol("hello\nworld", 0, 0);
        Assert.That(pos == 0, $"Expected pos 0 but got {pos}");
        return Task.CompletedTask;
    }

    private static Task GetPositionAtRowCol_Row1Col2_CorrectOffset()
    {
        // "hello\nworld", row 1 col 2 → offset 8 (5 chars + \n + 2 chars)
        var pos = InputCursorNavigation.GetPositionAtRowCol("hello\nworld", 1, 2);
        Assert.That(pos == 8, $"Expected pos 8 but got {pos}");
        return Task.CompletedTask;
    }

    private static Task GetPositionAtRowCol_ColBeyondLineEnd_ClampsToEnd()
    {
        // "hello\nhi", row 1 col 99 → clamps to end of 'hi' = position 8
        var pos = InputCursorNavigation.GetPositionAtRowCol("hello\nhi", 1, 99);
        Assert.That(pos == 8, $"Expected pos 8 (end of 'hi') but got {pos}");
        return Task.CompletedTask;
    }

    // ── Word jump ─────────────────────────────────────────────────────────────

    private static Task WordJumpRight_JumpsToNextWord()
    {
        // "hello world", from pos 0 → jumps to pos 6 (start of 'world')
        var pos = InputCursorNavigation.WordJumpRight("hello world", 0);
        Assert.That(pos == 6, $"Expected pos 6 but got {pos}");
        return Task.CompletedTask;
    }

    private static Task WordJumpLeft_JumpsToPreviousWord()
    {
        // "hello world", from pos 11 (end) → jumps to pos 6 (start of 'world')
        var pos = InputCursorNavigation.WordJumpLeft("hello world", 11);
        Assert.That(pos == 6, $"Expected pos 6 but got {pos}");
        return Task.CompletedTask;
    }

    // ── Line start/end ────────────────────────────────────────────────────────

    private static Task GetLineStart_MidLine_ReturnsLineStart()
    {
        // "hello\nworld", pos 8 (mid 'world') → line start = 6
        var pos = InputCursorNavigation.GetLineStart("hello\nworld", 8);
        Assert.That(pos == 6, $"Expected line start 6 but got {pos}");
        return Task.CompletedTask;
    }

    private static Task GetLineEnd_MidLine_ReturnsLineEnd()
    {
        // "hello\nworld", pos 7 (mid 'world') → line end = 11
        var pos = InputCursorNavigation.GetLineEnd("hello\nworld", 7);
        Assert.That(pos == 11, $"Expected line end 11 but got {pos}");
        return Task.CompletedTask;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool ContainsCursorBeforeText(Panel panel, string fragment)
    {
        // Render the panel to a string and check for the fragment
        var sb = new System.Text.StringBuilder();
        using var writer = new System.IO.StringWriter(sb);
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Detect,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(writer)
        });
        console.Write(panel);
        var rendered = sb.ToString();
        return rendered.Contains(fragment, StringComparison.Ordinal);
    }
}
