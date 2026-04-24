namespace DevTeam.UnitTests.Tests;

internal static class WorkspaceMcpSessionConfigFactoryTests
{
    public static IEnumerable<TestCase> GetTests() =>
    [
        new("BuildSessionHooks_NullConfig_IsNotCalled", BuildSessionHooks_NullConfig_IsNotCalled),
        new("BuildSessionHooks_OnPreToolUse_Allow_WhenDefined", BuildSessionHooks_OnPreToolUse_Allow_WhenDefined),
        new("BuildSessionHooks_OnPreToolUse_Deny_WhenCallbackReturnsDeny", BuildSessionHooks_OnPreToolUse_Deny_WhenCallbackReturnsDeny),
        new("BuildSessionHooks_OnPostToolUse_IsSet_WhenDefined", BuildSessionHooks_OnPostToolUse_IsSet_WhenDefined),
        new("BuildSessionHooks_OnPostToolUse_IsNull_WhenNotDefined", BuildSessionHooks_OnPostToolUse_IsNull_WhenNotDefined),
        new("BuildSessionHooks_OnErrorOccurred_Retry_WhenCallbackReturnsRetry", BuildSessionHooks_OnErrorOccurred_Retry_WhenCallbackReturnsRetry),
        new("BuildSessionHooks_OnErrorOccurred_Abort_WhenCallbackReturnsAbort", BuildSessionHooks_OnErrorOccurred_Abort_WhenCallbackReturnsAbort),
        new("BuildSessionHooks_OnlyRequestedHooks_ArePopulated", BuildSessionHooks_OnlyRequestedHooks_ArePopulated),
        new("CustomAgentDefinition_Navigator_HasReadOnlyTools", CustomAgentDefinition_Navigator_HasReadOnlyTools),
        new("ScoutAgentDefinitions_NavigatorIfRequested_ReturnsEmptyWhenFalse", ScoutAgentDefinitions_NavigatorIfRequested_ReturnsEmptyWhenFalse),
        new("ScoutAgentDefinitions_NavigatorIfRequested_ReturnsNavigatorWhenTrue", ScoutAgentDefinitions_NavigatorIfRequested_ReturnsNavigatorWhenTrue),
    ];

    // ── SessionHooks mapping ────────────────────────────────────────────────

    private static Task BuildSessionHooks_NullConfig_IsNotCalled()
    {
        // No assertion needed — just verify BuildSessionHooks does not throw
        // when individual hook callbacks are all null (empty config).
        var config = new SessionHooksConfig();
        var hooks = WorkspaceMcpSessionConfigFactory.BuildSessionHooks(config);

        Assert.That(hooks.OnPreToolUse is null, "Expected OnPreToolUse to be null when config callback is null");
        Assert.That(hooks.OnPostToolUse is null, "Expected OnPostToolUse to be null when config callback is null");
        Assert.That(hooks.OnSessionStart is null, "Expected OnSessionStart to be null when config callback is null");
        Assert.That(hooks.OnSessionEnd is null, "Expected OnSessionEnd to be null when config callback is null");
        Assert.That(hooks.OnErrorOccurred is null, "Expected OnErrorOccurred to be null when config callback is null");
        return Task.CompletedTask;
    }

    private static async Task BuildSessionHooks_OnPreToolUse_Allow_WhenDefined()
    {
        var config = new SessionHooksConfig
        {
            OnPreToolUse = (_, _) => PreToolDecision.Allow
        };
        var hooks = WorkspaceMcpSessionConfigFactory.BuildSessionHooks(config);

        Assert.That(hooks.OnPreToolUse is not null, "Expected OnPreToolUse to be set");
        var output = await hooks.OnPreToolUse!(
            new GitHub.Copilot.SDK.PreToolUseHookInput { ToolName = "grep", ToolArgs = "{}" },
            default!);
        Assert.That(output?.PermissionDecision == "allow", $"Expected 'allow' but got: {output?.PermissionDecision}");
    }

    private static async Task BuildSessionHooks_OnPreToolUse_Deny_WhenCallbackReturnsDeny()
    {
        var config = new SessionHooksConfig
        {
            OnPreToolUse = (_, _) => PreToolDecision.Deny
        };
        var hooks = WorkspaceMcpSessionConfigFactory.BuildSessionHooks(config);

        var output = await hooks.OnPreToolUse!(
            new GitHub.Copilot.SDK.PreToolUseHookInput { ToolName = "bash", ToolArgs = "rm -rf /" },
            default!);
        Assert.That(output?.PermissionDecision == "deny", $"Expected 'deny' but got: {output?.PermissionDecision}");
    }

