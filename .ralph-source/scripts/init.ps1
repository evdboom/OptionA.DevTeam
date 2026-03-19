# ---- init.ps1 ----
# Project scaffolding: creates .ralph-project, .ralph-state, src/ directories and template files.
# Dot-sourced by ralph.ps1. Depends on: utils.ps1 (WriteAllText).

function Invoke-RalphInit([string]$RepoPath) {
  $ralphDir   = Join-Path $RepoPath ".ralph"
  $sourceDir  = Join-Path $ralphDir ".ralph-source"
  $projectDir = Join-Path $ralphDir ".ralph-project"
  $stateDir   = Join-Path $ralphDir ".ralph-state"

  # Verify .ralph-source exists (this script should be in it)
  if (-not (Test-Path $sourceDir)) {
    Write-Host "Error: .ralph/.ralph-source/ not found. Copy the ralph engine there first." -ForegroundColor Red
    exit 1
  }

  # Create project directories
  foreach ($d in @($projectDir, (Join-Path $projectDir "issues"), (Join-Path $projectDir "decisions"), $stateDir, (Join-Path $RepoPath "src"))) {
    if (-not (Test-Path $d)) { New-Item -ItemType Directory -Path $d -Force | Out-Null }
  }

  # Scaffold project files (only if they don't exist)
  $scaffolds = @{
    (Join-Path $projectDir "GOAL.md") = "# Vision`n`n(Describe what you want to build)`n`n# Context`n`n(Background and constraints)`n`n# Current Focus`n`n**Mode:** Planning`n(What should ralph work on first?)`n"
    (Join-Path $projectDir "ROADMAP.md") = "# ROADMAP`n`n(Ralph will create the roadmap based on your GOAL.md)`n"
    (Join-Path $projectDir "ACCEPTANCE.md") = "# Acceptance Criteria`n`n(Ralph will populate this as work progresses)`n"
    (Join-Path $projectDir "CLANCY.md") = "# Ralphie, please keep in mind`n`n(Write live instructions here while ralph is running)`n"
    (Join-Path $projectDir "issues" "_index.md") = "# Issue Index`n`nTrack all issues and their current status. Updated by roles as work progresses.`n`n| ID | Title | Status | Assigned | Priority | Iteration |`n|----|-------|--------|----------|----------|-----------|`n| - | (no issues yet) | - | - | - | - |`n"
  }

  foreach ($kv in $scaffolds.GetEnumerator()) {
    if (-not (Test-Path $kv.Key)) {
      [IO.File]::WriteAllText($kv.Key, $kv.Value)
      Write-Host "Created: $($kv.Key -replace [regex]::Escape($RepoPath), '.')" -ForegroundColor Green
    } else {
      Write-Host "Exists:  $($kv.Key -replace [regex]::Escape($RepoPath), '.')" -ForegroundColor DarkGray
    }
  }

  # Add .ralph-state to .gitignore if not already there
  $gitignorePath = Join-Path $RepoPath ".gitignore"
  $gitignoreContent = if (Test-Path $gitignorePath) { Get-Content $gitignorePath -Raw } else { "" }
  if ($gitignoreContent -notmatch '\.ralph-state') {
    $entry = "`n# Ralph ephemeral state (per-run, not tracked)`n.ralph/.ralph-state/`n"
    Add-Content -Path $gitignorePath -Value $entry
    Write-Host "Updated .gitignore to exclude .ralph/.ralph-state/" -ForegroundColor Green
  }

  # Create src/.gitkeep if src/ is empty
  $srcDir = Join-Path $RepoPath "src"
  if ((Get-ChildItem $srcDir -Force -ErrorAction SilentlyContinue | Measure-Object).Count -eq 0) {
    [IO.File]::WriteAllText((Join-Path $srcDir ".gitkeep"), "")
  }

  Write-Host "`nRalph project initialized. Edit .ralph/.ralph-project/GOAL.md then run:" -ForegroundColor Cyan
  Write-Host "  .\.ralph\ralph.ps1 -Iterations 10 -Credits 25" -ForegroundColor White
}
