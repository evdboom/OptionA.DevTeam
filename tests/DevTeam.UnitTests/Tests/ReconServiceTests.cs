using DevTeam.Core;

namespace DevTeam.UnitTests.Tests;

internal static class ReconServiceTests
{
    private const string RepoRoot = "/repo";
    private const string WorkspacePath = ".devteam";
    private const string DefaultModelName = "gpt-5-mini";

    public static IEnumerable<TestCase> GetTests() =>
    [
        new("BuildReconPrompt_IncludesGoal_WhenProvided", BuildReconPrompt_IncludesGoal_WhenProvided),
        new("BuildReconPrompt_OmitsGoalSection_WhenGoalIsNull", BuildReconPrompt_OmitsGoalSection_WhenGoalIsNull),
        new("BuildReconPrompt_OmitsGoalSection_WhenGoalIsEmpty", BuildReconPrompt_OmitsGoalSection_WhenGoalIsEmpty),
        new("BuildReconPrompt_IncludesRepoRoot", BuildReconPrompt_IncludesRepoRoot),
        new("BuildReconPrompt_ExpectsStructuredResponse", BuildReconPrompt_ExpectsStructuredResponse),
        new("RunAsync_StoresSummaryAsCodebaseContext", RunAsync_StoresSummaryAsCodebaseContext),
        new("RunAsync_FallsBackToRawOutput_WhenSummaryEmpty", RunAsync_FallsBackToRawOutput_WhenSummaryEmpty),
        new("BuildPrompt_InjectsCodebaseContext_ForPlannerRole", BuildPrompt_InjectsCodebaseContext_ForPlannerRole),
        new("BuildPrompt_OmitsCodebaseContext_WhenEmpty", BuildPrompt_OmitsCodebaseContext_WhenEmpty),
        new("BuildPrompt_OmitsCodebaseContext_ForTesterRole", BuildPrompt_OmitsCodebaseContext_ForTesterRole),
        new("BuildPrompt_IncludesScoutSkill_ForNavigatorRole", BuildPrompt_IncludesScoutSkill_ForNavigatorRole),
        new("BuildPrompt_IncludesWorkspaceProtectionSkill_ForGitRiskIssue", BuildPrompt_IncludesWorkspaceProtectionSkill_ForGitRiskIssue),
        new("BuildPrompt_InjectsTimeBudget_WithCorrectMinutes", BuildPrompt_InjectsTimeBudget_WithCorrectMinutes),
        new("BuildPrompt_IncludesBrownfieldDeltaInstructions", BuildPrompt_IncludesBrownfieldDeltaInstructions),
        new("BuildPrompt_EnforcesSelfContainedQuestions", BuildPrompt_EnforcesSelfContainedQuestions),
        new("ParseResponse_ReadsBrownfieldApproachAndRationale", ParseResponse_ReadsBrownfieldApproachAndRationale),
        new("ParseResponse_ParsesQuestionContextContinuationLines", ParseResponse_ParsesQuestionContextContinuationLines),
        new("WorkspaceStore_WritesCodebaseContextFile_WhenPresent", WorkspaceStore_WritesCodebaseContextFile_WhenPresent),
        new("WorkspaceStore_DoesNotWriteContextFile_WhenEmpty", WorkspaceStore_DoesNotWriteContextFile_WhenEmpty),
        new("WorkspaceStore_WritesRepoMemoryFiles_WhenPresent", WorkspaceStore_WritesRepoMemoryFiles_WhenPresent),
        new("WorkspaceStore_Initialize_HydratesRepoMemory_WhenPresent", WorkspaceStore_Initialize_HydratesRepoMemory_WhenPresent),
        new("BuildPrompt_IncludesArchitectPlanningFeedback_WhenPresent", BuildPrompt_IncludesArchitectPlanningFeedback_WhenPresent),
        new("BuildPrompt_KeepsArchitectPlanningFeedback_WhenRecentDecisionWindowIsCrowded", BuildPrompt_KeepsArchitectPlanningFeedback_WhenRecentDecisionWindowIsCrowded),
        new("ParseResponse_ParsesSelectedIssueIds_WithHashPrefix", ParseResponse_ParsesSelectedIssueIds_WithHashPrefix),
        new("ParseResponse_ParsesSelectedIssueIds_MixedHashAndPlain", ParseResponse_ParsesSelectedIssueIds_MixedHashAndPlain),
    ];

