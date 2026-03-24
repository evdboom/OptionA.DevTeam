using System.Collections.Concurrent;
using System.Text;
using DevTeam.Core;
using Microsoft.Extensions.Hosting;
using Spectre.Console;

namespace DevTeam.Cli.Shell;

/// <summary>
/// Core service driving the RazorConsole interactive shell.
/// Replaces the old RunInteractiveShellAsync function from Program.cs.
/// Thread-safe: background loop threads may call Add* methods from any thread.
/// </summary>
internal sealed class ShellService : IDisposable
{
    // ── Dependencies ───────────────────────────────────────────────────────────

    private readonly WorkspaceStore _store;
    private readonly DevTeamRuntime _runtime;
    private readonly LoopExecutor _loopExecutor;
    private readonly ToolUpdateService _toolUpdateService;
    private readonly ShellStartOptions _startOptions;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ShellSessionDiagnostics _diagnostics = new();
    private readonly KeepAwakeController _keepAwake = new();

    // ── Mutable shell state ────────────────────────────────────────────────────

    private readonly object _gate = new();
    private readonly List<ShellMessage> _messages = [];
    private readonly List<(DateTimeOffset At, string Entry)> _history = [];

    private Task<LoopExecutionReport>? _loopTask;
    private CancellationTokenSource? _loopCts;
    private string? _lastContextKey;
    private bool _keepAwakeEnabled;

    // ── Public surface ─────────────────────────────────────────────────────────

    public event Action? OnStateChanged;

    public IReadOnlyList<ShellMessage> Messages
    {
        get { lock (_gate) return [.. _messages]; }
    }

    public bool IsLoopRunning => _loopTask is { IsCompleted: false };
    public string PromptText => IsLoopRunning ? "devteam (running)> " : "devteam> ";

    // ── Constructor ────────────────────────────────────────────────────────────

    public ShellService(
        WorkspaceStore store,
        DevTeamRuntime runtime,
        LoopExecutor loopExecutor,
        ToolUpdateService toolUpdateService,
        ShellStartOptions startOptions,
        IHostApplicationLifetime lifetime)
    {
        _store = store;
        _runtime = runtime;
        _loopExecutor = loopExecutor;
        _toolUpdateService = toolUpdateService;
        _startOptions = startOptions;
        _lifetime = lifetime;
    }

    // ── Initialization ─────────────────────────────────────────────────────────

    /// <summary>Called once from DevTeamShell.OnInitialized to set up the session.</summary>
    public async Task InitializeAsync()
    {
        TryLoadState(out var initialState);

        AddBanner(_store.WorkspacePath);

        // Background update check — fire and forget
        _ = NotifyAboutUpdateAsync();

        if (initialState is null)
        {
            AddLine("[dim]No workspace found.[/] Use [cyan]/init[/] [dim]--goal \"<your goal>\"[/] to get started.");
        }

        // Apply keep-awake
        _keepAwakeEnabled = GetNullableBoolOption(_startOptions.Options, "keep-awake")
            ?? initialState?.Runtime.KeepAwakeEnabled ?? false;
        if (_keepAwakeEnabled)
        {
            try
            {
                _keepAwake.SetEnabled(true);
                AddLine("[dim]Keep-awake enabled for this session.[/]");
            }
            catch (InvalidOperationException ex)
            {
                AddLine($"[dim]{Markup.Escape(ex.Message)}[/]");
            }
        }

        if (initialState is not null)
        {
            CheckAndUpdateContext(initialState);
        }

        await Task.CompletedTask; // keeps method async for future awaits
    }

    // ── Input processing ───────────────────────────────────────────────────────

