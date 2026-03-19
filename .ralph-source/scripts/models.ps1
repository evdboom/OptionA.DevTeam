# ---- models.ps1 ----
# Model selection, cost tracking, role definitions, issue board, and handoff parsing.
# Dot-sourced by ralph.ps1. Depends on: utils.ps1 ($Script:WorkDir, Exec, ReadAllText).

$Script:ModelsCache = $null

function Get-ModelsData() {
  # Cache parsed MODELS.json for the session
  if (-not $Script:ModelsCache) {
    $modelsJsonPath = Join-Path $Script:WorkDir ".ralph" ".ralph-source" "MODELS.json"
    if (Test-Path $modelsJsonPath) {
      try {
        $Script:ModelsCache = Get-Content $modelsJsonPath -Raw | ConvertFrom-Json
      } catch {
        Write-Verbose "Failed to parse MODELS.json"
        $Script:ModelsCache = @()
      }
    } else {
      $Script:ModelsCache = @()
    }
  }
  return $Script:ModelsCache
}

function Get-DefaultModel() {
  # Returns the model name marked Default=true in MODELS.json, or $null
  $models = Get-ModelsData
  $defaultEntry = $models | Where-Object { $_.Default -eq $true } | Select-Object -First 1
  if ($defaultEntry) {
    return $defaultEntry.Name
  }
  return $null
}

function Get-ModelCost([string]$model) {
  $models = Get-ModelsData
  $entry = $models | Where-Object { $_.Name -eq $model } | Select-Object -First 1
  if ($entry) {
    return $entry.Cost
  }
  return 1
}

function Get-RoleDefinition([string]$roleName, [string]$rolesDir) {
  if ([string]::IsNullOrWhiteSpace($roleName)) { return "" }
  # Strip markdown formatting characters that models may include
  $clean = $roleName -replace '[*_`#>]', ''
  $slug = $clean.ToLower().Trim() -replace '\s+', '-'
  $rolePath = Join-Path $rolesDir "$slug.md"
  if (Test-Path $rolePath) {
    return [IO.File]::ReadAllText($rolePath)
  }
  return "(No role definition found for '$roleName' in $rolesDir)"
}

function Get-IssueBoard([string]$issuesDir) {
  if (-not (Test-Path $issuesDir)) { return "(No issues directory)" }
  $issueFiles = @(Get-ChildItem -Path $issuesDir -Filter "*.md" | Where-Object { $_.Name -notmatch '^_' -and $_.Name -ne 'README.md' } | Sort-Object Name)
  if ($issueFiles.Count -eq 0) { return "(No issues yet)" }
  $sb = [System.Text.StringBuilder]::new()
  foreach ($f in $issueFiles) {
    [void]$sb.AppendLine("### $($f.BaseName)")
    [void]$sb.AppendLine([IO.File]::ReadAllText($f.FullName))
    [void]$sb.AppendLine("")
  }
  return $sb.ToString().TrimEnd()
}

function Parse-NextSuggestions([string]$handoffText) {
  $role = $null
  $model = $null
  $pipeline = $null
  
  # Match NEXT_ROLE only as a line-start directive (with optional leading - or whitespace)
  if ($handoffText -match '(?m)^\s*-?\s*NEXT_ROLE:[ \t]*(.+?)(?:\r?\n|$)') {
    $role = $Matches[1].Trim() -replace '[*_`#>]', ''
    $role = $role.TrimEnd('.').Trim()
  }
  
  if ($handoffText -match '(?m)^\s*-?\s*PIPELINE:[ \t]*(.+?)(?:\r?\n|$)') {
    $pipeline = $Matches[1].Trim() -replace '[*_`#>]', ''
    $pipeline = $pipeline.Trim()
  }
  
  if ($handoffText -match '(?m)^\s*-?\s*NEXT_MODEL:[ \t]*(.+?)(?:\r?\n|$)') {
    $rawModel = $Matches[1].Trim()
    # Strip anything after the model name (e.g. parenthetical descriptions, comments)
    if ($rawModel -match '^([a-zA-Z0-9][\w\.\-]*)') {
      $candidate = $Matches[1]
      # Validate against known models from MODELS.json
      $knownModels = (Get-ModelsData) | ForEach-Object { $_.Name }
      if ($candidate -in $knownModels -or $candidate -eq 'default') {
        $model = $candidate
      } else {
        Write-Host "Warning: NEXT_MODEL '$candidate' not found in MODELS.json. Using default." -ForegroundColor Yellow
        $model = $null
      }
    }
  }
  
  # Parse PARALLEL: directive — lists issue groups that can run concurrently
  $parallel = @()
  if ($handoffText -match '(?m)^\s*-?\s*PARALLEL:[ \t]*(.+?)(?:\r?\n|$)') {
    $rawParallel = $Matches[1].Trim() -replace '[*_`#>]', ''
    $parallel = @($rawParallel -split '\|' | ForEach-Object { $_.Trim() } | Where-Object { $_ -match '\d' })
  }
  
  return @{ Role = $role; Model = $model; Pipeline = $pipeline; Parallel = $parallel }
}

function Get-RoleSuggestedModel([string]$roleName) {
  # Parse the ## Suggested Model section from the role definition file
  if ([string]::IsNullOrWhiteSpace($roleName)) { return $null }
  $slug = ($roleName -replace '[*_`#>]', '').ToLower().Trim() -replace '\s+', '-'
  $rolePath = Join-Path $Script:WorkDir ".ralph" ".ralph-source" "roles" "$slug.md"
  if (-not (Test-Path $rolePath)) { return $null }
  $content = [IO.File]::ReadAllText($rolePath)
  # Match: ## Suggested Model\n`model-name` — description
  if ($content -match '(?m)^## Suggested Model\s*\r?\n\s*`([a-zA-Z0-9][\w\.\-]*)`') {
    $candidate = $Matches[1]
    $knownModels = (Get-ModelsData) | ForEach-Object { $_.Name }
    if ($candidate -in $knownModels) {
      return $candidate
    }
  }
  return $null
}

function Select-Model([string]$suggestedModel, $creditsUsed, $creditCap, [string]$roleName) {
  $defaultModel = Get-DefaultModel
  
  # Priority: handoff NEXT_MODEL > role file Suggested Model > MODELS.json default
  $effectiveModel = $suggestedModel
  if ([string]::IsNullOrWhiteSpace($effectiveModel)) {
    $roleModel = Get-RoleSuggestedModel $roleName
    if ($roleModel) {
      Write-Verbose "Using suggested model '$roleModel' from role definition for '$roleName'"
      $effectiveModel = $roleModel
    }
  }
  if ([string]::IsNullOrWhiteSpace($effectiveModel)) {
    return $defaultModel
  }
  
  $cost = Get-ModelCost $effectiveModel
  
  # If model is free or we have enough credits, use it
  if ($cost -eq 0 -or ($creditsUsed + $cost) -le $creditCap) {
    return $effectiveModel
  }
  
  # Otherwise fall back to default model from MODELS.json
  $fallbackName = if ($defaultModel) { $defaultModel } else { "default" }
  Write-Host "Insufficient credits for $effectiveModel (cost: $cost, available: $($creditCap - $creditsUsed)). Falling back to $fallbackName." -ForegroundColor Yellow
  return $defaultModel
}