    private static Task BuildReconPrompt_IncludesGoal_WhenProvided()
    {
        var prompt = ReconService.BuildReconPrompt(RepoRoot, "Build a REST API");
        Assert.Contains("Build a REST API", prompt);
        Assert.Contains("reconnaissance", prompt);
        Assert.Contains("READ-ONLY", prompt);
        return Task.CompletedTask;
    }

    private static Task BuildReconPrompt_OmitsGoalSection_WhenGoalIsNull()
    {
        var prompt = ReconService.BuildReconPrompt(RepoRoot, null);
        Assert.Contains("reconnaissance", prompt);
        Assert.DoesNotContain("Active goal", prompt);
        return Task.CompletedTask;
    }

    private static Task BuildReconPrompt_OmitsGoalSection_WhenGoalIsEmpty()
    {
        var prompt = ReconService.BuildReconPrompt(RepoRoot, "   ");
        Assert.DoesNotContain("Active goal", prompt);
        return Task.CompletedTask;
    }

    private static Task BuildReconPrompt_IncludesRepoRoot()
    {
        var prompt = ReconService.BuildReconPrompt("/some/path", null);
        Assert.Contains("/some/path", prompt);
        return Task.CompletedTask;
    }

    private static Task BuildReconPrompt_ExpectsStructuredResponse()
    {
        var prompt = ReconService.BuildReconPrompt(RepoRoot, null);
        Assert.Contains("OUTCOME:", prompt);
        Assert.Contains("SUMMARY:", prompt);
        return Task.CompletedTask;
    }

    private static async Task RunAsync_StoresSummaryAsCodebaseContext()
    {
        var output = "OUTCOME: completed\nSUMMARY:\n## Tech stack\n.NET 10\nISSUES:\n(none)\nSKILLS_USED:\n(none)\nTOOLS_USED:\n- rg\nQUESTIONS:\n(none)";
        var agent = new RecordingAgentClient(output);
        var factory = new FuncAgentClientFactory(_ => agent);
        var fs = new InMemoryFileSystem();
        var state = new WorkspaceState
        {
            RepoRoot = RepoRoot,
            ActiveGoal = new GoalState { GoalText = "Test goal" },
            Models = [new ModelDefinition { Name = DefaultModelName, Cost = 0, IsDefault = true }]
        };
        var store = new WorkspaceStore(WorkspacePath, fs);

        var svc = new ReconService(factory);
        var context = await svc.RunAsync(state, store, "fake", TimeSpan.FromSeconds(10), CancellationToken.None);

        Assert.Contains(".NET 10", context);
        Assert.That(state.CodebaseContext == context, $"Expected CodebaseContext to equal returned context");
    }

    private static async Task RunAsync_FallsBackToRawOutput_WhenSummaryEmpty()
    {
        var agent = new RecordingAgentClient("No structured output here — just raw text");
        var factory = new FuncAgentClientFactory(_ => agent);
        var fs = new InMemoryFileSystem();
        var state = new WorkspaceState
        {
            RepoRoot = RepoRoot,
            Models = [new ModelDefinition { Name = DefaultModelName, Cost = 0, IsDefault = true }]
        };
        var store = new WorkspaceStore(WorkspacePath, fs);

        var svc = new ReconService(factory);
        var context = await svc.RunAsync(state, store, "fake", TimeSpan.FromSeconds(10), CancellationToken.None);

        Assert.Contains("raw text", context);
    }

