<#
  Registers "LocationTracker" as a scheduled task that runs serve.ps1 at logon,
  so the app + public link come back automatically after a reboot and keep
  running without any terminal open.

  Run once:  powershell -ExecutionPolicy Bypass -File install-task.ps1
  Remove:    schtasks /delete /tn LocationTracker /f
#>

$ErrorActionPreference = 'Stop'
$root   = Split-Path -Parent $MyInvocation.MyCommand.Path
$script = Join-Path $root 'serve.ps1'
$name   = 'LocationTracker'

$action    = New-ScheduledTaskAction -Execute 'powershell.exe' `
    -Argument "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$script`""
$trigger   = New-ScheduledTaskTrigger -AtLogOn
$settings  = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1) `
    -ExecutionTimeLimit ([TimeSpan]::Zero)
$principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Limited

Register-ScheduledTask -TaskName $name -Action $action -Trigger $trigger `
    -Settings $settings -Principal $principal -Force | Out-Null

Write-Host "Registered scheduled task '$name' (runs at logon)."
Write-Host "Start it now with:  schtasks /run /tn $name"
Write-Host "The public URL will be written to: $(Join-Path $root 'current-url.txt')"
