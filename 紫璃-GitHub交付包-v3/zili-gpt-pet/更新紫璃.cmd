@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\install-pet.ps1" -Update
if errorlevel 1 echo Update did not complete. See the message above.
pause