    private static Task BuildPrompt_InjectsCodebaseContext_ForPlannerRole()
    {
        var state = new WorkspaceState
        {
            RepoRoot = RepoRoot,
            CodebaseContext = "## Tech stack\nNode.js 20",
            Models = [new ModelDefinition { Name = DefaultModelName, Cost = 0, IsDefault = true }]
        };
        var issue = new IssueItem { Id = 1, Title = "Plan something", RoleSlug = "planner", Status = ItemStatus.Open };
        state.Issues.Add(issue);

        var prompt = AgentPromptBuilder.BuildPrompt(state, issue);

        Assert.Contains("Project map / codebase context", prompt);
        Assert.Contains("Node.js 20", prompt);
        return Task.CompletedTask;
    }

    private static Task BuildPrompt_OmitsCodebaseContext_WhenEmpty()
    {
        var state = new WorkspaceState
        {
            RepoRoot = RepoRoot,
            CodebaseContext = "",
            Models = [new ModelDefinition { Name = DefaultModelName, Cost = 0, IsDefault = true }]
        };
        var issue = new IssueItem { Id = 1, Title = "Plan something", RoleSlug = "planner", Status = ItemStatus.Open };
        state.Issues.Add(issue);

        var prompt = AgentPromptBuilder.BuildPrompt(state, issue);

        Assert.DoesNotContain("Project map / codebase context", prompt);
        return Task.CompletedTask;
    }

    private static Task BuildPrompt_OmitsCodebaseContext_ForTesterRole()
    {
        var state = new WorkspaceState
        {
            RepoRoot = RepoRoot,
            CodebaseContext = "## Tech stack\nNode.js 20",
            Models = [new ModelDefinition { Name = DefaultModelName, Cost = 0, IsDefault = true }]
        };
        var issue = new IssueItem { Id = 1, Title = "Test something", RoleSlug = "tester", Status = ItemStatus.Open };
        state.Issues.Add(issue);

        var prompt = AgentPromptBuilder.BuildPrompt(state, issue);

        Assert.DoesNotContain("Project map / codebase context", prompt);
        return Task.CompletedTask;
    }

    private static Task BuildPrompt_IncludesScoutSkill_ForNavigatorRole()
    {
        var state = new WorkspaceState
        {
            RepoRoot = RepoRoot,
            Models = [new ModelDefinition { Name = DefaultModelName, Cost = 0, IsDefault = true }],
            Skills = [new SkillDefinition { Slug = "scout", Name = "Scout", Body = "# Skill: Scout\nMap the relevant files.", SourcePath = ".devteam-source/skills/scout/SKILL.md" }]
        };
        var issue = new IssueItem { Id = 1, Title = "Map the billing area", RoleSlug = "navigator", Status = ItemStatus.Open };
        state.Issues.Add(issue);

        var prompt = AgentPromptBuilder.BuildPrompt(state, issue);

        Assert.Contains("Relevant skill slugs:", prompt);
        Assert.Contains("scout", prompt);
        Assert.Contains("Skill manifest (load on demand)", prompt);
        Assert.Contains("source=.devteam-source/skills/scout/SKILL.md", prompt);
        return Task.CompletedTask;
    }

    private static Task BuildPrompt_IncludesWorkspaceProtectionSkill_ForGitRiskIssue()
    {
        var state = new WorkspaceState
        {
            RepoRoot = RepoRoot,
            Models = [new ModelDefinition { Name = DefaultModelName, Cost = 0, IsDefault = true }],
            Skills =
            [
                new SkillDefinition
                {
                    Slug = "workspace-protection",
                    Name = "Workspace Protection",
                    Body = "# Skill: Workspace Protection\nProtect .devteam runtime state.",
                    SourcePath = ".devteam-source/skills/workspace-protection/SKILL.md"
                }
            ]
        };
        var issue = new IssueItem
        {
            Id = 1,
            Title = "Prevent accidental git restore --force",
            Detail = "Harden around git clean and workspace.json safety.",
            RoleSlug = "developer",
            Status = ItemStatus.Open
        };
        state.Issues.Add(issue);

        var prompt = AgentPromptBuilder.BuildPrompt(state, issue);

        Assert.Contains("Relevant skill slugs:", prompt);
        Assert.Contains("workspace-protection", prompt);
        Assert.Contains("source=.devteam-source/skills/workspace-protection/SKILL.md", prompt);
        return Task.CompletedTask;
    }

