using System.Text;
using DevTeam.Core;

var command = NormalizeCommand(args.Length == 0 ? "help" : args[0]);
var options = ParseOptions(args.Skip(1).ToArray());
var workspacePath = GetOption(options, "workspace") ?? ".devteam";
var store = new WorkspaceStore(workspacePath);
var runtime = new DevTeamRuntime();
var loopExecutor = new LoopExecutor(runtime, store);

try
{
    switch (command)
    {
        case "start":
            return await RunInteractiveShellAsync(store, runtime, loopExecutor);

        case "workspace-mcp":
        {
            var workspace = GetOption(options, "workspace") ?? ".devteam";
            var server = new WorkspaceMcpServer(workspace);
            await server.RunAsync(Console.OpenStandardInput(), Console.OpenStandardOutput());
            return 0;
        }

        case "init":
        {
            var totalCap = GetDoubleOption(options, "total-credit-cap", 25);
            var premiumCap = GetDoubleOption(options, "premium-credit-cap", 6);
            var goal = GetOption(options, "goal") ?? GetPositionalValue(options);
            var gitInitialized = GitWorkspace.EnsureRepository(Environment.CurrentDirectory);
            var state = store.Initialize(Environment.CurrentDirectory, totalCap, premiumCap);
            var mode = GetOption(options, "mode");
            state.Runtime.WorkspaceMcpEnabled = GetBoolOption(options, "workspace-mcp", true);
            state.Runtime.PipelineSchedulingEnabled = GetBoolOption(options, "pipeline-scheduling", true);
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

        case "set-goal":
        case "goal":
        {
            var state = store.Load();
            var goal = GetPositionalValue(options) ?? throw new InvalidOperationException("Missing goal text.");
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
            runtime.ApprovePlan(state, note);
            store.Save(state);
            Console.WriteLine("Approved the current plan. Execution work can now continue.");
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
            var report = await RunLoopAsync(store, runtime, loopExecutor, state, options);
            Console.WriteLine($"Loop complete after {report.IterationsExecuted} iteration(s). Final state: {report.FinalState}");
            PrintBudget(state.Budget);
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
            PrintPlan(store);
            return 0;

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
            var result = client.InvokeAsync(new AgentInvocationRequest
            {
                Prompt = prompt,
                Model = model,
                WorkingDirectory = Path.GetFullPath(workingDirectory),
                Timeout = TimeSpan.FromSeconds(timeoutSeconds),
                ExtraArguments = extraArgs,
                WorkspacePath = Path.GetFullPath(workspacePath),
                EnableWorkspaceMcp = GetBoolOption(options, "workspace-mcp", false),
                ToolHostPath = System.Reflection.Assembly.GetEntryAssembly()?.Location
            }).GetAwaiter().GetResult();

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
    LoopExecutor loopExecutor)
{
    Console.WriteLine("DevTeam interactive shell");
    Console.WriteLine($"Workspace: {store.WorkspacePath}");
    Console.WriteLine("Type /help for commands. Type /exit to quit.");

    while (true)
    {
        WorkspaceState? state = null;
        try
        {
            state = store.Load();
            var openQuestions = state.Questions.Where(item => item.Status == QuestionStatus.Open).ToList();
            if (openQuestions.Count > 0)
            {
                Console.WriteLine($"Open questions: {openQuestions.Count} (see {Path.Combine(store.WorkspacePath, "questions.md")})");
            }
        }
        catch
        {
        }

        Console.Write("devteam> ");
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

        if (!line.StartsWith("/", StringComparison.Ordinal) && state is not null)
        {
            var openQuestions = state.Questions.Where(item => item.Status == QuestionStatus.Open).ToList();
            if (openQuestions.Count == 1)
            {
                runtime.AnswerQuestion(state, openQuestions[0].Id, line);
                store.Save(state);
                Console.WriteLine($"Answered question #{openQuestions[0].Id}.");
                continue;
            }

            if (state.Phase == WorkflowPhase.Planning
                && state.Issues.Any(item => item.IsPlanningIssue && item.Status == ItemStatus.Done))
            {
                runtime.RecordPlanningFeedback(state, line);
                store.Save(state);
                Console.WriteLine("Captured planning feedback. Revising plan...");
                var report = await RunLoopAsync(store, runtime, loopExecutor, state, ParseOptions(["--max-iterations", "2"]));
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

                case "init":
                {
                    var totalCap = GetDoubleOption(options, "total-credit-cap", 25);
                    var premiumCap = GetDoubleOption(options, "premium-credit-cap", 6);
                    var goal = GetOption(options, "goal") ?? GetPositionalValue(options);
                    var gitInitialized = GitWorkspace.EnsureRepository(Environment.CurrentDirectory);
                    var initialized = store.Initialize(Environment.CurrentDirectory, totalCap, premiumCap);
                    var mode = GetOption(options, "mode");
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
                    PrintPlan(store);
                    break;

                case "goal":
                case "set-goal":
                {
                    var current = store.Load();
                    var goal = GetPositionalValue(options) ?? throw new InvalidOperationException("Missing goal text.");
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

                case "approve":
                case "approve-plan":
                {
                    var current = store.Load();
                    var note = GetOption(options, "note") ?? GetPositionalValue(options) ?? "User approved the current plan.";
                    runtime.ApprovePlan(current, note);
                    store.Save(current);
                    Console.WriteLine("Approved the current plan.");
                    break;
                }

                case "feedback":
                {
                    var current = store.Load();
                    var feedback = GetPositionalValue(options) ?? throw new InvalidOperationException("Usage: /feedback <text>");
                    runtime.RecordPlanningFeedback(current, feedback);
                    store.Save(current);
                    Console.WriteLine("Captured planning feedback. Revising plan...");
                    var report = await RunLoopAsync(store, runtime, loopExecutor, current, ParseOptions(["--max-iterations", "2"]));
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
                    Console.WriteLine($"Answered question #{values[0]}.");
                    break;
                }

                case "run":
                case "run-loop":
                {
                    var current = store.Load();
                    var report = await RunLoopAsync(store, runtime, loopExecutor, current, options);
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
                    break;
                }

                case "run-once":
                {
                    var current = store.Load();
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
                    Console.WriteLine($"Unknown command '{tokens[0]}'. Type /help.");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }
}

static async Task<LoopExecutionReport> RunLoopAsync(
    WorkspaceStore store,
    DevTeamRuntime runtime,
    LoopExecutor loopExecutor,
    WorkspaceState state,
    Dictionary<string, List<string>> options)
{
    var backend = GetOption(options, "backend") ?? "sdk";
    var maxSubagents = GetIntOption(options, "max-subagents", 1);
    var maxIterations = GetIntOption(options, "max-iterations", 10);
    var timeoutSeconds = GetIntOption(options, "timeout-seconds", 180);
    var verbosity = ParseVerbosity(GetOption(options, "verbosity"));
    var report = await loopExecutor.RunAsync(
        state,
        new LoopExecutionOptions
        {
            Backend = backend,
            MaxSubagents = maxSubagents,
            MaxIterations = maxIterations,
            AgentTimeout = TimeSpan.FromSeconds(timeoutSeconds),
            Verbosity = verbosity
        },
        Console.WriteLine);
    store.Save(state);
    return report;
}

static void PrintStatus(WorkspaceState state, DevTeamRuntime runtime)
{
    var report = runtime.BuildStatusReport(state);
    Console.WriteLine($"Phase: {state.Phase}");
    Console.WriteLine($"Mode: {state.Runtime.ActiveModeSlug}");
    Console.WriteLine(
        $"Counts: roadmap={report.Counts["roadmap"]}, issues={report.Counts["issues"]}, " +
        $"questions={report.Counts["questions"]}, runs={report.Counts["runs"]}, " +
        $"modes={state.Modes.Count}, " +
        $"decisions={report.Counts["decisions"]}, roles={report.Counts["roles"]}, superpowers={report.Counts["superpowers"]}");
    Console.WriteLine(
        $"Budget: {report.Budget.CreditsCommitted}/{report.Budget.TotalCreditCap} total, " +
        $"{report.Budget.PremiumCreditsCommitted}/{report.Budget.PremiumCreditCap} premium");
    if (report.QueuedRuns.Count > 0)
    {
        Console.WriteLine("Queued runs:");
        foreach (var run in report.QueuedRuns)
        {
            Console.WriteLine($"  - #{run.Id} issue #{run.IssueId} {run.RoleSlug} via {run.ModelName}");
        }
    }
    if (report.OpenQuestions.Count > 0)
    {
        Console.WriteLine("Open questions:");
        foreach (var question in report.OpenQuestions)
        {
            Console.WriteLine($"  - #{question.Id} ({(question.IsBlocking ? "blocking" : "non-blocking")}) {question.Text}");
        }
    }
    if (report.RecentDecisions.Count > 0)
    {
        Console.WriteLine("Recent decisions:");
        foreach (var decision in report.RecentDecisions)
        {
            Console.WriteLine($"  - #{decision.Id} [{decision.Source}] {decision.Title}");
        }
    }
}

static void PrintBudget(BudgetState budget)
{
    Console.WriteLine(
        $"Budget: {budget.CreditsCommitted}/{budget.TotalCreditCap} total, " +
        $"{budget.PremiumCreditsCommitted}/{budget.PremiumCreditCap} premium");
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
    Console.WriteLine("  start [--workspace PATH]");
    Console.WriteLine("  init [--workspace PATH] [--goal TEXT] [--mode SLUG] [--total-credit-cap N] [--premium-credit-cap N] [--workspace-mcp true|false] [--pipeline-scheduling true|false]");
    Console.WriteLine("  set-goal <TEXT> [--workspace PATH]");
    Console.WriteLine("  set-mode <SLUG> [--workspace PATH]");
    Console.WriteLine("  add-roadmap <TITLE> [--detail TEXT] [--priority N] [--workspace PATH]");
    Console.WriteLine("  add-issue <TITLE> --role ROLE [--area AREA] [--detail TEXT] [--priority N] [--roadmap-item-id N] [--depends-on N [N...]] [--workspace PATH]");
    Console.WriteLine("  add-question <TEXT> [--blocking] [--workspace PATH]");
    Console.WriteLine("  answer-question <ID> <ANSWER> [--workspace PATH]");
    Console.WriteLine("  approve-plan [--note TEXT] [--workspace PATH]");
    Console.WriteLine("  feedback <TEXT> [--workspace PATH]");
    Console.WriteLine("  questions [--workspace PATH]");
    Console.WriteLine("  plan [--workspace PATH]");
    Console.WriteLine("  budget [--total N] [--premium N] [--workspace PATH]");
    Console.WriteLine("  run-once [--max-subagents N] [--workspace PATH]");
    Console.WriteLine("  run-loop [--backend sdk|cli] [--max-iterations N] [--max-subagents N] [--timeout-seconds N] [--verbosity quiet|normal|detailed] [--workspace PATH]");
    Console.WriteLine("  complete-run --run-id N --outcome completed|failed|blocked --summary TEXT [--workspace PATH]");
    Console.WriteLine("  status [--workspace PATH]");
    Console.WriteLine("  agent-invoke [--backend sdk|cli] [--prompt TEXT] [--model NAME] [--timeout-seconds N] [--working-directory PATH] [--extra-arg ARG ...]");
    Console.WriteLine("  workspace-mcp --workspace PATH");
}

static void PrintInteractiveHelp()
{
    Console.WriteLine("Interactive commands:");
    Console.WriteLine("  /init \"goal text\" [--mode SLUG]");
    Console.WriteLine("  /status");
    Console.WriteLine("  /mode <slug>");
    Console.WriteLine("  /add-issue \"title\" --role ROLE [--area AREA] [--detail TEXT] [--priority N] [--roadmap-item-id N] [--depends-on N [N...]]");
    Console.WriteLine("  /plan");
    Console.WriteLine("  /questions");
    Console.WriteLine("  /budget [--total N] [--premium N]");
    Console.WriteLine("  /run [--max-iterations N] [--timeout-seconds N]");
    Console.WriteLine("  /feedback <text>");
    Console.WriteLine("  /approve [note]");
    Console.WriteLine("  /answer <id> <text>");
    Console.WriteLine("  /goal <text>");
    Console.WriteLine("  /exit");
    Console.WriteLine("If exactly one question is open, you can type a plain answer without `/answer`.");
    Console.WriteLine("While a plan is awaiting approval, plain text is treated as planning feedback and re-runs planning.");
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
    options.TryGetValue(key, out var values) && values.Count > 0 ? string.Join(" ", values) : null;

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

static int? GetNullableIntOption(Dictionary<string, List<string>> options, string key) =>
    int.TryParse(GetOption(options, key), out var value) ? value : null;

static IReadOnlyList<int> GetMultiIntOption(Dictionary<string, List<string>> options, string key) =>
    options.TryGetValue(key, out var values)
        ? values.Select(int.Parse).ToList()
        : [];

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

static void UpdateBudget(WorkspaceState state, Dictionary<string, List<string>> options)
{
    var total = GetDoubleOption(options, "total", state.Budget.TotalCreditCap);
    var premium = GetDoubleOption(options, "premium", state.Budget.PremiumCreditCap);
    state.Budget.TotalCreditCap = total;
    state.Budget.PremiumCreditCap = premium;
}
