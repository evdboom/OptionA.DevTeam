namespace DevTeam.Cli.Shell;

/// <summary>
/// Pure cursor-navigation helpers for the interactive input buffer.
/// All methods are stateless and operate only on string positions.
/// </summary>
internal static class InputCursorNavigation
{
    /// <summary>Returns (row, col) for a cursor position within the buffer text.</summary>
    internal static (int Row, int Col) GetCursorRowCol(string text, int pos)
    {
        pos = Math.Clamp(pos, 0, text.Length);
        var row = 0;
        var col = 0;
        for (var i = 0; i < pos; i++)
        {
            if (text[i] == '\n') { row++; col = 0; }
            else { col++; }
        }
        return (row, col);
    }

    /// <summary>Total number of logical rows in the buffer text.</summary>
    internal static int CountRows(string text) =>
        string.IsNullOrEmpty(text) ? 1 : text.Count(c => c == '\n') + 1;

    /// <summary>Returns the buffer position corresponding to (row, col), clamped to the line end.</summary>
    internal static int GetPositionAtRowCol(string text, int targetRow, int targetCol)
    {
        var pos = 0;
        var currentRow = 0;
        while (currentRow < targetRow && pos < text.Length)
        {
            if (text[pos] == '\n') currentRow++;
            pos++;
        }
        if (currentRow < targetRow) return text.Length;

        var col = 0;
        while (col < targetCol && pos < text.Length && text[pos] != '\n')
        {
            pos++;
            col++;
        }
        return pos;
    }

    /// <summary>Returns the buffer position of the start of the line containing <paramref name="pos"/>.</summary>
    internal static int GetLineStart(string text, int pos)
    {
        pos = Math.Clamp(pos, 0, text.Length);
        while (pos > 0 && text[pos - 1] != '\n') pos--;
        return pos;
    }

    /// <summary>Returns the buffer position of the end of the line containing <paramref name="pos"/> (before any trailing newline).</summary>
    internal static int GetLineEnd(string text, int pos)
    {
        pos = Math.Clamp(pos, 0, text.Length);
        while (pos < text.Length && text[pos] != '\n') pos++;
        return pos;
    }

    /// <summary>Ctrl+Left: jump to start of previous word.</summary>
    internal static int WordJumpLeft(string text, int pos)
    {
        if (pos <= 0) return 0;
        pos--;
        while (pos > 0 && char.IsWhiteSpace(text[pos])) pos--;
        while (pos > 0 && !char.IsWhiteSpace(text[pos - 1])) pos--;
        return pos;
    }

    /// <summary>Ctrl+Right: jump to start of next word.</summary>
    internal static int WordJumpRight(string text, int pos)
    {
        var len = text.Length;
        if (pos >= len) return len;
        while (pos < len && !char.IsWhiteSpace(text[pos])) pos++;
        while (pos < len && char.IsWhiteSpace(text[pos])) pos++;
        return pos;
    }
}
