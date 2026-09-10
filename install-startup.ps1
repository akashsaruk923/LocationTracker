<#
  No-admin auto-start: writes a launcher into the current user's Startup folder
  that runs serve.ps1 hidden at every logon.

  Run once:  powershell -ExecutionPolicy Bypass -File install-startup.ps1
  Remove:    del "%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\LocationTracker.cmd"
#>

$ErrorActionPreference = 'Stop'
$root    = Split-Path -Parent $MyInvocation.MyCommand.Path
$serve   = Join-Path $root 'serve.ps1'
$startup = [Environment]::GetFolderPath('Startup')
$dest    = Join-Path $startup 'LocationTracker.cmd'

$body = @"
@echo off
REM Location Tracker - start app + public tunnel hidden at logon.
start "" /min powershell.exe -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "$serve"
"@

Set-Content -Path $dest -Value $body -Encoding ascii
Write-Host "Installed: $dest"
Write-Host "It runs: $serve"
Write-Host ""
Write-Host "Start it now without logging off:"
Write-Host "  `"$dest`""
