@echo off
REM Location Tracker - start the app + public tunnel, hidden.
REM Copied to the Startup folder (as LocationTracker.cmd) with an absolute path
REM by install-startup.ps1 so it runs at every logon.
start "" /min powershell.exe -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "%~dp0serve.ps1"