    private static async Task BuildSessionHooks_OnPostToolUse_IsSet_WhenDefined()
    {
        string? capturedTool = null;
        var config = new SessionHooksConfig
        {
            OnPostToolUse = (toolName, _, _) => { capturedTool = toolName; }
        };
        var hooks = WorkspaceMcpSessionConfigFactory.BuildSessionHooks(config);

        Assert.That(hooks.OnPostToolUse is not null, "Expected OnPostToolUse to be set");
        await hooks.OnPostToolUse!(
            new GitHub.Copilot.SDK.PostToolUseHookInput { ToolName = "view", ToolArgs = "{}", ToolResult = "content" },
            default!);
        Assert.That(capturedTool == "view", $"Expected capturedTool to be 'view' but was: {capturedTool}");
    }

    private static Task BuildSessionHooks_OnPostToolUse_IsNull_WhenNotDefined()
    {
        var config = new SessionHooksConfig();
        var hooks = WorkspaceMcpSessionConfigFactory.BuildSessionHooks(config);
        Assert.That(hooks.OnPostToolUse is null, "Expected OnPostToolUse to be null when not configured");
        return Task.CompletedTask;
    }

    private static async Task BuildSessionHooks_OnErrorOccurred_Retry_WhenCallbackReturnsRetry()
    {
        var config = new SessionHooksConfig
        {
            OnErrorOccurred = (_, _) => ErrorHandlingDecision.Retry
        };
        var hooks = WorkspaceMcpSessionConfigFactory.BuildSessionHooks(config);

        Assert.That(hooks.OnErrorOccurred is not null, "Expected OnErrorOccurred to be set");
        var output = await hooks.OnErrorOccurred!(
            new GitHub.Copilot.SDK.ErrorOccurredHookInput { ErrorContext = "tool", Error = "timeout" },
            default!);
        Assert.That(output?.ErrorHandling == "retry", $"Expected 'retry' but got: {output?.ErrorHandling}");
    }

    private static async Task BuildSessionHooks_OnErrorOccurred_Abort_WhenCallbackReturnsAbort()
    {
        var config = new SessionHooksConfig
        {
            OnErrorOccurred = (_, _) => ErrorHandlingDecision.Abort
        };
        var hooks = WorkspaceMcpSessionConfigFactory.BuildSessionHooks(config);

        var output = await hooks.OnErrorOccurred!(
            new GitHub.Copilot.SDK.ErrorOccurredHookInput { ErrorContext = "tool", Error = "fatal" },
            default!);
        Assert.That(output?.ErrorHandling == "abort", $"Expected 'abort' but got: {output?.ErrorHandling}");
    }

    private static Task BuildSessionHooks_OnlyRequestedHooks_ArePopulated()
    {
        var config = new SessionHooksConfig
        {
            OnPreToolUse = (_, _) => PreToolDecision.Allow
            // All other hooks omitted intentionally
        };
        var hooks = WorkspaceMcpSessionConfigFactory.BuildSessionHooks(config);

        Assert.That(hooks.OnPreToolUse is not null, "Expected OnPreToolUse to be set");
        Assert.That(hooks.OnPostToolUse is null, "Expected OnPostToolUse to be null when not requested");
        Assert.That(hooks.OnSessionStart is null, "Expected OnSessionStart to be null when not requested");
        Assert.That(hooks.OnSessionEnd is null, "Expected OnSessionEnd to be null when not requested");
        Assert.That(hooks.OnErrorOccurred is null, "Expected OnErrorOccurred to be null when not requested");
        return Task.CompletedTask;
    }

    // ── ScoutAgentDefinitions ───────────────────────────────────────────────

    private static Task CustomAgentDefinition_Navigator_HasReadOnlyTools()
    {
        var nav = ScoutAgentDefinitions.Navigator;

        Assert.That(nav.Name == "navigator", $"Expected name 'navigator' but got: {nav.Name}");
        Assert.That(nav.Tools.Count > 0, "Expected Navigator to have at least one tool");
        Assert.That(nav.Tools.Contains("grep", StringComparer.OrdinalIgnoreCase),
            $"Expected Navigator tools to include 'grep' but got: {string.Join(", ", nav.Tools)}");
        Assert.That(!nav.Tools.Contains("edit", StringComparer.OrdinalIgnoreCase),
            $"Navigator must not include write tool 'edit' but got: {string.Join(", ", nav.Tools)}");
        Assert.That(!string.IsNullOrWhiteSpace(nav.Prompt),
            "Expected Navigator to have a non-empty system prompt");
        return Task.CompletedTask;
    }

    private static Task ScoutAgentDefinitions_NavigatorIfRequested_ReturnsEmptyWhenFalse()
    {
        var result = ScoutAgentDefinitions.NavigatorIfRequested(false);
        Assert.That(result.Count == 0, $"Expected empty list when includeScout=false but got {result.Count} items");
        return Task.CompletedTask;
    }

    private static Task ScoutAgentDefinitions_NavigatorIfRequested_ReturnsNavigatorWhenTrue()
    {
        var result = ScoutAgentDefinitions.NavigatorIfRequested(true);
        Assert.That(result.Count == 1, $"Expected 1 agent when includeScout=true but got {result.Count}");
        Assert.That(result[0].Name == "navigator", $"Expected name 'navigator' but got: {result[0].Name}");
        return Task.CompletedTask;
    }
}