    private static Task BuildPrompt_InjectsTimeBudget_WithCorrectMinutes()
    {
        var state = new WorkspaceState
        {
            RepoRoot = RepoRoot,
            Models = [new ModelDefinition { Name = DefaultModelName, Cost = 0, IsDefault = true }]
        };
        var issue = new IssueItem { Id = 1, Title = "Design the technical approach", RoleSlug = "architect", Status = ItemStatus.Open };
        state.Issues.Add(issue);

        var prompt = AgentPromptBuilder.BuildPrompt(state, issue, TimeSpan.FromMinutes(25));

        Assert.Contains("Time budget:", prompt);
        Assert.Contains("25 minutes", prompt);
        Assert.Contains("hard session timeout", prompt);
        Assert.Contains("emit split follow-up ISSUES", prompt);
        return Task.CompletedTask;
    }

    private static Task BuildPrompt_IncludesBrownfieldDeltaInstructions()
    {
        var state = new WorkspaceState
        {
            RepoRoot = RepoRoot,
            CodebaseContext = "## Existing patterns\nMVC controllers",
            Models = [new ModelDefinition { Name = DefaultModelName, Cost = 0, IsDefault = true }]
        };
        var issue = new IssueItem { Id = 1, Title = "Update controller", RoleSlug = "developer", Status = ItemStatus.Open };
        state.Issues.Add(issue);

        var prompt = AgentPromptBuilder.BuildPrompt(state, issue);

        Assert.Contains("Brownfield delta", prompt);
        Assert.Contains("project map/codebase context", prompt);
        Assert.Contains("APPROACH: extend|replace|workaround", prompt);
        Assert.Contains("RATIONALE:", prompt);
        return Task.CompletedTask;
    }

        private static Task BuildPrompt_IncludesArchitectPlanningFeedback_WhenPresent()
        {
            var state = new WorkspaceState
            {
                RepoRoot = RepoRoot,
                Phase = WorkflowPhase.ArchitectPlanning,
                Models = [new ModelDefinition { Name = DefaultModelName, Cost = 0, IsDefault = true }]
            };
            state.Decisions.Add(new DecisionRecord
            {
                Id = 1,
                Source = "architect-plan-feedback",
                Title = "Architect plan feedback from user",
                Detail = "Remove issue #3 because it belongs to another repository.",
                CreatedAtUtc = DateTimeOffset.UtcNow
            });

            var issue = new IssueItem { Id = 2, Title = "Design the technical approach", RoleSlug = "architect", Status = ItemStatus.Open };
            state.Issues.Add(issue);

            var prompt = AgentPromptBuilder.BuildPrompt(state, issue);

            Assert.Contains("Planning feedback to apply:", prompt);
            Assert.Contains("Remove issue #3 because it belongs to another repository.", prompt);
            return Task.CompletedTask;
        }

