using System.Net.Http;
using System.Text;
using DevTeam.Cli;
using DevTeam.Core;
using ReadLine = System.ReadLine;

var command = NormalizeCommand(args.Length == 0 ? "help" : args[0]);
var options = ParseOptions(args.Skip(1).ToArray());
var workspacePath = GetOption(options, "workspace") ?? ".devteam";
var store = new WorkspaceStore(workspacePath);
var runtime = new DevTeamRuntime();
var loopExecutor = new LoopExecutor(runtime, store);
using var toolUpdateService = new ToolUpdateService();

try
{
    switch (command)
    {
        case "start":
            return await RunInteractiveShellAsync(store, runtime, loopExecutor, toolUpdateService, options);

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
            Console.WriteLine("Captured planning feedback.");
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

static async Task<int> RunInteractiveShellAsync(
    WorkspaceStore store,
    DevTeamRuntime runtime,
    LoopExecutor loopExecutor,
    ToolUpdateService toolUpdateService,
    Dictionary<string, List<string>> startOptions)
{
    using var keepAwakeController = new KeepAwakeController();
    var shellKeepAwakeEnabled = GetNullableBoolOption(startOptions, "keep-awake");
    Console.WriteLine(ConsoleTheme.Label("DevTeam interactive shell"));
    Console.WriteLine($"Workspace: {ConsoleTheme.Accent(store.WorkspacePath)}");
    Console.WriteLine($"Type {ConsoleTheme.Command("/help")} for commands. {ConsoleTheme.Command("/exit")} to quit. {ConsoleTheme.Muted("Tab to autocomplete.")}"  );
    await NotifyAboutAvailableUpdateAsync(toolUpdateService);
    TryLoadState(store, out var initialState);
    if (initialState is null)
    {
        Console.WriteLine();
        Console.WriteLine($"No workspace found. Use {ConsoleTheme.Command("/init")} --goal \"<your goal>\" to get started.");
    }
    if (shellKeepAwakeEnabled is null)
    {
        shellKeepAwakeEnabled = initialState?.Runtime.KeepAwakeEnabled ?? false;
    }
    ApplyKeepAwakeSetting(keepAwakeController, shellKeepAwakeEnabled.Value, interactiveShell: true, Console.WriteLine);

    ReadLine.HistoryEnabled = true;
    ReadLine.AutoCompletionHandler = new ShellAutoCompleteHandler();

    while (true)
    {
        WorkspaceState? state = null;
        try
        {
            state = store.Load();
            var openQuestions = state.Questions.Where(item => item.Status == QuestionStatus.Open).ToList();
            if (openQuestions.Count > 0)
            {
                Console.WriteLine($"{ConsoleTheme.Warning($"{openQuestions.Count} open question{(openQuestions.Count == 1 ? "" : "s")}")} — use {ConsoleTheme.Command("/questions")} to list or {ConsoleTheme.Command("/answer")} <id> <text>");
            }
        }
        catch
        {
        }

        ConsoleTheme.WritePrompt("devteam> ");
        var line = Console.IsInputRedirected ? Console.ReadLine() : ReadLine.Read("");
        if (line is null)
        {
            return 0;
        }

        line = line.Trim();
        if (line.Length == 0)
        {
            continue;
        }

        if (line.StartsWith("@", StringComparison.Ordinal) && state is not null)
        {
            var spaceIndex = line.IndexOf(' ');
            var roleInput = spaceIndex > 1 ? line[1..spaceIndex] : line[1..];
            var userMessage = spaceIndex > 1 ? line[(spaceIndex + 1)..].Trim() : "";
            if (string.IsNullOrWhiteSpace(userMessage))
            {
                Console.WriteLine($"Usage: {ConsoleTheme.Command("@role")} <message>");
                continue;
            }
            if (!runtime.TryResolveRoleSlug(state, roleInput, out var roleSlug))
            {
                Console.WriteLine($"Unknown role '{ConsoleTheme.Warning(roleInput)}'. Known roles: {string.Join(", ", runtime.GetKnownRoleSlugs(state).OrderBy(s => s))}");
                continue;
            }
            await InvokeRoleDirectAsync(store, runtime, state, roleSlug, userMessage);
            continue;
        }

        if (!line.StartsWith("/", StringComparison.Ordinal) && state is not null)
        {
            var openQuestions = state.Questions.Where(item => item.Status == QuestionStatus.Open).ToList();
            if (openQuestions.Count == 1)
            {
                runtime.AnswerQuestion(state, openQuestions[0].Id, line);
                store.Save(state);
                var stillOpen = state.Questions.Count(q => q.Status == QuestionStatus.Open);
                if (stillOpen == 0)
                {
                    Console.WriteLine($"Answered question #{openQuestions[0].Id}. {ConsoleTheme.Success("All questions resolved.")}");
                    if (state.Phase == WorkflowPhase.Planning)
                    {
                        Console.WriteLine($"Use {ConsoleTheme.Command("/approve")} to move forward or type feedback to revise the plan.");
                    }
                }
                else
                {
                    Console.WriteLine($"Answered question #{openQuestions[0].Id}. {ConsoleTheme.Muted($"{stillOpen} remaining.")}");
                }
                continue;
            }

            if (state.Phase == WorkflowPhase.Planning
                && state.Issues.Any(item => item.IsPlanningIssue && item.Status == ItemStatus.Done))
            {
                runtime.RecordPlanningFeedback(state, line);
                store.Save(state);
                Console.WriteLine("Captured planning feedback. Revising plan...");
                var report = await RunLoopAsync(store, runtime, loopExecutor, state, ParseOptions(["--max-iterations", "2"]), interactiveShell: true);
                Console.WriteLine($"Loop complete after {report.IterationsExecuted} iteration(s). Final state: {report.FinalState}");
                Console.WriteLine("Use /plan to inspect the revised plan, or /approve to continue.");
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
            switch (command)
            {
                case "exit":
                case "quit":
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

                case "init":
                {
                    var force = GetBoolOption(options, "force", false);
                    if (!force && File.Exists(store.StatePath))
                    {
                        Console.WriteLine($"Workspace already initialized at {store.WorkspacePath}.");
                        Console.WriteLine("Use /init --force to reinitialize (this will reset all workspace state).");
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
                    var current = store.Load();
                    var note = GetOption(options, "note") ?? GetPositionalValue(options) ?? "User approved the current plan.";
                    if (current.Phase == WorkflowPhase.ArchitectPlanning)
                    {
                        runtime.ApproveArchitectPlan(current, note);
                        store.Save(current);
                        Console.WriteLine(ConsoleTheme.Success("Approved the architect plan. Execution phase begins."));
                        Console.Write($"Run the execution loop now? ({ConsoleTheme.Command("Y")}/n) ");
                        var answer = (Console.IsInputRedirected ? Console.ReadLine() : ReadLine.Read(""))?.Trim();
                        if (string.IsNullOrEmpty(answer) || answer.StartsWith("y", StringComparison.OrdinalIgnoreCase))
                        {
                            var report = await RunLoopAsync(store, runtime, loopExecutor, current, ParseOptions([]), interactiveShell: true);
                            Console.WriteLine($"Loop complete after {report.IterationsExecuted} iteration(s). Final state: {report.FinalState}");
                            PrintBudget(current.Budget);
                        }
                    }
                    else
                    {
                        runtime.ApprovePlan(current, note);
                        store.Save(current);
                        if (current.Phase == WorkflowPhase.ArchitectPlanning)
                        {
                            Console.WriteLine(ConsoleTheme.Success("Approved the high-level plan.") + $" Phase: {ConsoleTheme.Phase("ArchitectPlanning")}");
                            Console.Write($"Run the architect planning loop now? ({ConsoleTheme.Command("Y")}/n) ");
                            var answer = (Console.IsInputRedirected ? Console.ReadLine() : ReadLine.Read(""))?.Trim();
                            if (string.IsNullOrEmpty(answer) || answer.StartsWith("y", StringComparison.OrdinalIgnoreCase))
                            {
                                var report = await RunLoopAsync(store, runtime, loopExecutor, current, ParseOptions([]), interactiveShell: true);
                                Console.WriteLine($"Loop complete after {report.IterationsExecuted} iteration(s). Final state: {report.FinalState}");
                                PrintBudget(current.Budget);
                                PrintArchitectSummary(current);
                            }
                        }
                        else
                        {
                            Console.WriteLine(ConsoleTheme.Success("Approved the current plan."));
                        }
                    }
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

                case "feedback":
                {
                    var current = store.Load();
                    var feedback = GetPositionalValue(options) ?? throw new InvalidOperationException("Usage: /feedback <text>");
                    runtime.RecordPlanningFeedback(current, feedback);
                    store.Save(current);
                    Console.WriteLine("Captured planning feedback. Revising plan...");
                    var report = await RunLoopAsync(store, runtime, loopExecutor, current, ParseOptions(["--max-iterations", "2"]), interactiveShell: true);
                    Console.WriteLine($"Loop complete after {report.IterationsExecuted} iteration(s). Final state: {report.FinalState}");
                    Console.WriteLine("Use /plan to inspect the revised plan, or /approve to continue.");
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
                    if (remaining == 0)
                    {
                        Console.WriteLine($"Answered question #{values[0]}. {ConsoleTheme.Success("All questions resolved.")}");
                        if (current.Phase == WorkflowPhase.Planning)
                        {
                            Console.WriteLine($"Use {ConsoleTheme.Command("/approve")} to move forward or type feedback to revise the plan.");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Answered question #{values[0]}. {ConsoleTheme.Muted($"{remaining} remaining.")}");
                    }
                    break;
                }

                case "run":
                case "run-loop":
                {
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
                    var report = await RunLoopAsync(store, runtime, loopExecutor, current, options, interactiveShell: true);
                    Console.WriteLine($"Loop complete after {report.IterationsExecuted} iteration(s). Final state: {report.FinalState}");
                    PrintBudget(current.Budget);
                    if (report.FinalState == "waiting-for-user")
                    {
                        Console.WriteLine($"Check {Path.Combine(store.WorkspacePath, "questions.md")} and answer with /answer.");
                    }
                    else if (report.FinalState == "awaiting-plan-approval")
                    {
                        Console.WriteLine("Use /plan to review, type feedback to revise, or /approve to move into execution.");
                    }
                    else if (report.FinalState == "awaiting-architect-approval")
                    {
                        PrintArchitectSummary(current);
                    }
                    break;
                }

                case "run-once":
                {
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
                    Console.WriteLine($"Unknown command '{ConsoleTheme.Warning(tokens[0])}'. Type {ConsoleTheme.Command("/help")}.");
                    break;
            }
        }
        catch (Exception ex)
        {
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
    Console.WriteLine($"Invoking {ConsoleTheme.Role(roleSlug)} via {ConsoleTheme.Muted(model)}...");

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

        Console.WriteLine();
        Console.WriteLine(ConsoleTheme.Label($"── {roleSlug} ──"));
        Console.WriteLine(parsed.Summary);

        if (parsed.Issues.Count > 0)
        {
            var anchor = state.Issues.FirstOrDefault(i => !i.IsPlanningIssue) ?? state.Issues.FirstOrDefault();
            if (anchor is not null)
            {
                runtime.AddGeneratedIssues(state, anchor.Id, parsed.Issues);
                Console.WriteLine();
                Console.WriteLine($"{ConsoleTheme.Accent($"{parsed.Issues.Count} issue(s) proposed")} and added to the board.");
            }
        }
        if (parsed.Questions.Count > 0)
        {
            runtime.AddQuestions(state, parsed.Questions);
            Console.WriteLine();
            Console.WriteLine($"{ConsoleTheme.Warning($"{parsed.Questions.Count} question(s)")} added. Use {ConsoleTheme.Command("/questions")} to review.");
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
    bool interactiveShell = false)
{
    var backend = GetOption(options, "backend") ?? "sdk";
    var maxSubagents = GetIntOption(options, "max-subagents", 1);
    var maxIterations = GetIntOption(options, "max-iterations", 10);
    var timeoutSeconds = GetIntOption(options, "timeout-seconds", 600);
    var verbosity = ParseVerbosity(GetOption(options, "verbosity"));
    using var keepAwakeController = new KeepAwakeController();
    using var renderer = interactiveShell && !Console.IsOutputRedirected
        ? new LoopConsoleRenderer()
        : null;
    Action<IReadOnlyList<RunProgressSnapshot>>? progressReporter = renderer is null ? null : snapshots => renderer.ReportProgress(snapshots);
    Action<string> logger = renderer is null ? Console.WriteLine : message => renderer.Log(message);
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
        logger);
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
    Console.WriteLine($"{ConsoleTheme.Label("Phase:")} {ConsoleTheme.Phase(state.Phase.ToString())}");
    Console.WriteLine($"{ConsoleTheme.Label("Mode:")} {ConsoleTheme.Accent(state.Runtime.ActiveModeSlug)}");
    Console.WriteLine($"{ConsoleTheme.Label("Keep awake:")} {(state.Runtime.KeepAwakeEnabled ? ConsoleTheme.Success("enabled") : ConsoleTheme.Muted("disabled"))}");
    Console.WriteLine($"{ConsoleTheme.Label("Auto-approve:")} {(state.Runtime.AutoApproveEnabled ? ConsoleTheme.Success("enabled") : ConsoleTheme.Muted("disabled"))}");
    Console.WriteLine(
        $"Counts: roadmap={report.Counts["roadmap"]}, issues={report.Counts["issues"]}, " +
        $"questions={report.Counts["questions"]}, runs={report.Counts["runs"]}, " +
        $"modes={state.Modes.Count}, " +
        $"decisions={report.Counts["decisions"]}, roles={report.Counts["roles"]}, superpowers={report.Counts["superpowers"]}");
    Console.WriteLine(
        $"{ConsoleTheme.Label("Budget:")} {ConsoleTheme.BudgetUsage(report.Budget.CreditsCommitted, report.Budget.TotalCreditCap)} credits " +
        $"({ConsoleTheme.Number($"{report.Budget.TotalCreditCap - report.Budget.CreditsCommitted:0.##}")} remaining), " +
        $"premium {ConsoleTheme.BudgetUsage(report.Budget.PremiumCreditsCommitted, report.Budget.PremiumCreditCap)} " +
        $"({ConsoleTheme.Number($"{report.Budget.PremiumCreditCap - report.Budget.PremiumCreditsCommitted:0.##}")} remaining)");
    if (report.QueuedRuns.Count > 0)
    {
        Console.WriteLine(ConsoleTheme.Label("Queued runs:"));
        foreach (var run in report.QueuedRuns)
        {
            Console.WriteLine($"  - #{ConsoleTheme.Number(run.Id.ToString())} issue #{ConsoleTheme.Number(run.IssueId.ToString())} {ConsoleTheme.Role(run.RoleSlug)} via {ConsoleTheme.Accent(run.ModelName)}");
        }
    }
    if (report.OpenQuestions.Count > 0)
    {
        Console.WriteLine(ConsoleTheme.Label("Open questions:"));
        foreach (var question in report.OpenQuestions)
        {
            Console.WriteLine($"  - #{ConsoleTheme.Number(question.Id.ToString())} ({(question.IsBlocking ? ConsoleTheme.Warning("blocking") : ConsoleTheme.Muted("non-blocking"))}) {question.Text}");
        }
    }
    if (report.RecentDecisions.Count > 0)
    {
        Console.WriteLine(ConsoleTheme.Label("Recent decisions:"));
        foreach (var decision in report.RecentDecisions)
        {
            Console.WriteLine($"  - #{ConsoleTheme.Number(decision.Id.ToString())} [{ConsoleTheme.Accent(decision.Source)}] {decision.Title}");
        }
    }
}

static void PrintBudget(BudgetState budget)
{
    Console.WriteLine(
        $"{ConsoleTheme.Label("Budget:")} {ConsoleTheme.BudgetUsage(budget.CreditsCommitted, budget.TotalCreditCap)} credits " +
        $"({ConsoleTheme.Number($"{budget.TotalCreditCap - budget.CreditsCommitted:0.##}")} remaining), " +
        $"premium {ConsoleTheme.BudgetUsage(budget.PremiumCreditsCommitted, budget.PremiumCreditCap)} " +
        $"({ConsoleTheme.Number($"{budget.PremiumCreditCap - budget.PremiumCreditsCommitted:0.##}")} remaining)");
}

static void PrintArchitectSummary(WorkspaceState state)
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
        return;
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
        Console.WriteLine("No plan has been written yet.");
        return;
    }

    Console.WriteLine($"Plan file: {path}");
    Console.WriteLine(File.ReadAllText(path).TrimEnd());
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
    Console.WriteLine("  set-goal <TEXT> [--goal-file PATH] [--workspace PATH]");
    Console.WriteLine("  set-mode <SLUG> [--workspace PATH]");
    Console.WriteLine("  set-keep-awake <true|false> [--workspace PATH]");
    Console.WriteLine("  set-auto-approve <true|false> [--workspace PATH]");
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
    Console.WriteLine($"  {ConsoleTheme.Command("/status")}");
    Console.WriteLine($"  {ConsoleTheme.Command("/mode")} <slug>");
    Console.WriteLine($"  {ConsoleTheme.Command("/keep-awake")} <on|off>");
    Console.WriteLine($"  {ConsoleTheme.Command("/add-issue")} \"title\" --role ROLE [--area AREA] [--detail TEXT] [--priority N] [--roadmap-item-id N] [--depends-on N [N...]]");
    Console.WriteLine($"  {ConsoleTheme.Command("/plan")}");
    Console.WriteLine($"  {ConsoleTheme.Command("/questions")}");
    Console.WriteLine($"  {ConsoleTheme.Command("/budget")} [--total N] [--premium N]");
    Console.WriteLine($"  {ConsoleTheme.Command("/check-update")}");
    Console.WriteLine($"  {ConsoleTheme.Command("/update")}");
    Console.WriteLine($"  {ConsoleTheme.Command("/run")} [--max-iterations N] [--timeout-seconds N] [--keep-awake true|false]");
    Console.WriteLine($"  {ConsoleTheme.Command("/feedback")} <text>");
    Console.WriteLine($"  {ConsoleTheme.Command("/approve")} [note]");
    Console.WriteLine($"  {ConsoleTheme.Command("/answer")} <id> <text>");
    Console.WriteLine($"  {ConsoleTheme.Command("/goal")} <text> [--goal-file PATH]");
    Console.WriteLine($"  {ConsoleTheme.Command("/exit")}");
    Console.WriteLine("If exactly one question is open, you can type a plain answer without `/answer`.");
    Console.WriteLine("While a plan is awaiting approval, plain text is treated as planning feedback and re-runs planning.");
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

static string? GetOption(Dictionary<string, List<string>> options, string key) =>
    ResolveOptionValues(options, key) is { Count: > 0 } values ? string.Join(" ", values) : null;

static int GetIntOption(Dictionary<string, List<string>> options, string key, int fallback) =>
    int.TryParse(GetOption(options, key), out var value) ? value : fallback;

static double GetDoubleOption(Dictionary<string, List<string>> options, string key, double fallback) =>
    double.TryParse(GetOption(options, key), out var value) ? value : fallback;

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

file sealed class ShellAutoCompleteHandler : IAutoCompleteHandler
{
    private static readonly string[] Commands =
    [
        "/init", "/status", "/plan", "/run", "/approve", "/feedback",
        "/questions", "/answer", "/budget", "/goal", "/mode",
        "/keep-awake", "/auto-approve", "/add-issue", "/customize",
        "/check-update", "/update", "/exit", "/help"
    ];

    private static readonly string[] RoleMentions =
    [
        "@planner", "@architect", "@orchestrator",
        "@developer", "@backend-developer", "@frontend-developer", "@fullstack-developer",
        "@tester", "@reviewer", "@ux", "@user", "@game-designer",
        "@navigator", "@analyst", "@security", "@docs", "@devops", "@refactorer"
    ];

    public char[] Separators { get; set; } = [' '];

    public string[] GetSuggestions(string text, int index)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Commands;
        }

        if (text.StartsWith("@", StringComparison.Ordinal))
        {
            return RoleMentions
                .Where(r => r.StartsWith(text, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        return Commands
            .Where(cmd => cmd.StartsWith(text, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }
}
