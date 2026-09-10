<#
  One-time Tailscale Funnel setup. Run this yourself (Claude's safety layer
  blocks it):

      powershell -ExecutionPolicy Bypass -File setup-funnel.ps1

  It exposes the local app on your fixed Tailscale URL over HTTPS with no
  browser warning. The funnel config is stored by the Tailscale service and
  comes back on its own after a reboot.

  If it prints a link about enabling HTTPS or Funnel, open that link, click the
  toggle, then run this script again.
#>

$ts = "$env:ProgramFiles\Tailscale\tailscale.exe"
if (-not (Test-Path $ts)) { Write-Error "Tailscale not found at $ts"; exit 1 }

$dns = ((& $ts status --json | ConvertFrom-Json).Self.DNSName).TrimEnd('.')
Write-Host "Your fixed URL will be: https://$dns"
Write-Host ""

# Make sure HTTPS certs are usable (no-op if already enabled).
Write-Host "Requesting HTTPS certificate for $dns ..."
& $ts cert $dns 2>&1 | ForEach-Object { Write-Host "  $_" }
Write-Host ""

Write-Host "Enabling Funnel on port 5080 (background) ..."
& $ts funnel --bg --https=443 5080 2>&1 | ForEach-Object { Write-Host "  $_" }
Write-Host ""

Write-Host "--- funnel status ---"
& $ts funnel status 2>&1 | ForEach-Object { Write-Host "  $_" }
Write-Host ""
Set-Content -Path (Join-Path $PSScriptRoot 'current-url.txt') -Value "https://$dns" -Encoding ascii
Write-Host "Done. Open:  https://$dns"
