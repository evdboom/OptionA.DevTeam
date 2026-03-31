using DevTeam.Core;
using static DevTeam.Cli.CliOptionParser;
using static DevTeam.Cli.CliWorkspaceHelper;
using static DevTeam.Cli.WorkspaceStatusPrinter;
using static DevTeam.Cli.UpdateCommandHandler;
using static DevTeam.Cli.CliLoopHandler;

namespace DevTeam.Cli;

internal sealed class CliDispatcher(
    WorkspaceStore store,
    DevTeamRuntime runtime,
    LoopExecutor loopExecutor,
    ToolUpdateService toolUpdateService,
    string workspacePath)
{
    public async Task<int> DispatchAsync(string command, Dictionary<string, List<string>> options)
    {
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
}
