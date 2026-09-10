<#
  Location Tracker supervisor - keeps the .NET app running (against local SQL
  Server) and restarts it if it stops. Started at logon by the Startup-folder
  entry (see install-startup.ps1).

  Public link:
    * TAILSCALE (default when Tailscale is installed): the funnel is configured
      once with  setup-funnel.ps1  and is then served by the Tailscale service
      itself - it survives reboots on its own, so this script only babysits the
      app. Fixed URL, no interstitial.
    * ngrok: used only if  use-ngrok.txt  exists next to this file (plus
      ngrok-domain.txt + ngrok installed). Fixed URL but a one-time browser
      warning per visitor.
    * otherwise a Cloudflare quick tunnel (random URL).
  The live URL is written to current-url.txt .
#>

$ErrorActionPreference = 'Continue'
$root       = Split-Path -Parent $MyInvocation.MyCommand.Path
$appUrl     = 'http://localhost:5080'
$urlFile    = Join-Path $root 'current-url.txt'
$domainFile = Join-Path $root 'ngrok-domain.txt'
$ngrokFlag  = Join-Path $root 'use-ngrok.txt'
$logDir     = Join-Path $root 'logs'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null

$mutex = New-Object System.Threading.Mutex($false, 'Global\LocationTrackerServe')
try { if (-not $mutex.WaitOne(0)) { Write-Host "already running - exiting"; return } }
catch [System.Threading.AbandonedMutexException] { Write-Host "took over abandoned mutex" }

function Find-Exe([string]$name, [string[]]$extra) {
    $c = Get-Command $name -ErrorAction SilentlyContinue
    if ($c) { return $c.Source }
    foreach ($p in $extra) { if ($p -and (Test-Path $p)) { return $p } }
    $null
}
$tailscaleExe = Find-Exe 'tailscale' @("$env:ProgramFiles\Tailscale\tailscale.exe")
$ngrokExe = Find-Exe 'ngrok' @(
    "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\Ngrok.Ngrok_Microsoft.Winget.Source_8wekyb3d8bbwe\ngrok.exe",
    "$env:LOCALAPPDATA\Microsoft\WinGet\Links\ngrok.exe")
$cfExe = Find-Exe 'cloudflared' @(
    "$env:ProgramFiles\cloudflared\cloudflared.exe",
    "${env:ProgramFiles(x86)}\cloudflared\cloudflared.exe")

$useTailscale = $tailscaleExe -and -not (Test-Path $ngrokFlag)
$useNgrok     = -not $useTailscale -and $ngrokExe -and (Test-Path $domainFile)

function Test-App {
    try { (Invoke-WebRequest "$appUrl/health" -TimeoutSec 3 -UseBasicParsing).StatusCode -eq 200 }
    catch { $false }
}

function Start-App {
    Write-Host "[app] starting..."
    Start-Process -FilePath 'dotnet' -ArgumentList 'run','-c','Release','--urls',$appUrl `
        -WorkingDirectory $root -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $logDir 'app.out.log') `
        -RedirectStandardError  (Join-Path $logDir 'app.err.log') -PassThru
}

function Report-TailscaleUrl {
    try {
        $j = & $tailscaleExe status --json 2>$null | ConvertFrom-Json
        $tsHost = $j.Self.DNSName.TrimEnd('.')
        if ($tsHost) { Set-Content -Path $urlFile -Value "https://$tsHost" -Encoding ascii; Write-Host "[url] https://$tsHost" }
    } catch {}
}

function Start-Ngrok {
    Get-Process ngrok -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill() } catch {} }
    Start-Sleep 8
    $domain = (Get-Content $domainFile -Raw).Trim()
    for ($t = 1; $t -le 6; $t++) {
        $p = Start-Process -FilePath $ngrokExe -ArgumentList 'http',"--url=$domain",'5080','--log=stdout' `
            -WindowStyle Hidden -RedirectStandardOutput (Join-Path $logDir 'tunnel.out.log') `
            -RedirectStandardError (Join-Path $logDir 'tunnel.err.log') -PassThru
        Start-Sleep 6
        if (-not $p.HasExited) { Set-Content $urlFile "https://$domain" -Encoding ascii; Write-Host "[url] https://$domain"; return $p }
        Start-Sleep 30
    }
    $null
}

function Start-Cloudflared {
    if (-not $cfExe) { return $null }
    $out = Join-Path $logDir ("cf_{0}.log" -f (Get-Date -Format 'yyyyMMdd_HHmmss'))
    $p = Start-Process -FilePath $cfExe -ArgumentList 'tunnel','--url',$appUrl,'--no-autoupdate' `
        -WindowStyle Hidden -RedirectStandardOutput $out -RedirectStandardError "$out.err" -PassThru
    for ($i = 0; $i -lt 30; $i++) {
        Start-Sleep 1
        $m = Select-String -Path $out -Pattern 'https://[a-z0-9-]+\.trycloudflare\.com' -EA SilentlyContinue | Select-Object -First 1
        if ($m) { Set-Content $urlFile $m.Matches[0].Value -Encoding ascii; Write-Host "[url] $($m.Matches[0].Value)"; break }
    }
    $p
}

Write-Host "mode: $(if($useTailscale){'tailscale'}elseif($useNgrok){'ngrok'}else{'cloudflare'})"
Get-CimInstance Win32_Process -Filter "name='LocationTracker.Api.exe'" -EA SilentlyContinue |
    ForEach-Object { try { Stop-Process -Id $_.ProcessId -Force } catch {} }
Start-Sleep 2

$app = Start-App
for ($i = 0; $i -lt 40 -and -not (Test-App); $i++) { Start-Sleep 1 }

$tunnel = $null
if ($useTailscale) { Report-TailscaleUrl }
elseif ($useNgrok) { $tunnel = Start-Ngrok }
else               { $tunnel = Start-Cloudflared }

while ($true) {
    Start-Sleep 15
    try {
        if (-not (Test-App)) {
            Write-Host "[app] restarting"
            if ($app -and -not $app.HasExited) { try { $app.Kill() } catch {} }
            $app = Start-App
            for ($i = 0; $i -lt 40 -and -not (Test-App); $i++) { Start-Sleep 1 }
        }
        if ($useTailscale) { Report-TailscaleUrl }
        elseif (-not $tunnel -or $tunnel.HasExited) {
            Write-Host "[tunnel] restarting"
            $tunnel = if ($useNgrok) { Start-Ngrok } else { Start-Cloudflared }
        }
    } catch { Write-Host "[loop] $_" }
}
