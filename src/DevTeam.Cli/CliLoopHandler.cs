using System.Diagnostics.CodeAnalysis;

using DevTeam.Core;
using DevTeam.Cli.Shell;

namespace DevTeam.Cli;

internal static class CliLoopHandler
{
    internal static async Task<int> RunShellAsync(
        WorkspaceStore store,
        DevTeamRuntime runtime,
        LoopExecutor loopExecutor,
        ToolUpdateService toolUpdateService,
        Dictionary<string, List<string>> startOptions)
    {
        using var exitCts = new CancellationTokenSource();
        using var shell = new ShellService(
            store, runtime, loopExecutor, toolUpdateService,
            new ShellStartOptions(startOptions), () => exitCts.Cancel());

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            exitCts.Cancel();
        };

        var noTty = CliOptionParser.GetBoolOption(startOptions, "no-tty", false)
            || Console.IsInputRedirected
            || Console.IsOutputRedirected;

        var outputFormat = CliOptionParser.GetOption(startOptions, "output-format") ?? "plain";

        if (noTty)
        {
            await NonInteractiveShellHost.RunAsync(shell, exitCts.Token, outputFormat);
        }
        else
        {
            await SpectreShellHost.RunAsync(shell, exitCts.Token);
        }
        return 0;
    }

    [SuppressMessage("Major Code Smell", "S3776", Justification = "Budget fallback flow is intentionally explicit for CLI readability.")]
    internal static async Task InvokeRoleDirectAsync(
        WorkspaceStore store,
        DevTeamRuntime runtime,
        WorkspaceState state,
        string roleSlug,
        string userMessage)
    {
        var policy = DevTeamRuntime.GetRoleModelPolicy(state, roleSlug);
        var model = policy.PrimaryModel;
        var cost = state.Models.FirstOrDefault(m => string.Equals(m.Name, model, StringComparison.OrdinalIgnoreCase))?.Cost ?? 1;
        var remaining = state.Budget.TotalCreditCap - state.Budget.CreditsCommitted;
        if (remaining < cost && cost > 0)
        {
            var fallback = policy.FallbackModel;
            var fallbackCost = state.Models.FirstOrDefault(m => string.Equals(m.Name, fallback, StringComparison.OrdinalIgnoreCase))?.Cost ?? 0;
            if (remaining >= fallbackCost)
            {
                Console.WriteLine(ConsoleTheme.Warning($"Budget low — falling back to {fallback}"));
                model = fallback;
                cost = fallbackCost;
            }
            else
            {
                var freeModel = state.Models.FirstOrDefault(m => Math.Abs(m.Cost) < double.Epsilon && m.IsDefault)
                    ?? state.Models.FirstOrDefault(m => Math.Abs(m.Cost) < double.Epsilon);
                if (freeModel is not null)
                {
                    Console.WriteLine(ConsoleTheme.Warning($"Budget exhausted — falling back to free model {freeModel.Name}"));
                    model = freeModel.Name;
                    cost = 0;
                }
                else
                {
                    Console.WriteLine(ConsoleTheme.Warning("Budget exhausted and no free model available. Use /budget to increase."));
                    return;
                }
            }
        }

        var prompt = AgentPromptBuilder.BuildAdHocPrompt(state, roleSlug, userMessage);
        var sessionId = $"devteam-adhoc-{roleSlug}";
        var provider = ProviderSelectionService.ResolveProvider(state, model);
        ChatConsole.WriteEvent("→", $"Asking {roleSlug} via {model}...", "dim cyan");

        var client = new DefaultAgentClientFactory().Create("sdk");
        try
        {
            var response = await client.InvokeAsync(new AgentInvocationRequest
            {
                Prompt = prompt,
                Model = model,
                SessionId = sessionId,
                WorkingDirectory = state.RepoRoot,
                WorkspacePath = store.WorkspacePath,
                Provider = provider,
                Timeout = TimeSpan.FromMinutes(10),
                EnableWorkspaceMcp = state.Runtime.WorkspaceMcpEnabled,
                WorkspaceMcpServerName = state.Runtime.WorkspaceMcpServerName,
                ExternalMcpServers = state.McpServers
            });

            state.Budget.CreditsCommitted += cost;
            if (policy.AllowPremium)
            {
                state.Budget.PremiumCreditsCommitted += cost;
            }

            var parsed = AgentPromptBuilder.ParseResponse(response);

            ChatConsole.WriteAgent(roleSlug, string.IsNullOrWhiteSpace(parsed.Summary) ? "(no summary)" : parsed.Summary);

            if (parsed.Issues.Count > 0)
            {
                var anchor = state.Issues.FirstOrDefault(i => !i.IsPlanningIssue) ?? state.Issues.FirstOrDefault();
                if (anchor is not null)
                {
                    runtime.AddGeneratedIssues(state, anchor.Id, parsed.Issues);
                    ChatConsole.WriteSuccess($"{parsed.Issues.Count} issue(s) proposed and added to the board.");
                }
            }
            if (parsed.Questions.Count > 0)
            {
                runtime.AddQuestions(state, parsed.Questions);
                ChatConsole.WriteWarning($"{parsed.Questions.Count} question(s) added — they'll appear at the prompt.");
            }

            DevTeamRuntime.MergeWorkspaceAdditions(state, store.Load());
            store.Save(state);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ConsoleTheme.Error($"Agent error: {ex.Message}"));
        }
    }

    [SuppressMessage("Major Code Smell", "S107", Justification = "Legacy CLI entrypoint keeps explicit parameter list for callsite clarity.")]
    internal static async Task<LoopExecutionReport> RunLoopAsync(
        WorkspaceStore store,
        DevTeamRuntime runtime,
        LoopExecutor loopExecutor,
        WorkspaceState state,
        Dictionary<string, List<string>> options,
        bool interactiveShell = false,
        bool chatLogging = false,
        CancellationToken cancellationToken = default,
        Action<string>? overrideLogger = null)
    {
        var backend = CliOptionParser.GetOption(options, "backend") ?? "sdk";
        var providerName = CliOptionParser.GetOption(options, "provider");
        var maxSubagents = CliOptionParser.GetIntOption(options, "max-subagents", state.Runtime.DefaultMaxSubagents);
        var maxIterations = CliOptionParser.GetIntOption(options, "max-iterations", state.Runtime.DefaultMaxIterations);
        var timeoutSeconds = CliOptionParser.GetIntOption(options, "timeout-seconds", 600);
        var verbosity = CliOptionParser.ParseVerbosity(CliOptionParser.GetOption(options, "verbosity"));
        if (!string.IsNullOrWhiteSpace(providerName))
        {
            ProviderSelectionService.GetRequiredProvider(state, providerName);
        }
        using var keepAwakeController = new KeepAwakeController();
        using var renderer = interactiveShell && !Console.IsOutputRedirected
            ? new LoopConsoleRenderer()
            : null;
        Action<IReadOnlyList<RunProgressSnapshot>>? progressReporter = renderer is null ? null : snapshots => renderer.ReportProgress(snapshots);
        Action<string> logger;
        if (overrideLogger is not null)
        {
            logger = overrideLogger;
        }
        else if (renderer is not null)
        {
            logger = message => renderer.Log(message);
        }
        else if (chatLogging)
        {
            logger = ChatConsole.WriteLoopLog;
        }
        else
        {
            logger = Console.WriteLine;
        }
        var keepAwakeEnabled = CliWorkspaceHelper.ResolveKeepAwakeEnabled(state, options);
        CliWorkspaceHelper.ApplyKeepAwakeSetting(keepAwakeController, keepAwakeEnabled, interactiveShell, logger);
        var executionOptions = new LoopExecutionOptions
        {
            Backend = backend,
            ProviderName = providerName,
            MaxSubagents = maxSubagents,
            MaxIterations = maxIterations,
            AgentTimeout = TimeSpan.FromSeconds(timeoutSeconds),
            HeartbeatInterval = interactiveShell ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(10),
            Verbosity = verbosity,
            ProgressReporter = progressReporter
        };
        var report = await loopExecutor.RunAsync(
            state,
            executionOptions,
            logger,
            cancellationToken);
        store.Save(state);
        return report;
    }

    [SuppressMessage("Major Code Smell", "S3776", Justification = "Plan display flow mirrors user approval states and prompts.")]
    internal static async Task ShowPlanAsync(
        WorkspaceStore store,
        DevTeamRuntime runtime,
        LoopExecutor loopExecutor,
        WorkspaceState state,
        Dictionary<string, List<string>> options,
        bool interactive)
    {
        var result = await PlanWorkflow.EnsurePlanAsync(
            store,
            state,
            planningState =>
            {
                Console.WriteLine(interactive
                    ? "No plan has been written yet. Running the planner..."
                    : "No plan has been written yet. Running the planner first...");
                return RunLoopAsync(store, runtime, loopExecutor, planningState, BuildPlanningOptions(options), interactiveShell: interactive);
            });

        switch (result.Status)
        {
            case PlanPreparationStatus.MissingGoal:
                Console.WriteLine(interactive
                    ? $"No active goal is set yet. Use {ConsoleTheme.Command("/goal")} or {ConsoleTheme.Command("/init")} --goal first."
                    : "No active goal is set yet. Use `goal` or `init --goal` first.");
                return;
            case PlanPreparationStatus.Failed:
                Console.WriteLine("The planner ran, but no plan file was written yet.");
                if (result.Report?.FinalState == "waiting-for-user")
                {
                    Console.WriteLine(interactive
                        ? $"Answer the open questions first with {ConsoleTheme.Command("/answer")}."
                        : $"Check {Path.Combine(store.WorkspacePath, "questions.md")} and answer the open questions first.");
                }
                return;
            case PlanPreparationStatus.Generated:
                Console.WriteLine(ConsoleTheme.Success("Plan generated."));
                break;
        }

        if (state.Phase == WorkflowPhase.ArchitectPlanning)
        {
            if (WorkspaceStatusPrinter.PrintArchitectSummary(state))
            {
                return;
            }

            Console.WriteLine($"High-level plan approved. Phase: {ConsoleTheme.Phase("ArchitectPlanning")}");
            Console.WriteLine(interactive
                ? $"Run {ConsoleTheme.Command("/run")} to let the architect create execution issues, then {ConsoleTheme.Command("/approve")} again."
                : "Run the loop to let the architect create execution issues, then approve again.");
            return;
        }

        WorkspaceStatusPrinter.PrintPlan(store);
        WorkspaceStatusPrinter.PrintOpenQuestions(state, interactive, runtime);

        if (state.Phase == WorkflowPhase.Planning)
        {
            Console.WriteLine(interactive
                ? $"Reply with feedback as plain text or use {ConsoleTheme.Command("/approve")} when the plan looks good."
                : "Review the plan, provide feedback with `feedback`, or use `approve-plan` when it looks good.");
            return;
        }

        if (PlanWorkflow.IsAwaitingArchitectApproval(state))
        {
            Console.WriteLine(interactive
                ? $"Reply with feedback as plain text or use {ConsoleTheme.Command("/approve")} when the architect plan looks good."
                : "Review the architect output, provide feedback with `feedback`, or use `approve-plan` when it looks good.");
        }
    }

    internal static Dictionary<string, List<string>> BuildPlanningOptions(Dictionary<string, List<string>> options)
    {
        var result = options.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToList(),
            StringComparer.OrdinalIgnoreCase);
        if (!result.ContainsKey("max-iterations"))
        {
            result["max-iterations"] = ["1"];
        }
        if (!result.ContainsKey("max-subagents"))
        {
            result["max-subagents"] = ["1"];
        }
        return result;
    }
}
