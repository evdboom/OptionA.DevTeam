using Spectre.Console;

namespace DevTeam.Cli.Shell;

internal enum ShellMessageKind
{
    Line,
    Panel,
}

/// <summary>
/// A single renderable message in the shell's scrolling history.
/// <see cref="Markup"/> holds a raw Spectre.Console markup string (NOT pre-escaped).
/// For <see cref="ShellMessageKind.Panel"/>, Title, BorderColor, and TitleColor
/// control the panel header and border appearance.
/// </summary>
internal sealed record ShellMessage(
    ShellMessageKind Kind,
    string Markup,
    string? Title = null,
    Color? BorderColor = null,
    Color? TitleColor = null,
    bool IsHeartbeat = false,
    Justify? TitleJustify = null);
