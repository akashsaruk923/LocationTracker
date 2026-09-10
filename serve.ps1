<#
  Location Tracker - keep the app + a public link running, restart either if it
  stops. Runs until you close it / log off. Started at logon by the Startup-folder
  entry (startup-entry.cmd) or the scheduled task (install-task.ps1).

  Public link:
    * ngrok-domain.txt present + ngrok usable -> that FIXED https URL.
    * otherwise a Cloudflare quick tunnel (random URL, changes each run).
  The live URL is always written to current-url.txt .
#>

$ErrorActionPreference = 'Continue'
$root       = Split-Path -Parent $MyInvocation.MyCommand.Path
$appUrl     = 'http://localhost:5080'
$urlFile    = Join-Path $root 'current-url.txt'
$domainFile = Join-Path $root 'ngrok-domain.txt'
$logDir     = Join-Path $root 'logs'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null

# --- single instance -------------------------------------------------------
$mutex = New-Object System.Threading.Mutex($false, 'Global\LocationTrackerServe')
try {
    if (-not $mutex.WaitOne(0)) { Write-Host "another serve.ps1 is already running - exiting"; return }
} catch [System.Threading.AbandonedMutexException] {
    Write-Host "took over an abandoned mutex - continuing"
}

function Find-Exe([string]$name, [string[]]$extra) {
    $c = Get-Command $name -ErrorAction SilentlyContinue
    if ($c) { return $c.Source }
    foreach ($p in $extra) { if ($p -and (Test-Path $p)) { return $p } }
    $null
}
$ngrokExe = Find-Exe 'ngrok' @(
    "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\Ngrok.Ngrok_Microsoft.Winget.Source_8wekyb3d8bbwe\ngrok.exe",
    "$env:LOCALAPPDATA\Microsoft\WinGet\Links\ngrok.exe")
$cfExe = Find-Exe 'cloudflared' @(
    "$env:ProgramFiles\cloudflared\cloudflared.exe",
    "${env:ProgramFiles(x86)}\cloudflared\cloudflared.exe")

function Test-App {
    try { (Invoke-WebRequest "$appUrl/health" -TimeoutSec 3 -UseBasicParsing).StatusCode -eq 200 }
    catch { $false }
}

function Kill-Orphans {
    Get-CimInstance Win32_Process -Filter "name='LocationTracker.Api.exe' or name='ngrok.exe' or name='cloudflared.exe'" -ErrorAction SilentlyContinue |
        ForEach-Object { try { Stop-Process -Id $_.ProcessId -Force -ErrorAction Stop } catch {} }
}

function Start-App {
    Write-Host "[app] starting..."
    Start-Process -FilePath 'dotnet' -ArgumentList 'run','-c','Release','--urls',$appUrl `
        -WorkingDirectory $root -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $logDir 'app.out.log') `
        -RedirectStandardError  (Join-Path $logDir 'app.err.log') -PassThru
}

function Start-Tunnel {
    $useNgrok = $ngrokExe -and (Test-Path $domainFile)
    if ($useNgrok) {
        $domain = (Get-Content $domainFile -Raw).Trim()
        # ngrok free = one agent per endpoint. Kill any stray agent and give the
        # server session time to expire, or the new one hits ERR_NGROK_334.
        Get-Process ngrok -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill() } catch {} }
        Start-Sleep 8
        for ($try = 1; $try -le 6; $try++) {
            Write-Host "[tunnel] ngrok -> https://$domain (attempt $try)"
            $p = Start-Process -FilePath $ngrokExe `
                -ArgumentList 'http',"--url=$domain",'5080','--log=stdout' `
                -WindowStyle Hidden `
                -RedirectStandardOutput (Join-Path $logDir 'tunnel.out.log') `
                -RedirectStandardError  (Join-Path $logDir 'tunnel.err.log') -PassThru
            Start-Sleep 6
            if (-not $p.HasExited) {
                Set-Content -Path $urlFile -Value "https://$domain" -Encoding ascii
                Write-Host "[tunnel] PUBLIC URL: https://$domain (fixed)"
                return $p
            }
            Write-Host "[tunnel] ngrok exited (likely ERR_NGROK_334) - waiting 30s"
            Start-Sleep 30
        }
        Write-Host "[tunnel] ngrok not starting - falling back to cloudflare"
    }

    if (-not $cfExe) { Write-Host "[tunnel] no cloudflared found"; return $null }
    Write-Host "[tunnel] cloudflare quick tunnel (random URL)"
    $out = Join-Path $logDir ("cf_{0}.log" -f (Get-Date -Format 'yyyyMMdd_HHmmss'))
    $p = Start-Process -FilePath $cfExe -ArgumentList 'tunnel','--url',$appUrl,'--no-autoupdate' `
        -WindowStyle Hidden -RedirectStandardOutput $out -RedirectStandardError "$out.err" -PassThru
    $url = $null
    for ($i = 0; $i -lt 30 -and -not $url; $i++) {
        Start-Sleep 1
        if (Test-Path $out) {
            $m = Select-String -Path $out -Pattern 'https://[a-z0-9-]+\.trycloudflare\.com' -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($m) { $url = $m.Matches[0].Value }
        }
    }
    if ($url) {
        Set-Content -Path $urlFile -Value $url -Encoding ascii
        Write-Host "[tunnel] PUBLIC URL: $url"
    }
    $p
}

Write-Host "ngrok=$ngrokExe"
Write-Host "cloudflared=$cfExe"
Kill-Orphans
Start-Sleep 2

$app = Start-App
for ($i = 0; $i -lt 40 -and -not (Test-App); $i++) { Start-Sleep 1 }
$tunnel = Start-Tunnel

while ($true) {
    Start-Sleep 15
    try {
        if (-not (Test-App)) {
            Write-Host "[app] down - restarting"
            if ($app -and -not $app.HasExited) { try { $app.Kill() } catch {} }
            $app = Start-App
            for ($i = 0; $i -lt 40 -and -not (Test-App); $i++) { Start-Sleep 1 }
        }
        if (-not $tunnel -or $tunnel.HasExited) {
            Write-Host "[tunnel] down - restarting"
            $tunnel = Start-Tunnel
        }
    } catch { Write-Host "[loop] $_" }
}