        private static Task BuildPrompt_KeepsArchitectPlanningFeedback_WhenRecentDecisionWindowIsCrowded()
        {
            var now = DateTimeOffset.UtcNow;
            var state = new WorkspaceState
            {
                RepoRoot = RepoRoot,
                Phase = WorkflowPhase.ArchitectPlanning,
                Models = [new ModelDefinition { Name = DefaultModelName, Cost = 0, IsDefault = true }]
            };

            // Older, but still relevant user feedback.
            state.Decisions.Add(new DecisionRecord
            {
                Id = 1,
                Source = "architect-plan-feedback",
                Title = "Architect plan feedback from user",
                Detail = "Drop the analyst task; it is out of scope for this repo.",
                CreatedAtUtc = now.AddMinutes(-10)
            });

            // Newer non-feedback decisions that can crowd out the generic recent-decision window.
            for (var i = 0; i < 6; i++)
            {
                state.Decisions.Add(new DecisionRecord
                {
                    Id = 2 + i,
                    Source = "execution",
                    Title = $"Execution note {i + 1}",
                    Detail = "Background run detail.",
                    CreatedAtUtc = now.AddMinutes(-i)
                });
            }

            var issue = new IssueItem { Id = 2, Title = "Design the technical approach", RoleSlug = "architect", Status = ItemStatus.Open };
            state.Issues.Add(issue);

            var prompt = AgentPromptBuilder.BuildPrompt(state, issue);

            Assert.Contains("Planning feedback to apply:", prompt);
            Assert.Contains("Drop the analyst task; it is out of scope for this repo.", prompt);
            return Task.CompletedTask;
        }

    private static Task ParseResponse_ReadsBrownfieldApproachAndRationale()
    {
        var parsed = AgentPromptBuilder.ParseResponse(new AgentInvocationResult
        {
            ExitCode = 0,
            StdOut = """
                OUTCOME: completed
                SUMMARY:
                Updated the controller safely.
                APPROACH: extend
                RATIONALE:
                The existing controller pattern already matches the feature boundary.
                ISSUES:
                (none)
                SKILLS_USED:
                (none)
                TOOLS_USED:
                (none)
                QUESTIONS:
                (none)
                """
        });

        Assert.That(parsed.Approach == "extend", $"Expected approach 'extend' but got '{parsed.Approach}'");
        Assert.Contains("existing controller pattern", parsed.Rationale);
        return Task.CompletedTask;
    }

    private static Task BuildPrompt_EnforcesSelfContainedQuestions()
    {
        var state = new WorkspaceState
        {
            RepoRoot = RepoRoot,
            Models = [new ModelDefinition { Name = DefaultModelName, Cost = 0, IsDefault = true }],
            Runtime = RuntimeConfiguration.CreateDefault(),
            Roles =
            [
                new RoleDefinition { Slug = "developer", Name = "Developer" }
            ]
        };
        var issue = new IssueItem { Id = 1, Title = "Implement command", RoleSlug = "developer", Status = ItemStatus.Open };
        state.Issues.Add(issue);

        var prompt = AgentPromptBuilder.BuildPrompt(state, issue);

        Assert.Contains("Every question must be self-contained", prompt);
        Assert.Contains("context: <optional supporting facts from this run>", prompt);
        return Task.CompletedTask;
    }

    private static Task ParseResponse_ParsesQuestionContextContinuationLines()
    {
        var parsed = AgentPromptBuilder.ParseResponse(new AgentInvocationResult
        {
            ExitCode = 0,
            StdOut = """
                OUTCOME: blocked
                SUMMARY:
                Need clarification.
                ISSUES:
                (none)
                SKILLS_USED:
                (none)
                TOOLS_USED:
                (none)
                QUESTIONS:
                - [blocking] Which deployment environment should we target first?
                  context: The run found conflicting staging/prod config defaults.
                  context: Current release notes mention both.
                """
        });

        Assert.That(parsed.Questions.Count == 1, $"Expected one parsed question but got {parsed.Questions.Count}.");
        var question = parsed.Questions[0];
        Assert.That(question.IsBlocking, "Expected parsed question to be blocking.");
        Assert.Contains("Which deployment environment", question.Text);
        Assert.Contains("Context:", question.Text);
        Assert.Contains("staging/prod", question.Text);
        return Task.CompletedTask;
    }

