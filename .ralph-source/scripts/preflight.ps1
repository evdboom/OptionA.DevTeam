# ---- preflight.ps1 ----
# Pre-loop setup: directory validation, fresh/continue state, credits, iteration tracking.
# Dot-sourced by ralph.ps1. Depends on: utils.ps1, models.ps1.

function Invoke-Preflight {
  param(
    [string]$RepoPath,
    [switch]$Continue,
    [string]$Credits,
    [string]$Iterations,
    [bool]$Verbose
  )

  Write-Section "Preflight"
  EnsureGitRepo

  # Clean up stale parallel agent processes and worktrees from previous runs
  Clear-StaleAgents

  # Directory structure
  $ralphDir   = Join-Path $RepoPath ".ralph"
  $sourceDir  = Join-Path $ralphDir ".ralph-source"
  $projectDir = Join-Path $ralphDir ".ralph-project"
  $stateDir   = Join-Path $ralphDir ".ralph-state"

  $agentsPath = Join-Path $sourceDir "AGENTS.md"
  $goalPath   = Join-Path $projectDir "GOAL.md"

  Require-File $agentsPath
  Require-File $goalPath

  $acceptPath   = Join-Path $projectDir "ACCEPTANCE.md"
  $rolesDir     = Join-Path $sourceDir "roles"
  $issuesDir    = Join-Path $projectDir "issues"
  $decisionsDir = Join-Path $projectDir "decisions"

  # Ensure directories exist
  foreach ($d in @($stateDir, $issuesDir, $decisionsDir)) {
    if (-not (Test-Path $d)) { New-Item -ItemType Directory -Path $d | Out-Null }
  }

  # If not continuing, clear state and acceptance
  if (-not $Continue) {
    Write-Host "Starting fresh iteration cycle (no -Continue flag)..." -ForegroundColor Yellow
    
    if (Test-Path $stateDir) {
      Get-ChildItem -Path $stateDir -File | Remove-Item -Force
      # Remove only completed/done issues; preserve open/in-progress/blocked ones
      $issueFiles = @(Get-ChildItem -Path $issuesDir -File | Where-Object { $_.Name -notmatch '^_' -and $_.Name -ne 'README.md' })
      $survivingIssues = @()
      foreach ($f in $issueFiles) {
        $statusLine = Get-Content $f.FullName -TotalCount 5 | Where-Object { $_ -match '^\s*-\s*\*\*Status:\*\*\s*(.+)' }
        $status = if ($statusLine -and $Matches[1]) { $Matches[1].Trim().ToLower() } else { 'unknown' }
        if ($status -in @('done', 'completed')) {
          Remove-Item $f.FullName -Force
          Write-Verbose "Removed completed issue: $($f.Name)"
        } else {
          $survivingIssues += $f
        }
      }
      # Rebuild issue index from surviving issues
      $indexPath = Join-Path $issuesDir "_index.md"
      $indexHeader = "# Issue Index`n`nTrack all issues and their current status. Updated by roles as work progresses.`n`n| ID | Title | Status | Assigned | Priority | Iteration |`n|----|-------|--------|----------|----------|-----------|`n"
      if ($survivingIssues.Count -eq 0) {
        WriteAllText $indexPath ($indexHeader + "| - | (no issues yet) | - | - | - | - |`n")
      } else {
        $rows = foreach ($f in $survivingIssues | Sort-Object Name) {
          $lines = Get-Content $f.FullName -TotalCount 10
          $id = if ($f.Name -match '^(\d+)') { $Matches[1] } else { '?' }
          $title = ($lines | Where-Object { $_ -match '^#\s+Issue\s+\d+:\s*(.+)' } | ForEach-Object { $Matches[1].Trim() }) -join ''
          if (-not $title) { $title = $f.BaseName }
          $st = ($lines | Where-Object { $_ -match '^\s*-\s*\*\*Status:\*\*\s*(.+)' } | ForEach-Object { $Matches[1].Trim() }) -join ''
          $as = ($lines | Where-Object { $_ -match '^\s*-\s*\*\*Assigned:\*\*\s*(.+)' } | ForEach-Object { $Matches[1].Trim() }) -join ''
          $pr = ($lines | Where-Object { $_ -match '^\s*-\s*\*\*Priority:\*\*\s*(.+)' } | ForEach-Object { $Matches[1].Trim() }) -join ''
          $it = ($lines | Where-Object { $_ -match '^\s*-\s*\*\*Created:\*\*\s*iteration\s*(.+)' } | ForEach-Object { $Matches[1].Trim() }) -join ''
          "| $id | $title | $st | $as | $pr | $it |"
        }
        WriteAllText $indexPath ($indexHeader + ($rows -join "`n") + "`n")
      }
      Write-Verbose "Cleared .ralph run state (preserved open/in-progress issues)"
    }
    
    # Clear acceptance.md
    if (Test-Path $acceptPath) {
      WriteAllText $acceptPath ""
      Write-Verbose "Cleared acceptance.md"
    }
    
    # Commit cleanup of old loop files if there are changes
    try {
      $gitStatus = git status --porcelain .ralph/.ralph-project .ralph/.ralph-state 2>$null
      if ($gitStatus) {
        git add .ralph/.ralph-project .ralph/.ralph-state 2>$null | Out-Null
        git commit -m "cleanup old loop" 2>$null | Out-Null
        Write-Verbose "Committed cleanup of old loop files"
      }
    } catch {
      Write-Verbose "Could not commit cleanup (no git changes or not in repo)"
    }
  } else {
    Write-Host "Continuing previous iteration cycle..." -ForegroundColor Cyan
  }

  $handoffPath  = Join-Path $stateDir "handoff.md"
  $historyPath  = Join-Path $stateDir "history.log"
  $creditsPath  = Join-Path $stateDir "credits.json"

  if (-not (Test-Path $handoffPath)) {
    WriteAllText $handoffPath "# Handoff`n- (first run)`n"
  }

  if (-not (Test-Path $historyPath)) {
    WriteAllText $historyPath ""
  }

  # Initialize or load state tracking (credits and iterations)
  $creditsUsed = 0
  $startIteration = 1
  $maxIterations = 0
  
  if (Test-Path $creditsPath) {
    $creditsData = Get-Content $creditsPath -Raw | ConvertFrom-Json
    $creditsUsed = $creditsData.used
    
    $creditCap = Parse-ValueWithAddition $Credits $creditsData.cap
    
    if ($creditsData.PSObject.Properties.Name -contains 'currentIteration') {
      $lastIteration = $creditsData.currentIteration
      $startIteration = $lastIteration + 1
      
      if ($creditsData.PSObject.Properties.Name -contains 'maxIterations') {
        $maxIterations = Parse-ValueWithAddition $Iterations $creditsData.maxIterations
      } else {
        $maxIterations = Parse-ValueWithAddition $Iterations $lastIteration
      }
    } else {
      $maxIterations = Parse-ValueWithAddition $Iterations 0
    }
    
    $creditsData.cap = $creditCap
    if (-not ($creditsData.PSObject.Properties.Name -contains 'maxIterations')) {
      $creditsData | Add-Member -NotePropertyName 'maxIterations' -NotePropertyValue $maxIterations -Force
    } else {
      $creditsData.maxIterations = $maxIterations
    }
    
    if (-not ($creditsData.PSObject.Properties.Name -contains 'currentIteration')) {
      $creditsData | Add-Member -NotePropertyName 'currentIteration' -NotePropertyValue 0 -Force
    }
    
    $creditsData | ConvertTo-Json -Depth 10 | Set-Content $creditsPath
  } else {
    $creditCap = Parse-ValueWithAddition $Credits 0
    $maxIterations = Parse-ValueWithAddition $Iterations 0
    
    $creditsData = @{ 
      used = 0
      cap = $creditCap
      currentIteration = 0
      maxIterations = $maxIterations
      history = @()
    }
    $creditsData | ConvertTo-Json -Depth 10 | Set-Content $creditsPath
  }

  if ($Continue) {
    Write-Host "Continuing from iteration $startIteration (max: $maxIterations, credits: $creditCap)..." -ForegroundColor Cyan
  } else {
    Write-Host "Starting fresh iteration cycle (iterations: $maxIterations, credits: $creditCap)..." -ForegroundColor Cyan
  }

  # Return all state as a hashtable for the main loop
  return @{
    RalphDir      = $ralphDir
    SourceDir     = $sourceDir
    ProjectDir    = $projectDir
    StateDir      = $stateDir
    AgentsPath    = $agentsPath
    GoalPath      = $goalPath
    AcceptPath    = $acceptPath
    RolesDir      = $rolesDir
    IssuesDir     = $issuesDir
    DecisionsDir  = $decisionsDir
    HandoffPath   = $handoffPath
    HistoryPath   = $historyPath
    CreditsPath   = $creditsPath
    CreditsUsed   = $creditsUsed
    CreditCap     = $creditCap
    StartIteration = $startIteration
    MaxIterations = $maxIterations
  }
}
