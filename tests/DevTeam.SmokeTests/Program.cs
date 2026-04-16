using DevTeam.Cli;
using DevTeam.Core;
using static DevTeam.SmokeTests.SmokeTestFunctions;


var tests = new List<(string Name, Action Run)>
{
    ("RunOnce bootstraps and queues planner work", TestRunOnceBootstrapsAndQueues),
    ("Completed run unlocks dependent architect work", TestCompleteRunUnlocksDependency),
    ("Planning phase waits for plan approval", TestPlanningPhaseRequiresApproval),
    ("Blocking questions only halt when no ready work exists", TestBlockingQuestionWaitState),
    ("Premium cap forces fallback model", TestPremiumCapFallback),
    ("Role suggested model overrides default policy", TestRoleSuggestedModelOverridesDefaultPolicy),
    ("CLI agent client builds a copilot invocation", TestCliAgentClientInvocationShape),
    ("SDK agent client is the default integration backend", TestSdkAgentClientFactory),
    ("SDK CLI path resolver prefers installed copilot", TestSdkCliPathResolverPrefersInstalledCopilot),
    ("SDK CLI path resolver fails when copilot is missing", TestSdkCliPathResolverFailsWhenMissing),
    ("Tool update check detects newer stable versions", TestToolUpdateCheckDetectsNewerStableVersions),
    ("Tool update command targets the global package", TestToolUpdateCommandTargetsGlobalPackage),
    ("Bug report command writes issue draft", TestBugReportCommandWritesIssueDraft),
    ("Plan command shows architect summary when approval is pending", TestPlanCommandShowsArchitectSummaryWhenApprovalPending),
    ("Plan command shows latest architect summary during architect planning", TestPlanCommandShowsArchitectSummaryDuringArchitectPlanning),
    ("SDK session config wires workspace MCP server", TestSdkSessionConfigWiresWorkspaceMcp),
    ("SDK session config wires external MCP servers", TestSdkSessionConfigWiresExternalMcpServers),
    ("Workspace loads MCP server definitions", TestWorkspaceLoadsMcpServers),
    ("Planning session is reused across planning retries", TestPlanningSessionIsReusedAcrossPlanningRetries),
    ("Issue retries reuse the same session", TestIssueRetriesReuseTheSameSession),
    ("Parallel pipelines keep isolated sessions", TestParallelPipelinesKeepIsolatedSessions),
    ("Plan workflow generates plan when missing", TestPlanWorkflowGeneratesPlanWhenMissing),
    ("Plan workflow blocks run before planning", TestPlanWorkflowBlocksRunBeforePlanning),
    ("Workspace loads default modes", TestWorkspaceLoadsModes),
    ("Set mode updates active mode and pipeline defaults", TestSetModeUpdatesRuntimeConfiguration),
    ("Set keep-awake updates runtime configuration", TestSetKeepAwakeUpdatesRuntimeConfiguration),
    ("Goal input resolver loads markdown from file", TestGoalInputResolverLoadsMarkdownFromFile),
    ("DevTeam assets load from .devteam-source first", TestPromptAssetsPreferDevTeamSource),
    ("Run-loop executes queued work with normal verbosity", TestRunLoopExecutesWork),
    ("Execution loop uses orchestrator-selected batch", TestExecutionLoopUsesOrchestratorSelectedBatch),
    ("Run-loop resumes previously queued runs", TestRunLoopResumesQueuedRuns),
    ("Run-loop persists agent questions for user input", TestRunLoopPersistsQuestions),
    ("Planning run writes plan artifact for approval", TestPlanningRunWritesPlanArtifact),
    ("Init clears stale legacy workspace artifacts", TestInitClearsLegacyArtifacts),
    ("Planning feedback reopens the planning issue", TestPlanningFeedbackReopensPlanning),
    ("Architect feedback reopens architect issue", TestArchitectFeedbackReopensArchitectIssue),
    ("Architect feedback queues architect rerun", TestArchitectFeedbackQueuesArchitectRerun),
    ("Architect rerun heals stale reopened planning issue", TestArchitectRerunHealsStaleReopenedPlanningIssue),
    ("Agent-generated issues keep the loop moving", TestAgentGeneratedIssues),
    ("Issue board markdown mirrors workspace state", TestIssueBoardMirror),
    ("Generated issue roles are normalized", TestGeneratedIssueRoleNormalization),
    ("Role aliases are exposed for validation feedback", TestRoleAliasesExposed),
    ("Parallel loop executes independent areas concurrently", TestParallelLoopExecutesIndependentAreas),
    ("Conflict prevention avoids same-area parallel runs", TestConflictPreventionAvoidsSameAreaRuns),
    ("Architect pipeline completion creates developer follow-up", TestArchitectPipelineCompletionCreatesDeveloperFollowUp),
    ("Developer pipeline completion creates tester follow-up", TestDeveloperPipelineCompletionCreatesTesterFollowUp),
    ("Architect work gates non-architect execution", TestArchitectWorkGatesNonArchitectExecution),
    ("Priority gap can reduce pipeline concurrency", TestPriorityGapReducesPipelineConcurrency),
    ("Mode guardrails appear in agent prompt", TestModeGuardrailsAppearInPrompt),
    ("Pipeline handoff appears in agent prompt", TestPipelineHandoffAppearsInPrompt),
    ("Collapsed response headers still parse cleanly", TestCollapsedResponseHeadersParseCleanly),
    ("Run artifacts capture superpowers and tools used", TestRunArtifactsCaptureUsageMetadata),
    ("Legacy workspaces hydrate missing roles and superpowers", TestLegacyWorkspaceHydratesMetadata),
    ("Workspace loads legacy execution selection timestamps", TestWorkspaceLoadsLegacyExecutionSelectionTimestamp),
    ("Friendly role names resolve to canonical roles", TestFriendlyRoleNamesResolve),
    ("External repos fall back to packaged prompt assets", TestExternalReposFallBackToPackagedAssets),
    ("Git helper initializes repository when missing", TestGitHelperInitializesRepository),
    ("Git helper stages only iteration changes", TestGitHelperStagesOnlyIterationChanges),
    ("Loop stages changed files after iteration", TestLoopStagesChangedFilesAfterIteration),
    ("Workspace manifest shards large collections", TestWorkspaceManifestShardsCollections),
    ("Concurrent workspace saves remain parseable", TestConcurrentWorkspaceSavesRemainParseable),
    ("Workspace save tolerates replace-blocking readers", TestWorkspaceSaveToleratesReplaceBlockingReaders),
    ("Prompt asset bodies are not persisted in state files", TestPromptAssetsAreNotPersisted),
    ("Execution orchestrator emits heartbeat while selecting batch", TestExecutionOrchestratorEmitsHeartbeat),
    ("Existing pipeline follow-ups are normalized", TestExistingPipelineFollowUpsAreNormalized),
    ("Plan approval transitions to architect planning", TestPlanApprovalTransitionsToArchitectPlanning),
    ("Architect approval transitions to execution", TestArchitectApprovalTransitionsToExecution),
    ("Auto-approve skips both approval gates", TestAutoApproveSkipsBothGates),
    ("Autopilot mode enables auto-approve", TestAutopilotModeEnablesAutoApprove),
    ("Design-only roles receive file boundary enforcement", TestDesignOnlyRolesReceiveFileBoundary),
    ("Edit-issue command updates queued issue fields", TestEditIssueCommandUpdatesQueuedIssue),
    ("Planner cannot create duplicate architect issues", TestPlannerCannotCreateDuplicateArchitectIssues),
    ("Init rejects misspelled goal option", TestInitRejectsMisspelledGoalOption),
    ("Architect run updates plan artifact with execution details", TestArchitectRunUpdatesPlanArtifact),
    ("Conflict prevention holds at max-subagents 4", TestConflictPreventionHoldsAtHighSubagentCount)
};

var failures = new List<string>();
foreach (var (name, run) in tests)
{
    try
    {
        run();
        Console.WriteLine($"PASS: {name}");
    }
    catch (Exception ex)
    {
        failures.Add($"{name}: {ex.Message}");
        Console.WriteLine($"FAIL: {name}");
    }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine("Smoke tests failed:");
    foreach (var failure in failures)
    {
        Console.Error.WriteLine($"  - {failure}");
    }
    return 1;
}

Console.WriteLine($"All {tests.Count} smoke tests passed.");
return 0;

