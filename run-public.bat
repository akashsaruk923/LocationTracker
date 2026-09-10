@echo off
REM ================================================================
REM  Location Tracker - start the app + a public link.
REM  Double-click this file. Two windows open:
REM    1. the .NET app  (writes to your local SQL Server)
REM    2. cloudflared   (prints the public https://...trycloudflare.com URL)
REM  Keep BOTH windows open. Closing either takes the site down.
REM  The URL changes every time you run this.
REM ================================================================

cd /d "%~dp0"

echo Starting the app...
start "LocationTracker - APP" cmd /k "dotnet run -c Release --urls http://localhost:5080"

echo Waiting for the app to come up...
:wait
timeout /t 2 /nobreak >nul
curl -s -o nul http://localhost:5080/health && goto ready
goto wait

:ready
echo App is up. Starting the public tunnel...
start "LocationTracker - PUBLIC LINK" cmd /k "cloudflared tunnel --url http://localhost:5080"

echo.
echo Done. Look in the "PUBLIC LINK" window for the https://...trycloudflare.com address.
echo Close both windows to stop.
pause
