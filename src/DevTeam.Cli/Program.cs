using DevTeam.Core;

var command = args.Length == 0 ? "help" : args[0].ToLowerInvariant();
var options = ParseOptions(args.Skip(1).ToArray());
var workspacePath = GetOption(options, "workspace") ?? ".devteam";
var store = new WorkspaceStore(workspacePath);
var runtime = new DevTeamRuntime();

try
{
    switch (command)
    {
        case "init":
        {
            var totalCap = GetDoubleOption(options, "total-credit-cap", 25);
            var premiumCap = GetDoubleOption(options, "premium-credit-cap", 6);
            var goal = GetOption(options, "goal");
            var state = store.Initialize(Environment.CurrentDirectory, totalCap, premiumCap);
            if (!string.IsNullOrWhiteSpace(goal))
            {
                runtime.SetGoal(state, goal);
                store.Save(state);
            }

            Console.WriteLine($"Initialized devteam workspace at {Path.GetFullPath(workspacePath)}");
            if (!string.IsNullOrWhiteSpace(goal))
            {
                Console.WriteLine($"Active goal saved: {goal}");
            }
            return 0;
        }

        case "set-goal":
        {
            var state = store.Load();
            var goal = GetPositionalValue(options) ?? throw new InvalidOperationException("Missing goal text.");
            runtime.SetGoal(state, goal);
            store.Save(state);
            Console.WriteLine("Updated active goal.");
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
            var role = GetOption(options, "role") ?? throw new InvalidOperationException("Missing --role.");
            var detail = GetOption(options, "detail") ?? "";
            var priority = GetIntOption(options, "priority", 50);
            var roadmapId = GetNullableIntOption(options, "roadmap-item-id");
            var dependsOn = GetMultiIntOption(options, "depends-on");
            var issue = runtime.AddIssue(state, title, detail, role, priority, roadmapId, dependsOn);
            store.Save(state);
            Console.WriteLine($"Created issue #{issue.Id}: {issue.Title}");
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

        case "run-once":
        {
            var state = store.Load();
            var maxSubagents = GetIntOption(options, "max-subagents", 3);
            var result = runtime.RunOnce(state, maxSubagents);
            store.Save(state);
            Console.WriteLine($"Loop state: {result.State}");
            if (result.Created.Count > 0)
            {
                Console.WriteLine($"Bootstrapped: {string.Join(", ", result.Created)}");
            }
            foreach (var run in result.QueuedRuns)
            {
                Console.WriteLine(
                    $"Queued run #{run.RunId} for issue #{run.IssueId} ({run.RoleSlug} via {run.ModelName}): {run.Title}");
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
        {
            var state = store.Load();
            var report = runtime.BuildStatusReport(state);
            Console.WriteLine(
                $"Counts: roadmap={report.Counts["roadmap"]}, issues={report.Counts["issues"]}, " +
                $"questions={report.Counts["questions"]}, runs={report.Counts["runs"]}, " +
                $"roles={report.Counts["roles"]}, superpowers={report.Counts["superpowers"]}");
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
                ExtraArguments = extraArgs
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

static void PrintHelp()
{
    Console.WriteLine("DevTeam CLI");
    Console.WriteLine("Commands:");
    Console.WriteLine("  init [--workspace PATH] [--goal TEXT] [--total-credit-cap N] [--premium-credit-cap N]");
    Console.WriteLine("  set-goal <TEXT> [--workspace PATH]");
    Console.WriteLine("  add-roadmap <TITLE> [--detail TEXT] [--priority N] [--workspace PATH]");
    Console.WriteLine("  add-issue <TITLE> --role ROLE [--detail TEXT] [--priority N] [--roadmap-item-id N] [--depends-on N [N...]] [--workspace PATH]");
    Console.WriteLine("  add-question <TEXT> [--blocking] [--workspace PATH]");
    Console.WriteLine("  answer-question <ID> <ANSWER> [--workspace PATH]");
    Console.WriteLine("  run-once [--max-subagents N] [--workspace PATH]");
    Console.WriteLine("  complete-run --run-id N --outcome completed|failed|blocked --summary TEXT [--workspace PATH]");
    Console.WriteLine("  status [--workspace PATH]");
    Console.WriteLine("  agent-invoke [--backend sdk|cli] [--prompt TEXT] [--model NAME] [--timeout-seconds N] [--working-directory PATH] [--extra-arg ARG ...]");
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

static string? GetOption(Dictionary<string, List<string>> options, string key) =>
    options.TryGetValue(key, out var values) && values.Count > 0 ? string.Join(" ", values) : null;

static int GetIntOption(Dictionary<string, List<string>> options, string key, int fallback) =>
    int.TryParse(GetOption(options, key), out var value) ? value : fallback;

static double GetDoubleOption(Dictionary<string, List<string>> options, string key, double fallback) =>
    double.TryParse(GetOption(options, key), out var value) ? value : fallback;

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
