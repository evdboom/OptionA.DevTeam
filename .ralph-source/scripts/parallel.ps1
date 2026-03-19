# ---- parallel.ps1 ----
# Parallel agent infrastructure: git worktrees, background jobs, merging.
# Dot-sourced by ralph.ps1. Depends on: utils.ps1 (Exec).

function Get-WorktreeBase() {
  return Join-Path ([System.IO.Path]::GetTempPath()) "ralph-worktrees"
}

function Clear-StaleAgents() {
  # Kill orphaned copilot processes from previous parallel runs
  $base = Get-WorktreeBase
  $staleProcs = @(Get-CimInstance Win32_Process -Filter "Name='copilot.exe'" -ErrorAction SilentlyContinue | Where-Object {
    $_.CommandLine -and $_.CommandLine -match [regex]::Escape($base)
  })
  foreach ($sp in $staleProcs) {
    Write-Host "  Killing stale copilot process (PID $($sp.ProcessId)) from previous parallel run" -ForegroundColor Yellow
    Stop-Process -Id $sp.ProcessId -Force -ErrorAction SilentlyContinue
  }

  # Remove leftover worktree directories
  if (Test-Path $base) {
    $dirs = @(Get-ChildItem $base -Directory -ErrorAction SilentlyContinue)
    if ($dirs.Count -gt 0) {
      Write-Host "  Cleaning $($dirs.Count) stale worktree(s)..." -ForegroundColor Yellow
      foreach ($d in $dirs) {
        $branchName = "ralph-$($d.Name)"
        Exec "git worktree remove `"$($d.FullName)`" --force" -AllowFail | Out-Null
        Remove-Item -Path $d.FullName -Recurse -Force -ErrorAction SilentlyContinue
        Exec "git branch -D $branchName" -AllowFail | Out-Null
      }
      Exec "git worktree prune" -AllowFail | Out-Null
    }
  }
}

function New-AgentWorktree([string]$agentId) {
  $base = Get-WorktreeBase
  if (-not (Test-Path $base)) { New-Item -ItemType Directory -Path $base | Out-Null }
  $worktreeDir = Join-Path $base $agentId

  # Clean up if exists from previous failed run
  if (Test-Path $worktreeDir) {
    Remove-AgentWorktree $agentId
  }

  $branchName = "ralph-$agentId"
  # Delete leftover branch if any
  Exec "git branch -D $branchName" -AllowFail | Out-Null

  $result = Exec "git worktree add `"$worktreeDir`" -b $branchName HEAD" -AllowFail
  if ($result.ExitCode -ne 0) {
    throw "Failed to create worktree for ${agentId}: $($result.StdErr)"
  }

  Write-Verbose "Created worktree for $agentId at $worktreeDir"
  return $worktreeDir
}

function Remove-AgentWorktree([string]$agentId) {
  $base = Get-WorktreeBase
  $worktreeDir = Join-Path $base $agentId
  $branchName = "ralph-$agentId"

  if (Test-Path $worktreeDir) {
    Exec "git worktree remove `"$worktreeDir`" --force" -AllowFail | Out-Null
  }
  Exec "git worktree prune" -AllowFail | Out-Null
  # If git worktree remove failed, force-delete the directory
  if (Test-Path $worktreeDir) {
    Remove-Item -Path $worktreeDir -Recurse -Force -ErrorAction SilentlyContinue
    Exec "git worktree prune" -AllowFail | Out-Null
  }
  Exec "git branch -D $branchName" -AllowFail | Out-Null
  Write-Verbose "Removed worktree and branch for $agentId"
}

function Start-AgentJob {
  param(
    [string]$agentId,
    [string]$worktreePath,
    [string]$shortPrompt,
    [string]$model,
    [string[]]$extraArgs
  )

  $jobScript = {
    param($workdir, $prompt, $mdl, $eArgs)

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = "copilot"
    $psi.WorkingDirectory = $workdir
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow  = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError  = $true
    $psi.RedirectStandardInput  = $true

    if ($eArgs) { foreach ($a in $eArgs) { [void]$psi.ArgumentList.Add($a) } }
    # Prevent agent from hanging in non-interactive background job
    [void]$psi.ArgumentList.Add("--no-ask-user")
    [void]$psi.ArgumentList.Add("--no-auto-update")
    if ($mdl) {
      [void]$psi.ArgumentList.Add("--model")
      [void]$psi.ArgumentList.Add($mdl)
    }
    [void]$psi.ArgumentList.Add("-p")
    [void]$psi.ArgumentList.Add($prompt)

    $proc = New-Object System.Diagnostics.Process
    $proc.StartInfo = $psi
    [void]$proc.Start()
    $proc.StandardInput.Close()  # Close stdin so copilot can't block on input

    # Read stdout and stderr asynchronously to avoid deadlock
    $stdoutTask = $proc.StandardOutput.ReadToEndAsync()
    $stderrTask = $proc.StandardError.ReadToEndAsync()
    $proc.WaitForExit()
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()

    [pscustomobject]@{
      ExitCode = $proc.ExitCode
      StdOut   = $stdout
      StdErr   = $stderr
    }
  }

  return Start-Job -Name $agentId -ScriptBlock $jobScript -ArgumentList $worktreePath, $shortPrompt, $model, $extraArgs
}

function Merge-AgentResult {
  param(
    [string]$agentId,
    [string]$model,
    [string[]]$extraArgs
  )
  $base = Get-WorktreeBase
  $worktreeDir = Join-Path $base $agentId
  $branchName = "ralph-$agentId"

  # Commit any uncommitted changes in the worktree via -C flag
  # First restore AGENTS.md from MAIN branch (not worktree HEAD, which may
  # already contain the sub-agent version if the agent committed it).
  $mainBranch = (Exec "git rev-parse --abbrev-ref HEAD" -AllowFail).StdOut.Trim()
  Exec "git -C `"$worktreeDir`" checkout $mainBranch -- .ralph/.ralph-source/AGENTS.md" -AllowFail | Out-Null
  Exec "git -C `"$worktreeDir`" add -A" -AllowFail | Out-Null
  $wStatus = Exec "git -C `"$worktreeDir`" status --porcelain" -AllowFail
  if ($wStatus.StdOut.Trim()) {
    Exec "git -C `"$worktreeDir`" commit -m `"Agent $agentId completed work`"" -AllowFail | Out-Null
  }

  # Merge agent branch into current branch
  $mergeResult = Exec "git merge $branchName --no-edit" -AllowFail
  if ($mergeResult.ExitCode -ne 0) {
    Write-Host "  Merge conflict from agent $agentId — spawning conflict resolver..." -ForegroundColor Yellow
    $resolved = Resolve-MergeConflict -agentId $agentId -model $model -extraArgs $extraArgs
    if ($resolved) {
      Write-Host "  Conflict resolved by resolver agent for $agentId" -ForegroundColor Green
      return $true
    }
    Write-Host "  Resolver failed for $agentId — aborting merge, branch ralph-$agentId preserved" -ForegroundColor Red
    Exec "git merge --abort" -AllowFail | Out-Null
    return $false
  }
  return $true
}

function Resolve-MergeConflict {
  param(
    [string]$agentId,
    [string]$model,
    [string[]]$extraArgs
  )
  # We are mid-merge with conflicts in the working tree. Spawn a copilot
  # agent whose sole job is to resolve conflicts, stage, and commit.
  $conflictFiles = (Exec "git diff --name-only --diff-filter=U" -AllowFail).StdOut.Trim()
  if (-not $conflictFiles) {
    # No actual conflicts (maybe already resolved?)
    Exec "git commit --no-edit" -AllowFail | Out-Null
    return $true
  }

  $superpowerPath = ".ralph/.ralph-source/superpowers/resolve-conflict.md"

  $resolverPrompt = @"
You are a CONFLICT RESOLVER agent. Your ONLY job is to resolve the git merge conflicts in this repo.

Read the superpower instructions first: $superpowerPath

Conflicted files:
$conflictFiles

Steps:
1. Read $superpowerPath for full instructions
2. Read each conflicted file listed above
3. Resolve every conflict by combining both sides intelligently
4. Stage each resolved file with: git add <file>
5. Verify no markers remain: git diff --check
6. Complete the merge: git commit --no-edit

Do NOT modify any file that is not in the conflict list.
Do NOT create issues, plan future work, or output NEXT_ROLE/PIPELINE/PARALLEL.
"@

  Write-Host "  Resolver: $($conflictFiles -split "`n" | Measure-Object | Select-Object -ExpandProperty Count) file(s) in conflict" -ForegroundColor DarkCyan

  $resolverResult = Invoke-Copilot `
    -prompt $resolverPrompt `
    -workdir (Get-Location).Path `
    -extraArgs $extraArgs `
    -model $model `
    -label "Conflict resolver ($agentId)" `
    -quietProgress

  # Check if the merge was completed (no conflicts remain)
  $remaining = (Exec "git diff --name-only --diff-filter=U" -AllowFail).StdOut.Trim()
  if ($remaining) {
    Write-Host "  Resolver left unresolved conflicts: $remaining" -ForegroundColor Red
    return $false
  }

  # If the resolver resolved files but didn't commit, do it now
  $mergeHead = Test-Path (Join-Path (Exec "git rev-parse --git-dir" -AllowFail).StdOut.Trim() "MERGE_HEAD")
  if ($mergeHead) {
    Exec "git commit --no-edit" -AllowFail | Out-Null
  }

  return $true
}

function Wait-AgentJobs {
  param(
    [System.Management.Automation.Job[]]$jobs,
    [int]$timeoutSeconds = 2400,
    [string]$label = 'agents',
    [hashtable]$agentLabels = @{}   # jobName -> display label (falls back to $label)
  )

  $startTime = Get-Date
  $agentStart = @{}
  $agentDone = @{}       # jobName -> elapsed seconds when completed
  $agentPrinted = @{}    # jobName -> $true once the done/fail line is printed
  foreach ($j in $jobs) { $agentStart[$j.Name] = Get-Date }

  $frames = @('⠋','⠙','⠹','⠸','⠼','⠴','⠦','⠧','⠇','⠏')
  $spin = 0

  while ($true) {
    $running = @($jobs | Where-Object { $_.State -eq 'Running' })
    $doneCount = $jobs.Count - $running.Count

    # Detect newly completed agents and print a permanent line for each
    foreach ($j in $jobs) {
      if ($j.State -ne 'Running' -and -not $agentDone.ContainsKey($j.Name)) {
        $agentDone[$j.Name] = [int]((Get-Date) - $agentStart[$j.Name]).TotalSeconds
      }
      if ($agentDone.ContainsKey($j.Name) -and -not $agentPrinted.ContainsKey($j.Name)) {
        $agentPrinted[$j.Name] = $true
        $lbl = if ($agentLabels.ContainsKey($j.Name)) { $agentLabels[$j.Name] } else { $label }
        $doneElapsed = $agentDone[$j.Name]
        # Clear the spinner line first
        [Console]::Write("`r" + (" " * ([Math]::Max(([Console]::BufferWidth - 1), 80))) + "`r")
        if ($j.State -eq 'Completed') {
          Write-Host ("  {0} {1} done after {2}s" -f ([char]0x2713), $lbl, $doneElapsed) -ForegroundColor Green
        } else {
          Write-Host ("  {0} {1} failed after {2}s" -f ([char]0x2717), $lbl, $doneElapsed) -ForegroundColor Red
        }
      }
    }

    if ($running.Count -eq 0) { break }

    # Timeout check
    $elapsed = [int]((Get-Date) - $startTime).TotalSeconds
    if ($timeoutSeconds -gt 0 -and $elapsed -ge $timeoutSeconds) {
      [Console]::Write("`r" + (" " * ([Math]::Max(([Console]::BufferWidth - 1), 80))) + "`r")
      Write-Host "Parallel agent timeout after ${timeoutSeconds}s. Stopping remaining agents..." -ForegroundColor Yellow
      $running | Stop-Job
      break
    }

    $f = $frames[$spin % $frames.Count]
    $spin++

    # Build a single-line summary of all running agents
    $runLabels = @()
    foreach ($j in $running) {
      $lbl = if ($agentLabels.ContainsKey($j.Name)) { $agentLabels[$j.Name] } else { $label }
      $agentElapsed = [int]((Get-Date) - $agentStart[$j.Name]).TotalSeconds
      $runLabels += "{0} {1}s" -f $lbl, $agentElapsed
    }
    $statusLine = "  {0} {1} running ({2}/{3}): {4}" -f $f, $running.Count, $doneCount, $jobs.Count, ($runLabels -join ' | ')
    # Truncate to terminal width to avoid wrapping (use [Console]::Write for reliable \r)
    $termWidth = try { [Console]::BufferWidth } catch { 120 }
    if ($termWidth -lt 40) { $termWidth = 120 }
    $maxLen = $termWidth - 1
    if ($statusLine.Length -gt $maxLen) { $statusLine = $statusLine.Substring(0, $maxLen - 3) + '...' }
    [Console]::Write("`r{0}" -f $statusLine.PadRight($maxLen))

    Start-Sleep -Milliseconds 250
  }

  # Clear spinner line and show final summary
  [Console]::Write("`r" + (" " * ([Math]::Max(([Console]::BufferWidth - 1), 80))) + "`r")
  $totalElapsed = [int]((Get-Date) - $startTime).TotalSeconds
  $completedCount = @($jobs | Where-Object { $_.State -eq 'Completed' }).Count
  Write-Host ("  {0} {1}/{2} agents completed ({3}, {4}s)" -f ([char]0x2713), $completedCount, $jobs.Count, $label, $totalElapsed) -ForegroundColor Green

  $results = @{}
  foreach ($job in $jobs) {
    try {
      $result = Receive-Job -Job $job -ErrorAction Stop
      $results[$job.Name] = $result
    } catch {
      $results[$job.Name] = [pscustomobject]@{ ExitCode = 1; StdOut = ""; StdErr = $_.Exception.Message }
    }
    Remove-Job -Job $job -Force -ErrorAction SilentlyContinue
  }

  return $results
}
