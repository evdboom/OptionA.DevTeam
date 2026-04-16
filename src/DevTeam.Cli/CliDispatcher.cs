using DevTeam.Core;
using static DevTeam.Cli.CliOptionParser;
using static DevTeam.Cli.CliWorkspaceHelper;
using static DevTeam.Cli.WorkspaceStatusPrinter;
using static DevTeam.Cli.UpdateCommandHandler;
using static DevTeam.Cli.CliLoopHandler;

namespace DevTeam.Cli;

internal sealed class CliDispatcher
{
    private readonly WorkspaceStore _store;
    private readonly DevTeamRuntime _runtime;
    private readonly LoopExecutor _loopExecutor;
    private readonly ToolUpdateService _toolUpdateService;
    private readonly string _workspacePath;
    private readonly IConsoleOutput _output;

    public CliDispatcher(
        WorkspaceStore store,
        DevTeamRuntime runtime,
        LoopExecutor loopExecutor,
        ToolUpdateService toolUpdateService,
        string workspacePath,
        IConsoleOutput? output = null)
    {
        _store = store;
        _runtime = runtime;
        _loopExecutor = loopExecutor;
        _toolUpdateService = toolUpdateService;
        _workspacePath = workspacePath;
        _output = output ?? new ConsoleOutput();
    }

