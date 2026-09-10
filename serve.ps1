<#
  Location Tracker - keep the app + a public link running.

  Starts the .NET app (against your local SQL Server) and a Cloudflare quick
  tunnel, writes the current public URL to  current-url.txt , and restarts
  either one if it stops. Runs until you close it / log off.

  Registered as a logon scheduled task by  install-task.ps1  so it survives
  reboots. The trycloudflare URL changes on every (re)start - always read the
  latest from current-url.txt.
#>

$ErrorActionPreference = 'Stop'
$root     = Split-Path -Parent $MyInvocation.MyCommand.Path
$appUrl   = 'http://localhost:5080'
$urlFile  = Join-Path $root 'current-url.txt'
$logDir   = Join-Path $root 'logs'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null

function Test-App {
    try { (Invoke-WebRequest "$appUrl/health" -TimeoutSec 3 -UseBasicParsing).StatusCode -eq 200 }
    catch { $false }
}

function Start-App {
    Write-Host "[app] starting..."
    Start-Process -FilePath 'dotnet' `
        -ArgumentList 'run','-c','Release','--urls',$appUrl `
        -WorkingDirectory $root -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $logDir 'app.out.log') `
        -RedirectStandardError  (Join-Path $logDir 'app.err.log') -PassThru
}

function Start-Tunnel {
    Write-Host "[tunnel] starting..."
    $out = Join-Path $logDir 'tunnel.log'
    if (Test-Path $out) { Remove-Item $out -Force }
    $p = Start-Process -FilePath 'cloudflared' `
        -ArgumentList 'tunnel','--url',$appUrl,'--no-autoupdate' `
        -WindowStyle Hidden -RedirectStandardOutput $out `
        -RedirectStandardError (Join-Path $logDir 'tunnel.err.log') -PassThru
    $url = $null
    for ($i = 0; $i -lt 30 -and -not $url; $i++) {
        Start-Sleep -Seconds 1
        if (Test-Path $out) {
            $m = Select-String -Path $out -Pattern 'https://[a-z0-9-]+\.trycloudflare\.com' | Select-Object -First 1
            if ($m) { $url = $m.Matches[0].Value }
        }
    }
    if ($url) {
        Set-Content -Path $urlFile -Value $url -Encoding utf8
        Write-Host "[tunnel] PUBLIC URL: $url  (saved to current-url.txt)"
    } else {
        Write-Host "[tunnel] could not read a URL from cloudflared output"
    }
    $p
}

$app    = Start-App
1..30 | ForEach-Object { if (-not (Test-App)) { Start-Sleep 1 } }
$tunnel = Start-Tunnel

while ($true) {
    Start-Sleep -Seconds 15
    if ($app.HasExited -or -not (Test-App)) {
        Write-Host "[app] down - restarting"
        try { $app.Kill() } catch {}
        $app = Start-App
        1..30 | ForEach-Object { if (-not (Test-App)) { Start-Sleep 1 } }
    }
    if ($tunnel.HasExited) {
        Write-Host "[tunnel] down - restarting"
        $tunnel = Start-Tunnel
    }
}
