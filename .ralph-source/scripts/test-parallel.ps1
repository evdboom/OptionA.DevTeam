<#
.SYNOPSIS
  Test parallel agent job infrastructure with 2 mock agents.
  Validates that Start-AgentJob + Wait-AgentJobs work without deadlocking.
.DESCRIPTION
  Uses a mock command (powershell -c) instead of copilot to test:
  1. Jobs start and complete without deadlock
  2. stdout and stderr are captured correctly
  3. Wait-AgentJobs returns results for all agents
  4. A large-output job doesn't deadlock (the old synchronous ReadToEnd bug)
#>

param([switch]$Verbose)
$ErrorActionPreference = 'Stop'

# --- Extract just the functions we need from ralph.ps1 ---

# Minimal Start-AgentJob that accepts a custom command (not hardcoded to copilot)
function Start-TestAgentJob {
  param(
    [string]$agentId,
    [string]$command,   # e.g. "powershell"
    [string[]]$arguments
  )

  $jobScript = {
    param($cmd, $args_)

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $cmd
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow  = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError  = $true

    if ($args_) { foreach ($a in $args_) { [void]$psi.ArgumentList.Add($a) } }

    $proc = New-Object System.Diagnostics.Process
    $proc.StartInfo = $psi
    [void]$proc.Start()

    # Async reading — the fix we're testing
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

  return Start-Job -Name $agentId -ScriptBlock $jobScript -ArgumentList $command, $arguments
}

# Wait-AgentJobs copied from ralph.ps1
function Wait-AgentJobs([System.Management.Automation.Job[]]$jobs, [int]$timeoutSeconds = 60) {
  $startTime = Get-Date
  $frames = @('|','/','-','\')
  $spin = 0

  while ($true) {
    $running = @($jobs | Where-Object { $_.State -eq 'Running' })
    $doneCount = @($jobs | Where-Object { $_.State -ne 'Running' }).Count

    if ($running.Count -eq 0) { break }

    $elapsed = [int]((Get-Date) - $startTime).TotalSeconds
    if ($timeoutSeconds -gt 0 -and $elapsed -ge $timeoutSeconds) {
      Write-Host "`nTimeout after ${timeoutSeconds}s. Stopping remaining agents..." -ForegroundColor Yellow
      $running | Stop-Job
      break
    }

    $f = $frames[$spin % $frames.Count]
    $spin++
    Write-Host -NoNewline ("`r  Agents: {0} running, {1}/{2} done {3} ({4}s)   " -f $running.Count, $doneCount, $jobs.Count, $f, $elapsed)
    Start-Sleep -Milliseconds 200
  }

  Write-Host -NoNewline ("`r" + (" " * 60) + "`r")

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

# --- Tests ---

$passed = 0
$failed = 0

function Assert-Equal($name, $actual, $expected) {
  if ($actual -eq $expected) {
    Write-Host "  PASS: $name" -ForegroundColor Green
    $script:passed++
  } else {
    Write-Host "  FAIL: $name — expected '$expected', got '$actual'" -ForegroundColor Red
    $script:failed++
  }
}

function Assert-Contains($name, $actual, $substring) {
  if ($actual -and $actual.Contains($substring)) {
    Write-Host "  PASS: $name" -ForegroundColor Green
    $script:passed++
  } else {
    Write-Host "  FAIL: $name — expected to contain '$substring', got '$actual'" -ForegroundColor Red
    $script:failed++
  }
}

# ====== Test 1: Two simple parallel jobs ======
Write-Host "`n=== Test 1: Two simple parallel jobs ===" -ForegroundColor Cyan

$job1 = Start-TestAgentJob -agentId "test-g1" -command "powershell" -arguments @("-NoProfile", "-Command", "Write-Output 'hello from agent 1'; Write-Error 'warn1'; exit 0")
$job2 = Start-TestAgentJob -agentId "test-g2" -command "powershell" -arguments @("-NoProfile", "-Command", "Write-Output 'hello from agent 2'; exit 0")

$results = Wait-AgentJobs @($job1, $job2) -timeoutSeconds 30

Assert-Equal "Agent count" $results.Count 2
Assert-Equal "Agent 1 exit code" $results["test-g1"].ExitCode 0
Assert-Contains "Agent 1 stdout" $results["test-g1"].StdOut "hello from agent 1"
Assert-Contains "Agent 1 stderr" $results["test-g1"].StdErr "warn1"
Assert-Equal "Agent 2 exit code" $results["test-g2"].ExitCode 0
Assert-Contains "Agent 2 stdout" $results["test-g2"].StdOut "hello from agent 2"

# ====== Test 2: Large stdout (deadlock repro) ======
Write-Host "`n=== Test 2: Large stdout + stderr (deadlock scenario) ===" -ForegroundColor Cyan
# This would deadlock with the old synchronous ReadToEnd pattern because
# stdout buffer fills while we're waiting to read it, while stderr also has data

$bigOutputCmd = '$out = "x" * 80; for ($i = 0; $i -lt 500; $i++) { Write-Output $out; Write-Error $out }; exit 0'

$job3 = Start-TestAgentJob -agentId "test-big" -command "powershell" -arguments @("-NoProfile", "-Command", $bigOutputCmd)

$results2 = Wait-AgentJobs @($job3) -timeoutSeconds 30

Assert-Equal "Big output exit code" $results2["test-big"].ExitCode 0
# 500 lines * 81 chars (80 + newline) = ~40k chars each stream
$outLen = $results2["test-big"].StdOut.Length
$errLen = $results2["test-big"].StdErr.Length
if ($outLen -gt 30000 -and $errLen -gt 30000) {
  Write-Host "  PASS: Large output captured (stdout: $outLen chars, stderr: $errLen chars)" -ForegroundColor Green
  $passed++
} else {
  Write-Host "  FAIL: Output too small (stdout: $outLen, stderr: $errLen) — possible truncation" -ForegroundColor Red
  $failed++
}

# ====== Test 3: Failed job (non-zero exit) ======
Write-Host "`n=== Test 3: Failed job (non-zero exit) ===" -ForegroundColor Cyan

$job4 = Start-TestAgentJob -agentId "test-fail" -command "powershell" -arguments @("-NoProfile", "-Command", "Write-Error 'something broke'; exit 1")

$results3 = Wait-AgentJobs @($job4) -timeoutSeconds 15

Assert-Equal "Failed job exit code" $results3["test-fail"].ExitCode 1
Assert-Contains "Failed job stderr" $results3["test-fail"].StdErr "something broke"

# ====== Summary ======
Write-Host "`n=== Summary ===" -ForegroundColor Cyan
Write-Host "Passed: $passed / $($passed + $failed)" -ForegroundColor $(if ($failed -eq 0) { 'Green' } else { 'Yellow' })
if ($failed -gt 0) {
  Write-Host "FAILED: $failed test(s)" -ForegroundColor Red
  exit 1
}
Write-Host "All tests passed!" -ForegroundColor Green
exit 0
