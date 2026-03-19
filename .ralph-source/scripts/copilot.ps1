# ---- copilot.ps1 ----
# Invoke the Copilot CLI with async output handling, progress display, timeout, and Ctrl+C support.
# Dot-sourced by ralph.ps1. No dependencies on other modules.

function Invoke-Copilot(
  [string]$prompt,
  [string]$workdir,
  [string[]]$extraArgs,
  [string]$model,
  [string]$label = "Copilot",
  [int]$timeoutSeconds = 2400,
  [switch]$stream,
  [switch]$spinner,
  [switch]$quietProgress
) {
  $psi = New-Object System.Diagnostics.ProcessStartInfo
  $psi.FileName = "copilot"
  $psi.WorkingDirectory = $workdir
  $psi.UseShellExecute = $false
  $psi.CreateNoWindow  = $true
  $psi.RedirectStandardOutput = $true
  $psi.RedirectStandardError  = $true

  if ($extraArgs) { foreach ($a in $extraArgs) { [void]$psi.ArgumentList.Add($a) } }
  
  # Add model selection if specified
  if (-not [string]::IsNullOrWhiteSpace($model)) {
    [void]$psi.ArgumentList.Add("--model")
    [void]$psi.ArgumentList.Add($model)
  }
  
  [void]$psi.ArgumentList.Add("-p")
  [void]$psi.ArgumentList.Add($prompt)

  $p = New-Object System.Diagnostics.Process
  $p.StartInfo = $psi

  $frames = @('⠋','⠙','⠹','⠸','⠼','⠴','⠦','⠧','⠇','⠏')
  $spin = 0
  $startTime = Get-Date

  if ($stream) {
    # ── STREAM MODE: use Register-ObjectEvent for real-time line output ──
    $outSb = [System.Text.StringBuilder]::new()
    $errSb = [System.Text.StringBuilder]::new()

    $outAction = {
      $line = $Event.SourceEventArgs.Data
      if ($null -ne $line) {
        [void]$outSb.AppendLine($line)
        Write-Host $line
      }
    }
    $errAction = {
      $line = $Event.SourceEventArgs.Data
      if ($null -ne $line) {
        [void]$errSb.AppendLine($line)
        Write-Host $line -ForegroundColor DarkYellow
      }
    }

    $outEvent = Register-ObjectEvent -InputObject $p -EventName OutputDataReceived -Action $outAction
    $errEvent = Register-ObjectEvent -InputObject $p -EventName ErrorDataReceived -Action $errAction

    [void]$p.Start()
    $p.BeginOutputReadLine()
    $p.BeginErrorReadLine()

    while (-not $p.HasExited) {
      if ($timeoutSeconds -gt 0 -and ((Get-Date) - $startTime).TotalSeconds -ge $timeoutSeconds) {
        Write-Host "`nTimeout after ${timeoutSeconds}s. Killing copilot process..." -ForegroundColor Yellow
        $p.Kill()
        Unregister-Event -SourceIdentifier $outEvent.Name -ErrorAction SilentlyContinue
        Unregister-Event -SourceIdentifier $errEvent.Name -ErrorAction SilentlyContinue
        throw "Copilot timed out after ${timeoutSeconds}s (model: $model)"
      }
      Start-Sleep -Milliseconds 250
    }

    Start-Sleep -Milliseconds 500
    Unregister-Event -SourceIdentifier $outEvent.Name -ErrorAction SilentlyContinue
    Unregister-Event -SourceIdentifier $errEvent.Name -ErrorAction SilentlyContinue
    $p.WaitForExit()

    $stdout = $outSb.ToString().Trim()
    $stderr = $errSb.ToString().Trim()
  } else {
    # ── SPINNER / QUIET MODE: use ReadToEndAsync — no PS events, no console interference ──
    [void]$p.Start()
    $stdoutTask = $p.StandardOutput.ReadToEndAsync()
    $stderrTask = $p.StandardError.ReadToEndAsync()

    # Show initial spinner
    if ($spinner -or $quietProgress) {
      [Console]::Write(("  {0} {1} 0s" -f $frames[0], $label))
    }

    while (-not $p.HasExited) {
      # Check for Ctrl+C
      try {
        if ([Console]::KeyAvailable) {
          $key = [Console]::ReadKey($true)
          if ($key.Key -eq 'C' -and $key.Modifiers -eq 'Control') {
            Write-Host "`nCtrl+C detected. Terminating copilot process..." -ForegroundColor Yellow
            $p.Kill()
            throw "Process terminated by user"
          }
        }
      } catch [System.IO.IOException] { }
      catch [System.InvalidOperationException] { }

      # Check for timeout
      if ($timeoutSeconds -gt 0 -and ((Get-Date) - $startTime).TotalSeconds -ge $timeoutSeconds) {
        if ($spinner -or $quietProgress) { [Console]::Write("`r" + (" " * 80) + "`r") }
        Write-Host "Timeout after ${timeoutSeconds}s. Killing copilot process..." -ForegroundColor Yellow
        $p.Kill()
        throw "Copilot timed out after ${timeoutSeconds}s (model: $model)"
      }

      # Update spinner
      if ($spinner -or $quietProgress) {
        $f = $frames[$spin % $frames.Count]
        $spin++
        $elapsed = [int]((Get-Date) - $startTime).TotalSeconds
        [Console]::Write(("`r  {0} {1} {2}s          " -f $f, $label, $elapsed))
      }

      [System.Threading.Thread]::Sleep(250)
    }

    # Await the .NET tasks for captured output
    $stdout = $stdoutTask.GetAwaiter().GetResult().Trim()
    $stderr = $stderrTask.GetAwaiter().GetResult().Trim()

    $elapsed = [int]((Get-Date) - $startTime).TotalSeconds
    if ($spinner -or $quietProgress) {
      [Console]::Write(("`r" + (" " * 80) + "`r"))
      Write-Host ("  Done  {0} ({1}s)" -f $label, $elapsed) -ForegroundColor Green
    }

    $p.WaitForExit()
  }

  if ($p.ExitCode -ne 0) {
    $errorMsg = "Copilot failed (exit $($p.ExitCode))."
    if ($stderr) { $errorMsg += "`n`nSTDERR:`n$stderr" }
    if ($stdout) { $errorMsg += "`n`nSTDOUT:`n$stdout" }
    if (-not $stderr -and -not $stdout) { $errorMsg += "`n`nNo output captured." }
    throw $errorMsg
  }

  return $stdout
}