    public async Task ProcessInputAsync(string line)
    {
        line = line.Trim();
        if (string.IsNullOrEmpty(line)) return;

        AddHistory(line);

        TryLoadState(out var state);
        var openQuestions = state?.Questions.Where(q => q.Status == QuestionStatus.Open).ToList() ?? [];

        // ── @role prefix ────────────────────────────────────────────────────────
        if (line.StartsWith("@", StringComparison.Ordinal) && state is not null)
        {
            var spaceIdx = line.IndexOf(' ');
            var roleInput = spaceIdx > 1 ? line[1..spaceIdx] : line[1..];
            var userMsg = spaceIdx > 1 ? line[(spaceIdx + 1)..].Trim() : "";
            if (string.IsNullOrWhiteSpace(userMsg))
            {
                AddHint("Usage: [cyan]@role[/] <message>");
                return;
            }
            if (!_runtime.TryResolveRoleSlug(state, roleInput, out var roleSlug))
            {
                AddWarning($"Unknown role '{Markup.Escape(roleInput)}'. Known roles: {string.Join(", ", _runtime.GetKnownRoleSlugs(state).OrderBy(s => s))}");
                return;
            }
            AddHint($"[italic]You → {Markup.Escape(roleSlug)}:[/] [dim]{Markup.Escape(userMsg.Length > 120 ? userMsg[..117] + "..." : userMsg)}[/]");
            await InvokeRoleDirectAsync(state, roleSlug, userMsg);
            RefreshContext(state);
            return;
        }

        // ── Non-slash smart handling ─────────────────────────────────────────────
        if (!line.StartsWith("/", StringComparison.Ordinal) && state is not null)
        {
            // Plain answer for first open question
            if (openQuestions.Count > 0 && !IsLoopRunning)
            {
                var q = openQuestions[0];
                _runtime.AnswerQuestion(state, q.Id, line);
                _store.Save(state);
                var stillOpen = state.Questions.Count(x => x.Status == QuestionStatus.Open);
                AddSuccess(stillOpen == 0 ? "Got it! All questions answered." : $"Got it! {stillOpen} question(s) remaining.");
                _lastContextKey = null;
                RefreshContext(_store.Load());
                return;
            }

            // "approve" / "yes" shortcut
            if (IsApproveIntent(line))
            {
                if (IsLoopRunning)
                {
                    AddWarning("A loop is running. Use /stop first, then approve.");
                    return;
                }
                var canApproveArch = state.Phase == WorkflowPhase.ArchitectPlanning && PlanWorkflow.IsAwaitingArchitectApproval(state);
                var canApprovePlan = state.Phase == WorkflowPhase.Planning && state.Issues.Any(i => i.IsPlanningIssue && i.Status == ItemStatus.Done);
                if (!canApproveArch && !canApprovePlan)
                {
                    AddHint("Nothing to approve right now. Use [dim]/plan[/] to generate a plan first.");
                    return;
                }
                if (canApproveArch)
                {
                    _runtime.ApproveArchitectPlan(state, "Approved via chat.");
                    _store.Save(state);
                    AddSuccess("Architect plan approved. Starting execution loop...");
                }
                else
                {
                    _runtime.ApprovePlan(state, "Approved via chat.");
                    _store.Save(state);
                    AddSuccess(state.Phase == WorkflowPhase.ArchitectPlanning
                        ? "Plan approved. Running architect planning..."
                        : "Plan approved. Starting execution loop...");
                }
                StartLoopInBackground(state, ParseOptions([]));
                _lastContextKey = null;
                return;
            }

            // Planning feedback
            if ((state.Phase == WorkflowPhase.Planning && state.Issues.Any(i => i.IsPlanningIssue && i.Status == ItemStatus.Done))
                || PlanWorkflow.IsAwaitingArchitectApproval(state))
            {
                if (IsLoopRunning)
                {
                    AddWarning("A loop is running. Use /stop first before sending planning feedback.");
                    return;
                }
                _runtime.RecordPlanningFeedback(state, line);
                _store.Save(state);
                AddEvent("✎", state.Phase == WorkflowPhase.ArchitectPlanning
                    ? "Captured architect plan feedback. Revising..."
                    : "Captured planning feedback. Revising...", "cyan");
                var bgLogger = MakeBackgroundLogger();
                var report = await RunLoopCoreAsync(state, ParseOptions(["--max-iterations", "2"]), CancellationToken.None, bgLogger);
                AddEvent("✓", $"Revision complete ({report.IterationsExecuted} iteration(s)). Final state: {Markup.Escape(report.FinalState)}", "green");
                AddHint(state.Phase == WorkflowPhase.ArchitectPlanning
                    ? "Type [bold]approve[/] to begin execution, or use [dim]/plan[/] to review."
                    : "Type [bold]approve[/] to proceed, or use [dim]/plan[/] to review.");
                _lastContextKey = null;
                RefreshContext(_store.Load());
                return;
            }
        }

        // ── Tokenize and dispatch ───────────────────────────────────────────────
        var tokens = TokenizeInput(line);
        if (tokens.Count == 0) return;

        var command = NormalizeCommand(tokens[0]);
        var options = ParseOptions(tokens.Skip(1).ToArray());

        try
        {
            if (!string.Equals(command, "bug", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(command, "bug-report", StringComparison.OrdinalIgnoreCase))
            {
                _diagnostics.RecordCommand(line);
            }

            switch (command)
            {
                case "exit":
                case "quit":
                    if (IsLoopRunning) _loopCts?.Cancel();
                    _lifetime.StopApplication();
                    return;

                case "help":
                    AddInteractiveHelp();
                    break;

                case "check-update":
                    await HandleCheckUpdateAsync();
                    break;

                case "update":
                    await HandleUpdateAsync();
                    break;

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
                    var redact = GetBoolOption(options, "redact-paths", true);
                    var histCount = GetIntOption(options, "history-count", 8);
                    var errCount = GetIntOption(options, "error-count", 5);
                    var report = BugReportBuilder.Build(_store, _runtime, _diagnostics, redact, histCount, errCount);
                    var savePath = GetOption(options, "save");
                    if (!string.IsNullOrWhiteSpace(savePath))
                    {
                        var fullPath = Path.GetFullPath(Path.IsPathRooted(savePath)
                            ? savePath
                            : Path.Combine(Environment.CurrentDirectory, savePath));
                        var dir = Path.GetDirectoryName(fullPath);
                        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
                        File.WriteAllText(fullPath, report);
                        AddSuccess($"Saved bug report to {Markup.Escape(fullPath)}");
                    }
                    AddSystem(Markup.Escape(report.TrimEnd()), "bug report");
                    break;
                }

                case "init":
                {
                    var force = GetBoolOption(options, "force", false);
                    if (!force && File.Exists(_store.StatePath))
                    {
                        AddLine($"Workspace already initialized at {Markup.Escape(_store.WorkspacePath)}. Use /init --force to reinitialize.");
                        break;
                    }
                    if (force && IsLoopRunning)
                    {
                        AddLine("A loop is running. Use [cyan]/stop[/] first before reinitializing the workspace.");
                        break;
                    }
                    var totalCap = GetDoubleOption(options, "total-credit-cap", 25);
                    var premiumCap = GetDoubleOption(options, "premium-credit-cap", 6);
                    var goal = GoalInputResolver.Resolve(
                        GetOption(options, "goal") ?? GetPositionalValue(options),
                        GetOption(options, "goal-file"),
                        Environment.CurrentDirectory);
                    var gitInit = GitWorkspace.EnsureRepository(Environment.CurrentDirectory);
                    var initialized = _store.Initialize(Environment.CurrentDirectory, totalCap, premiumCap);
                    var modeName = GetOption(options, "mode");
                    initialized.Runtime.KeepAwakeEnabled = GetBoolOption(options, "keep-awake", initialized.Runtime.KeepAwakeEnabled);
                    initialized.Runtime.WorkspaceMcpEnabled = GetBoolOption(options, "workspace-mcp", true);
                    initialized.Runtime.PipelineSchedulingEnabled = GetBoolOption(options, "pipeline-scheduling", true);
                    if (!string.IsNullOrWhiteSpace(modeName)) _runtime.SetMode(initialized, modeName);
                    if (!string.IsNullOrWhiteSpace(goal)) _runtime.SetGoal(initialized, goal);
                    _store.Save(initialized);
                    AddSuccess($"Initialized workspace at {Markup.Escape(_store.WorkspacePath)}");
                    if (gitInit) AddSuccess($"Initialized git repository at {Markup.Escape(Path.GetFullPath(Environment.CurrentDirectory))}");
                    if (!string.IsNullOrWhiteSpace(goal)) AddSuccess($"Active goal saved: {Markup.Escape(goal)}");
                    AddHint($"Next step: run [cyan]/plan[/] to generate the high-level plan.");
                    break;
                }

                case "status":
                    PrintStatus(_store.Load());
                    break;

                case "history":
                {
                    List<(DateTimeOffset At, string Entry)> snap;
                    lock (_gate) snap = [.. _history];
                    if (snap.Count == 0)
                    {
                        AddHint("No session history yet.");
                        break;
                    }
                    var sb = new StringBuilder();
                    var start = snap[0].At;
                    foreach (var (at, entry) in snap)
                    {
                        var elapsed = at - start;
                        sb.AppendLine($"[dim]+{(int)elapsed.TotalMinutes:D2}:{elapsed.Seconds:D2}[/] {Markup.Escape(entry)}");
                    }
                    AddSystem(sb.ToString().TrimEnd(), "history");
                    break;
                }

                case "add-issue":
                {
                    var current = _store.Load();
                    var title = GetPositionalValue(options) ?? throw new InvalidOperationException("Missing issue title.");
                    var role = GetOption(options, "role") ?? throw new InvalidOperationException($"Missing --role. Valid roles: {string.Join(", ", _runtime.GetKnownRoleSlugs(current))}");
                    var detail = GetOption(options, "detail") ?? "";
                    var area = GetOption(options, "area");
                    var priority = GetIntOption(options, "priority", 50);
                    var roadmapId = GetNullableIntOption(options, "roadmap-item-id");
                    var dependsOn = GetMultiIntOption(options, "depends-on");
                    var issue = _runtime.AddIssue(current, title, detail, role, priority, roadmapId, dependsOn, area);
                    _store.Save(current);
                    AddSuccess($"Created issue #{issue.Id}: {Markup.Escape(issue.Title)} ({Markup.Escape(issue.RoleSlug)})");
                    break;
                }

                case "questions":
                    PrintQuestions(_store.Load());
                    break;

                case "plan":
                {
                    var current = _store.Load();
                    await ShowPlanAsync(current, options);
                    break;
                }

                case "goal":
                case "set-goal":
                {
                    var current = _store.Load();
                    var goal = GoalInputResolver.Resolve(
                        GetPositionalValue(options),
                        GetOption(options, "goal-file"),
                        Environment.CurrentDirectory)
                        ?? throw new InvalidOperationException("Missing goal text. Provide inline text or --goal-file PATH.");
                    _runtime.SetGoal(current, goal);
                    _store.Save(current);
                    AddSuccess("Updated active goal.");
                    break;
                }

                case "mode":
                case "set-mode":
                {
                    var current = _store.Load();
                    var modeArg = GetPositionalValue(options) ?? throw new InvalidOperationException("Usage: /mode <slug>");
                    _runtime.SetMode(current, modeArg);
                    _store.Save(current);
                    AddSuccess($"Updated active mode to {Markup.Escape(current.Runtime.ActiveModeSlug)}.");
                    break;
                }

                case "keep-awake":
                case "set-keep-awake":
                {
                    TryLoadState(out var current);
                    var requested = GetPositionalValue(options) ?? GetOption(options, "enabled");
                    if (string.IsNullOrWhiteSpace(requested))
                    {
                        AddLine($"Keep-awake for this shell: {(_keepAwakeEnabled ? "enabled" : "disabled")}." +
                            (current is not null ? $" Workspace default: {(current.Runtime.KeepAwakeEnabled ? "enabled" : "disabled")}." : ""));
                        break;
                    }
                    var enabled = ParseBoolOrThrow(requested, "Usage: /keep-awake <on|off>");
                    _keepAwakeEnabled = enabled;
                    try { _keepAwake.SetEnabled(enabled); } catch (InvalidOperationException ex) { AddWarning(ex.Message); }
                    if (current is not null)
                    {
                        _runtime.SetKeepAwake(current, enabled);
                        _store.Save(current);
                        AddSuccess($"Keep-awake {(enabled ? "enabled" : "disabled")} for this shell and workspace.");
                    }
                    else
                    {
                        AddSuccess($"Keep-awake {(enabled ? "enabled" : "disabled")} for this shell session.");
                    }
                    break;
                }

                case "approve":
                case "approve-plan":
                {
                    if (IsLoopRunning)
                    {
                        AddWarning("A loop is running. Use /stop first, then /approve.");
                        break;
                    }
                    var current = _store.Load();
                    var note = GetOption(options, "note") ?? GetPositionalValue(options) ?? "User approved the current plan.";
                    if (current.Phase == WorkflowPhase.ArchitectPlanning && PlanWorkflow.IsAwaitingArchitectApproval(current))
                    {
                        _runtime.ApproveArchitectPlan(current, note);
                        _store.Save(current);
                        AddSuccess("Architect plan approved. Starting execution loop...");
                        StartLoopInBackground(current, ParseOptions([]));
                    }
                    else
                    {
                        _runtime.ApprovePlan(current, note);
                        _store.Save(current);
                        AddSuccess(current.Phase == WorkflowPhase.ArchitectPlanning
                            ? "Plan approved. Running architect planning..."
                            : "Plan approved. Starting execution loop...");
                        StartLoopInBackground(current, ParseOptions([]));
                    }
                    AddHint("/stop to cancel · /wait to re-attach");
                    _lastContextKey = null;
                    break;
                }

                case "set-auto-approve":
                case "auto-approve":
                {
                    TryLoadState(out var current);
                    var requested = GetPositionalValue(options) ?? GetOption(options, "enabled");
                    if (string.IsNullOrWhiteSpace(requested))
                    {
                        AddLine($"Auto-approve: {(current?.Runtime.AutoApproveEnabled == true ? "enabled" : "disabled")}.");
                        break;
                    }
                    var enabled = ParseBoolOrThrow(requested, "Usage: /auto-approve <on|off>");
                    if (current is not null)
                    {
                        _runtime.SetAutoApprove(current, enabled);
                        _store.Save(current);
                    }
                    AddSuccess($"Auto-approve {(enabled ? "enabled" : "disabled")}.");
                    break;
                }

                case "set-max-iterations":
                case "max-iterations":
                {
                    TryLoadState(out var current);
                    var requested = GetPositionalValue(options) ?? GetOption(options, "value");
                    if (string.IsNullOrWhiteSpace(requested))
                    {
                        AddLine($"Default max-iterations: {current?.Runtime.DefaultMaxIterations ?? 10}. Usage: /max-iterations <N>");
                        break;
                    }
                    if (!int.TryParse(requested, out var n) || n < 1)
                    {
                        AddError("max-iterations must be a positive integer.");
                        break;
                    }
                    if (current is not null)
                    {
                        _runtime.SetDefaultMaxIterations(current, n);
                        _store.Save(current);
                        AddSuccess($"Default max-iterations set to {n}.");
                    }
                    else AddWarning("No workspace loaded — run /init first.");
                    break;
                }

                case "set-max-subagents":
                case "max-subagents":
                {
                    TryLoadState(out var current);
                    var requested = GetPositionalValue(options) ?? GetOption(options, "value");
                    if (string.IsNullOrWhiteSpace(requested))
                    {
                        AddLine($"Default max-subagents: {current?.Runtime.DefaultMaxSubagents ?? 1}. Usage: /max-subagents <N>");
                        AddHint("1 = sequential (safe default) · 2–4 = parallel pipelines · higher = more concurrent agents");
                        break;
                    }
                    if (!int.TryParse(requested, out var n) || n < 1)
                    {
                        AddError("max-subagents must be a positive integer.");
                        break;
                    }
                    if (current is not null)
                    {
                        _runtime.SetDefaultMaxSubagents(current, n);
                        _store.Save(current);
                        AddSuccess($"Default max-subagents set to {n}.");
                        if (n > 4) AddHint("High subagent count can increase credit consumption quickly.");
                    }
                    else AddWarning("No workspace loaded — run /init first.");
                    break;
                }

                case "feedback":
                {
                    if (IsLoopRunning)
                    {
                        AddWarning("A loop is running. Use /stop first, then /feedback.");
                        break;
                    }
                    var current = _store.Load();
                    var feedbackText = GetPositionalValue(options) ?? throw new InvalidOperationException("Usage: /feedback <text>");
                    _runtime.RecordPlanningFeedback(current, feedbackText);
                    _store.Save(current);
                    AddEvent("✎", current.Phase == WorkflowPhase.ArchitectPlanning
                        ? "Captured architect plan feedback. Revising..."
                        : "Captured planning feedback. Revising...", "cyan");
                    var bgLogger2 = MakeBackgroundLogger();
                    var feedbackReport = await RunLoopCoreAsync(current, ParseOptions(["--max-iterations", "2"]), CancellationToken.None, bgLogger2);
                    AddEvent("✓", $"Revision complete ({feedbackReport.IterationsExecuted} iteration(s)). Final state: {Markup.Escape(feedbackReport.FinalState)}", "green");
                    AddHint(current.Phase == WorkflowPhase.ArchitectPlanning
                        ? "Type [bold]approve[/] to begin execution, or use [dim]/plan[/] to review."
                        : "Type [bold]approve[/] to proceed, or use [dim]/plan[/] to review.");
                    _lastContextKey = null;
                    RefreshContext(_store.Load());
                    break;
                }

                case "answer":
                case "answer-question":
                {
                    var current = _store.Load();
                    var vals = GetPositionalValues(options);
                    if (vals.Count < 2) throw new InvalidOperationException("Usage: /answer <id> <answer>");
                    _runtime.AnswerQuestion(current, int.Parse(vals[0]), string.Join(" ", vals.Skip(1)));
                    _store.Save(current);
                    var remaining = current.Questions.Count(q => q.Status == QuestionStatus.Open);
                    AddSuccess(remaining == 0 ? "Got it! All questions answered." : $"Got it! {remaining} question(s) remaining.");
                    _lastContextKey = null;
                    RefreshContext(_store.Load());
                    break;
                }

                case "run":
                case "run-loop":
                {
                    if (IsLoopRunning)
                    {
                        AddLine("A loop is already running. Use [cyan]/stop[/] to cancel it or [cyan]/wait[/] to re-attach.");
                        break;
                    }
                    var current = _store.Load();
                    if (PlanWorkflow.RequiresPlanningBeforeRun(current, _store))
                    {
                        AddLine("No plan has been written yet. Run [cyan]/plan[/] first.");
                        break;
                    }
                    if (PlanWorkflow.IsAwaitingApproval(current, _store))
                    {
                        AddLine("A plan is ready. Use [cyan]/plan[/] to review, type feedback to revise, or [cyan]/approve[/] to continue.");
                        break;
                    }
                    var usingIterations = options.ContainsKey("max-iterations") ? int.Parse(options["max-iterations"][0]) : current.Runtime.DefaultMaxIterations;
                    var usingSubagents = options.ContainsKey("max-subagents") ? int.Parse(options["max-subagents"][0]) : current.Runtime.DefaultMaxSubagents;
                    AddHint($"max-iterations: [bold]{usingIterations}[/] · max-subagents: [bold]{usingSubagents}[/] [dim](change with /max-iterations N or /max-subagents N)[/]");
                    StartLoopInBackground(current, options);
                    AddEvent("🚀", "Loop started — running in the background. You can type commands while work progresses.", "green");
                    AddHint("/stop to cancel · /wait to re-attach · /status to check progress");
                    break;
                }

                case "stop":
                {
                    if (!IsLoopRunning)
                    {
                        AddHint("No loop is currently running.");
                        break;
                    }
                    _loopCts?.Cancel();
                    AddWarning("Stop requested — the loop will finish its current agent call then stop.");
                    break;
                }

                case "wait":
                {
                    if (_loopTask is null || _loopTask.IsCompleted)
                    {
                        AddHint("No loop is currently running.");
                        break;
                    }
                    AddEvent("⏳", "Waiting for loop to complete...", "dim");
                    try
                    {
                        var waitReport = await _loopTask;
                        var current = _store.Load();
                        AddSystem($"Loop finished — [bold]{waitReport.IterationsExecuted}[/] iteration(s). Final state: [cyan]{Markup.Escape(waitReport.FinalState)}[/]", "loop complete");
                        PrintBudget(current.Budget);
                        if (waitReport.FinalState == "awaiting-architect-approval") TryPrintArchitectSummary(current);
                    }
                    catch (OperationCanceledException)
                    {
                        AddWarning("Loop was cancelled.");
                    }
                    _loopTask = null;
                    _loopCts?.Dispose();
                    _loopCts = null;
                    _lastContextKey = null;
                    RefreshContext(_store.Load());
                    break;
                }

                case "run-once":
                {
                    if (IsLoopRunning)
                    {
                        AddWarning("A loop is running. Use /stop first.");
                        break;
                    }
                    var current = _store.Load();
                    if (PlanWorkflow.RequiresPlanningBeforeRun(current, _store))
                    {
                        AddLine("No plan has been written yet. Run [cyan]/plan[/] first.");
                        break;
                    }
                    var result = _runtime.RunOnce(current, GetIntOption(options, "max-subagents", 3));
                    _store.Save(current);
                    PrintLoopResult(result);
                    PrintBudget(current.Budget);
                    break;
                }

                case "budget":
                {
                    var current = _store.Load();
                    var total = GetDoubleOption(options, "total", current.Budget.TotalCreditCap);
                    var premium = GetDoubleOption(options, "premium", current.Budget.PremiumCreditCap);
                    current.Budget.TotalCreditCap = total;
                    current.Budget.PremiumCreditCap = premium;
                    _store.Save(current);
                    PrintBudget(current.Budget);
                    break;
                }

                default:
                {
                    _diagnostics.RecordError($"Unknown command: {tokens[0]}");
                    AddLine($"Unknown command '[yellow]{Markup.Escape(tokens[0])}[/]'. Type [cyan]/help[/].");
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _diagnostics.RecordError(ex.Message);
            AddError(ex.Message);
        }

        // Check for state context changes after command completes
        TryLoadState(out var freshState);
        if (freshState is not null) RefreshContext(freshState);
    }

    // ── Loop management ────────────────────────────────────────────────────────

    private void StartLoopInBackground(WorkspaceState state, Dictionary<string, List<string>> options)
    {
        var cts = new CancellationTokenSource();
        _loopCts = cts;
        var bgLogger = MakeBackgroundLogger();
        _loopTask = RunLoopCoreAsync(state, options, cts.Token, bgLogger);
        _loopTask.ContinueWith(t =>
        {
            try
            {
                if (t.IsCanceled || (t.IsFaulted && t.Exception?.GetBaseException() is OperationCanceledException))
                {
                    AddWarning("Loop was cancelled.");
                }
                else if (t.IsFaulted)
                {
                    AddError($"Loop failed: {Markup.Escape(t.Exception?.GetBaseException().Message ?? "unknown error")}");
                }
                else
                {
                    var r = t.Result;
                    var completedState = _store.Load();
                    AddSystem($"Loop finished — [bold]{r.IterationsExecuted}[/] iteration(s). Final state: [cyan]{Markup.Escape(r.FinalState)}[/]", "loop complete");
                    AddHistory($"loop complete — {r.IterationsExecuted} iteration(s), state: {r.FinalState}");
                    PrintBudget(completedState.Budget);
                    if (r.FinalState == "awaiting-architect-approval") TryPrintArchitectSummary(completedState);
                }
            }
            catch { /* swallow continuation errors */ }

            _loopTask = null;
            _loopCts?.Dispose();
            _loopCts = null;
            _lastContextKey = null;

            TryLoadState(out var finalState);
            if (finalState is not null) RefreshContext(finalState);
        }, TaskScheduler.Default);
    }

    private async Task<LoopExecutionReport> RunLoopCoreAsync(
        WorkspaceState state,
        Dictionary<string, List<string>> options,
        CancellationToken cancellationToken,
        Action<string>? logger = null)
    {
        var backend = GetOption(options, "backend") ?? "sdk";
        var maxSubagents = GetIntOption(options, "max-subagents", state.Runtime.DefaultMaxSubagents);
        var maxIterations = GetIntOption(options, "max-iterations", state.Runtime.DefaultMaxIterations);
        var timeoutSeconds = GetIntOption(options, "timeout-seconds", 600);
        var verbosity = ParseVerbosity(GetOption(options, "verbosity"));

        Action<string> log = logger ?? MakeBackgroundLogger();

        var executionOptions = new LoopExecutionOptions
        {
            Backend = backend,
            MaxSubagents = maxSubagents,
            MaxIterations = maxIterations,
            AgentTimeout = TimeSpan.FromSeconds(timeoutSeconds),
            HeartbeatInterval = TimeSpan.FromSeconds(5),
            Verbosity = verbosity,
            ProgressReporter = null
        };

        var report = await _loopExecutor.RunAsync(state, executionOptions, log, cancellationToken);
        _store.Save(state);
        return report;
    }

    private Action<string> MakeBackgroundLogger() =>
        msg => { AddLoopLog(msg); };

    // ── Plan workflow ──────────────────────────────────────────────────────────

    private async Task ShowPlanAsync(WorkspaceState state, Dictionary<string, List<string>> options)
    {
        var planningOptions = options.ToDictionary(p => p.Key, p => p.Value.ToList(), StringComparer.OrdinalIgnoreCase);
        if (!planningOptions.ContainsKey("max-iterations")) planningOptions["max-iterations"] = ["1"];
        if (!planningOptions.ContainsKey("max-subagents")) planningOptions["max-subagents"] = ["1"];

        var bgLogger = MakeBackgroundLogger();
        var result = await PlanWorkflow.EnsurePlanAsync(
            _store,
            state,
            planningState =>
            {
                AddLine("No plan has been written yet. Running the planner...");
                return RunLoopCoreAsync(_store.Load(), planningOptions, CancellationToken.None, bgLogger);
            });

        switch (result.Status)
        {
            case PlanPreparationStatus.MissingGoal:
                AddLine($"No active goal is set yet. Use [cyan]/goal[/] or [cyan]/init[/] --goal first.");
                return;
            case PlanPreparationStatus.Failed:
                AddLine("The planner ran, but no plan file was written yet.");
                if (result.Report?.FinalState == "waiting-for-user")
                    AddLine($"Answer the open questions first with [cyan]/answer[/].");
                return;
            case PlanPreparationStatus.Generated:
                AddSuccess("Plan generated.");
                break;
        }

        // Reload state since the plan loop may have changed it
        state = _store.Load();

        if (state.Phase == WorkflowPhase.ArchitectPlanning)
        {
            if (TryPrintArchitectSummary(state)) return;
            AddLine($"High-level plan approved. Phase: [cyan]ArchitectPlanning[/]");
            AddLine($"Run [cyan]/run[/] to let the architect create execution issues, then [cyan]/approve[/] again.");
            return;
        }

        // Show plan file
        var planPath = Path.Combine(_store.WorkspacePath, "plan.md");
        if (File.Exists(planPath))
            AddSystem(Markup.Escape(File.ReadAllText(planPath).TrimEnd()), "plan");
        else
            AddHint("No plan has been written yet.");

        // Show open questions
        var openQs = state.Questions.Where(q => q.Status == QuestionStatus.Open).OrderBy(q => q.Id).ToList();
        if (openQs.Count > 0)
        {
            var sb = new StringBuilder($"[bold]─── {openQs.Count} open question{(openQs.Count == 1 ? "" : "s")} ───[/]\n");
            foreach (var oq in openQs)
            {
                var tag = oq.IsBlocking ? "[yellow]blocking[/]" : "[dim]non-blocking[/]";
                sb.AppendLine($"  [dim]#{oq.Id}[/] [{tag}] {Markup.Escape(oq.Text)}");
            }
            sb.Append($"Answer with: [cyan]/answer[/] <id> <text>");
            AddSystem(sb.ToString(), "questions");
        }

        if (state.Phase == WorkflowPhase.Planning)
        {
            AddLine($"Reply with feedback as plain text or use [cyan]/approve[/] when the plan looks good.");
            return;
        }

        if (PlanWorkflow.IsAwaitingArchitectApproval(state))
            AddLine($"Reply with feedback as plain text or use [cyan]/approve[/] when the architect plan looks good.");
    }

    // ── Role invocation ────────────────────────────────────────────────────────

    private async Task InvokeRoleDirectAsync(WorkspaceState state, string roleSlug, string userMessage)
    {
        var policy = _runtime.GetRoleModelPolicy(state, roleSlug);
        var model = policy.PrimaryModel;
        var cost = state.Models.FirstOrDefault(m => string.Equals(m.Name, model, StringComparison.OrdinalIgnoreCase))?.Cost ?? 1;
        var remaining = state.Budget.TotalCreditCap - state.Budget.CreditsCommitted;
        if (remaining < cost && cost > 0)
        {
            var fallback = policy.FallbackModel;
            var fallbackCost = state.Models.FirstOrDefault(m => string.Equals(m.Name, fallback, StringComparison.OrdinalIgnoreCase))?.Cost ?? 0;
            if (remaining >= fallbackCost)
            {
                AddWarning($"Budget low — falling back to {Markup.Escape(fallback)}");
                model = fallback;
                cost = fallbackCost;
            }
            else
            {
                AddWarning("Budget exhausted. Use /budget to increase.");
                return;
            }
        }

        var prompt = AgentPromptBuilder.BuildAdHocPrompt(state, roleSlug, userMessage);
        var sessionId = $"devteam-adhoc-{roleSlug}";
        AddEvent("→", $"Asking {Markup.Escape(roleSlug)} via {Markup.Escape(model)}...", "dim cyan");

        var client = AgentClientFactory.Create("sdk");
        try
        {
            var response = await client.InvokeAsync(new AgentInvocationRequest
            {
                Prompt = prompt,
                Model = model,
                SessionId = sessionId,
                WorkingDirectory = state.RepoRoot,
                WorkspacePath = _store.WorkspacePath,
                Timeout = TimeSpan.FromMinutes(10),
                EnableWorkspaceMcp = state.Runtime.WorkspaceMcpEnabled,
                WorkspaceMcpServerName = state.Runtime.WorkspaceMcpServerName,
                ExternalMcpServers = state.McpServers
            });

            state.Budget.CreditsCommitted += cost;
            if (policy.AllowPremium) state.Budget.PremiumCreditsCommitted += cost;

            var parsed = AgentPromptBuilder.ParseResponse(response);
            AddAgent(roleSlug, string.IsNullOrWhiteSpace(parsed.Summary) ? "(no summary)" : parsed.Summary);

            if (parsed.Issues.Count > 0)
            {
                var anchor = state.Issues.FirstOrDefault(i => !i.IsPlanningIssue) ?? state.Issues.FirstOrDefault();
                if (anchor is not null)
                {
                    _runtime.AddGeneratedIssues(state, anchor.Id, parsed.Issues);
                    AddSuccess($"{parsed.Issues.Count} issue(s) proposed and added to the board.");
                }
            }
            if (parsed.Questions.Count > 0)
            {
                _runtime.AddQuestions(state, parsed.Questions);
                AddWarning($"{parsed.Questions.Count} question(s) added — they'll appear at the prompt.");
            }

            _runtime.MergeWorkspaceAdditions(state, _store.Load());
            _store.Save(state);
        }
        catch (Exception ex)
        {
            AddError($"Agent error: {Markup.Escape(ex.Message)}");
        }
    }

    // ── Context tracking ───────────────────────────────────────────────────────

    private void CheckAndUpdateContext(WorkspaceState state)
    {
        var openQuestions = state.Questions.Where(q => q.Status == QuestionStatus.Open).ToList();
        var planAwaiting = !IsLoopRunning
            && state.Phase == WorkflowPhase.Planning
            && state.Issues.Any(i => i.IsPlanningIssue && i.Status == ItemStatus.Done)
            && openQuestions.Count == 0;
        var archAwaiting = !IsLoopRunning
            && PlanWorkflow.IsAwaitingArchitectApproval(state)
            && openQuestions.Count == 0;

        var contextKey = $"{state.Phase}|q:{string.Join(",", openQuestions.Select(q => q.Id))}|pa:{planAwaiting}|arch:{archAwaiting}|loop:{IsLoopRunning}";

        if (contextKey == _lastContextKey) return;
        _lastContextKey = contextKey;

        if (openQuestions.Count > 0 && !IsLoopRunning)
        {
            var q = openQuestions[0];
            AddQuestion(q.Text, q.Id, q.IsBlocking, 1, openQuestions.Count);
            AddHint(openQuestions.Count > 1
                ? $"Type your answer (question 1 of {openQuestions.Count}), or [dim]/questions[/] to see all."
                : "Type your answer below.");
        }
        else if (openQuestions.Count > 0)
        {
            AddWarning($"{openQuestions.Count} open question(s) — the loop may be paused. Use /answer <id> <text>.");
        }
        else if (planAwaiting)
        {
            AddSystem("A plan is ready. Type [bold]approve[/] to proceed into execution, or type feedback to revise it. Use [dim]/plan[/] to review.", "plan ready ✓");
        }
        else if (archAwaiting)
        {
            AddSystem("The architect plan is ready. Type [bold]approve[/] to begin execution, or share feedback to revise.", "architect plan ready ✓");
        }
    }

    private void RefreshContext(WorkspaceState state)
    {
        CheckAndUpdateContext(state);
    }

    // ── Status / budget display ────────────────────────────────────────────────

    private void PrintStatus(WorkspaceState state)
    {
        var report = _runtime.BuildStatusReport(state);

        var sb = new StringBuilder();
        sb.AppendLine($"[bold]Phase:[/] [cyan]{Markup.Escape(state.Phase.ToString())}[/]  " +
            $"[bold]Mode:[/] [cyan]{Markup.Escape(state.Runtime.ActiveModeSlug)}[/]  " +
            $"[bold]Max-iter:[/] {state.Runtime.DefaultMaxIterations}  " +
            $"[bold]Max-sub:[/] {state.Runtime.DefaultMaxSubagents}");

        var issues = state.Issues.OrderBy(i => i.Id).ToList();
        if (issues.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("[dim]# Role              Status      Title[/]");
            foreach (var issue in issues)
            {
                var sc = issue.Status switch
                {
                    ItemStatus.Done => "green",
                    ItemStatus.InProgress => "yellow",
                    ItemStatus.Blocked => "red",
                    _ => "dim"
                };
                var title = issue.Title.Length > 55 ? issue.Title[..52] + "..." : issue.Title;
                sb.AppendLine($"[dim]{issue.Id,3}[/] [cyan]{Markup.Escape(issue.RoleSlug),-16}[/] [{sc}]{issue.Status,-11}[/] {Markup.Escape(title)}");
            }
        }
        else
        {
            sb.AppendLine("[dim]No issues.[/]");
        }

        sb.AppendLine();
        sb.Append($"[bold]Budget:[/] {report.Budget.CreditsCommitted:0.##}/{report.Budget.TotalCreditCap} credits " +
            $"[dim]({report.Budget.TotalCreditCap - report.Budget.CreditsCommitted:0.##} remaining)[/]  " +
            $"premium {report.Budget.PremiumCreditsCommitted:0.##}/{report.Budget.PremiumCreditCap} " +
            $"[dim]({report.Budget.PremiumCreditCap - report.Budget.PremiumCreditsCommitted:0.##} remaining)[/]");

        if (report.OpenQuestions.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"\n[bold yellow]{report.OpenQuestions.Count} open question(s)[/]");
            foreach (var q in report.OpenQuestions)
            {
                var tag = q.IsBlocking ? "[yellow]blocking[/]" : "[dim]non-blocking[/]";
                sb.AppendLine($"  [dim]#{q.Id}[/] [{tag}] {Markup.Escape(q.Text)}");
            }
        }

        if (report.QueuedRuns.Count > 0)
        {
            sb.AppendLine();
            sb.Append($"[bold]{report.QueuedRuns.Count} queued/running run(s)[/]");
            foreach (var run in report.QueuedRuns)
                sb.AppendLine($"\n  [dim]#{run.Id}[/] issue [dim]#{run.IssueId}[/] [cyan]{Markup.Escape(run.RoleSlug)}[/] via {Markup.Escape(run.ModelName)}");
        }

        AddSystem(sb.ToString().TrimEnd(), "status");
    }

    private void PrintBudget(BudgetState budget)
    {
        AddLine($"[bold]Budget:[/] {budget.CreditsCommitted:0.##}/{budget.TotalCreditCap} credits " +
            $"[dim]({budget.TotalCreditCap - budget.CreditsCommitted:0.##} remaining)[/], " +
            $"premium {budget.PremiumCreditsCommitted:0.##}/{budget.PremiumCreditCap} " +
            $"[dim]({budget.PremiumCreditCap - budget.PremiumCreditsCommitted:0.##} remaining)[/]");
    }

    private void PrintQuestions(WorkspaceState state)
    {
        var openQs = state.Questions.Where(q => q.Status == QuestionStatus.Open).OrderBy(q => q.Id).ToList();
        if (openQs.Count == 0)
        {
            AddLine("No open questions.");
            return;
        }
        var sb = new StringBuilder();
        foreach (var q in openQs)
        {
            var tag = q.IsBlocking ? "[yellow]blocking[/]" : "[dim]non-blocking[/]";
            sb.AppendLine($"[dim]#{q.Id}[/] [{tag}] {Markup.Escape(q.Text)}");
        }
        AddSystem(sb.ToString().TrimEnd(), $"questions ({openQs.Count})");
    }

    private bool TryPrintArchitectSummary(WorkspaceState state)
    {
        var runs = state.AgentRuns
            .Where(r => string.Equals(r.RoleSlug, "architect", StringComparison.OrdinalIgnoreCase)
                && r.Status == AgentRunStatus.Completed
                && !string.IsNullOrWhiteSpace(r.Summary))
            .OrderByDescending(r => r.UpdatedAtUtc)
            .Take(1)
            .ToList();
        if (runs.Count == 0) return false;

        var sb = new StringBuilder();
        foreach (var run in runs)
        {
            var issue = state.Issues.FirstOrDefault(i => i.Id == run.IssueId);
            if (issue is not null) sb.AppendLine($"[bold]{Markup.Escape(issue.Title)}[/]");
            foreach (var summaryLine in run.Summary.Split('\n'))
                sb.AppendLine(Markup.Escape(summaryLine));
        }

        var created = state.Issues
            .Where(i => !i.IsPlanningIssue
                && !string.Equals(i.RoleSlug, "architect", StringComparison.OrdinalIgnoreCase)
                && i.Status != ItemStatus.Done)
            .OrderByDescending(i => i.Priority).ThenBy(i => i.Id)
            .ToList();
        if (created.Count > 0)
        {
            sb.AppendLine($"\n[bold]Execution issues created ({created.Count}):[/]");
            foreach (var iss in created)
                sb.AppendLine($"  [dim]#{iss.Id}[/] [[cyan]{Markup.Escape(iss.RoleSlug)}[/]] {Markup.Escape(iss.Title)}");
        }

        sb.Append($"\nUse [cyan]/approve[/] to begin execution or type feedback to revise.");
        AddSystem(sb.ToString().TrimEnd(), "architect summary");
        return true;
    }

    private void PrintLoopResult(LoopResult result)
    {
        AddLine($"Loop state: {Markup.Escape(result.State)}");
        foreach (var run in result.QueuedRuns)
            AddLine($"Queued run [dim]#{run.RunId}[/] for issue [dim]#{run.IssueId}[/] ({Markup.Escape(run.RoleSlug)} via {Markup.Escape(run.ModelName)}): {Markup.Escape(run.Title)}");
    }

    // ── Update helpers ─────────────────────────────────────────────────────────

    private async Task NotifyAboutUpdateAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var status = await _toolUpdateService.CheckAsync(cts.Token);
            if (status.IsUpdateAvailable)
                AddHint($"Update available: [bold]{Markup.Escape(status.LatestVersion)}[/] (current {Markup.Escape(status.CurrentVersion)}). Run [cyan]/update[/] to install it.");
        }
        catch { /* non-critical */ }
    }

    private async Task HandleCheckUpdateAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var status = await _toolUpdateService.CheckAsync(cts.Token);
            if (status.IsUpdateAvailable)
                AddLine($"Update available: [bold]{Markup.Escape(status.LatestVersion)}[/] (current {Markup.Escape(status.CurrentVersion)}). Run [cyan]/update[/] to install it.");
            else
                AddLine($"OptionA.DevTeam is up to date ({Markup.Escape(status.CurrentVersion)}).");
        }
        catch (ToolUpdateUnavailableException ex) { AddLine(Markup.Escape(ex.Message)); }
        catch (HttpRequestException) { AddLine("Update check is unavailable right now."); }
        catch (TaskCanceledException) { AddLine("Update check timed out."); }
    }

    private async Task HandleUpdateAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var status = await _toolUpdateService.CheckAsync(cts.Token);
            if (!status.IsUpdateAvailable)
            {
                AddLine($"OptionA.DevTeam is already up to date ({Markup.Escape(status.CurrentVersion)}).");
                return;
            }
            var launch = _toolUpdateService.ScheduleGlobalUpdate(status.LatestVersion);
            AddLine($"Scheduling update to [bold]{Markup.Escape(status.LatestVersion)}[/] (current {Markup.Escape(status.CurrentVersion)}).");
            AddLine($"If the updater does not complete, run: [dim]{Markup.Escape(launch.ManualCommand)}[/]");
            AddLine("Closing the shell so the updater can replace the installed tool.");
            _lifetime.StopApplication();
        }
        catch (ToolUpdateUnavailableException ex) { AddLine(Markup.Escape(ex.Message)); }
        catch (HttpRequestException) { AddLine("Update check is unavailable right now."); }
        catch (TaskCanceledException) { AddLine("Update check timed out."); }
    }

    // ── Help text ──────────────────────────────────────────────────────────────

    private void AddInteractiveHelp()
    {
        var sb = new StringBuilder();
        sb.AppendLine("[bold]Interactive commands:[/]");
        sb.AppendLine("  [cyan]/init[/] \"goal text\" [--goal-file PATH] [--force] [--mode SLUG] [--keep-awake true|false]");
        sb.AppendLine("  [cyan]/customize[/] [--force]    Copy default assets to .devteam-source/ for editing");
        sb.AppendLine("  [cyan]/bug[/] [--save PATH] [--redact-paths true|false]");
        sb.AppendLine("  [cyan]/status[/]");
        sb.AppendLine("  [cyan]/history[/]              Show session command history (last 50)");
        sb.AppendLine("  [cyan]/mode[/] <slug>");
        sb.AppendLine("  [cyan]/keep-awake[/] <on|off>");
        sb.AppendLine("  [cyan]/add-issue[/] \"title\" --role ROLE [--area AREA] [--detail TEXT] [--priority N] [--depends-on N ...]");
        sb.AppendLine("  [cyan]/plan[/]");
        sb.AppendLine("  [cyan]/questions[/]");
        sb.AppendLine("  [cyan]/budget[/] [--total N] [--premium N]");
        sb.AppendLine("  [cyan]/check-update[/]");
        sb.AppendLine("  [cyan]/update[/]");
        sb.AppendLine("  [cyan]/max-iterations[/] <N>    Set workspace default max iterations");
        sb.AppendLine("  [cyan]/max-subagents[/] <N>     Set workspace default max subagents (1=sequential, 2–4=parallel)");
        sb.AppendLine("  [cyan]/run[/] [--max-iterations N] [--max-subagents N] [--timeout-seconds N]  [dim]starts in background[/]");
        sb.AppendLine("  [cyan]/stop[/]              Cancel the running loop");
        sb.AppendLine("  [cyan]/wait[/]              Re-attach to the running loop and wait for it to finish");
        sb.AppendLine("  [cyan]/feedback[/] <text>");
        sb.AppendLine("  [cyan]/approve[/] [note]");
        sb.AppendLine("  [cyan]/answer[/] <id> <text>  [dim]works while the loop is running[/]");
        sb.AppendLine("  [cyan]/goal[/] <text> [--goal-file PATH]");
        sb.AppendLine("  [cyan]/exit[/]");
        sb.AppendLine();
        sb.AppendLine("If exactly one question is open, you can type a plain answer without /answer.");
        sb.AppendLine("While a plan is awaiting approval, plain text is treated as planning feedback.");
        sb.AppendLine();
        sb.AppendLine("[bold]Direct role invocation:[/]");
        sb.Append("  [cyan]@role[/] <message>    e.g. [dim]@architect can you review our API design?[/]");
        AddSystem(sb.ToString(), "help");
    }

    // ── Banner ─────────────────────────────────────────────────────────────────

    private void AddBanner(string workspacePath)
    {
        AddLine($"[bold cyan]─── DevTeam ───[/]");
        AddLine($"Workspace: [cyan]{Markup.Escape(workspacePath)}[/]  [dim]· /help for commands · /exit to quit[/]");
    }

    // ── Message factories ──────────────────────────────────────────────────────

    private void AddMessage(ShellMessage msg)
    {
        lock (_gate)
            _messages.Add(msg);
        NotifyStateChanged();
    }

    private void AddLine(string markup) =>
        AddMessage(new ShellMessage(ShellMessageKind.Line, markup));

    private void AddSystem(string markup, string? header = null) =>
        AddMessage(new ShellMessage(ShellMessageKind.Panel, markup,
            Title: header ?? "devteam",
            BorderColor: Color.Grey));

    private void AddAgent(string roleSlug, string text, string? outcome = null)
    {
        var borderColor = outcome switch
        {
            "completed" => Color.Green,
            "blocked" => Color.Yellow,
            "failed" => Color.Red,
            _ => Color.Cyan,
        };
        var titleColor = borderColor;
        var headerText = outcome is not null ? $"{roleSlug} — {outcome}" : roleSlug;
        AddMessage(new ShellMessage(ShellMessageKind.Panel, Markup.Escape(text),
            Title: headerText,
            BorderColor: borderColor,
            TitleColor: titleColor));
    }

    private void AddQuestion(string questionText, int? questionId = null, bool isBlocking = true, int index = 1, int total = 1)
    {
        var counter = total > 1 ? $" ({index}/{total})" : "";
        var blocking = isBlocking ? " blocking" : "";
        AddMessage(new ShellMessage(ShellMessageKind.Panel, $"[white]{Markup.Escape(questionText)}[/]",
            Title: $"question{counter}{blocking}",
            BorderColor: Color.Yellow,
            TitleColor: Color.Yellow));
    }

    private void AddEvent(string icon, string markup, string style = "dim") =>
        AddLine($"[{style}]{Markup.Escape(icon)} {markup}[/]");

    private void AddSuccess(string text) =>
        AddLine($"[bold green]✓[/] [green]{Markup.Escape(text)}[/]");

    private void AddWarning(string text) =>
        AddLine($"[bold yellow]⚠[/] [yellow]{Markup.Escape(text)}[/]");

    private void AddError(string text) =>
        AddLine($"[bold red]✗[/] [red]{Markup.Escape(text)}[/]");

    private void AddHint(string markup) =>
        AddLine($"[dim]{markup}[/]");

    private void AddLoopLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        var trimmed = message.TrimStart();
        var pad = new string(' ', message.Length - trimmed.Length);
        var escaped = Markup.Escape(trimmed);

        string markup;
        if (trimmed.StartsWith("Iteration ", StringComparison.OrdinalIgnoreCase))
            markup = $"\n[grey]{pad}── {escaped} ──[/]";
        else if (trimmed.StartsWith("Running issue #", StringComparison.OrdinalIgnoreCase)
              || trimmed.StartsWith("Bootstrapped:", StringComparison.OrdinalIgnoreCase))
            markup = $"[dim]{pad}→ {escaped}[/]";
        else if (trimmed.StartsWith("Still running", StringComparison.OrdinalIgnoreCase))
            markup = $"[dim]{pad}⏳ {escaped}[/]";
        else if (trimmed.StartsWith("Outcome:", StringComparison.OrdinalIgnoreCase))
        {
            var outcome = trimmed["Outcome:".Length..].Trim();
            var color = outcome switch { "completed" => "green", "blocked" => "yellow", "failed" => "red", _ => "grey" };
            markup = $"[{color}]{pad}  ✓ outcome: {Markup.Escape(outcome)}[/]";
        }
        else if (trimmed.StartsWith("Budget", StringComparison.OrdinalIgnoreCase))
            markup = $"[dim]{pad}💰 {escaped}[/]";
        else
            markup = $"[dim]{pad}{escaped}[/]";

        AddMessage(new ShellMessage(ShellMessageKind.Line, markup));
    }

    private void AddHistory(string entry)
    {
        lock (_gate)
        {
            _history.Add((DateTimeOffset.Now, entry));
            if (_history.Count > 50) _history.RemoveAt(0);
        }
    }

    private void NotifyStateChanged() => OnStateChanged?.Invoke();

    // ── CopyPackagedAssets ─────────────────────────────────────────────────────

    private void CopyPackagedAssets(string targetRoot, bool force)
    {
        // Walk up from the tool install directory to find packaged .devteam-source
        string? sourceRoot = null;
        var searchBase = AppContext.BaseDirectory;
        for (var d = new DirectoryInfo(searchBase); d is not null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, ".devteam-source");
            if (Directory.Exists(candidate))
            {
                sourceRoot = candidate;
                break;
            }
        }

        if (sourceRoot is null)
        {
            AddWarning("Could not locate packaged .devteam-source assets.");
            return;
        }

        var copied = 0;
        var skipped = 0;
        foreach (var srcFile in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceRoot, srcFile);
            var destFile = Path.Combine(targetRoot, relative);
            var destDir = Path.GetDirectoryName(destFile);
            if (!string.IsNullOrWhiteSpace(destDir)) Directory.CreateDirectory(destDir);
            if (!force && File.Exists(destFile))
            {
                skipped++;
                continue;
            }
            File.Copy(srcFile, destFile, overwrite: true);
            copied++;
        }
        AddSuccess($"Copied {copied} asset(s) to {Markup.Escape(targetRoot)}{(skipped > 0 ? $" ({skipped} skipped — use --force to overwrite)" : "")}.");
    }

    // ── Option parsing helpers (mirrored from Program.cs) ─────────────────────

    private static Dictionary<string, List<string>> ParseOptions(string[] tokens)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var positional = new List<string>();
        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (token.StartsWith("--", StringComparison.Ordinal))
            {
                var key = token[2..];
                if (!result.TryGetValue(key, out var vals)) { vals = []; result[key] = vals; }
                while (i + 1 < tokens.Length && !tokens[i + 1].StartsWith("--", StringComparison.Ordinal))
                    vals.Add(tokens[++i]);
                if (vals.Count == 0) vals.Add("true");
            }
            else
            {
                positional.Add(token);
            }
        }
        result["__positional"] = positional;
        return result;
    }

    private static List<string> TokenizeInput(string input)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        foreach (var ch in input)
        {
            if (ch == '"') { inQuotes = !inQuotes; continue; }
            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (current.Length > 0) { tokens.Add(current.ToString()); current.Clear(); }
                continue;
            }
            current.Append(ch);
        }
        if (current.Length > 0) tokens.Add(current.ToString());
        return tokens;
    }

    private static string NormalizeCommand(string value) =>
        value.Trim().TrimStart('/').ToLowerInvariant();

    private static string? GetOption(Dictionary<string, List<string>> options, string key) =>
        options.TryGetValue(key, out var values) && values.Count > 0 ? string.Join(" ", values) : null;

    private static int GetIntOption(Dictionary<string, List<string>> options, string key, int fallback) =>
        int.TryParse(GetOption(options, key), out var v) ? v : fallback;

    private static double GetDoubleOption(Dictionary<string, List<string>> options, string key, double fallback) =>
        double.TryParse(GetOption(options, key), out var v) ? v : fallback;

    private static bool GetBoolOption(Dictionary<string, List<string>> options, string key, bool fallback)
    {
        var value = GetOption(options, key);
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        return value.Trim().ToLowerInvariant() switch
        {
            "true" or "1" or "yes" or "on" => true,
            "false" or "0" or "no" or "off" => false,
            _ => fallback
        };
    }

    private static bool? GetNullableBoolOption(Dictionary<string, List<string>> options, string key)
    {
        var value = GetOption(options, key);
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.Trim().ToLowerInvariant() switch
        {
            "true" or "1" or "yes" or "on" => true,
            "false" or "0" or "no" or "off" => false,
            _ => null
        };
    }

    private static bool ParseBoolOrThrow(string value, string errorMessage) =>
        value.Trim().ToLowerInvariant() switch
        {
            "true" or "1" or "yes" or "on" => true,
            "false" or "0" or "no" or "off" => false,
            _ => throw new InvalidOperationException(errorMessage)
        };

    private static int? GetNullableIntOption(Dictionary<string, List<string>> options, string key) =>
        int.TryParse(GetOption(options, key), out var v) ? v : null;

    private static IReadOnlyList<int> GetMultiIntOption(Dictionary<string, List<string>> options, string key) =>
        options.TryGetValue(key, out var values) && values.Count > 0 ? values.Select(int.Parse).ToList() : [];

    private static string? GetPositionalValue(Dictionary<string, List<string>> options) =>
        options.TryGetValue("__positional", out var vals) && vals.Count > 0 ? string.Join(" ", vals) : null;

    private static IReadOnlyList<string> GetPositionalValues(Dictionary<string, List<string>> options) =>
        options.TryGetValue("__positional", out var vals) ? vals : [];

    private static bool IsApproveIntent(string line) =>
        line.Equals("approve", StringComparison.OrdinalIgnoreCase) ||
        line.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
        line.Equals("y", StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("approve ", StringComparison.OrdinalIgnoreCase);

    private static LoopVerbosity ParseVerbosity(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "quiet" => LoopVerbosity.Quiet,
            "detailed" => LoopVerbosity.Detailed,
            _ => LoopVerbosity.Normal
        };

    private bool TryLoadState(out WorkspaceState? state)
    {
        try { state = _store.Load(); return true; }
        catch (InvalidOperationException) { state = null; return false; }
        catch (IOException) { state = null; return false; }
        catch (UnauthorizedAccessException) { state = null; return false; }
        catch (System.Text.Json.JsonException) { state = null; return false; }
    }

    // ── IDisposable ────────────────────────────────────────────────────────────

    public void Dispose()
    {
        _loopCts?.Cancel();
        _loopCts?.Dispose();
        _keepAwake.Dispose();
    }
}
