<#
.SYNOPSIS
  Ralph Wiggum loop for agentic iteration using Copilot CLI.

.REQUIRES
  - Git repo
  - GitHub CLI with Copilot enabled (gh copilot ...)
  - AGENTS.md in repo root
  - GOAL.md in repo root

.EXAMPLE
  pwsh .\.ralph\ralph.ps1 -Iterations 40 -Verbose

.EXAMPLE
  pwsh .\.ralph\ralph.ps1 -Iterations 80 -AutoRunAllowed -Verbose

.EXAMPLE
  pwsh .\.ralph\ralph.ps1 -Continue -Credits 40 -Verbose

.EXAMPLE
  pwsh .\.ralph\ralph.ps1 -Iterations 50 -Credits 100

.EXAMPLE
  pwsh .\.ralph\ralph.ps1 -Continue -Iterations +10 -Credits +20
#>

[CmdletBinding()]
param(
  # Initialize a new ralph project in the current directory
  [switch]$Init,

  [string]$Iterations = "25",

  # Where your repo is (defaults to repo root, i.e. parent of .ralph/)
  [string]$RepoPath = (Split-Path $PSScriptRoot -Parent),

  # Copilot invocation. Adjust if your setup differs.
  [string]$CopilotCommand = "copilot -p",

  # If set, we will parse CMD: lines from the plan and execute allowed ones.
  [switch]$AutoRunAllowed,

  # Commands allowlist prefix match. Extend as you like.
  [string[]]$AllowedCommandPrefixes = @(
    "git ",
    "dotnet ",
    "npm ",
    "pnpm ",
    "yarn ",
    "node ",
    "python ",
    "pytest ",
    "pwsh ",
    "powershell ",
    "cmd /c ",
    "bash ",
    "sh "
  ),

  # Stop if build/test fails (only applies when AutoRunAllowed runs checks)
  [switch]$StopOnFailure = $false,

  # Show Copilot's tool-by-tool output live (recommended)
  [switch]$StreamCopilotOutput = $true,

  # Spinner while Copilot is running (helps even if streaming is off)
  [switch]$ShowSpinner = $true,

  # Extra args to copilot CLI (e.g. --allow-all to bypass safety checks, use with caution!)
  [string[]]$CopilotExtraArgs = @("--allow-all"),

  # Continue previous iteration cycle (if not set, .ralph folder and acceptance.md will be cleared)
  [switch]$Continue,

  # Maximum credits allowed (default 25). Costs match MODELS.json directly.
  # Use +N syntax to add to existing credits when using -Continue
  [string]$Credits = "25",

  # Maximum concurrent agents (default 1 = sequential). Set higher to run parallel agents on independent issues.
  [int]$MaxConcurrency = 1
)

# ---- Globals ----
$Verbose = [bool]($PSCmdlet.MyInvocation.BoundParameters.ContainsKey('Verbose') -and $PSCmdlet.MyInvocation.BoundParameters['Verbose'])
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$OutputEncoding = [System.Text.Encoding]::UTF8
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

# ---- Resolve source directory ----
# Entrypoint lives in .ralph/  →  scripts are in .ralph/.ralph-source/scripts/
$sourceDir = Join-Path $PSScriptRoot ".ralph-source"
$scriptsDir = Join-Path $sourceDir "scripts"

# ---- Dot-source modules (order matters: dependencies first) ----
. (Join-Path $scriptsDir "utils.ps1")
. (Join-Path $scriptsDir "copilot.ps1")
. (Join-Path $scriptsDir "models.ps1")
. (Join-Path $scriptsDir "parallel.ps1")
. (Join-Path $scriptsDir "init.ps1")
. (Join-Path $scriptsDir "preflight.ps1")
. (Join-Path $scriptsDir "iteration.ps1")

# ---- Dispatch ----
if ($Init) {
  Invoke-RalphInit $RepoPath
  exit 0
}

# Set the global WorkDir used by utils/Exec
$Script:WorkDir = $RepoPath

# ---- Prevent machine sleep/lock while Ralph is running ----
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class SleepGuard {
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetThreadExecutionState(uint esFlags);
    private const uint ES_CONTINUOUS       = 0x80000000;
    private const uint ES_SYSTEM_REQUIRED  = 0x00000001;
    private const uint ES_DISPLAY_REQUIRED = 0x00000002;
    public static void Prevent() {
        SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED);
    }
    public static void Allow() {
        SetThreadExecutionState(ES_CONTINUOUS);
    }
}
"@
[SleepGuard]::Prevent()

Push-Location $RepoPath
try {
  $state = Invoke-Preflight `
    -RepoPath $RepoPath `
    -Continue:$Continue `
    -Credits $Credits `
    -Iterations $Iterations `
    -Verbose:$Verbose

  $result = Invoke-IterationLoop `
    -State $state `
    -RepoPath $RepoPath `
    -AutoRunAllowed:$AutoRunAllowed `
    -AllowedCommandPrefixes $AllowedCommandPrefixes `
    -StopOnFailure:$StopOnFailure `
    -StreamCopilotOutput:$StreamCopilotOutput `
    -ShowSpinner:$ShowSpinner `
    -CopilotExtraArgs $CopilotExtraArgs `
    -MaxConcurrency $MaxConcurrency `
    -Verbose:$Verbose

  # Final summary
  Write-Host ""
  Write-Host ("="*72) -ForegroundColor Green
  Write-Host "Ralph iteration loop complete." -ForegroundColor Green
  Write-Host ("Total credits used: {0:0.##} / {1:0.##}" -f $result.CreditsUsed, $result.CreditCap) -ForegroundColor Cyan
  Write-Host ("State directory: {0}" -f $result.StateDir) -ForegroundColor DarkGray
  Write-Host ("="*72) -ForegroundColor Green
} finally {
  [SleepGuard]::Allow()
  Stop-Process -Name copilot -Force -ErrorAction SilentlyContinue
  Pop-Location
}
