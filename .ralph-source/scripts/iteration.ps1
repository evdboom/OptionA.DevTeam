# ---- iteration.ps1 ----
# Main iteration loop: prompt building, copilot invocation (sequential & parallel),
# result processing, credit tracking, CLANCY handling.
# Dot-sourced by ralph.ps1. Depends on: utils.ps1, copilot.ps1, models.ps1, parallel.ps1.

function Invoke-IterationLoop {
  param(
    [hashtable]$State,         # From Invoke-Preflight
    [string]$RepoPath,
    [switch]$AutoRunAllowed,
    [string[]]$AllowedCommandPrefixes,
    [switch]$StopOnFailure,
    [switch]$StreamCopilotOutput,
    [switch]$ShowSpinner,
    [string[]]$CopilotExtraArgs,
    [int]$MaxConcurrency,
    [bool]$Verbose
  )

  # Unpack state
  $projectDir  = $State.ProjectDir
  $stateDir    = $State.StateDir
  $issuesDir   = $State.IssuesDir
  $handoffPath = $State.HandoffPath
  $historyPath = $State.HistoryPath
  $creditsPath = $State.CreditsPath
  $goalPath    = $State.GoalPath
  $acceptPath  = $State.AcceptPath
  $rolesDir    = $State.RolesDir

  $creditsUsed   = $State.CreditsUsed
  $creditCap     = $State.CreditCap
  $startIteration = $State.StartIteration
  $maxIterations = $State.MaxIterations

  $consecutiveParallelFailures = 0  # Circuit breaker
  $conflictedGroups = @{}  # Track issue groups that caused merge conflicts

  for ($i = $startIteration; $i -le $maxIterations; $i++) {
    # Check for Ctrl+C
    if ([Console]::KeyAvailable) {
      $key = [Console]::ReadKey($true)
      if ($key.Key -eq 'C' -and $key.Modifiers -eq 'Control') {
        Write-Host "`nCtrl+C detected. Stopping after current iteration..." -ForegroundColor Yellow
        break
      }
    }
    
    # Parse suggestions from previous iteration
    $handoffContent = ReadAllText $handoffPath
    $suggestions = Parse-NextSuggestions $handoffContent
    
    # Check if project is complete
    if ($suggestions.Role -and $suggestions.Role -match '^\s*None\s*\.?\s*$') {
      Write-Host ""
      Write-Host "Project marked as complete (NEXT_ROLE: None). Stopping iteration loop." -ForegroundColor Green
      Write-Host "Total credits used: $creditsUsed / $creditCap" -ForegroundColor Cyan
      break
    }
    
    $suggestedModel = if ($suggestions.Model) { $suggestions.Model } else { $null }
    $currentRole = if ($suggestions.Role) { $suggestions.Role } else { "Orchestrator" }
    $currentModel = Select-Model $suggestedModel $creditsUsed $creditCap $currentRole
    $displayModel = if ($currentModel) { $currentModel } else { (Get-DefaultModel) ?? "default" }
    
    $displayRole = $currentRole
    $displayPipeline = if ($suggestions.Pipeline) { $suggestions.Pipeline } else { $null }
    Print-IterationHeader $i $maxIterations $creditsUsed $creditCap $displayModel $displayRole $displayPipeline -quiet:(!$Verbose)

    # Check CLANCY.md for user break-in instructions
    $clancyPath = Join-Path $projectDir "CLANCY.md"
    $clancyText = ""
    if (Test-Path $clancyPath) {
      $clancyRaw = ReadAllText $clancyPath
      $clancyClean = ($clancyRaw -replace '(?m)^> Yes father.*$', '').Trim()
      if ($clancyClean -and $clancyClean -ne '# Ralphie, please keep in mind') {
        $clancyText = $clancyClean
        Write-Host "CLANCY.md: User break-in instructions detected" -ForegroundColor Magenta
        if ($Verbose) { Write-Host $clancyText -ForegroundColor Magenta }
      }
    }

    if ($Verbose) { Write-Section "0) Read GOAL.md (prompt source)" }
    $goalText = ReadAllText $goalPath
    if (-not $goalText.Trim()) { throw "GOAL.md is empty. Add your goal/prompt content." }
    if ($Verbose) { Write-Verbose ("GOAL.md chars: {0}" -f $goalText.Length) }

    if ($Verbose) { Write-Section "1) Capture current diff (what last iteration did)" }
    $diffObj = GitDiffSummary
    $stat = if ($diffObj.Stat) { $diffObj.Stat } else { "(no changes)" }
    if ($Verbose) { Write-Host $stat }

    if (-not [string]::IsNullOrWhiteSpace($diffObj.Diff)) {
      $diffSnapshotPath = Join-Path $stateDir ("diff_{0:000}.patch" -f $i)
      WriteAllText $diffSnapshotPath ($diffObj.Diff + "`n")
    }

    if ($Verbose) { Write-Section "2) Stage current changes (so next loop sees only new changes)" }
    StageAll "Staging working tree changes..."
    $stagedStat = Exec "git diff --cached --stat" -AllowFail
    if ($Verbose) {
      if ($stagedStat.StdOut.Trim()) {
        Write-Host "Staged changes:"
        Write-Host $stagedStat.StdOut.Trim()
      } else {
        Write-Host "No staged changes."
      }
    }

    if ($Verbose) { Write-Section "3) Build lean prompt with file pointers" }

    $suggestedRoleName = if ($suggestions.Role) { $suggestions.Role } else { "Orchestrator" }
    if ($Verbose) { Write-Verbose "Role: $suggestedRoleName" }

    # Snapshot handoff before this iteration
    $handoff = ReadAllText $handoffPath

    # Determine role definition path (relative for the prompt)
    $roleSlug = ($suggestedRoleName -replace '[*_`#>]', '').ToLower().Trim() -replace '\s+', '-'
    $roleRelPath = ".ralph/.ralph-source/roles/$roleSlug.md"

    $pipelineNotice = ""
    if ($suggestions.Pipeline) {
      $pipelineNotice = "`nPIPELINE in progress: $($suggestions.Pipeline) — you are executing the '$suggestedRoleName' step."
    }

    $diffNotice = ""
    $diffSnapshotName = "diff_{0:000}.patch" -f $i
    $diffSnapshotFullPath = Join-Path $stateDir $diffSnapshotName
    if (Test-Path $diffSnapshotFullPath) {
      $diffNotice = "`nThe previous iteration's changes are saved in .ralph/.ralph-state/$diffSnapshotName — read it to see what changed."
    }

    $clancySection = ""
    if ($clancyText) {
      $clancySection = @"

--- USER OVERRIDE (HIGHEST PRIORITY) ---
The user ("Clancy") has injected live instructions via CLANCY.md. These OVERRIDE your current plan.
You MUST:
1. Take concrete action on it THIS iteration (create an ADR, update ROADMAP, write code, etc.)
2. Include a '## CLANCY Response' section in your output describing EXACTLY what you did.
Do NOT just mention it in passing — the user expects visible results.

$clancyText
"@
    }

    $creditsRemaining = $creditCap - $creditsUsed
    $handoffRelPath = ".ralph/.ralph-state/handoff.md"

    # Role-specific instruction block
    $roleInstructions = if ($suggestedRoleName -match '^Orchestrator') {
      @"
Instructions (Orchestrator-specific):
1. Read your role definition, the issue index, and the handoff from the previous iteration
2. Plan and delegate — create or update issues, assign roles, define pipelines
3. Do NOT mark any issue as "done" — only the role that completed the work may do that
4. Do NOT update ROADMAP.md checkboxes to [x] — that is the completing role's job
5. Do NOT read or audit source code files — you are planning, not verifying
6. You MAY set issues to open, in-progress, or blocked
7. Create issues for new work (remember to update the issue index)
8. Write your structured output to $handoffRelPath with ONLY these sections:
   # Role, # Plan, # Issues, # Actions, # Acceptance Criteria, # Handoff
9. Keep this iteration SHORT — plan, delegate, hand off. Do not over-read or over-verify.
"@
    } else {
      @"
Instructions:
1. Read your role definition and assigned issues
2. Execute the work described in your role and issues
3. Update issue statuses as you work
4. Create issues for any new work you identify that is not currently tracked (remember to update the issue index)
5. Write your structured output to $handoffRelPath with ONLY these sections:
   # Role, # Plan, # Issues, # Actions, # Acceptance Criteria, # Handoff
"@
    }

    $shortPrompt = @"
You are: $suggestedRoleName | Iteration: $i/$maxIterations | Credits used: $creditsUsed/$creditCap (remaining: $creditsRemaining) | Model: $currentModel

Read these files to understand the project and your task:
- .ralph/.ralph-project/GOAL.md — the project goal
- .ralph/.ralph-source/AGENTS.md — architecture, roles, output format, and constraints
- .ralph/.ralph-project/ROADMAP.md — project roadmap and progress
- $roleRelPath — your role definition and instructions
- .ralph/.ralph-project/issues/ — the issue board (read _index.md first, then assigned issues)
- .ralph/.ralph-project/ACCEPTANCE.md — acceptance criteria
- .ralph/.ralph-state/handoff.md — output from the previous iteration (what was done, what's next)$diffNotice$pipelineNotice

All project source code is in the src/ directory. Work there for any code changes.

Superpowers (step-by-step skill instructions) are in .ralph/.ralph-source/superpowers/. Read the ones relevant to your role before starting (see AGENTS.md for which to load).
$clancySection
$roleInstructions
"@
    if ($Verbose) { 
      $modelCost = Get-ModelCost $(if ($currentModel) { $currentModel } else { (Get-DefaultModel) ?? "default" })
      Write-Host "Using model: $displayModel (cost: $modelCost credits)" -ForegroundColor Cyan 
      Write-Host "Prompt size: $($shortPrompt.Length) chars" -ForegroundColor DarkGray
    }

    # ==================== PARALLEL DISPATCH (disabled — short-circuited to sequential) ====================
    if ($suggestions.Parallel.Count -gt 0 -and $MaxConcurrency -gt 1) {
      Write-Host "Parallel dispatch disabled (short-circuited). Running sequentially." -ForegroundColor DarkYellow
    }
    if ($false) {
    # --- Original parallel block preserved below (inactive) ---
      $agentGroups = @{}
      $groupIndex = 0

      foreach ($group in $suggestions.Parallel) {
        $groupIndex++
        $agentId = "iter{0:000}-g{1}" -f $i, $groupIndex
        $agentIds += $agentId

        $siblingGroups = @($suggestions.Parallel | Where-Object { $_ -ne $group })
        $siblingNotice = ""
        if ($siblingGroups.Count -gt 0) {
          $siblingNotice = @"

CONCURRENCY: You are one of $($suggestions.Parallel.Count) concurrent agents.
Your assigned issues: $group
Other agents are working on: $($siblingGroups -join ', ')
IMPORTANT: Stay within your assigned issue scope. Do NOT modify files that other agents might touch.
"@
        }

        $agentPrompt = @"
You are: $suggestedRoleName (SUB-AGENT) | Iteration: $i/$maxIterations | Agent: $groupIndex/$($suggestions.Parallel.Count) | Model: $currentModel

You are a parallel sub-agent, NOT the main loop. Complete your assigned issues and stop.

Read these files to understand the project and your task:
- .ralph/.ralph-source/AGENTS.md — sub-agent spec, output format, and constraints (READ FIRST)
- .ralph/.ralph-project/GOAL.md — the project goal
- $roleRelPath — your role definition and instructions
- .ralph/.ralph-project/issues/ — the issue board (read _index.md first, then your assigned issues)
- .ralph/.ralph-project/ACCEPTANCE.md — acceptance criteria
- .ralph/.ralph-state/handoff.md — context from the previous iteration$diffNotice

All project source code is in the src/ directory. Work there for any code changes.
$siblingNotice
PARALLEL ASSIGNMENT: You are agent $groupIndex of $($suggestions.Parallel.Count). Your issue group: $group
Focus ONLY on these issues. Write your structured output to $handoffRelPath when done.
Do NOT include NEXT_ROLE, NEXT_MODEL, PIPELINE, or PARALLEL in your output.
Do NOT create new issues — suggest them in your handoff for the Orchestrator.
Do NOT stage or commit .ralph/.ralph-source/AGENTS.md — the orchestrator manages that file.

Superpowers (step-by-step skill instructions) are in .ralph/.ralph-source/superpowers/. Read the ones relevant to your role before starting (see AGENTS.md for which to load).
$clancySection
Instructions:
1. Read AGENTS.md (sub-agent spec) and your role definition
2. Read your assigned issues: $group
3. Execute the work described in those issues
4. Update issue statuses as you work
5. Write your structured output to $handoffRelPath with ONLY these sections:
   # Role, # Plan, # Issues, # Actions, # Acceptance Criteria, # Handoff
"@

        try {
          $worktreePath = New-AgentWorktree $agentId
          $agentGroups[$agentId] = @{ Group = $group; Worktree = $worktreePath }

          # Ensure .ralph-state exists in worktree (gitignored, so not in checkout)
          $wtStateDir = Join-Path $worktreePath ".ralph\.ralph-state"
          if (-not (Test-Path $wtStateDir)) { New-Item -ItemType Directory -Path $wtStateDir -Force | Out-Null }
          # Copy handoff and latest diff patch so agents have iteration context
          if (Test-Path $handoffPath) { Copy-Item $handoffPath (Join-Path $wtStateDir "handoff.md") -Force }
          $latestDiff = Join-Path $stateDir ("diff_{0:000}.patch" -f $i)
          if (Test-Path $latestDiff) { Copy-Item $latestDiff (Join-Path $wtStateDir ("diff_{0:000}.patch" -f $i)) -Force }

          # Replace AGENTS.md with the sub-agent scoped version
          $subagentSpec = Join-Path $sourceDir "AGENTS-SUBAGENT.md"
          $wtAgentsPath = Join-Path $worktreePath ".ralph\.ralph-source\AGENTS.md"
          if (Test-Path $subagentSpec) { Copy-Item $subagentSpec $wtAgentsPath -Force }

          Write-Host "  Starting agent $agentId for issues: $group (model: $displayModel)" -ForegroundColor Cyan
          $job = Start-AgentJob -agentId $agentId -worktreePath $worktreePath -shortPrompt $agentPrompt -model $currentModel -extraArgs $CopilotExtraArgs
          $agentJobs += $job
        } catch {
          Write-Host "  Failed to start agent ${agentId}: $($_.Exception.Message)" -ForegroundColor Red
        }
      }

      if ($agentJobs.Count -eq 0) {
        Write-Host "All parallel agents failed to start. Falling back to sequential." -ForegroundColor Yellow
      } else {
        # Build per-agent labels for the progress display
        $agentLabelMap = @{}
        foreach ($id in $agentIds) {
          $issueGroup = if ($agentGroups.ContainsKey($id)) { $agentGroups[$id].Group } else { '?' }
          $agentLabelMap[$id] = "$displayRole on $displayModel [$issueGroup]"
        }
        $agentResults = Wait-AgentJobs $agentJobs -timeoutSeconds 2400 -label "$displayRole on $displayModel" -agentLabels $agentLabelMap

        $mergedCount = 0
        $failedAgents = @()
        $allPlanTexts = @()

        foreach ($agentId in $agentIds) {
          if (-not $agentResults.ContainsKey($agentId)) {
            Write-Host "  Agent ${agentId}: no result (failed to start)" -ForegroundColor Yellow
            $failedAgents += $agentId
            continue
          }

          $result = $agentResults[$agentId]
          if ($result.ExitCode -ne 0) {
            Write-Host "  Agent ${agentId}: FAILED (exit code $($result.ExitCode))" -ForegroundColor Red
            if ($result.StdErr) { Write-Verbose "  StdErr: $($result.StdErr)" }
            $failedAgents += $agentId

            $errorLogPath = Join-Path $stateDir ("error_{0:000}_{1}.log" -f $i, $agentId)
            WriteAllText $errorLogPath ("Agent: $agentId`nGroup: $($agentGroups[$agentId].Group)`nExitCode: $($result.ExitCode)`nStdErr:`n$($result.StdErr)")
          } else {
            $merged = Merge-AgentResult -agentId $agentId -model $currentModel -extraArgs $CopilotExtraArgs
            if ($merged) {
              Write-Host "  Agent ${agentId}: merged successfully" -ForegroundColor Green
              $mergedCount++
            } else {
              Write-Host "  Agent ${agentId}: merge conflict — resolver failed, branch preserved" -ForegroundColor Yellow
              $failedAgents += $agentId
              # Track this issue group so future iterations skip parallel for it
              $conflictGroup = if ($agentGroups.ContainsKey($agentId)) { $agentGroups[$agentId].Group } else { $null }
              if ($conflictGroup) { $conflictedGroups[$conflictGroup] = $i }
            }

            if ($result.StdOut) { $allPlanTexts += $result.StdOut }
          }
        }

        $handoffContent = ReadAllText $handoffPath
        $planText = ($allPlanTexts -join "`n`n---`n`n")

        $modelCost = Get-ModelCost $currentModel
        $succeededCount = $agentJobs.Count - $failedAgents.Count
        $parallelCost = $modelCost * [Math]::Max($succeededCount, 0)
        $creditsUsed += $parallelCost

        if ($mergedCount -eq 0) {
          $consecutiveParallelFailures++
          if ($consecutiveParallelFailures -ge 3) {
            Write-Host "Circuit breaker: $consecutiveParallelFailures consecutive parallel failures — disabling parallel dispatch for remaining iterations" -ForegroundColor Red
          }
        } else {
          $consecutiveParallelFailures = 0
        }

        # Save credit history
        $creditsData = Get-Content $creditsPath -Raw | ConvertFrom-Json
        $creditsData.used = $creditsUsed
        if ($creditsData.PSObject.Properties.Name -contains 'currentIteration') {
          $creditsData.currentIteration = $i
        } else {
          $creditsData | Add-Member -NotePropertyName 'currentIteration' -NotePropertyValue $i -Force
        }
        $creditsData.history += @{
          iteration = $i
          model = $displayModel
          role = $displayRole
          pipeline = $displayPipeline
          cost = $parallelCost
          parallel = $suggestions.Parallel.Count
          merged = $mergedCount
          failed = $failedAgents.Count
          timestamp = (Get-Date).ToString("o")
        }
        $creditsData | ConvertTo-Json -Depth 10 | Set-Content $creditsPath

        if (-not [string]::IsNullOrWhiteSpace($planText)) {
          $planPath = Join-Path $stateDir ("plan_{0:000}.md" -f $i)
          WriteAllText $planPath ($planText + "`n")
          Add-Content -Path $historyPath -Value ("`n--- Iteration {0:000} (PARALLEL, Model: $displayModel, Cost: $parallelCost, Agents: {1}, Merged: {2}) ---`n{3}`n" -f $i, $agentJobs.Count, $mergedCount, $planText)
        }

        $resultPath = Join-Path $stateDir ("result_{0:000}.md" -f $i)
        WriteAllText $resultPath ($handoffContent + "`n")

        foreach ($agentId in $agentIds) {
          try { Remove-AgentWorktree $agentId } catch { Write-Verbose "Cleanup warning for ${agentId}: $_" }
        }

        Write-Host ""
        Write-Host ("="*72) -ForegroundColor Green
        Write-Host ("Iteration {0}/{1} Complete (PARALLEL) | Agents: {2} | Merged: {3} | Failed: {4} | Credits: {5:0.##}/{6:0.##}" -f $i, $maxIterations, $agentJobs.Count, $mergedCount, $failedAgents.Count, $creditsUsed, $creditCap) -ForegroundColor Green
        Write-Host ("="*72) -ForegroundColor Green

        if ($failedAgents.Count -gt 0) {
          Write-Host "Failed agents: $($failedAgents -join ', ')" -ForegroundColor Yellow
          $failureNote = "`n`n--- PARALLEL AGENT FAILURES ---`nThe following agents failed or had merge conflicts: $($failedAgents -join ', ')`nTheir branches (ralph-<agentId>) are preserved. The Orchestrator should re-plan these issues.`n"
          Add-Content -Path $handoffPath -Value $failureNote
        }

        Write-Host ""
        Write-Host "--- Handoff ---" -ForegroundColor Cyan
        Write-Host (ReadAllText $handoffPath)
        Write-Host ""

        StageAll "Staging parallel agent results..."
        continue
      }
    }
    # --- End original parallel block ---
    # ==================== END PARALLEL DISPATCH (disabled) ====================
    
    # ==================== SEQUENTIAL INVOCATION ====================
    # Orchestrator should be fast (plan & delegate); implementation roles get full timeout
    $roleTimeout = if ($suggestedRoleName -match '^Orchestrator') { 300 } else { 2400 }

    try {
      $planText = Invoke-Copilot `
        -prompt $shortPrompt `
        -workdir $RepoPath `
        -extraArgs $CopilotExtraArgs `
        -model $currentModel `
        -label "$displayRole on $displayModel (iter $i/$maxIterations)" `
        -timeoutSeconds $roleTimeout `
        -stream:($StreamCopilotOutput -and $Verbose) `
        -spinner:($ShowSpinner -and $Verbose) `
        -quietProgress:(-not $Verbose)
    } catch {
      if ($_.Exception.Message -match 'terminated|cancelled') {
        Write-Host "`nIteration cancelled. Exiting..." -ForegroundColor Yellow
        break
      }
      
      # Try fallback to default model
      $defaultModel = Get-DefaultModel
      $didFallback = $false
      if ($currentModel -and $defaultModel -and $currentModel -ne $defaultModel) {
        Write-Host "`nModel '$currentModel' failed. Retrying with default model '$defaultModel'..." -ForegroundColor Yellow
        try {
          $planText = Invoke-Copilot `
            -prompt $shortPrompt `
            -workdir $RepoPath `
            -extraArgs $CopilotExtraArgs `
            -model $defaultModel `
            -label "$displayRole on $defaultModel [fallback] (iter $i/$maxIterations)" `
            -stream:($StreamCopilotOutput -and $Verbose) `
            -spinner:($ShowSpinner -and $Verbose) `
            -quietProgress:(-not $Verbose)
          $currentModel = $defaultModel
          $displayModel = $defaultModel
          $didFallback = $true
          Write-Host "Fallback to '$defaultModel' succeeded." -ForegroundColor Green
        } catch {
          if ($_.Exception.Message -match 'terminated|cancelled') {
            Write-Host "`nIteration cancelled. Exiting..." -ForegroundColor Yellow
            break
          }
          Write-Host "Fallback model also failed." -ForegroundColor Red
        }
      }

      if (-not $didFallback) {
        $errorLogPath = Join-Path $stateDir ("error_{0:000}.log" -f $i)
        $errorDetails = @"
Iteration: $i
Model: $displayModel
Timestamp: $(Get-Date -Format "o")
Error: $($_.Exception.Message)

Command attempted:
copilot $(if ($currentModel) { "--model $currentModel" } else { "(default model)" }) -p "<context>"

"@
        WriteAllText $errorLogPath $errorDetails
        
        Write-Host "`nCopilot invocation failed!" -ForegroundColor Red
        Write-Host "Model attempted: $displayModel" -ForegroundColor Yellow
        Write-Host "Error details saved to: $errorLogPath" -ForegroundColor Yellow
        Write-Host "`nPossible causes:" -ForegroundColor Cyan
        Write-Host "  - Model '$displayModel' may not exist or is unavailable" -ForegroundColor Gray
        Write-Host "  - Rate limiting or authentication issue" -ForegroundColor Gray
        Write-Host "  - Network connectivity problem" -ForegroundColor Gray
        Write-Host "`nTry running manually: copilot --help" -ForegroundColor Cyan
        Write-Host "Or check available models: gh copilot --help" -ForegroundColor Cyan
        
        throw
      }
    }

    # Update credits
    $modelCost = Get-ModelCost $currentModel
    $creditsUsed += $modelCost
    
    $creditsData = Get-Content $creditsPath -Raw | ConvertFrom-Json
    $creditsData.used = $creditsUsed
    
    if ($creditsData.PSObject.Properties.Name -contains 'currentIteration') {
      $creditsData.currentIteration = $i
    } else {
      $creditsData | Add-Member -NotePropertyName 'currentIteration' -NotePropertyValue $i -Force
    }
    
    $creditsData.history += @{
      iteration = $i
      model = $displayModel
      role = $displayRole
      pipeline = $displayPipeline
      cost = $modelCost
      timestamp = (Get-Date).ToString("o")
    }
    $creditsData | ConvertTo-Json -Depth 10 | Set-Content $creditsPath

    # Save output
    $planPath = $null
    if (-not [string]::IsNullOrWhiteSpace($planText)) {
      $planPath = Join-Path $stateDir ("plan_{0:000}.md" -f $i)
      WriteAllText $planPath ($planText + "`n")
      Add-Content -Path $historyPath -Value ("`n--- Iteration {0:000} (Model: $displayModel, Cost: $modelCost) ---`n{1}`n" -f $i, $planText)
    } else {
      Write-Host "Warning: Copilot produced no output for iteration $i" -ForegroundColor Yellow
    }
    
    # Process handoff
    $handoffContent = ReadAllText $handoffPath
    if ([string]::IsNullOrWhiteSpace($handoffContent) -or $handoffContent -eq $handoff) {
      if (-not [string]::IsNullOrWhiteSpace($planText)) {
        if ($Verbose) { Write-Host "Warning: Copilot didn't update handoff.md, using full output" -ForegroundColor Yellow }
        WriteAllText $handoffPath ($planText + "`n")
        $handoffContent = $planText
      } else {
        Write-Host "Warning: Copilot produced no output and didn't update handoff.md — preserving previous handoff" -ForegroundColor Yellow
        $handoffContent = $handoff
      }
    }

    # CLANCY acknowledgement
    if ($clancyText -and -not [string]::IsNullOrWhiteSpace($handoffContent)) {
      $clancyAddressed = $handoffContent -match '(?i)CLANCY\s*Response|## CLANCY'
      if ($clancyAddressed) {
        $nextIssueNum = 1
        $existingIssues = @(Get-ChildItem -Path $issuesDir -Filter '*.md' -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^\d{4}-' })
        if ($existingIssues.Count -gt 0) {
          $maxNum = ($existingIssues | ForEach-Object { [int]($_.Name.Substring(0,4)) } | Measure-Object -Maximum).Maximum
          $nextIssueNum = $maxNum + 1
        }
        $issueId = "{0:0000}" -f $nextIssueNum

        $clancyLines = @($clancyText -split "`n" | Where-Object { $_ -and $_ -notmatch '^#' }) 
        $clancyFirstLine = if ($clancyLines.Count -gt 0) { $clancyLines[0].Trim().Substring(0, [Math]::Min(60, $clancyLines[0].Trim().Length)) } else { "User break-in request" }
        $issueSlug = ($clancyFirstLine -replace '[^a-zA-Z0-9 ]', '' -replace '\s+', '-').ToLower().Substring(0, [Math]::Min(40, ($clancyFirstLine -replace '[^a-zA-Z0-9 ]', '' -replace '\s+', '-').ToLower().Length))
        $issuePath = Join-Path $issuesDir "$issueId-clancy-$issueSlug.md"

        $issueContent = @"
# Issue $issueId`: User request (CLANCY break-in, iteration $i)

**Status:** open
**Assigned:** (model decides — Architect/Developer as appropriate)
**Priority:** high
**Source:** CLANCY.md break-in at iteration $i

## Description

The user injected the following instruction during the live loop:

> $($clancyText -replace '(?m)^# .*$', '' | ForEach-Object { $_.Trim() } | Where-Object { $_ })

## Acceptance Criteria
- [ ] Instruction incorporated into project decisions (ADR or ROADMAP)
- [ ] Concrete action taken (code, design, or plan update)
- [ ] Result documented in handoff

## Notes
Auto-created by ralph.ps1 CLANCY mechanism. The next Orchestrator or Architect should pick this up.
"@
        WriteAllText $issuePath $issueContent
        Write-Host "CLANCY issue created: $issueId-clancy-$issueSlug" -ForegroundColor Magenta

        $ackContent = "# Ralphie, please keep in mind`n`n> Yes father (iteration $i) — addressed in result_$("{0:000}" -f $i).md, tracked as issue $issueId`n"
        WriteAllText $clancyPath $ackContent
        Write-Host "CLANCY.md acknowledged: addressed in output, issue $issueId created" -ForegroundColor Magenta
      } else {
        Write-Host "CLANCY.md NOT acknowledged (output didn't include '## CLANCY Response' section — will retry next iteration)" -ForegroundColor Yellow
      }
    } elseif ($clancyText) {
      Write-Host "CLANCY.md NOT acknowledged (copilot produced no output — will retry next iteration)" -ForegroundColor Yellow
    }

    # Archive result
    $resultPath = Join-Path $stateDir ("result_{0:000}.md" -f $i)
    WriteAllText $resultPath ($handoffContent + "`n")
    if ($Verbose) {
      Write-Host "Result archived to: $resultPath" -ForegroundColor Green
    }

    if ($Verbose) {
      Write-Host ""
      if ($planPath) {
        Write-Host "Plan saved to: $planPath" -ForegroundColor Green
      }
    }

    # Execute CMD: lines
    if ($Verbose) { Write-Section "5) Execute work (optional) or print commands" }
    $cmds = Extract-CmdLines $planText
    $cmdCount = @($cmds).Count

    if ($cmdCount -eq 0) {
      if ($Verbose) { Write-Host "No CMD: lines found. The plan likely expects manual edits." -ForegroundColor DarkYellow }
    } else {
      if ($Verbose) {
        Write-Host ("Found {0} command(s):" -f $cmdCount)
        $n = 0
        foreach ($c in $cmds) {
          $n++
          $allowed = IsAllowedCommand $c $AllowedCommandPrefixes
          $mark = if ($allowed) { "[allowed]" } else { "[blocked]" }
          Write-Host ("  {0}. {1} {2}" -f $n, $mark, $c)
        }
      }

      if ($AutoRunAllowed) {
        if ($Verbose) {
          Write-Host ""
          Write-Host "AutoRunAllowed is ON. Executing allowed commands only..." -ForegroundColor Yellow
        }
        foreach ($c in $cmds) {
          if (IsAllowedCommand $c $AllowedCommandPrefixes) {
            try {
              $r = Exec $c -AllowFail
              if ($StopOnFailure -and $r.ExitCode -ne 0) {
                throw "StopOnFailure: command failed: $c"
              }
            } catch {
              Write-Host "Command failed: $c" -ForegroundColor Red
              Write-Host $_.Exception.Message -ForegroundColor Red
              if ($StopOnFailure) { throw }
            }
          } else {
            if ($Verbose) { Write-Verbose "Skipping blocked command: $c" }
          }
        }
      } else {
        if ($Verbose) {
          Write-Host ""
          Write-Host "AutoRunAllowed is OFF. Run the allowed commands manually, then re-run next iteration." -ForegroundColor DarkYellow
        }
      }
    }

    # Summary
    if ($Verbose) { Write-Section "6) Handoff status" }
    
    Write-Host ("="*72) -ForegroundColor Green
    Write-Host ("Iteration {0}/{1} Complete | Role: {2} | Credits: {3:0.##}/{4:0.##} | Model: {5}" -f $i, $maxIterations, $displayRole, $creditsUsed, $creditCap, $displayModel) -ForegroundColor Green
    Write-Host ("="*72) -ForegroundColor Green
    Write-Host ""
    Write-Host "--- Handoff ---" -ForegroundColor Cyan
    Write-Host $handoffContent
    Write-Host ""
    if ($Verbose) {
      if ($planPath) {
        Write-Host "Handoff saved to: $handoffPath" -ForegroundColor Green
        Write-Host "Result saved to: $resultPath" -ForegroundColor Green
        Write-Host "Full output saved to: $planPath" -ForegroundColor Green
      } else {
        Write-Host "Handoff saved to: $handoffPath" -ForegroundColor Green
        Write-Host "Result saved to: $resultPath" -ForegroundColor Green
        Write-Host "Warning: No plan output was saved (Copilot produced no output)" -ForegroundColor Yellow
      }
    }

    if ($Verbose) { Write-Section "7) Show status" }
    $status = Exec "git status --porcelain=v1" -AllowFail
    if ($Verbose) {
      if ($status.StdOut.Trim()) {
        Write-Host "Working tree status:"
        Write-Host $status.StdOut.Trim()
      } else {
        Write-Host "Working tree clean."
      }
    }
  }

  # Return final state for summary
  return @{
    CreditsUsed    = $creditsUsed
    CreditCap      = $creditCap
    MaxIterations  = $maxIterations
    StateDir       = $stateDir
    CreditsPath    = $creditsPath
  }
}