    private static Task ParseResponse_ParsesSelectedIssueIds_WithHashPrefix()
    {
        // LLMs frequently write "- #13" instead of "- 13" for issue references.
        // Both forms must be accepted.
        var parsed = AgentPromptBuilder.ParseResponse(new AgentInvocationResult
        {
            ExitCode = 0,
            StdOut = """
                OUTCOME: completed
                SUMMARY:
                Selected the next batch.
                SELECTED_ISSUES:
                - #5
                - #12
                - #23
                ISSUES:
                (none)
                SKILLS_USED:
                (none)
                TOOLS_USED:
                (none)
                QUESTIONS:
                (none)
                """
        });

        Assert.That(parsed.SelectedIssueIds.Count == 3, $"Expected 3 selected issue ids but got {parsed.SelectedIssueIds.Count}.");
        Assert.That(parsed.SelectedIssueIds.Contains(5), "Expected issue #5 to be parsed from '- #5'.");
        Assert.That(parsed.SelectedIssueIds.Contains(12), "Expected issue #12 to be parsed from '- #12'.");
        Assert.That(parsed.SelectedIssueIds.Contains(23), "Expected issue #23 to be parsed from '- #23'.");
        return Task.CompletedTask;
    }

    private static Task ParseResponse_ParsesSelectedIssueIds_MixedHashAndPlain()
    {
        // Responses may mix "- 5" and "- #6" formats; both must be parsed correctly.
        var parsed = AgentPromptBuilder.ParseResponse(new AgentInvocationResult
        {
            ExitCode = 0,
            StdOut = """
                OUTCOME: completed
                SUMMARY:
                Selected the next batch.
                SELECTED_ISSUES:
                - 5
                - #6
                ISSUES:
                (none)
                SKILLS_USED:
                (none)
                TOOLS_USED:
                (none)
                QUESTIONS:
                (none)
                """
        });

        Assert.That(parsed.SelectedIssueIds.Count == 2, $"Expected 2 selected issue ids but got {parsed.SelectedIssueIds.Count}.");
        Assert.That(parsed.SelectedIssueIds.Contains(5), "Expected issue 5 from '- 5'.");
        Assert.That(parsed.SelectedIssueIds.Contains(6), "Expected issue 6 from '- #6'.");
        return Task.CompletedTask;
    }

    private static Task WorkspaceStore_WritesCodebaseContextFile_WhenPresent()
    {
        var fs = new InMemoryFileSystem();
        var store = new WorkspaceStore(WorkspacePath, fs);
        var state = new WorkspaceState
        {
            RepoRoot = RepoRoot,
            CodebaseContext = "## Tech stack\nGo 1.22",
            Models = [new ModelDefinition { Name = DefaultModelName, Cost = 0, IsDefault = true }]
        };

        store.Save(state);

        var path = Path.Combine(store.WorkspacePath, "codebase-context.md");
        Assert.That(fs.FileExists(path), $"Expected codebase-context.md at {path} to be written");
        var content = fs.ReadAllText(path);
        Assert.Contains("Go 1.22", content);
        return Task.CompletedTask;
    }

    private static Task WorkspaceStore_DoesNotWriteContextFile_WhenEmpty()
    {
        var fs = new InMemoryFileSystem();
        var store = new WorkspaceStore(WorkspacePath, fs);
        var state = new WorkspaceState
        {
            RepoRoot = RepoRoot,
            CodebaseContext = "",
            Models = [new ModelDefinition { Name = DefaultModelName, Cost = 0, IsDefault = true }]
        };

        store.Save(state);

        var path = Path.Combine(store.WorkspacePath, "codebase-context.md");
        Assert.That(!fs.FileExists(path), "Expected codebase-context.md NOT to be written when context is empty");
        return Task.CompletedTask;
    }

