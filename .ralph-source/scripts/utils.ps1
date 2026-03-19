# ---- utils.ps1 ----
# Low-level utilities: file I/O, git helpers, display, command parsing.
# Dot-sourced by ralph.ps1. No dependencies on other modules.

function Parse-ValueWithAddition([string]$value, [int]$currentValue) {
  if ($value -match '^\+(\d+)$') {
    return $currentValue + [int]$Matches[1]
  }
  return [int]$value
}

function Write-Section([string]$title) {
  Write-Host ""
  Write-Host "=== $title ===" -ForegroundColor Cyan
}

function Require-File([string]$path) {
  if (-not (Test-Path $path)) {
    throw "Required file missing: $path"
  }
}

function Exec([string]$cmd, [switch]$AllowFail) {
  Write-Verbose "RUN ($Script:WorkDir): $cmd"

  $pinfo = New-Object System.Diagnostics.ProcessStartInfo
  $pinfo.FileName = "cmd.exe"
  $pinfo.Arguments = "/c $cmd"
  $pinfo.WorkingDirectory = $Script:WorkDir
  $pinfo.RedirectStandardOutput = $true
  $pinfo.RedirectStandardError = $true
  $pinfo.UseShellExecute = $false
  $pinfo.CreateNoWindow = $true

  $p = New-Object System.Diagnostics.Process
  $p.StartInfo = $pinfo
  [void]$p.Start()
  $stdout = $p.StandardOutput.ReadToEnd()
  $stderr = $p.StandardError.ReadToEnd()
  $p.WaitForExit()

  if ($stdout) { Write-Verbose $stdout.TrimEnd() }
  if ($stderr) { Write-Verbose $stderr.TrimEnd() }

  if (-not $AllowFail -and $p.ExitCode -ne 0) {
    throw "Command failed (exit $($p.ExitCode)): $cmd`n$stderr"
  }

  return [pscustomobject]@{ ExitCode = $p.ExitCode; StdOut = $stdout; StdErr = $stderr }
}

function GitDiffSummary() {
  $stat = Exec "git diff --stat" -AllowFail
  $diff = Exec "git diff" -AllowFail
  return [pscustomobject]@{
    Stat = $stat.StdOut.Trim()
    Diff = $diff.StdOut.Trim()
  }
}

function EnsureGitRepo() {
  Exec "git rev-parse --is-inside-work-tree" | Out-Null
}

function StageAll([string]$msg) {
  Write-Verbose $msg
  Exec "git add -A" | Out-Null
}

function ReadAllText([string]$path) {
  if (Test-Path $path) { return [IO.File]::ReadAllText($path) }
  return ""
}

function WriteAllText([string]$path, [string]$content) {
  $dir = Split-Path -Parent $path
  if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
  [IO.File]::WriteAllText($path, $content, [Text.UTF8Encoding]::new($false))
}

function Extract-CmdLines([string]$text) {
  if ([string]::IsNullOrWhiteSpace($text)) { return @() }

  $lines = $text -split "`r?`n"
  $cmds = New-Object System.Collections.Generic.List[string]

  foreach ($l in $lines) {
    if ($l -match '^\s*CMD:\s*(.+?)\s*$') {
      $cmds.Add($Matches[1])
    }
  }

  return @($cmds.ToArray())
}

function IsAllowedCommand([string]$cmd, [string[]]$prefixes) {
  foreach ($p in $prefixes) {
    if ($cmd.StartsWith($p, [System.StringComparison]::OrdinalIgnoreCase)) {
      return $true
    }
  }
  return $false
}

function Print-IterationHeader([int]$i, [int]$n, $creditsUsed, $creditCap, [string]$model, [string]$role, [string]$pipeline, [switch]$quiet) {
  $roleDisplay = if ($role) { $role } else { "(auto)" }
  $pipelineDisplay = if ($pipeline) { " | Pipeline: $pipeline" } else { "" }
  if ($quiet) {
    Write-Host ("Iteration {0}/{1} | Role: {2} | Credits: {3:0.##}/{4:0.##} | Model: {5}{6}" -f $i, $n, $roleDisplay, $creditsUsed, $creditCap, $model, $pipelineDisplay) -ForegroundColor Cyan
  } else {
    Write-Host ""
    Write-Host ("#"*72) -ForegroundColor DarkGray
    Write-Host ("Iteration {0}/{1} | Role: {2} | Credits: {3:0.##}/{4:0.##} | Model: {5}" -f $i, $n, $roleDisplay, $creditsUsed, $creditCap, $model) -ForegroundColor Yellow
    if ($pipelineDisplay) { Write-Host ("Pipeline: {0}" -f $pipeline) -ForegroundColor Magenta }
    Write-Host ("#"*72) -ForegroundColor DarkGray
  }
}
