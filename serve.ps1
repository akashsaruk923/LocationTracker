<#
  Location Tracker - keep the app + a public link running, and restart either
  one if it stops. Runs until you close it / log off.

  Registered as a logon scheduled task by  install-task.ps1  so it survives
  reboots without a terminal open.

  Public link:
    * If  ngrok-domain.txt  exists (one line, e.g. akash-loc.ngrok-free.app) and
      ngrok is on PATH with an authtoken configured -> that FIXED URL is used.
    * Otherwise a Cloudflare quick tunnel is used (random URL, changes each run).
  The live URL is always written to  current-url.txt .
#>

$ErrorActionPreference = 'Stop'
$root       = Split-Path -Parent $MyInvocation.MyCommand.Path
$appUrl     = 'http://localhost:5080'
$urlFile    = Join-Path $root 'current-url.txt'
$domainFile = Join-Path $root 'ngrok-domain.txt'
$logDir     = Join-Path $root 'logs'
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

function Use-Ngrok {
    if (-not (Test-Path $domainFile)) { return $false }
    return [bool](Get-Command ngrok -ErrorAction SilentlyContinue)
}

function Start-Tunnel {
    if (Use-Ngrok) {
        $domain = (Get-Content $domainFile -Raw).Trim()
        Write-Host "[tunnel] ngrok -> https://$domain"
        $p = Start-Process -FilePath 'ngrok' `
            -ArgumentList 'http',"--url=$domain",'5080','--log=stdout' `
            -WindowStyle Hidden -RedirectStandardOutput (Join-Path $logDir 'tunnel.log') `
            -RedirectStandardError (Join-Path $logDir 'tunnel.err.log') -PassThru
        Set-Content -Path $urlFile -Value "https://$domain" -Encoding utf8
        Write-Host "[tunnel] PUBLIC URL: https://$domain  (fixed)"
        return $p
    }

    Write-Host "[tunnel] cloudflare quick tunnel (random URL)"
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
    }
    $p
}

$app = Start-App
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