    private static Task WorkspaceStore_WritesRepoMemoryFiles_WhenPresent()
    {
        var fs = new InMemoryFileSystem();
        var store = new WorkspaceStore(WorkspacePath, fs);
        var state = new WorkspaceState
        {
            RepoRoot = RepoRoot,
            Phase = WorkflowPhase.Execution,
            ActiveGoal = new GoalState { GoalText = "Ship repo memory bootstrap" },
            CodebaseContext = "## Existing patterns\nLayered services",
            Models = [new ModelDefinition { Name = DefaultModelName, Cost = 0, IsDefault = true }]
        };
        state.Decisions.Add(new DecisionRecord
        {
            Id = 1,
            Title = "Use tracked repo memory",
            Detail = "Keep runtime state untracked but export durable repo context.",
            Source = "architect",
            CreatedAtUtc = DateTimeOffset.Parse("2026-04-25T12:00:00Z")
        });

        store.Save(state);

        var repoMemoryDir = new RepoMemoryStore(RepoRoot, fs).DirectoryPath;
        Assert.That(fs.DirectoryExists(repoMemoryDir), $"Expected repo memory directory at {repoMemoryDir}");
        Assert.That(fs.FileExists(Path.Combine(repoMemoryDir, "manifest.json")), "Expected manifest.json to be written.");
        Assert.That(fs.FileExists(Path.Combine(repoMemoryDir, "GOAL.md")), "Expected GOAL.md to be written.");
        Assert.That(fs.FileExists(Path.Combine(repoMemoryDir, "STATUS.md")), "Expected STATUS.md to be written.");
        Assert.That(fs.FileExists(Path.Combine(repoMemoryDir, "DECISIONS.md")), "Expected DECISIONS.md to be written.");
        Assert.That(fs.FileExists(Path.Combine(repoMemoryDir, "CONTEXT.md")), "Expected CONTEXT.md to be written.");
        Assert.Contains("Ship repo memory bootstrap", fs.ReadAllText(Path.Combine(repoMemoryDir, "GOAL.md")));
        Assert.Contains("Phase: Execution", fs.ReadAllText(Path.Combine(repoMemoryDir, "STATUS.md")));
        Assert.Contains("Use tracked repo memory", fs.ReadAllText(Path.Combine(repoMemoryDir, "DECISIONS.md")));
        return Task.CompletedTask;
    }

    private static Task WorkspaceStore_Initialize_HydratesRepoMemory_WhenPresent()
    {
        var fs = new InMemoryFileSystem();
        var sourceStore = new WorkspaceStore(".devteam-source-workspace", fs);
        var sourceState = new WorkspaceState
        {
            RepoRoot = RepoRoot,
            Phase = WorkflowPhase.ArchitectPlanning,
            ActiveGoal = new GoalState { GoalText = "Resume durable context" },
            CodebaseContext = "## Existing patterns\nEvent sourcing",
            Models = [new ModelDefinition { Name = DefaultModelName, Cost = 0, IsDefault = true }]
        };
        sourceState.Decisions.Add(new DecisionRecord
        {
            Id = 1,
            Title = "Keep repo memory tracked",
            Detail = "Tracked files should bootstrap a fresh workspace on a new machine.",
            Source = "planner",
            CreatedAtUtc = DateTimeOffset.Parse("2026-04-25T13:00:00Z")
        });
        sourceStore.Save(sourceState);

        var hydratedStore = new WorkspaceStore(".devteam-hydrated", fs);
        var hydratedState = hydratedStore.Initialize(RepoRoot, 25, 6);

        Assert.That(hydratedState.ActiveGoal?.GoalText == "Resume durable context",
            $"Expected goal to be hydrated from repo memory but got '{hydratedState.ActiveGoal?.GoalText}'");
        Assert.That(hydratedState.Phase == WorkflowPhase.ArchitectPlanning,
            $"Expected phase ArchitectPlanning but got {hydratedState.Phase}");
        Assert.Contains("Event sourcing", hydratedState.CodebaseContext);
        Assert.That(hydratedState.Decisions.Count >= 1, "Expected durable decisions to be hydrated from repo memory.");
        Assert.Contains("Keep repo memory tracked", hydratedState.Decisions[0].Title);
        return Task.CompletedTask;
    }
}


