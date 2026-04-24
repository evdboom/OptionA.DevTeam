namespace DevTeam.Core;

/// <summary>
/// SDK-agnostic hook configuration passed through AgentInvocationRequest.
/// Translated to GitHub.Copilot.SDK.SessionHooks inside WorkspaceMcpSessionConfigFactory.
/// </summary>
public sealed class SessionHooksConfig
{
    /// <summary>
    /// Called before each tool execution. Return a <see cref="PreToolDecision"/> to allow, deny, or gate the call.
    /// </summary>
    public Func<string, string, PreToolDecision>? OnPreToolUse { get; init; }

    /// <summary>
    /// Called after each tool execution with the tool name, arguments, and result.
    /// </summary>
    public Action<string, string, string>? OnPostToolUse { get; init; }

    /// <summary>
    /// Called when the session starts or resumes. Source is "startup", "resume", or "new".
    /// </summary>
    public Action<string>? OnSessionStart { get; init; }

    /// <summary>
    /// Called when the session ends.
    /// </summary>
    public Action<string>? OnSessionEnd { get; init; }

    /// <summary>
    /// Called when an error occurs. Return an <see cref="ErrorHandlingDecision"/> to retry, skip, or abort.
    /// </summary>
    public Func<string, string, ErrorHandlingDecision>? OnErrorOccurred { get; init; }
}

public enum PreToolDecision { Allow, Deny, Ask }

public enum ErrorHandlingDecision { Retry, Skip, Abort }
