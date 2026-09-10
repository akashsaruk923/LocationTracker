@echo off
REM Launches serve.ps1 hidden at logon. No admin needed.
REM Install: copy this file into the folder that opens when you run  shell:startup
start "" /min powershell.exe -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "%~dp0serve.ps1"
