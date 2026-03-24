using System.Collections.Concurrent;
using System.Net.Http;
using System.Text;
using DevTeam.Cli;
using DevTeam.Cli.Shell;
using DevTeam.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RazorConsole.Core;
using RazorConsole.Core.Abstractions.Rendering;
using Spectre.Console;

var command = NormalizeCommand(args.Length == 0 ? "help" : args[0]);
var options = ParseOptions(args.Skip(1).ToArray());
var workspacePath = GetOption(options, "workspace") ?? ".devteam";
var store = new WorkspaceStore(workspacePath);
var runtime = new DevTeamRuntime();
var loopExecutor = new LoopExecutor(runtime, store);
using var toolUpdateService = new ToolUpdateService();

try
{
    CommandOptionValidator.ValidateCli(command, options);

    switch (command)
    {
        case "start":
            return await RunShellAsync(store, runtime, loopExecutor, toolUpdateService, options);

        case "check-update":
            return await CheckForToolUpdatesAsync(toolUpdateService);

        case "update":
            return await ScheduleToolUpdateAsync(toolUpdateService, interactiveShell: false);

        case "workspace-mcp":
        {
            var workspace = GetOption(options, "workspace") ?? ".devteam";
            var server = new WorkspaceMcpServer(workspace);
            await server.RunAsync(Console.OpenStandardInput(), Console.OpenStandardOutput());
            return 0;
        }

        case "init":
        {
            var force = GetBoolOption(options, "force", false);
            if (!force && File.Exists(store.StatePath))
            {
                Console.Error.WriteLine($"Workspace already initialized at {Path.GetFullPath(workspacePath)}.");
                Console.Error.WriteLine("Use --force to reinitialize (this will reset all workspace state).");
                return 1;
            }
            var totalCap = GetDoubleOption(options, "total-credit-cap", 25);
            var premiumCap = GetDoubleOption(options, "premium-credit-cap", 6);
            var goal = GoalInputResolver.Resolve(
                GetOption(options, "goal") ?? GetPositionalValue(options),
                GetOption(options, "goal-file"),
                Environment.CurrentDirectory);
            var gitInitialized = GitWorkspace.EnsureRepository(Environment.CurrentDirectory);
            var state = store.Initialize(Environment.CurrentDirectory, totalCap, premiumCap);
            var mode = GetOption(options, "mode");
            state.Runtime.KeepAwakeEnabled = GetBoolOption(options, "keep-awake", state.Runtime.KeepAwakeEnabled);
            state.Runtime.WorkspaceMcpEnabled = GetBoolOption(options, "workspace-mcp", true);
            state.Runtime.PipelineSchedulingEnabled = GetBoolOption(options, "pipeline-scheduling", true);
            state.Runtime.AutoApproveEnabled = GetBoolOption(options, "auto-approve", state.Runtime.AutoApproveEnabled);
            if (!string.IsNullOrWhiteSpace(mode))
            {
                runtime.SetMode(state, mode);
            }
            if (!string.IsNullOrWhiteSpace(goal))
            {
                runtime.SetGoal(state, goal);
            }
            store.Save(state);

            Console.WriteLine($"Initialized devteam workspace at {Path.GetFullPath(workspacePath)}");
            if (gitInitialized)
            {
                Console.WriteLine($"Initialized git repository at {Path.GetFullPath(Environment.CurrentDirectory)}");
            }
            if (!string.IsNullOrWhiteSpace(goal))
            {
                Console.WriteLine($"Active goal saved: {goal}");
            }
            return 0;
        }

        case "customize":
        {
            var target = Path.Combine(Environment.CurrentDirectory, ".devteam-source");
            var force = GetBoolOption(options, "force", false);
            CopyPackagedAssets(target, force);
            return 0;
        }

        case "bug":
        case "bug-report":
        {
            EmitBugReport(store, runtime, options, shellDiagnostics: null);
            return 0;
        }

        case "set-goal":
        case "goal":
        {
            var state = store.Load();
            var goal = GoalInputResolver.Resolve(
                GetPositionalValue(options),
                GetOption(options, "goal-file"),
                Environment.CurrentDirectory)
                ?? throw new InvalidOperationException("Missing goal text. Provide inline text or --goal-file PATH.");
            runtime.SetGoal(state, goal);
            store.Save(state);
            Console.WriteLine("Updated active goal.");
            return 0;
        }

        case "set-mode":
        case "mode":
        {
            var state = store.Load();
            var mode = GetPositionalValue(options) ?? throw new InvalidOperationException("Missing mode slug.");
            runtime.SetMode(state, mode);
            store.Save(state);
            Console.WriteLine($"Updated active mode to {state.Runtime.ActiveModeSlug}.");
            return 0;
        }

        case "set-keep-awake":
        case "keep-awake":
        {
            var state = store.Load();
            var requested = GetPositionalValue(options) ?? GetOption(options, "enabled")
                ?? throw new InvalidOperationException("Usage: set-keep-awake <true|false> [--workspace PATH]");
            var enabled = ParseBoolOrThrow(requested, "Usage: set-keep-awake <true|false> [--workspace PATH]");
            runtime.SetKeepAwake(state, enabled);
            store.Save(state);
            Console.WriteLine($"Updated keep-awake setting to {(enabled ? "enabled" : "disabled")}.");
            return 0;
        }

        case "add-roadmap":
        {
            var state = store.Load();
            var title = GetPositionalValue(options) ?? throw new InvalidOperationException("Missing roadmap title.");
            var detail = GetOption(options, "detail") ?? "";
            var priority = GetIntOption(options, "priority", 50);
            var item = runtime.AddRoadmapItem(state, title, detail, priority);
            store.Save(state);
            Console.WriteLine($"Created roadmap item #{item.Id}: {item.Title}");
            return 0;
        }

        case "add-issue":
        {
            var state = store.Load();
            var title = GetPositionalValue(options) ?? throw new InvalidOperationException("Missing issue title.");
            var role = GetOption(options, "role") ?? throw new InvalidOperationException(BuildMissingRoleMessage(runtime, state));
            var detail = GetOption(options, "detail") ?? "";
            var area = GetOption(options, "area");
            var priority = GetIntOption(options, "priority", 50);
            var roadmapId = GetNullableIntOption(options, "roadmap-item-id");
            var dependsOn = GetMultiIntOption(options, "depends-on");
            ValidateRoleOrThrow(runtime, state, role);
            var issue = runtime.AddIssue(state, title, detail, role, priority, roadmapId, dependsOn, area);
            store.Save(state);
            Console.WriteLine($"Created issue #{issue.Id}: {issue.Title} ({issue.RoleSlug}{(string.IsNullOrWhiteSpace(issue.Area) ? "" : $", area {issue.Area}")})");
            return 0;
        }

        case "add-question":
        {
            var state = store.Load();
            var text = GetPositionalValue(options) ?? throw new InvalidOperationException("Missing question text.");
            var question = runtime.AddQuestion(state, text, options.ContainsKey("blocking"));
            store.Save(state);
            Console.WriteLine($"Created {(question.IsBlocking ? "blocking" : "non-blocking")} question #{question.Id}");
            return 0;
        }

        case "answer-question":
        case "answer":
        {
            var state = store.Load();
            var values = GetPositionalValues(options);
            if (values.Count < 2)
            {
                throw new InvalidOperationException("Usage: answer-question <id> <answer>");
            }
            runtime.AnswerQuestion(state, int.Parse(values[0]), string.Join(" ", values.Skip(1)));
            store.Save(state);
            Console.WriteLine($"Answered question #{values[0]}");
            return 0;
        }

        case "approve-plan":
        case "approve":
        {
            var state = store.Load();
            var note = GetOption(options, "note") ?? GetPositionalValue(options) ?? "User approved the current plan.";
            if (state.Phase == WorkflowPhase.ArchitectPlanning)
            {
                runtime.ApproveArchitectPlan(state, note);
                store.Save(state);
                Console.WriteLine("Approved the architect plan. Execution work can now begin.");
            }
            else
            {
                runtime.ApprovePlan(state, note);
                store.Save(state);
                if (state.Phase == WorkflowPhase.ArchitectPlanning)
                {
                    Console.WriteLine("Approved the high-level plan. Architect planning phase is next — run the loop to let the architect design the execution plan, then approve again.");
                }
                else
                {
                    Console.WriteLine("Approved the current plan. Execution work can now continue.");
                }
            }
            return 0;
        }

        case "set-auto-approve":
        {
            var state = store.Load();
            var requested = GetPositionalValue(options) ?? GetOption(options, "enabled")
                ?? throw new InvalidOperationException("Usage: set-auto-approve <true|false> [--workspace PATH]");
            var enabled = ParseBoolOrThrow(requested, "Usage: set-auto-approve <true|false> [--workspace PATH]");
            runtime.SetAutoApprove(state, enabled);
            store.Save(state);
            Console.WriteLine($"Updated auto-approve setting to {(enabled ? "enabled" : "disabled")}.");
            return 0;
        }

        case "feedback":
        {
            var state = store.Load();
            var feedback = GetPositionalValue(options) ?? throw new InvalidOperationException("Missing feedback text.");
            runtime.RecordPlanningFeedback(state, feedback);
            store.Save(state);
            Console.WriteLine(state.Phase == WorkflowPhase.ArchitectPlanning
                ? "Captured architect plan feedback."
                : "Captured planning feedback.");
            return 0;
        }

        case "run-once":
        {
            var state = store.Load();
            if (PlanWorkflow.RequiresPlanningBeforeRun(state, store))
            {
                Console.WriteLine("No plan has been written yet. Run `plan` first.");
                return 1;
            }
            var maxSubagents = GetIntOption(options, "max-subagents", 3);
            var result = runtime.RunOnce(state, maxSubagents);
            store.Save(state);
            PrintLoopResult(result);
            return 0;
        }

        case "run-loop":
        case "run":
        {
            var state = store.Load();
            if (PlanWorkflow.RequiresPlanningBeforeRun(state, store))
            {
                Console.WriteLine("No plan has been written yet. Run `plan` first.");
                return 1;
            }
            var report = await RunLoopAsync(store, runtime, loopExecutor, state, options);
            Console.WriteLine($"Loop complete after {report.IterationsExecuted} iteration(s). Final state: {report.FinalState}");
            PrintBudget(state.Budget);
            if (report.FinalState == "awaiting-architect-approval")
            {
                PrintArchitectSummary(state);
            }
            return 0;
        }

        case "complete-run":
        {
            var state = store.Load();
            var runId = GetNullableIntOption(options, "run-id") ?? throw new InvalidOperationException("Missing --run-id.");
            var outcome = GetOption(options, "outcome") ?? throw new InvalidOperationException("Missing --outcome.");
            var summary = GetOption(options, "summary") ?? throw new InvalidOperationException("Missing --summary.");
            runtime.CompleteRun(state, runId, outcome, summary);
            store.Save(state);
            Console.WriteLine($"Updated run #{runId} as {outcome}");
            return 0;
        }

        case "status":
            PrintStatus(store.Load(), runtime);
            return 0;

        case "questions":
        {
            var state = store.Load();
            PrintQuestions(state, store);
            return 0;
        }

        case "plan":
        {
            var state = store.Load();
            await ShowPlanAsync(store, runtime, loopExecutor, state, options, interactive: false);
            return 0;
        }

        case "budget":
        {
            var state = store.Load();
            UpdateBudget(state, options);
            store.Save(state);
            PrintBudget(state.Budget);
            return 0;
        }

        case "agent-invoke":
        {
            var backend = GetOption(options, "backend") ?? "sdk";
            var prompt = GetOption(options, "prompt") ?? GetPositionalValue(options)
                ?? throw new InvalidOperationException("Missing prompt text.");
            var model = GetOption(options, "model");
            var timeoutSeconds = GetIntOption(options, "timeout-seconds", 1200);
            var workingDirectory = GetOption(options, "working-directory") ?? Environment.CurrentDirectory;
            var extraArgs = options.TryGetValue("extra-arg", out var values)
                ? values
                : [];

            var client = AgentClientFactory.Create(backend);
            var result = await client.InvokeAsync(new AgentInvocationRequest
            {
                Prompt = prompt,
                Model = model,
                WorkingDirectory = Path.GetFullPath(workingDirectory),
                Timeout = TimeSpan.FromSeconds(timeoutSeconds),
                ExtraArguments = extraArgs,
                WorkspacePath = Path.GetFullPath(workspacePath),
                EnableWorkspaceMcp = GetBoolOption(options, "workspace-mcp", false),
                ToolHostPath = System.Reflection.Assembly.GetEntryAssembly()?.Location
            });

            Console.WriteLine($"Backend: {result.BackendName}");
            Console.WriteLine($"Exit code: {result.ExitCode}");
            if (!string.IsNullOrWhiteSpace(result.StdOut))
            {
                Console.WriteLine(result.StdOut.TrimEnd());
            }
            if (!string.IsNullOrWhiteSpace(result.StdErr))
            {
                Console.Error.WriteLine(result.StdErr.TrimEnd());
            }
            return result.Success ? 0 : result.ExitCode;
        }

        default:
            PrintHelp();
            return 1;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static async Task<int> RunShellAsync(
    WorkspaceStore store,
    DevTeamRuntime runtime,
    LoopExecutor loopExecutor,
    ToolUpdateService toolUpdateService,
    Dictionary<string, List<string>> startOptions)
{
    var startOpts = new ShellStartOptions(startOptions);
    var host = Host.CreateDefaultBuilder()
        .UseRazorConsole<DevTeamShell>(configure =>
        {
            configure.ConfigureServices(services =>
            {
                services.Configure<ConsoleAppOptions>(opts =>
                {
                    opts.AutoClearConsole = false;
                    opts.EnableTerminalResizing = true;
                });
                services.AddSingleton(store);
                services.AddSingleton(runtime);
                services.AddSingleton(loopExecutor);
                services.AddSingleton(toolUpdateService);
                services.AddSingleton(startOpts);
                services.AddSingleton<ShellService>();
                services.AddTransient<ITranslationMiddleware, RawSpectreMarkupTranslator>();
            });
        })
        .Build();
    await host.RunAsync();
    return 0;
}

static async Task<int> RunInteractiveShellAsync(
    WorkspaceStore store,
    DevTeamRuntime runtime,
    LoopExecutor loopExecutor,
    ToolUpdateService toolUpdateService,
    Dictionary<string, List<string>> startOptions)
{
    using var keepAwakeController = new KeepAwakeController();
    var shellDiagnostics = new ShellSessionDiagnostics();
    var shellKeepAwakeEnabled = GetNullableBoolOption(startOptions, "keep-awake");
    ChatConsole.WriteBanner(store.WorkspacePath);
    await NotifyAboutAvailableUpdateAsync(toolUpdateService);
    TryLoadState(store, out var initialState);
    if (initialState is null)
    {
        ChatConsole.WriteNoWorkspace();
    }
    if (shellKeepAwakeEnabled is null)
    {
        shellKeepAwakeEnabled = initialState?.Runtime.KeepAwakeEnabled ?? false;
    }
    ApplyKeepAwakeSetting(keepAwakeController, shellKeepAwakeEnabled.Value, interactiveShell: true, Console.WriteLine);

    // ReadLine removed — shell uses RazorConsole TextInput

    // Background loop tracking.
    Task<LoopExecutionReport>? loopTask = null;
    CancellationTokenSource? loopCts = null;
    bool IsLoopRunning() => loopTask is { IsCompleted: false };
    string? lastContextKey = null;

    // Queue for log messages from background loop threads — drains before each prompt and on loop complete.
    var pendingLog = new ConcurrentQueue<string>();
    Action<string> backgroundLogger = msg => pendingLog.Enqueue(msg);
    void DrainPendingLog()
    {
        while (pendingLog.TryDequeue(out var m))
            ChatConsole.WriteLoopLog(m);
    }

    // In-memory session history — last 50 entries.
    var sessionHistory = new List<(DateTimeOffset At, string Entry)>();
    void AddHistory(string entry)
    {
        sessionHistory.Add((DateTimeOffset.Now, entry));
        if (sessionHistory.Count > 50)
            sessionHistory.RemoveAt(0);
    }

    // Attach a fire-and-forget continuation so loop completion surfaces immediately,
    // even while Console.ReadLine() is blocking on the main thread.
    void NotifyOnLoopComplete(Task<LoopExecutionReport> task)
    {
        task.ContinueWith(t =>
        {
            // Clear any partial input line and flush buffered log messages first.
            Console.Write("\r\x1b[K");
            DrainPendingLog();
            try
            {
                if (t.IsCanceled || (t.IsFaulted && t.Exception?.GetBaseException() is OperationCanceledException))
                {
                    ChatConsole.WriteWarning("Loop was cancelled.");
                }
                else if (t.IsFaulted)
                {
                    ChatConsole.WriteError($"Loop failed: {t.Exception?.GetBaseException().Message}");
                }
                else
                {
                    var r = t.Result;
                    var completedState = store.Load();
                    ChatConsole.WriteSystem(
                        $"Loop finished — [bold]{r.IterationsExecuted}[/] iteration(s). Final state: [cyan]{Markup.Escape(r.FinalState)}[/]",
                        "loop complete");
                    AddHistory($"loop complete — {r.IterationsExecuted} iteration(s), state: {r.FinalState}");
                    PrintBudget(completedState.Budget);
                    if (r.FinalState == "awaiting-architect-approval")
                    {
                        PrintArchitectSummary(completedState);
                    }
                }
            }
            catch { /* swallow — we're on a background continuation */ }
            // Reprint the prompt so the cursor line is clean.
            var prompt = IsLoopRunning() ? "devteam (running)> " : "devteam> ";
            ChatConsole.WritePrompt(prompt);
            lastContextKey = null; // force context refresh on next input
        }, TaskScheduler.Default);
    }

    while (true)
    {
        // Edge case: if the task completed between the ContinueWith and here, clear it.
        if (loopTask is { IsCompleted: true })
        {
            loopTask = null;
            loopCts?.Dispose();
            loopCts = null;
        }

        WorkspaceState? state = null;
        List<QuestionItem> openQuestions = [];
        try
        {
            state = store.Load();
            openQuestions = state.Questions.Where(item => item.Status == QuestionStatus.Open).ToList();

            var planAwaiting = !IsLoopRunning()
                && state.Phase == WorkflowPhase.Planning
                && state.Issues.Any(i => i.IsPlanningIssue && i.Status == ItemStatus.Done)
                && openQuestions.Count == 0;
            var archAwaiting = !IsLoopRunning()
                && PlanWorkflow.IsAwaitingArchitectApproval(state)
                && openQuestions.Count == 0;
            var contextKey = $"{state.Phase}|q:{string.Join(",", openQuestions.Select(q => q.Id))}|pa:{planAwaiting}|arch:{archAwaiting}|loop:{IsLoopRunning()}";

            if (contextKey != lastContextKey)
            {
                lastContextKey = contextKey;
                if (openQuestions.Count > 0 && !IsLoopRunning())
                {
                    var q = openQuestions[0];
                    ChatConsole.WriteQuestion(q.Text, q.Id, q.IsBlocking, 1, openQuestions.Count);
                    ChatConsole.WriteHint(openQuestions.Count > 1
                        ? $"Type your answer (question 1 of {openQuestions.Count}), or [dim]/questions[/] to see all."
                        : "Type your answer below.");
                }
                else if (openQuestions.Count > 0)
                {
                    ChatConsole.WriteWarning($"{openQuestions.Count} open question(s) — the loop may be paused. Use /answer <id> <text>.");
                }
                else if (planAwaiting)
                {
                    ChatConsole.WriteSystem(
                        "A plan is ready. Type [bold]approve[/] to proceed into execution, or type feedback to revise it. Use [dim]/plan[/] to review.",
                        "plan ready ✓");
                }
                else if (archAwaiting)
                {
                    ChatConsole.WriteSystem(
                        "The architect plan is ready. Type [bold]approve[/] to begin execution, or share feedback to revise.",
                        "architect plan ready ✓");
                }
            }
        }
        catch
        {
        }

        DrainPendingLog();
        var promptText = IsLoopRunning() ? "devteam (running)> " : "devteam> ";
        ChatConsole.WritePrompt(promptText);
        var line = Console.ReadLine();
        if (line is null)
        {
            return 0;
        }

        line = line.Trim();
        if (line.Length == 0)
        {
            continue;
        }

        AddHistory(line);

        if (line.StartsWith("@", StringComparison.Ordinal) && state is not null)
        {
            var spaceIndex = line.IndexOf(' ');
            var roleInput = spaceIndex > 1 ? line[1..spaceIndex] : line[1..];
            var userMessage = spaceIndex > 1 ? line[(spaceIndex + 1)..].Trim() : "";
            if (string.IsNullOrWhiteSpace(userMessage))
            {
                ChatConsole.WriteHint("Usage: [cyan]@role[/] <message>");
                continue;
            }
            if (!runtime.TryResolveRoleSlug(state, roleInput, out var roleSlug))
            {
                ChatConsole.WriteWarning($"Unknown role '{roleInput}'. Known roles: {string.Join(", ", runtime.GetKnownRoleSlugs(state).OrderBy(s => s))}");
                continue;
            }
            ChatConsole.WriteHint($"[italic]You → {Markup.Escape(roleSlug)}:[/] [dim]{Markup.Escape(userMessage.Length > 120 ? userMessage[..117] + "..." : userMessage)}[/]");
            await InvokeRoleDirectAsync(store, runtime, state, roleSlug, userMessage);
            continue;
        }

        if (!line.StartsWith("/", StringComparison.Ordinal) && state is not null)
        {
            // Answer the currently shown question (the first open one).
            if (openQuestions.Count > 0 && !IsLoopRunning())
            {
                var q = openQuestions[0];
                runtime.AnswerQuestion(state, q.Id, line);
                store.Save(state);
                var stillOpen = state.Questions.Count(question => question.Status == QuestionStatus.Open);
                ChatConsole.WriteSuccess(stillOpen == 0
                    ? "Got it! All questions answered."
                    : $"Got it! {stillOpen} question(s) remaining.");
                lastContextKey = null; // force refresh so the next question shows automatically
                continue;
            }

            // "approve" / "yes" shortcut — acts like /approve and immediately starts the loop.
            if (IsApproveIntent(line))
            {
                if (IsLoopRunning())
                {
                    ChatConsole.WriteWarning("A loop is running. Use /stop first, then approve.");
                    continue;
                }
                var canApproveArch = state.Phase == WorkflowPhase.ArchitectPlanning && PlanWorkflow.IsAwaitingArchitectApproval(state);
                var canApprovePlan = state.Phase == WorkflowPhase.Planning && state.Issues.Any(i => i.IsPlanningIssue && i.Status == ItemStatus.Done);
                if (!canApproveArch && !canApprovePlan)
                {
                    ChatConsole.WriteHint("Nothing to approve right now. Use [dim]/plan[/] to generate a plan first.");
                    continue;
                }
                if (canApproveArch)
                {
                    runtime.ApproveArchitectPlan(state, "Approved via chat.");
                    store.Save(state);
                    ChatConsole.WriteSuccess("Architect plan approved. Starting execution loop...");
                }
                else
                {
                    runtime.ApprovePlan(state, "Approved via chat.");
                    store.Save(state);
                    ChatConsole.WriteSuccess(state.Phase == WorkflowPhase.ArchitectPlanning
                        ? "Plan approved. Running architect planning..."
                        : "Plan approved. Starting execution loop...");
                }
                loopCts = new CancellationTokenSource();
                var approveCts = loopCts;
                loopTask = RunLoopAsync(store, runtime, loopExecutor, state, ParseOptions([]), interactiveShell: false, chatLogging: true, approveCts.Token, overrideLogger: backgroundLogger);
                NotifyOnLoopComplete(loopTask);
                ChatConsole.WriteHint("/stop to cancel · /wait to re-attach");
                lastContextKey = null;
                continue;
            }

            if ((state.Phase == WorkflowPhase.Planning
                    && state.Issues.Any(item => item.IsPlanningIssue && item.Status == ItemStatus.Done))
                || PlanWorkflow.IsAwaitingArchitectApproval(state))
            {
                if (IsLoopRunning())
                {
                    ChatConsole.WriteWarning("A loop is running. Use /stop first before sending planning feedback.");
                    continue;
                }
                runtime.RecordPlanningFeedback(state, line);
                store.Save(state);
                ChatConsole.WriteEvent("✎", state.Phase == WorkflowPhase.ArchitectPlanning
                    ? "Captured architect plan feedback. Revising..."
                    : "Captured planning feedback. Revising...", "cyan");
                var report = await RunLoopAsync(store, runtime, loopExecutor, state, ParseOptions(["--max-iterations", "2"]), interactiveShell: true);
                ChatConsole.WriteEvent("✓", $"Revision complete ({report.IterationsExecuted} iteration(s)). Final state: {report.FinalState}", "green");
                ChatConsole.WriteHint(state.Phase == WorkflowPhase.ArchitectPlanning
                    ? "Type [bold]approve[/] to begin execution, or use [dim]/plan[/] to review."
                    : "Type [bold]approve[/] to proceed, or use [dim]/plan[/] to review.");
                lastContextKey = null;
                continue;
            }
        }

        var tokens = TokenizeInput(line);
        if (tokens.Count == 0)
        {
            continue;
        }

        var command = NormalizeCommand(tokens[0]);
        var options = ParseOptions(tokens.Skip(1).ToArray());

        try
        {
            if (!string.Equals(command, "bug", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(command, "bug-report", StringComparison.OrdinalIgnoreCase))
            {
                shellDiagnostics.RecordCommand(line);
            }

            CommandOptionValidator.ValidateInteractive(command, options);

            switch (command)
            {
                case "exit":
                case "quit":
                    if (IsLoopRunning())
                    {
                        loopCts?.Cancel();
                    }
                    return 0;

                case "help":
                    PrintInteractiveHelp();
                    break;

                case "check-update":
                    await CheckForToolUpdatesAsync(toolUpdateService);
                    break;

                case "update":
                    return await ScheduleToolUpdateAsync(toolUpdateService, interactiveShell: true);

                case "customize":
                {
                    var target = Path.Combine(Environment.CurrentDirectory, ".devteam-source");
                    var force = GetBoolOption(options, "force", false);
                    CopyPackagedAssets(target, force);
                    break;
                }

                case "bug":
                case "bug-report":
                {
                    EmitBugReport(store, runtime, options, shellDiagnostics);
                    break;
                }

                case "init":
                {
                    var force = GetBoolOption(options, "force", false);
                    if (!force && File.Exists(store.StatePath))
                    {
                        Console.WriteLine($"Workspace already initialized at {store.WorkspacePath}.");
                        Console.WriteLine("Use /init --force to reinitialize (this will reset all workspace state).");
                        break;
                    }
                    if (force && IsLoopRunning())
                    {
                        Console.WriteLine($"A loop is running. Use {ConsoleTheme.Command("/stop")} first before reinitializing the workspace.");
                        break;
                    }
                    var totalCap = GetDoubleOption(options, "total-credit-cap", 25);
                    var premiumCap = GetDoubleOption(options, "premium-credit-cap", 6);
                    var goal = GoalInputResolver.Resolve(
                        GetOption(options, "goal") ?? GetPositionalValue(options),
                        GetOption(options, "goal-file"),
                        Environment.CurrentDirectory);
                    var gitInitialized = GitWorkspace.EnsureRepository(Environment.CurrentDirectory);
                    var initialized = store.Initialize(Environment.CurrentDirectory, totalCap, premiumCap);
                    var mode = GetOption(options, "mode");
                    initialized.Runtime.KeepAwakeEnabled = GetBoolOption(options, "keep-awake", initialized.Runtime.KeepAwakeEnabled);
                    initialized.Runtime.WorkspaceMcpEnabled = GetBoolOption(options, "workspace-mcp", true);
                    initialized.Runtime.PipelineSchedulingEnabled = GetBoolOption(options, "pipeline-scheduling", true);
                    if (!string.IsNullOrWhiteSpace(mode))
                    {
                        runtime.SetMode(initialized, mode);
                    }
                    if (!string.IsNullOrWhiteSpace(goal))
                    {
                        runtime.SetGoal(initialized, goal);
                    }
                    store.Save(initialized);
                    Console.WriteLine($"Initialized workspace at {store.WorkspacePath}");
                    if (gitInitialized)
                    {
                        Console.WriteLine($"Initialized git repository at {Path.GetFullPath(Environment.CurrentDirectory)}");
                    }
                    if (!string.IsNullOrWhiteSpace(goal))
                    {
                        Console.WriteLine($"Active goal saved: {goal}");
                    }
                    Console.WriteLine();
                    Console.WriteLine($"Next step: run {ConsoleTheme.Command("/plan")} to generate the high-level plan.");
                    break;
                }

                case "status":
                    PrintStatus(store.Load(), runtime);
                    break;

                case "history":
                {
                    if (sessionHistory.Count == 0)
                    {
                        ChatConsole.WriteHint("No session history yet.");
                        break;
                    }
                    var histTable = new Table { Border = TableBorder.Rounded, Expand = false };
                    histTable.AddColumn(new TableColumn("[dim]+mm:ss[/]").RightAligned());
                    histTable.AddColumn("[dim]Entry[/]");
                    var start = sessionHistory[0].At;
                    foreach (var (at, entry) in sessionHistory)
                    {
                        var elapsed = at - start;
                        histTable.AddRow(
                            $"[dim]+{(int)elapsed.TotalMinutes:D2}:{elapsed.Seconds:D2}[/]",
                            Markup.Escape(entry));
                    }
                    AnsiConsole.Write(histTable);
                    break;
                }

                case "add-issue":
                {
                    var current = store.Load();
                    var title = GetPositionalValue(options) ?? throw new InvalidOperationException("Missing issue title.");
                    var role = GetOption(options, "role") ?? throw new InvalidOperationException(BuildMissingRoleMessage(runtime, current));
                    var detail = GetOption(options, "detail") ?? "";
                    var area = GetOption(options, "area");
                    var priority = GetIntOption(options, "priority", 50);
                    var roadmapId = GetNullableIntOption(options, "roadmap-item-id");
                    var dependsOn = GetMultiIntOption(options, "depends-on");
                    ValidateRoleOrThrow(runtime, current, role);
                    var issue = runtime.AddIssue(current, title, detail, role, priority, roadmapId, dependsOn, area);
                    store.Save(current);
                    Console.WriteLine($"Created issue #{issue.Id}: {issue.Title} ({issue.RoleSlug}{(string.IsNullOrWhiteSpace(issue.Area) ? "" : $", area {issue.Area}")})");
                    break;
                }

                case "questions":
                    PrintQuestions(store.Load(), store);
                    break;

                case "plan":
                {
                    var current = store.Load();
                    await ShowPlanAsync(store, runtime, loopExecutor, current, options, interactive: true);
                    break;
                }

                case "goal":
                case "set-goal":
                {
                    var current = store.Load();
                    var goal = GoalInputResolver.Resolve(
                        GetPositionalValue(options),
                        GetOption(options, "goal-file"),
                        Environment.CurrentDirectory)
                        ?? throw new InvalidOperationException("Missing goal text. Provide inline text or --goal-file PATH.");
                    runtime.SetGoal(current, goal);
                    store.Save(current);
                    Console.WriteLine("Updated active goal.");
                    break;
                }

                case "mode":
                case "set-mode":
                {
                    var current = store.Load();
                    var mode = GetPositionalValue(options) ?? throw new InvalidOperationException("Usage: /mode <slug>");
                    runtime.SetMode(current, mode);
                    store.Save(current);
                    Console.WriteLine($"Updated active mode to {current.Runtime.ActiveModeSlug}.");
                    break;
                }

                case "keep-awake":
                case "set-keep-awake":
                {
                    var current = TryLoadState(store, out var loadedState) ? loadedState : null;
                    var requested = GetPositionalValue(options) ?? GetOption(options, "enabled");
                    if (string.IsNullOrWhiteSpace(requested))
                    {
                        var persisted = current?.Runtime.KeepAwakeEnabled;
                        Console.WriteLine($"Keep-awake for this shell: {(shellKeepAwakeEnabled == true ? "enabled" : "disabled")}.");
                        if (persisted is not null)
                        {
                            Console.WriteLine($"Workspace default: {(persisted.Value ? "enabled" : "disabled")}.");
                        }
                        break;
                    }

                    var enabled = ParseBoolOrThrow(requested, "Usage: /keep-awake <on|off>");
                    shellKeepAwakeEnabled = enabled;
                    ApplyKeepAwakeSetting(keepAwakeController, enabled, interactiveShell: true);
                    if (current is not null)
                    {
                        runtime.SetKeepAwake(current, enabled);
                        store.Save(current);
                        Console.WriteLine($"Keep-awake {(enabled ? "enabled" : "disabled")} for this shell and workspace.");
                    }
                    else
                    {
                        Console.WriteLine($"Keep-awake {(enabled ? "enabled" : "disabled")} for this shell session.");
                    }
                    break;
                }

                case "approve":
                case "approve-plan":
                {
                    if (IsLoopRunning())
                    {
                        ChatConsole.WriteWarning("A loop is running. Use /stop first, then /approve.");
                        break;
                    }
                    var current = store.Load();
                    var note = GetOption(options, "note") ?? GetPositionalValue(options) ?? "User approved the current plan.";
                    if (current.Phase == WorkflowPhase.ArchitectPlanning && PlanWorkflow.IsAwaitingArchitectApproval(current))
                    {
                        runtime.ApproveArchitectPlan(current, note);
                        store.Save(current);
                        ChatConsole.WriteSuccess("Architect plan approved. Starting execution loop...");
                        loopCts = new CancellationTokenSource();
                        var approveArchCts = loopCts;
                        loopTask = RunLoopAsync(store, runtime, loopExecutor, current, ParseOptions([]), interactiveShell: false, chatLogging: true, approveArchCts.Token, overrideLogger: backgroundLogger);
                        NotifyOnLoopComplete(loopTask);
                        ChatConsole.WriteHint("/stop to cancel · /wait to re-attach");
                    }
                    else
                    {
                        runtime.ApprovePlan(current, note);
                        store.Save(current);
                        ChatConsole.WriteSuccess(current.Phase == WorkflowPhase.ArchitectPlanning
                            ? "Plan approved. Running architect planning..."
                            : "Plan approved. Starting execution loop...");
                        loopCts = new CancellationTokenSource();
                        var approvePlanCts = loopCts;
                        loopTask = RunLoopAsync(store, runtime, loopExecutor, current, ParseOptions([]), interactiveShell: false, chatLogging: true, approvePlanCts.Token, overrideLogger: backgroundLogger);
                        NotifyOnLoopComplete(loopTask);
                        ChatConsole.WriteHint("/stop to cancel · /wait to re-attach");
                    }
                    lastContextKey = null;
                    break;
                }

                case "set-auto-approve":
                case "auto-approve":
                {
                    var current = TryLoadState(store, out var loadedState) ? loadedState : null;
                    var requested = GetPositionalValue(options) ?? GetOption(options, "enabled");
                    if (string.IsNullOrWhiteSpace(requested))
                    {
                        Console.WriteLine($"Auto-approve: {(current?.Runtime.AutoApproveEnabled == true ? "enabled" : "disabled")}.");
                        break;
                    }
                    var enabled = ParseBoolOrThrow(requested, "Usage: /auto-approve <on|off>");
                    if (current is not null)
                    {
                        runtime.SetAutoApprove(current, enabled);
                        store.Save(current);
                    }
                    Console.WriteLine($"Auto-approve {(enabled ? "enabled" : "disabled")}.");
                    break;
                }

                case "set-max-iterations":
                case "max-iterations":
                {
                    var current = TryLoadState(store, out var loadedState) ? loadedState : null;
                    var requested = GetPositionalValue(options) ?? GetOption(options, "value");
                    if (string.IsNullOrWhiteSpace(requested))
                    {
                        Console.WriteLine($"Default max-iterations: {current?.Runtime.DefaultMaxIterations ?? 10}. Usage: /max-iterations <N>");
                        break;
                    }
                    if (!int.TryParse(requested, out var n) || n < 1)
                    {
                        ChatConsole.WriteError("max-iterations must be a positive integer.");
                        break;
                    }
                    if (current is not null)
                    {
                        runtime.SetDefaultMaxIterations(current, n);
                        store.Save(current);
                        ChatConsole.WriteSuccess($"Default max-iterations set to {n}. All future /run calls will use this unless overridden.");
                    }
                    else
                    {
                        ChatConsole.WriteWarning("No workspace loaded — run /init first.");
                    }
                    break;
                }

                case "set-max-subagents":
                case "max-subagents":
                {
                    var current = TryLoadState(store, out var loadedState) ? loadedState : null;
                    var requested = GetPositionalValue(options) ?? GetOption(options, "value");
                    if (string.IsNullOrWhiteSpace(requested))
                    {
                        Console.WriteLine($"Default max-subagents: {current?.Runtime.DefaultMaxSubagents ?? 1}. Usage: /max-subagents <N>");
                        ChatConsole.WriteHint("1 = sequential (safe default) · 2–4 = parallel pipelines · higher = more concurrent agents");
                        break;
                    }
                    if (!int.TryParse(requested, out var n) || n < 1)
                    {
                        ChatConsole.WriteError("max-subagents must be a positive integer.");
                        break;
                    }
                    if (current is not null)
                    {
                        runtime.SetDefaultMaxSubagents(current, n);
                        store.Save(current);
                        ChatConsole.WriteSuccess($"Default max-subagents set to {n}. All future /run calls will use this unless overridden.");
                        if (n > 4)
                        {
                            ChatConsole.WriteHint("High subagent count can increase credit consumption quickly.");
                        }
                    }
                    else
                    {
                        ChatConsole.WriteWarning("No workspace loaded — run /init first.");
                    }
                    break;
                }

                case "feedback":
                {
                    if (IsLoopRunning())
                    {
                        ChatConsole.WriteWarning("A loop is running. Use /stop first, then /feedback.");
                        break;
                    }
                    var current = store.Load();
                    var feedback = GetPositionalValue(options) ?? throw new InvalidOperationException("Usage: /feedback <text>");
                    runtime.RecordPlanningFeedback(current, feedback);
                    store.Save(current);
                    ChatConsole.WriteEvent("✎", current.Phase == WorkflowPhase.ArchitectPlanning
                        ? "Captured architect plan feedback. Revising..."
                        : "Captured planning feedback. Revising...", "cyan");
                    var report = await RunLoopAsync(store, runtime, loopExecutor, current, ParseOptions(["--max-iterations", "2"]), interactiveShell: true);
                    ChatConsole.WriteEvent("✓", $"Revision complete ({report.IterationsExecuted} iteration(s)). Final state: {report.FinalState}", "green");
                    ChatConsole.WriteHint(current.Phase == WorkflowPhase.ArchitectPlanning
                        ? "Type [bold]approve[/] to begin execution, or use [dim]/plan[/] to review."
                        : "Type [bold]approve[/] to proceed, or use [dim]/plan[/] to review.");
                    lastContextKey = null;
                    break;
                }

                case "answer":
                case "answer-question":
                {
                    var current = store.Load();
                    var values = GetPositionalValues(options);
                    if (values.Count < 2)
                    {
                        throw new InvalidOperationException("Usage: /answer <id> <answer>");
                    }

                    runtime.AnswerQuestion(current, int.Parse(values[0]), string.Join(" ", values.Skip(1)));
                    store.Save(current);
                    var remaining = current.Questions.Count(q => q.Status == QuestionStatus.Open);
                    ChatConsole.WriteSuccess(remaining == 0
                        ? $"Got it! All questions answered."
                        : $"Got it! {remaining} question(s) remaining.");
                    lastContextKey = null;
                    break;
                }

                case "run":
                case "run-loop":
                {
                    if (IsLoopRunning())
                    {
                        Console.WriteLine($"A loop is already running. Use {ConsoleTheme.Command("/stop")} to cancel it or {ConsoleTheme.Command("/wait")} to re-attach.");
                        break;
                    }
                    var current = store.Load();
                    if (PlanWorkflow.RequiresPlanningBeforeRun(current, store))
                    {
                        Console.WriteLine("No plan has been written yet. Run /plan first.");
                        break;
                    }
                    if (PlanWorkflow.IsAwaitingApproval(current, store))
                    {
                        Console.WriteLine("A plan is ready. Use /plan to review, type feedback to revise, or /approve to continue.");
                        break;
                    }
                    // Show which defaults will be used if not explicitly overridden on this invocation
                    var usingIterations = options.ContainsKey("max-iterations") ? int.Parse(options["max-iterations"][0]) : current.Runtime.DefaultMaxIterations;
                    var usingSubagents = options.ContainsKey("max-subagents") ? int.Parse(options["max-subagents"][0]) : current.Runtime.DefaultMaxSubagents;
                    ChatConsole.WriteHint($"max-iterations: [bold]{usingIterations}[/] · max-subagents: [bold]{usingSubagents}[/] [dim](change with /max-iterations N or /max-subagents N)[/]");
                    loopCts = new CancellationTokenSource();
                    var bgOptions = options;
                    var bgCts = loopCts;
                    loopTask = RunLoopAsync(store, runtime, loopExecutor, current, bgOptions, interactiveShell: false, chatLogging: true, bgCts.Token, overrideLogger: backgroundLogger);
                    NotifyOnLoopComplete(loopTask);
                    ChatConsole.WriteEvent("🚀", "Loop started — running in the background. You can type commands while work progresses.", "green");
                    ChatConsole.WriteHint("/stop to cancel · /wait to re-attach · /status to check progress");
                    break;
                }

                case "stop":
                {
                    if (!IsLoopRunning())
                    {
                        ChatConsole.WriteHint("No loop is currently running.");
                        break;
                    }
                    loopCts?.Cancel();
                    ChatConsole.WriteWarning("Stop requested — the loop will finish its current agent call then stop.");
                    break;
                }

                case "wait":
                {
                    if (loopTask is null || loopTask.IsCompleted)
                    {
                        ChatConsole.WriteHint("No loop is currently running.");
                        break;
                    }
                    ChatConsole.WriteEvent("⏳", "Waiting for loop to complete...", "dim");
                    try
                    {
                        var completedReport = await loopTask;
                        var current = store.Load();
                        ChatConsole.WriteSystem(
                            $"Loop finished — [bold]{completedReport.IterationsExecuted}[/] iteration(s). Final state: [cyan]{Markup.Escape(completedReport.FinalState)}[/]",
                            "loop complete");
                        PrintBudget(current.Budget);
                        if (completedReport.FinalState == "awaiting-architect-approval")
                        {
                            PrintArchitectSummary(current);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        ChatConsole.WriteWarning("Loop was cancelled.");
                    }
                    loopTask = null;
                    loopCts?.Dispose();
                    loopCts = null;
                    lastContextKey = null;
                    break;
                }

                case "run-once":
                {
                    if (IsLoopRunning())
                    {
                        ChatConsole.WriteWarning("A loop is running. Use /stop first.");
                        break;
                    }
                    var current = store.Load();
                    if (PlanWorkflow.RequiresPlanningBeforeRun(current, store))
                    {
                        Console.WriteLine("No plan has been written yet. Run /plan first.");
                        break;
                    }
                    var result = runtime.RunOnce(current, GetIntOption(options, "max-subagents", 3));
                    store.Save(current);
                    PrintLoopResult(result);
                    PrintBudget(current.Budget);
                    break;
                }

                case "budget":
                {
                    var current = store.Load();
                    UpdateBudget(current, options);
                    store.Save(current);
                    PrintBudget(current.Budget);
                    break;
                }

                default:
                    var unknownCommandMessage = $"Unknown command '{ConsoleTheme.Warning(tokens[0])}'. Type {ConsoleTheme.Command("/help")}.";
                    shellDiagnostics.RecordError($"Unknown command: {tokens[0]}");
                    Console.WriteLine(unknownCommandMessage);
                    break;
            }
        }
        catch (Exception ex)
        {
            shellDiagnostics.RecordError(ex.Message);
            Console.WriteLine(ex.Message);
        }
    }
}

static async Task InvokeRoleDirectAsync(
    WorkspaceStore store,
    DevTeamRuntime runtime,
    WorkspaceState state,
    string roleSlug,
    string userMessage)
{
    var policy = runtime.GetRoleModelPolicy(state, roleSlug);
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
            Console.WriteLine(ConsoleTheme.Warning("Budget exhausted. Use /budget to increase."));
            return;
        }
    }

    var prompt = AgentPromptBuilder.BuildAdHocPrompt(state, roleSlug, userMessage);
    var sessionId = $"devteam-adhoc-{roleSlug}";
    ChatConsole.WriteEvent("→", $"Asking {roleSlug} via {model}...", "dim cyan");

    var client = AgentClientFactory.Create("sdk");
    try
    {
        var response = await client.InvokeAsync(new AgentInvocationRequest
        {
            Prompt = prompt,
            Model = model,
            SessionId = sessionId,
            WorkingDirectory = state.RepoRoot,
            WorkspacePath = store.WorkspacePath,
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

        runtime.MergeWorkspaceAdditions(state, store.Load());
        store.Save(state);
    }
    catch (Exception ex)
    {
        Console.WriteLine(ConsoleTheme.Error($"Agent error: {ex.Message}"));
    }
}

static async Task<LoopExecutionReport> RunLoopAsync(
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
    var backend = GetOption(options, "backend") ?? "sdk";
    var maxSubagents = GetIntOption(options, "max-subagents", state.Runtime.DefaultMaxSubagents);
    var maxIterations = GetIntOption(options, "max-iterations", state.Runtime.DefaultMaxIterations);
    var timeoutSeconds = GetIntOption(options, "timeout-seconds", 600);
    var verbosity = ParseVerbosity(GetOption(options, "verbosity"));
    using var keepAwakeController = new KeepAwakeController();
    // Don't use the cursor-clearing renderer when running in background (interactiveShell=false);
    // it would race with user input. Plain Console.WriteLine is used instead.
    using var renderer = interactiveShell && !Console.IsOutputRedirected
        ? new LoopConsoleRenderer()
        : null;
    Action<IReadOnlyList<RunProgressSnapshot>>? progressReporter = renderer is null ? null : snapshots => renderer.ReportProgress(snapshots);
    Action<string> logger = overrideLogger is not null
        ? overrideLogger
        : renderer is not null
            ? message => renderer.Log(message)
            : chatLogging
                ? ChatConsole.WriteLoopLog
                : Console.WriteLine;
    var keepAwakeEnabled = ResolveKeepAwakeEnabled(state, options);
    ApplyKeepAwakeSetting(keepAwakeController, keepAwakeEnabled, interactiveShell, logger);
    var executionOptions = new LoopExecutionOptions
    {
        Backend = backend,
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

static async Task ShowPlanAsync(
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
        if (PrintArchitectSummary(state))
        {
            return;
        }

        Console.WriteLine($"High-level plan approved. Phase: {ConsoleTheme.Phase("ArchitectPlanning")}");
        Console.WriteLine(interactive
            ? $"Run {ConsoleTheme.Command("/run")} to let the architect create execution issues, then {ConsoleTheme.Command("/approve")} again."
            : "Run the loop to let the architect create execution issues, then approve again.");
        return;
    }

    PrintPlan(store);
    PrintOpenQuestions(state, interactive);

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

static Dictionary<string, List<string>> BuildPlanningOptions(Dictionary<string, List<string>> options)
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

static void PrintStatus(WorkspaceState state, DevTeamRuntime runtime)
{
    var report = runtime.BuildStatusReport(state);

    // Header line
    AnsiConsole.MarkupLine($"[bold]Phase:[/] [cyan]{Markup.Escape(state.Phase.ToString())}[/]  " +
        $"[bold]Mode:[/] [cyan]{Markup.Escape(state.Runtime.ActiveModeSlug)}[/]  " +
        $"[bold]Max-iter:[/] {state.Runtime.DefaultMaxIterations}  " +
        $"[bold]Max-sub:[/] {state.Runtime.DefaultMaxSubagents}");

    // Issue table
    var issues = state.Issues.OrderBy(i => i.Id).ToList();
    if (issues.Count > 0)
    {
        var table = new Table { Border = TableBorder.Rounded, Expand = false };
        table.AddColumn(new TableColumn("[dim]#[/]").RightAligned());
        table.AddColumn("[dim]Role[/]");
        table.AddColumn("[dim]Status[/]");
        table.AddColumn("[dim]Title[/]");
        table.AddColumn(new TableColumn("[dim]Pri[/]").RightAligned());
        table.AddColumn("[dim]Pipeline[/]");

        foreach (var issue in issues)
        {
            var statusColor = issue.Status switch
            {
                ItemStatus.Done => "green",
                ItemStatus.InProgress => "yellow",
                ItemStatus.Blocked => "red",
                _ => "dim"
            };
            var title = issue.Title.Length > 60 ? issue.Title[..57] + "..." : issue.Title;
            var pipelineCell = FormatPipelineCell(state, issue);
            table.AddRow(
                $"[dim]{issue.Id}[/]",
                $"[cyan]{Markup.Escape(issue.RoleSlug)}[/]",
                $"[{statusColor}]{issue.Status}[/]",
                Markup.Escape(title),
                $"[dim]{issue.Priority}[/]",
                pipelineCell);
        }
        AnsiConsole.Write(table);
    }
    else
    {
        AnsiConsole.MarkupLine("[dim]No issues.[/]");
    }

    // Budget summary
    AnsiConsole.MarkupLine(
        $"[bold]Budget:[/] {report.Budget.CreditsCommitted:0.##}/{report.Budget.TotalCreditCap} credits " +
        $"[dim]({report.Budget.TotalCreditCap - report.Budget.CreditsCommitted:0.##} remaining)[/]  " +
        $"premium {report.Budget.PremiumCreditsCommitted:0.##}/{report.Budget.PremiumCreditCap} " +
        $"[dim]({report.Budget.PremiumCreditCap - report.Budget.PremiumCreditsCommitted:0.##} remaining)[/]");

    // Open questions
    if (report.OpenQuestions.Count > 0)
    {
        AnsiConsole.MarkupLine($"\n[bold yellow]{report.OpenQuestions.Count} open question(s)[/]");
        foreach (var q in report.OpenQuestions)
        {
            var tag = q.IsBlocking ? "[yellow]blocking[/]" : "[dim]non-blocking[/]";
            AnsiConsole.MarkupLine($"  [dim]#{q.Id}[/] [{tag}] {Markup.Escape(q.Text)}");
        }
    }

    // Queued/running runs
    if (report.QueuedRuns.Count > 0)
    {
        AnsiConsole.MarkupLine($"\n[bold]{report.QueuedRuns.Count} queued/running run(s)[/]");
        foreach (var run in report.QueuedRuns)
        {
            AnsiConsole.MarkupLine($"  [dim]#{run.Id}[/] issue [dim]#{run.IssueId}[/] [cyan]{Markup.Escape(run.RoleSlug)}[/] via {Markup.Escape(run.ModelName)}");
        }
    }
}

static string FormatPipelineCell(WorkspaceState state, IssueItem issue)
{
    if (issue.PipelineId is null)
    {
        return "[dim]—[/]";
    }
    var pipeline = state.Pipelines.FirstOrDefault(p => p.Id == issue.PipelineId);
    if (pipeline is null)
    {
        return $"[dim]#{issue.PipelineId}[/]";
    }
    var stageIndex = issue.PipelineStageIndex ?? 0;
    var roles = pipeline.RoleSequence;
    var parts = new List<string>();
    for (var i = 0; i < roles.Count; i++)
    {
        var r = Markup.Escape(roles[i]);
        if (i == stageIndex)
            parts.Add($"[bold cyan]{r}[/]");
        else if (i < stageIndex)
            parts.Add($"[green]{r}[/]");
        else
            parts.Add($"[dim]{r}[/]");
    }
    return $"[dim]#{pipeline.Id}[/] {string.Join(" → ", parts)}";
}

static void PrintBudget(BudgetState budget)
{
    Console.WriteLine(
        $"{ConsoleTheme.Label("Budget:")} {ConsoleTheme.BudgetUsage(budget.CreditsCommitted, budget.TotalCreditCap)} credits " +
        $"({ConsoleTheme.Number($"{budget.TotalCreditCap - budget.CreditsCommitted:0.##}")} remaining), " +
        $"premium {ConsoleTheme.BudgetUsage(budget.PremiumCreditsCommitted, budget.PremiumCreditCap)} " +
        $"({ConsoleTheme.Number($"{budget.PremiumCreditCap - budget.PremiumCreditsCommitted:0.##}")} remaining)");
}

static bool PrintArchitectSummary(WorkspaceState state)
{
    var architectRuns = state.AgentRuns
        .Where(run => string.Equals(run.RoleSlug, "architect", StringComparison.OrdinalIgnoreCase)
            && run.Status == AgentRunStatus.Completed
            && !string.IsNullOrWhiteSpace(run.Summary))
        .OrderByDescending(run => run.UpdatedAtUtc)
        .Take(1)
        .ToList();
    if (architectRuns.Count == 0)
    {
        return false;
    }

    Console.WriteLine();
    Console.WriteLine(ConsoleTheme.Label("─── Architect Summary ───"));
    foreach (var run in architectRuns)
    {
        var issue = state.Issues.FirstOrDefault(i => i.Id == run.IssueId);
        if (issue is not null)
        {
            Console.WriteLine($"  {ConsoleTheme.Accent(issue.Title)}");
        }
        foreach (var summaryLine in run.Summary.Split('\n'))
        {
            Console.WriteLine($"  {summaryLine}");
        }
    }

    var createdIssues = state.Issues
        .Where(i => !i.IsPlanningIssue
            && !string.Equals(i.RoleSlug, "architect", StringComparison.OrdinalIgnoreCase)
            && i.Status != ItemStatus.Done)
        .OrderByDescending(i => i.Priority)
        .ThenBy(i => i.Id)
        .ToList();
    if (createdIssues.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine(ConsoleTheme.Label($"Execution issues created ({createdIssues.Count}):"));
        foreach (var issue in createdIssues)
        {
            Console.WriteLine($"  #{ConsoleTheme.Number(issue.Id.ToString())} [{ConsoleTheme.Role(issue.RoleSlug)}] {issue.Title}");
        }
    }

    Console.WriteLine();
    Console.WriteLine($"Use {ConsoleTheme.Command("/approve")} to begin execution or type feedback to revise.");
    return true;
}

static void PrintQuestions(WorkspaceState state, WorkspaceStore store)
{
    var openQuestions = state.Questions
        .Where(item => item.Status == QuestionStatus.Open)
        .OrderBy(item => item.Id)
        .ToList();
    Console.WriteLine($"Questions file: {Path.Combine(store.WorkspacePath, "questions.md")}");
    if (openQuestions.Count == 0)
    {
        Console.WriteLine("No open questions.");
        return;
    }

    foreach (var question in openQuestions)
    {
        Console.WriteLine($"#{question.Id} [{(question.IsBlocking ? "blocking" : "non-blocking")}] {question.Text}");
    }
}

static void PrintOpenQuestions(WorkspaceState state, bool interactive)
{
    var openQuestions = state.Questions
        .Where(item => item.Status == QuestionStatus.Open)
        .OrderBy(item => item.Id)
        .ToList();

    if (openQuestions.Count == 0)
    {
        return;
    }

    Console.WriteLine();
    Console.WriteLine(ConsoleTheme.Label($"─── {openQuestions.Count} open question{(openQuestions.Count == 1 ? "" : "s")} ───"));
    foreach (var question in openQuestions)
    {
        var tag = question.IsBlocking ? ConsoleTheme.Warning("blocking") : ConsoleTheme.Muted("non-blocking");
        Console.WriteLine($"  #{ConsoleTheme.Number(question.Id.ToString())} [{tag}] {question.Text}");
    }
    Console.WriteLine(interactive
        ? $"Answer with: {ConsoleTheme.Command("/answer")} <id> <text>"
        : "Answer with: answer-question <id> <text>");
    Console.WriteLine();
}

static void PrintPlan(WorkspaceStore store)
{
    var path = Path.Combine(store.WorkspacePath, "plan.md");
    if (!File.Exists(path))
    {
        ChatConsole.WriteHint("No plan has been written yet.");
        return;
    }

    var content = File.ReadAllText(path).TrimEnd();
    ChatConsole.WriteSystem(Markup.Escape(content), "plan");
}

static void PrintLoopResult(LoopResult result)
{
    Console.WriteLine($"Loop state: {result.State}");
    if (result.Created.Count > 0)
    {
        Console.WriteLine($"Bootstrapped: {string.Join(", ", result.Created)}");
    }
    foreach (var run in result.QueuedRuns)
    {
        Console.WriteLine($"Queued run #{run.RunId} for issue #{run.IssueId} ({run.RoleSlug} via {run.ModelName}): {run.Title}");
    }
}

static void ValidateRoleOrThrow(DevTeamRuntime runtime, WorkspaceState state, string role)
{
    if (runtime.TryResolveRoleSlug(state, role, out var resolvedRole) && string.Equals(role.Trim(), resolvedRole, StringComparison.OrdinalIgnoreCase))
    {
        return;
    }

    var aliasMap = runtime.GetKnownRoleAliases(state);
    if (aliasMap.TryGetValue(role.Trim(), out var aliasTarget))
    {
        throw new InvalidOperationException(
            $"Role '{role.Trim()}' is an alias. Use the canonical role '{aliasTarget}'.\n{BuildRoleCatalog(runtime, state)}");
    }

    throw new InvalidOperationException(
        $"Unknown role '{role.Trim()}'.\n{BuildRoleCatalog(runtime, state)}");
}

static string BuildMissingRoleMessage(DevTeamRuntime runtime, WorkspaceState state) =>
    $"Missing --role.\n{BuildRoleCatalog(runtime, state)}";

static string BuildRoleCatalog(DevTeamRuntime runtime, WorkspaceState state)
{
    var roles = string.Join(", ", runtime.GetKnownRoleSlugs(state));
    var aliases = runtime.GetKnownRoleAliases(state);
    if (aliases.Count == 0)
    {
        return $"Valid roles: {roles}";
    }

    var aliasText = string.Join(", ", aliases.Select(pair => $"{pair.Key} -> {pair.Value}"));
    return $"Valid roles: {roles}\nKnown aliases: {aliasText}";
}

static void PrintHelp()
{
    Console.WriteLine("DevTeam CLI");
    Console.WriteLine("Commands (plain or slash-prefixed, for example `/init`):");
    Console.WriteLine("  start [--keep-awake true|false] [--workspace PATH]");
    Console.WriteLine("  init [--force] [--workspace PATH] [--goal TEXT | --goal-file PATH] [--mode SLUG] [--keep-awake true|false] [--total-credit-cap N] [--premium-credit-cap N] [--workspace-mcp true|false] [--pipeline-scheduling true|false]");
    Console.WriteLine("  customize [--force]                Copy default roles, modes, and superpowers to .devteam-source/ for editing");
    Console.WriteLine("  bug-report [--save PATH] [--redact-paths true|false] [--history-count N] [--error-count N] [--workspace PATH]");
    Console.WriteLine("  set-goal <TEXT> [--goal-file PATH] [--workspace PATH]");
    Console.WriteLine("  set-mode <SLUG> [--workspace PATH]");
    Console.WriteLine("  set-keep-awake <true|false> [--workspace PATH]");
    Console.WriteLine("  set-auto-approve <true|false> [--workspace PATH]");
    Console.WriteLine("  max-iterations <N> [--workspace PATH]    Set workspace default for max loop iterations");
    Console.WriteLine("  max-subagents <N> [--workspace PATH]     Set workspace default for max parallel subagents");
    Console.WriteLine("  add-roadmap <TITLE> [--detail TEXT] [--priority N] [--workspace PATH]");
    Console.WriteLine("  add-issue <TITLE> --role ROLE [--area AREA] [--detail TEXT] [--priority N] [--roadmap-item-id N] [--depends-on N [N...]] [--workspace PATH]");
    Console.WriteLine("  add-question <TEXT> [--blocking] [--workspace PATH]");
    Console.WriteLine("  answer-question <ID> <ANSWER> [--workspace PATH]");
    Console.WriteLine("  approve-plan [--note TEXT] [--workspace PATH]");
    Console.WriteLine("  feedback <TEXT> [--workspace PATH]");
    Console.WriteLine("  questions [--workspace PATH]");
    Console.WriteLine("  plan [--workspace PATH]");
    Console.WriteLine("  budget [--total N] [--premium N] [--workspace PATH]");
    Console.WriteLine("  check-update");
    Console.WriteLine("  update");
    Console.WriteLine("  run-once [--max-subagents N] [--workspace PATH]");
    Console.WriteLine("  run-loop [--backend sdk|cli] [--max-iterations N] [--max-subagents N] [--timeout-seconds N] [--verbosity quiet|normal|detailed] [--keep-awake true|false] [--workspace PATH]");
    Console.WriteLine("  complete-run --run-id N --outcome completed|failed|blocked --summary TEXT [--workspace PATH]");
    Console.WriteLine("  status [--workspace PATH]");
    Console.WriteLine("  agent-invoke [--backend sdk|cli] [--prompt TEXT] [--model NAME] [--timeout-seconds N] [--working-directory PATH] [--extra-arg ARG ...]");
    Console.WriteLine("  workspace-mcp --workspace PATH");
}

static void PrintInteractiveHelp()
{
    Console.WriteLine(ConsoleTheme.Label("Interactive commands:"));
    Console.WriteLine($"  {ConsoleTheme.Command("/init")} \"goal text\" [--goal-file PATH] [--force] [--mode SLUG] [--keep-awake true|false]");
    Console.WriteLine($"  {ConsoleTheme.Command("/customize")} [--force]    Copy default assets to .devteam-source/ for editing");
    Console.WriteLine($"  {ConsoleTheme.Command("/bug")} [--save PATH] [--redact-paths true|false]");
    Console.WriteLine($"  {ConsoleTheme.Command("/status")}");
    Console.WriteLine($"  {ConsoleTheme.Command("/history")}              Show session command history (last 50)");
    Console.WriteLine($"  {ConsoleTheme.Command("/mode")} <slug>");
    Console.WriteLine($"  {ConsoleTheme.Command("/keep-awake")} <on|off>");
    Console.WriteLine($"  {ConsoleTheme.Command("/add-issue")} \"title\" --role ROLE [--area AREA] [--detail TEXT] [--priority N] [--roadmap-item-id N] [--depends-on N [N...]]");
    Console.WriteLine($"  {ConsoleTheme.Command("/plan")}");
    Console.WriteLine($"  {ConsoleTheme.Command("/questions")}");
    Console.WriteLine($"  {ConsoleTheme.Command("/budget")} [--total N] [--premium N]");
    Console.WriteLine($"  {ConsoleTheme.Command("/check-update")}");
    Console.WriteLine($"  {ConsoleTheme.Command("/update")}");
    Console.WriteLine($"  {ConsoleTheme.Command("/max-iterations")} <N>    Set workspace default max iterations (used by all future /run calls)");
    Console.WriteLine($"  {ConsoleTheme.Command("/max-subagents")} <N>     Set workspace default max subagents (1=sequential, 2–4=parallel)");
    Console.WriteLine($"  {ConsoleTheme.Command("/run")} [--max-iterations N] [--max-subagents N] [--timeout-seconds N] [--keep-awake true|false]  {ConsoleTheme.Muted("starts in background — shell stays responsive")}");
    Console.WriteLine($"  {ConsoleTheme.Command("/stop")}              Cancel the running loop (waits for current agent call to finish)");
    Console.WriteLine($"  {ConsoleTheme.Command("/wait")}              Re-attach to the running loop and wait for it to finish");
    Console.WriteLine($"  {ConsoleTheme.Command("/feedback")} <text>");
    Console.WriteLine($"  {ConsoleTheme.Command("/approve")} [note]");
    Console.WriteLine($"  {ConsoleTheme.Command("/answer")} <id> <text>  {ConsoleTheme.Muted("works while the loop is running")}");
    Console.WriteLine($"  {ConsoleTheme.Command("/goal")} <text> [--goal-file PATH]");
    Console.WriteLine($"  {ConsoleTheme.Command("/exit")}");
    Console.WriteLine("If exactly one question is open, you can type a plain answer without `/answer`.");
    Console.WriteLine("While a plan is awaiting approval, plain text is treated as planning feedback and re-runs planning.");
    Console.WriteLine($"{ConsoleTheme.Muted("/answer")}, {ConsoleTheme.Muted("/questions")}, {ConsoleTheme.Muted("/status")}, {ConsoleTheme.Muted("/budget")}, and {ConsoleTheme.Muted("@role")} messages are all safe to use while the loop runs.");
    Console.WriteLine("If no plan exists yet, `/plan` runs the planner and then shows the generated plan.");
    Console.WriteLine();
    Console.WriteLine(ConsoleTheme.Label("Direct role invocation:"));
    Console.WriteLine($"  {ConsoleTheme.Command("@role")} <message>    Talk directly to any role (e.g. {ConsoleTheme.Muted("@architect can you review our API design?")})");
    Console.WriteLine($"  Roles: use {ConsoleTheme.Command("/status")} to see available roles. Tab-completes after {ConsoleTheme.Muted("@")}.");
}

static Dictionary<string, List<string>> ParseOptions(string[] tokens)
{
    var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    var positional = new List<string>();

    for (var index = 0; index < tokens.Length; index++)
    {
        var token = tokens[index];
        if (token.StartsWith("--", StringComparison.Ordinal))
        {
            var key = token[2..];
            if (!result.TryGetValue(key, out var values))
            {
                values = [];
                result[key] = values;
            }

            while (index + 1 < tokens.Length && !tokens[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                values.Add(tokens[++index]);
            }

            if (values.Count == 0)
            {
                values.Add("true");
            }
        }
        else
        {
            positional.Add(token);
        }
    }

    result["__positional"] = positional;
    return result;
}

static List<string> TokenizeInput(string input)
{
    var tokens = new List<string>();
    var current = new StringBuilder();
    var inQuotes = false;

    foreach (var ch in input)
    {
        if (ch == '"')
        {
            inQuotes = !inQuotes;
            continue;
        }

        if (char.IsWhiteSpace(ch) && !inQuotes)
        {
            if (current.Length > 0)
            {
                tokens.Add(current.ToString());
                current.Clear();
            }
            continue;
        }

        current.Append(ch);
    }

    if (current.Length > 0)
    {
        tokens.Add(current.ToString());
    }

    return tokens;
}

static string NormalizeCommand(string value) =>
    value.Trim().TrimStart('/').ToLowerInvariant();

static void EmitBugReport(
    WorkspaceStore store,
    DevTeamRuntime runtime,
    Dictionary<string, List<string>> options,
    ShellSessionDiagnostics? shellDiagnostics)
{
    var redactPaths = GetBoolOption(options, "redact-paths", true);
    var historyCount = GetIntOption(options, "history-count", 8);
    var errorCount = GetIntOption(options, "error-count", 5);
    var reportText = BugReportBuilder.Build(store, runtime, shellDiagnostics, redactPaths, historyCount, errorCount);
    var savePath = GetOption(options, "save");

    if (!string.IsNullOrWhiteSpace(savePath))
    {
        var fullPath = Path.GetFullPath(
            Path.IsPathRooted(savePath)
                ? savePath
                : Path.Combine(Environment.CurrentDirectory, savePath));
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, reportText);
        Console.WriteLine($"Saved bug report draft to {fullPath}");
        Console.WriteLine();
    }

    Console.WriteLine(reportText.TrimEnd());
}

static string? GetOption(Dictionary<string, List<string>> options, string key) =>
    ResolveOptionValues(options, key) is { Count: > 0 } values ? string.Join(" ", values) : null;

static int GetIntOption(Dictionary<string, List<string>> options, string key, int fallback) =>
    int.TryParse(GetOption(options, key), out var value) ? value : fallback;

static double GetDoubleOption(Dictionary<string, List<string>> options, string key, double fallback) =>
    double.TryParse(GetOption(options, key), out var value) ? value : fallback;

static bool IsApproveIntent(string line) =>
    line.Equals("approve", StringComparison.OrdinalIgnoreCase) ||
    line.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
    line.Equals("y", StringComparison.OrdinalIgnoreCase) ||
    line.StartsWith("approve ", StringComparison.OrdinalIgnoreCase);

static bool GetBoolOption(Dictionary<string, List<string>> options, string key, bool fallback)
{
    var value = GetOption(options, key);
    if (string.IsNullOrWhiteSpace(value))
    {
        return fallback;
    }

    return value.Trim().ToLowerInvariant() switch
    {
        "true" or "1" or "yes" or "on" => true,
        "false" or "0" or "no" or "off" => false,
        _ => fallback
    };
}

static bool? GetNullableBoolOption(Dictionary<string, List<string>> options, string key)
{
    var value = GetOption(options, key);
    if (string.IsNullOrWhiteSpace(value))
    {
        return null;
    }

    return ParseBoolOrThrow(value, $"Invalid boolean value '{value}'. Use true/false, yes/no, or on/off.");
}

static bool ParseBoolOrThrow(string value, string errorMessage)
{
    return value.Trim().ToLowerInvariant() switch
    {
        "true" or "1" or "yes" or "on" => true,
        "false" or "0" or "no" or "off" => false,
        _ => throw new InvalidOperationException(errorMessage)
    };
}

static bool ResolveKeepAwakeEnabled(WorkspaceState state, Dictionary<string, List<string>> options) =>
    GetNullableBoolOption(options, "keep-awake") ?? state.Runtime.KeepAwakeEnabled;

static bool TryLoadState(WorkspaceStore store, out WorkspaceState? state)
{
    try
    {
        state = store.Load();
        return true;
    }
    catch (InvalidOperationException)
    {
        state = null;
        return false;
    }
    catch (IOException)
    {
        state = null;
        return false;
    }
    catch (UnauthorizedAccessException)
    {
        state = null;
        return false;
    }
    catch (System.Text.Json.JsonException)
    {
        state = null;
        return false;
    }
}

static void ApplyKeepAwakeSetting(
    KeepAwakeController controller,
    bool enabled,
    bool interactiveShell,
    Action<string>? log = null)
{
    if (!enabled)
    {
        controller.SetEnabled(false);
        return;
    }

    try
    {
        controller.SetEnabled(true);
        log?.Invoke(interactiveShell
            ? "Keep-awake enabled for this session."
            : "Keep-awake enabled for this run.");
    }
    catch (InvalidOperationException ex)
    {
        log?.Invoke(ex.Message);
    }
}

static int? GetNullableIntOption(Dictionary<string, List<string>> options, string key) =>
    int.TryParse(GetOption(options, key), out var value) ? value : null;

static IReadOnlyList<int> GetMultiIntOption(Dictionary<string, List<string>> options, string key) =>
    ResolveOptionValues(options, key) is { Count: > 0 } values
        ? values.Select(int.Parse).ToList()
        : [];

static List<string>? ResolveOptionValues(Dictionary<string, List<string>> options, string key)
{
    if (options.TryGetValue(key, out var values))
    {
        return values;
    }

    return key switch
    {
        "max-iterations" when options.TryGetValue("max-iteration", out var aliasValues) => aliasValues,
        _ => null
    };
}

static string? GetPositionalValue(Dictionary<string, List<string>> options) =>
    options.TryGetValue("__positional", out var values) && values.Count > 0 ? string.Join(" ", values) : null;

static IReadOnlyList<string> GetPositionalValues(Dictionary<string, List<string>> options) =>
    options.TryGetValue("__positional", out var values) ? values : [];

static LoopVerbosity ParseVerbosity(string? value) =>
    value?.Trim().ToLowerInvariant() switch
    {
        "quiet" => LoopVerbosity.Quiet,
        "detailed" => LoopVerbosity.Detailed,
        _ => LoopVerbosity.Normal
    };

static async Task NotifyAboutAvailableUpdateAsync(ToolUpdateService toolUpdateService)
{
    var status = await TryCheckForToolUpdatesAsync(toolUpdateService, TimeSpan.FromSeconds(3));
    if (status?.IsUpdateAvailable == true)
    {
        Console.WriteLine($"{ConsoleTheme.Warning("Update available:")} {ConsoleTheme.Number(status.LatestVersion)} (current {status.CurrentVersion}). Run {ConsoleTheme.Command("/update")} to install it.");
    }
}

static async Task<ToolUpdateStatus?> TryCheckForToolUpdatesAsync(ToolUpdateService toolUpdateService, TimeSpan timeout)
{
    using var timeoutCts = new CancellationTokenSource(timeout);
    try
    {
        return await toolUpdateService.CheckAsync(timeoutCts.Token);
    }
    catch (ToolUpdateUnavailableException)
    {
        return null;
    }
    catch (HttpRequestException)
    {
        return null;
    }
    catch (TaskCanceledException)
    {
        return null;
    }
}

static async Task<int> CheckForToolUpdatesAsync(ToolUpdateService toolUpdateService)
{
    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    ToolUpdateStatus status;
    try
    {
        status = await toolUpdateService.CheckAsync(timeoutCts.Token);
    }
    catch (ToolUpdateUnavailableException ex)
    {
        Console.WriteLine(ex.Message);
        return 0;
    }
    catch (HttpRequestException)
    {
        Console.WriteLine("Update check is unavailable right now.");
        return 0;
    }
    catch (TaskCanceledException)
    {
        Console.WriteLine("Update check timed out.");
        return 0;
    }

    if (status.IsUpdateAvailable)
    {
        Console.WriteLine($"Update available: {status.LatestVersion} (current {status.CurrentVersion}).");
        Console.WriteLine($"Run `devteam update` or `/update` in the shell to install it.");
        return 0;
    }

    Console.WriteLine($"OptionA.DevTeam is up to date ({status.CurrentVersion}).");
    return 0;
}

static async Task<int> ScheduleToolUpdateAsync(ToolUpdateService toolUpdateService, bool interactiveShell)
{
    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    ToolUpdateStatus status;
    try
    {
        status = await toolUpdateService.CheckAsync(timeoutCts.Token);
    }
    catch (ToolUpdateUnavailableException ex)
    {
        Console.WriteLine(ex.Message);
        return 0;
    }
    catch (HttpRequestException)
    {
        Console.WriteLine("Update check is unavailable right now.");
        return 0;
    }
    catch (TaskCanceledException)
    {
        Console.WriteLine("Update check timed out.");
        return 0;
    }

    if (!status.IsUpdateAvailable)
    {
        Console.WriteLine($"OptionA.DevTeam is already up to date ({status.CurrentVersion}).");
        return 0;
    }

    var launch = toolUpdateService.ScheduleGlobalUpdate(status.LatestVersion);
    Console.WriteLine($"Scheduling update to {status.LatestVersion} (current {status.CurrentVersion}).");
    Console.WriteLine($"If the updater does not complete, run `{launch.ManualCommand}`.");
    if (interactiveShell)
    {
        Console.WriteLine("Closing the shell so the updater can replace the installed tool.");
    }

    return 0;
}

static void UpdateBudget(WorkspaceState state, Dictionary<string, List<string>> options)
{
    var total = GetDoubleOption(options, "total", state.Budget.TotalCreditCap);
    var premium = GetDoubleOption(options, "premium", state.Budget.PremiumCreditCap);
    state.Budget.TotalCreditCap = total;
    state.Budget.PremiumCreditCap = premium;
}

static void CopyPackagedAssets(string targetRoot, bool force)
{
    // Walk up from the tool install directory to find packaged .devteam-source,
    // matching the same resolution logic SeedData uses via EnumerateAssetRoots.
    string? sourceRoot = null;
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current is not null)
    {
        var candidate = Path.Combine(current.FullName, ".devteam-source");
        if (Directory.Exists(candidate) &&
            !string.Equals(Path.GetFullPath(candidate), Path.GetFullPath(targetRoot), StringComparison.OrdinalIgnoreCase))
        {
            sourceRoot = candidate;
            break;
        }
        current = current.Parent;
    }

    if (sourceRoot is null)
    {
        throw new InvalidOperationException(
            "No packaged assets found. " +
            "This command copies the built-in roles, modes, and superpowers so you can customize them.");
    }

    var created = 0;
    var skipped = 0;
    var overwritten = 0;

    foreach (var sourceFile in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
    {
        var relativePath = Path.GetRelativePath(sourceRoot, sourceFile);
        var targetFile = Path.Combine(targetRoot, relativePath);
        var targetDir = Path.GetDirectoryName(targetFile)!;

        if (!Directory.Exists(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        if (File.Exists(targetFile) && !force)
        {
            skipped++;
            continue;
        }

        if (File.Exists(targetFile))
        {
            overwritten++;
        }
        else
        {
            created++;
        }

        File.Copy(sourceFile, targetFile, overwrite: true);
    }

    Console.WriteLine($"Copied assets to {Path.GetFullPath(targetRoot)}");
    Console.WriteLine($"  {created} created, {overwritten} overwritten, {skipped} skipped (use --force to overwrite)");
    Console.WriteLine("Edit these files to customize roles, modes, superpowers, and model policies.");
}

file sealed class LoopConsoleRenderer : IDisposable
{
    private readonly object _gate = new();
    private int _progressLineCount;
    private bool _disposed;

    public void Log(string message)
    {
        lock (_gate)
        {
            ClearProgressBlock();
            Console.WriteLine(message);
        }
    }

    public void ReportProgress(IReadOnlyList<RunProgressSnapshot> snapshots)
    {
        lock (_gate)
        {
            ClearProgressBlock();
            foreach (var snapshot in snapshots.OrderBy(item => item.IssueId))
            {
                var scope = snapshot.IssueId is null
                    ? $"{snapshot.RoleSlug,-12} [{Truncate(snapshot.Title, 48)}]"
                    : $"{snapshot.RoleSlug,-12} issue #{snapshot.IssueId,-3} [{Truncate(snapshot.Title, 48)}]";
                Console.WriteLine($"Running {scope} {snapshot.Elapsed.TotalSeconds,4:0}s");
            }
            _progressLineCount = snapshots.Count;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            ClearProgressBlock();
            _disposed = true;
        }
    }

    private void ClearProgressBlock()
    {
        if (_progressLineCount <= 0)
        {
            return;
        }

        for (var index = 0; index < _progressLineCount; index++)
        {
            var targetTop = Console.CursorTop - 1;
            if (targetTop < 0)
            {
                break;
            }

            Console.SetCursorPosition(0, targetTop);
            Console.Write(new string(' ', Math.Max(1, Console.BufferWidth - 1)));
            Console.SetCursorPosition(0, targetTop);
        }

        _progressLineCount = 0;
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..Math.Max(0, maxLength - 1)] + "…";
    }
}

// ShellAutoCompleteHandler removed — shell uses RazorConsole TextInput