    public async Task<int> DispatchAsync(string command, Dictionary<string, List<string>> options)
    {
        switch (command)
        {
            case "start":
                return await RunShellAsync(_store, _runtime, _loopExecutor, _toolUpdateService, options);

            case "check-update":
                return await CheckForToolUpdatesAsync(_toolUpdateService);

            case "update":
                return await ScheduleToolUpdateAsync(_toolUpdateService, interactiveShell: false);

            case "ui-harness":
                return await UiHarness.RunAsync(options);

            case "workspace-mcp":
            {
                var workspace = GetOption(options, "workspace") ?? ".devteam";
                var mcpBackend = GetOption(options, "backend") ?? "sdk";
                var mcpTimeout = TimeSpan.FromSeconds(GetIntOption(options, "timeout-seconds", 600));
                var mcpRuntime = new DevTeamRuntime();
                var mcpStore = new WorkspaceStore(workspace);
                var mcpExecutor = new LoopExecutor(mcpRuntime, mcpStore);
                Func<int, string?, CancellationToken, Task<string>> spawnAgent =
                    (issueId, contextHint, ct) => mcpExecutor.SpawnIssueAsync(issueId, contextHint, mcpBackend, mcpTimeout, ct);
                var server = new WorkspaceMcpServer(workspace, spawnAgent);
                await server.RunAsync(Console.OpenStandardInput(), Console.OpenStandardOutput());
                return 0;
            }

            case "init":
            {
                var force = GetBoolOption(options, "force", false);
                if (!force && File.Exists(_store.StatePath))
                {
                    _output.WriteErrorLine($"Workspace already initialized at {Path.GetFullPath(_workspacePath)}.");
                    _output.WriteErrorLine("Use --force to reinitialize (this will reset all workspace state).");
                    return 1;
                }
                var totalCap = GetDoubleOption(options, "total-credit-cap", 25);
                var premiumCap = GetDoubleOption(options, "premium-credit-cap", 6);
                var goal = GoalInputResolver.Resolve(
                    GetOption(options, "goal") ?? GetPositionalValue(options),
                    GetOption(options, "goal-file"),
                    Environment.CurrentDirectory);
                var gitInitialized = GitWorkspace.EnsureRepository(Environment.CurrentDirectory);
                var state = _store.Initialize(Environment.CurrentDirectory, totalCap, premiumCap);
                var mode = GetOption(options, "mode");
                state.Runtime.KeepAwakeEnabled = GetBoolOption(options, "keep-awake", state.Runtime.KeepAwakeEnabled);
                state.Runtime.WorkspaceMcpEnabled = GetBoolOption(options, "workspace-mcp", true);
                state.Runtime.PipelineSchedulingEnabled = GetBoolOption(options, "pipeline-scheduling", true);
                state.Runtime.AutoApproveEnabled = GetBoolOption(options, "auto-approve", state.Runtime.AutoApproveEnabled);
                if (!string.IsNullOrWhiteSpace(mode))
                {
                    _runtime.SetMode(state, mode);
                }
                if (!string.IsNullOrWhiteSpace(goal))
                {
                    _runtime.SetGoal(state, goal);
                }
                _store.Save(state);

                _output.WriteLine($"Initialized devteam workspace at {Path.GetFullPath(_workspacePath)}");
                if (gitInitialized)
                {
                    _output.WriteLine($"Initialized git repository at {Path.GetFullPath(Environment.CurrentDirectory)}");
                }
                if (!string.IsNullOrWhiteSpace(goal))
                {
                    _output.WriteLine($"Active goal saved: {goal}");
                }

                var isNonEmptyRepo = Directory.EnumerateFileSystemEntries(Environment.CurrentDirectory)
                    .Any(f => !Path.GetFileName(f).StartsWith('.'));
                var runRecon = GetBoolOption(options, "recon", isNonEmptyRepo);
                if (runRecon)
                {
                    var backend = GetOption(options, "backend") ?? "sdk";
                    var timeout = TimeSpan.FromSeconds(GetIntOption(options, "timeout-seconds", 120));
                    _output.WriteLine("Running codebase reconnaissance...");
                    var recon = new ReconService(new DefaultAgentClientFactory());
                    var context = await recon.RunAsync(state, _store, backend, timeout, CancellationToken.None);
                    if (!string.IsNullOrWhiteSpace(context))
                    {
                        _output.WriteLine("Codebase context written to .devteam/codebase-context.md");
                    }
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

            case "export":
            {
                var outputPath = GetOption(options, "output");
                var archivePath = WorkspaceArchiveService.Export(_workspacePath, outputPath);
                _output.WriteLine($"Exported workspace to {archivePath}");
                return 0;
            }

            case "import":
            {
                var inputPath = GetOption(options, "input") ?? GetPositionalValue(options)
                    ?? throw new InvalidOperationException("Usage: import --input PATH [--force] [--workspace PATH]");
                var importedPath = WorkspaceArchiveService.Import(inputPath, _workspacePath, GetBoolOption(options, "force", false));
                _output.WriteLine($"Imported workspace into {importedPath}");
                return 0;
            }

            case "brownfield-log":
            {
                var path = Path.Combine(_workspacePath, "brownfield-delta.md");
                if (!File.Exists(path))
                {
                    _output.WriteLine("No brownfield delta log yet.");
                    return 0;
                }

                _output.WriteLine(File.ReadAllText(path));
                return 0;
            }

            case "start-here":
            {
                var state = File.Exists(_store.StatePath) ? _store.Load() : null;
                var persona = GetPositionalValue(options);
                _output.WriteLine(DevTeam.Cli.Shell.NonInteractiveShellHost.StripMarkup(
                    OnboardingGuideBuilder.BuildMarkup(state, _runtime, persona)));
                return 0;
            }

            case "bug":
            case "bug-report":
            {
                EmitBugReport(_store, _runtime, options, shellDiagnostics: null);
                return 0;
            }

            case "set-goal":
            case "goal":
            {
                var state = _store.Load();
                var goal = GoalInputResolver.Resolve(
                    GetPositionalValue(options),
                    GetOption(options, "goal-file"),
                    Environment.CurrentDirectory)
                    ?? throw new InvalidOperationException("Missing goal text. Provide inline text or --goal-file PATH.");
                _runtime.SetGoal(state, goal);
                _store.Save(state);
                _output.WriteLine("Updated active goal.");
                return 0;
            }

            case "pipeline":
            {
                var state = _store.Load();
                var source = state.Runtime.PipelineRolesCustomized ? "custom" : $"mode default ({state.Runtime.ActiveModeSlug})";
                _output.WriteLine($"Current pipeline: {string.Join(" -> ", state.Runtime.DefaultPipelineRoles)} [{source}]");
                return 0;
            }

            case "set-pipeline":
            {
                var state = _store.Load();
                var values = GetPositionalValues(options);
                if (values.Count == 0)
                {
                    throw new InvalidOperationException("Usage: set-pipeline <role...|default>");
                }

                if (values.Count == 1 && string.Equals(values[0], "default", StringComparison.OrdinalIgnoreCase))
                {
                    _runtime.ResetDefaultPipelineRoles(state);
                    _store.Save(state);
                    _output.WriteLine($"Reset pipeline to mode default: {string.Join(" -> ", state.Runtime.DefaultPipelineRoles)}");
                    return 0;
                }

                _runtime.SetDefaultPipelineRoles(state, values);
                _store.Save(state);
                _output.WriteLine($"Updated pipeline: {string.Join(" -> ", state.Runtime.DefaultPipelineRoles)}");
                return 0;
            }

            case "set-mode":
            case "mode":
            {
                var state = _store.Load();
                var mode = GetPositionalValue(options) ?? throw new InvalidOperationException("Missing mode slug.");
                _runtime.SetMode(state, mode);
                _store.Save(state);
                _output.WriteLine($"Updated active mode to {state.Runtime.ActiveModeSlug}.");
                return 0;
            }

            case "set-keep-awake":
            case "keep-awake":
            {
                var state = _store.Load();
                var requested = GetPositionalValue(options) ?? GetOption(options, "enabled")
                    ?? throw new InvalidOperationException("Usage: set-keep-awake <true|false> [--workspace PATH]");
                var enabled = ParseBoolOrThrow(requested, "Usage: set-keep-awake <true|false> [--workspace PATH]");
                _runtime.SetKeepAwake(state, enabled);
                _store.Save(state);
                _output.WriteLine($"Updated keep-awake setting to {(enabled ? "enabled" : "disabled")}.");
                return 0;
            }

            case "add-roadmap":
            {
                var state = _store.Load();
                var title = GetPositionalValue(options) ?? throw new InvalidOperationException("Missing roadmap title.");
                var detail = GetOption(options, "detail") ?? "";
                var priority = GetIntOption(options, "priority", 50);
                var item = _runtime.AddRoadmapItem(state, title, detail, priority);
                _store.Save(state);
                _output.WriteLine($"Created roadmap item #{item.Id}: {item.Title}");
                return 0;
            }

            case "add-issue":
            {
                var state = _store.Load();
                var title = GetPositionalValue(options) ?? throw new InvalidOperationException("Missing issue title.");
                var role = GetOption(options, "role") ?? throw new InvalidOperationException(BuildMissingRoleMessage(_runtime, state));
                var detail = GetOption(options, "detail") ?? "";
                var area = GetOption(options, "area");
                var priority = GetIntOption(options, "priority", 50);
                var roadmapId = GetNullableIntOption(options, "roadmap-item-id");
                var dependsOn = GetMultiIntOption(options, "depends-on");
                ValidateRoleOrThrow(_runtime, state, role);
                var issue = _runtime.AddIssue(state, title, detail, role, priority, roadmapId, dependsOn, area);
                _store.Save(state);
                _output.WriteLine($"Created issue #{issue.Id}: {issue.Title} ({issue.RoleSlug}{(string.IsNullOrWhiteSpace(issue.Area) ? "" : $", area {issue.Area}")})");
                return 0;
            }

            case "edit-issue":
            {
                var state = _store.Load();
                var request = IssueEditRequestParser.Parse(_runtime, state, options);
                var issue = _runtime.EditIssue(state, request);
                _store.Save(state);
                _output.WriteLine($"Updated issue #{issue.Id}: {issue.Title} ({issue.RoleSlug}{(string.IsNullOrWhiteSpace(issue.Area) ? "" : $", area {issue.Area}")}, priority {issue.Priority}, status {issue.Status.ToString().ToLowerInvariant()})");
                return 0;
            }

            case "add-question":
            {
                var state = _store.Load();
                var text = GetPositionalValue(options) ?? throw new InvalidOperationException("Missing question text.");
                var question = _runtime.AddQuestion(state, text, options.ContainsKey("blocking"));
                _store.Save(state);
                _output.WriteLine($"Created {(question.IsBlocking ? "blocking" : "non-blocking")} question #{question.Id}");
                return 0;
            }

            case "answer-question":
            case "answer":
            {
                var state = _store.Load();
                var values = GetPositionalValues(options);
                if (values.Count < 2)
                {
                    throw new InvalidOperationException("Usage: answer-question <id> <answer>");
                }
                _runtime.AnswerQuestion(state, int.Parse(values[0]), string.Join(" ", values.Skip(1)));
                _store.Save(state);
                _output.WriteLine($"Answered question #{values[0]}");
                return 0;
            }

            case "approve-plan":
            case "approve":
            {
                var state = _store.Load();
                var note = GetOption(options, "note") ?? GetPositionalValue(options) ?? "User approved the current plan.";
                if (state.Phase == WorkflowPhase.ArchitectPlanning)
                {
                    _runtime.ApproveArchitectPlan(state, note);
                    _store.Save(state);
                    _output.WriteLine("Approved the architect plan. Execution work can now begin.");
                }
                else
                {
                    _runtime.ApprovePlan(state, note);
                    _store.Save(state);
                    if (state.Phase == WorkflowPhase.ArchitectPlanning)
                    {
                        _output.WriteLine("Approved the high-level plan. Architect planning phase is next — run the loop to let the architect design the execution plan, then approve again.");
                    }
                    else
                    {
                        _output.WriteLine("Approved the current plan. Execution work can now continue.");
                    }
                }
                return 0;
            }

            case "set-auto-approve":
            {
                var state = _store.Load();
                var requested = GetPositionalValue(options) ?? GetOption(options, "enabled")
                    ?? throw new InvalidOperationException("Usage: set-auto-approve <true|false> [--workspace PATH]");
                var enabled = ParseBoolOrThrow(requested, "Usage: set-auto-approve <true|false> [--workspace PATH]");
                _runtime.SetAutoApprove(state, enabled);
                _store.Save(state);
                _output.WriteLine($"Updated auto-approve setting to {(enabled ? "enabled" : "disabled")}.");
                return 0;
            }

            case "feedback":
            {
                var state = _store.Load();
                var feedback = GetPositionalValue(options) ?? throw new InvalidOperationException("Missing feedback text.");
                _runtime.RecordPlanningFeedback(state, feedback);
                _store.Save(state);
                _output.WriteLine(state.Phase == WorkflowPhase.ArchitectPlanning
                    ? "Captured architect plan feedback."
                    : "Captured planning feedback.");
                return 0;
            }

            case "preview":
            {
                var state = _store.Load();
                if (PlanWorkflow.RequiresPlanningBeforeRun(state, _store))
                {
                    _output.WriteLine("No plan has been written yet. Run `plan` first.");
                    return 0;
                }
                if (PlanWorkflow.IsAwaitingApproval(state, _store))
                {
                    _output.WriteLine("A plan is ready. Review it with `plan`, provide feedback, or approve it before starting the loop.");
                    return 0;
                }

                var maxSubagents = GetIntOption(options, "max-subagents", state.Runtime.DefaultMaxSubagents);
                RunPreviewPrinter.PrintPreview(state, _runtime, maxSubagents);
                return 0;
            }

            case "diff-run":
            {
                var state = _store.Load();
                var values = GetPositionalValues(options);
                if (values.Count is < 1 or > 2 || !int.TryParse(values[0], out var runId) || (values.Count == 2 && !int.TryParse(values[1], out _)))
                {
                    throw new InvalidOperationException("Usage: diff-run <run-id> [compare-run-id]");
                }

                var compareRunId = values.Count == 2 ? int.Parse(values[1]) : (int?)null;
                var report = _runtime.BuildRunDiff(state, runId, compareRunId);
                _output.WriteLine(DevTeam.Cli.Shell.NonInteractiveShellHost.StripMarkup(RunDiffPrinter.BuildMarkup(report)));
                return 0;
            }

            case "run-once":
            {
                var state = _store.Load();
                if (PlanWorkflow.RequiresPlanningBeforeRun(state, _store))
                {
                    _output.WriteLine("No plan has been written yet. Run `plan` first.");
                    return 1;
                }
                var maxSubagents = GetIntOption(options, "max-subagents", 3);
                var result = _runtime.RunOnce(state, maxSubagents);
                _store.Save(state);
                PrintLoopResult(result);
                return 0;
            }

            case "run-loop":
            case "run":
            {
                var state = _store.Load();
                if (PlanWorkflow.RequiresPlanningBeforeRun(state, _store))
                {
                    _output.WriteLine("No plan has been written yet. Run `plan` first.");
                    return 1;
                }
                if (PlanWorkflow.IsAwaitingApproval(state, _store))
                {
                    _output.WriteLine("A plan is ready. Review it with `plan`, provide feedback, or approve it before starting the loop.");
                    return 1;
                }
                if (GetBoolOption(options, "dry-run", false))
                {
                    var maxSubagents = GetIntOption(options, "max-subagents", state.Runtime.DefaultMaxSubagents);
                    RunPreviewPrinter.PrintPreview(state, _runtime, maxSubagents);
                    _output.WriteLine("Dry run only — nothing was executed.");
                    return 0;
                }
                var report = await RunLoopAsync(_store, _runtime, _loopExecutor, state, options);
                _output.WriteLine($"Loop complete after {report.IterationsExecuted} iteration(s). Final state: {report.FinalState}");
                PrintBudget(state.Budget);
                if (report.FinalState == "awaiting-architect-approval")
                {
                    PrintArchitectSummary(state);
                }
                return 0;
            }

            case "complete-run":
            {
                var state = _store.Load();
                var runId = GetNullableIntOption(options, "run-id") ?? throw new InvalidOperationException("Missing --run-id.");
                var outcome = GetOption(options, "outcome") ?? throw new InvalidOperationException("Missing --outcome.");
                var summary = GetOption(options, "summary") ?? throw new InvalidOperationException("Missing --summary.");
                _runtime.CompleteRun(state, runId, outcome, summary);
                _store.Save(state);
                _output.WriteLine($"Updated run #{runId} as {outcome}");
                return 0;
            }

            case "status":
                PrintStatus(_store.Load(), _runtime);
                return 0;

            case "questions":
            {
                var state = _store.Load();
                PrintQuestions(state, _store, _runtime);
                return 0;
            }

            case "plan":
            {
                var state = _store.Load();
                await ShowPlanAsync(_store, _runtime, _loopExecutor, state, options, interactive: false);
                return 0;
            }

            case "budget":
            {
                var state = _store.Load();
                UpdateBudget(state, options);
                _store.Save(state);
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

                var client = new DefaultAgentClientFactory().Create(backend);
                var result = await client.InvokeAsync(new AgentInvocationRequest
                {
                    Prompt = prompt,
                    Model = model,
                    WorkingDirectory = Path.GetFullPath(workingDirectory),
                    Timeout = TimeSpan.FromSeconds(timeoutSeconds),
                    ExtraArguments = extraArgs,
                    WorkspacePath = Path.GetFullPath(_workspacePath),
                    EnableWorkspaceMcp = GetBoolOption(options, "workspace-mcp", false),
                    ToolHostPath = System.Reflection.Assembly.GetEntryAssembly()?.Location
                });

                _output.WriteLine($"Backend: {result.BackendName}");
                _output.WriteLine($"Exit code: {result.ExitCode}");
                if (!string.IsNullOrWhiteSpace(result.StdOut))
                {
                    _output.WriteLine(result.StdOut.TrimEnd());
                }
                if (!string.IsNullOrWhiteSpace(result.StdErr))
                {
                    _output.WriteErrorLine(result.StdErr.TrimEnd());
                }
                return result.Success ? 0 : result.ExitCode;
            }

            default:
                PrintHelp();
                return 1;
        }
    }
}
