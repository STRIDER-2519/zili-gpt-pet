@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\install-pet.ps1"
if errorlevel 1 echo Installation did not complete. See the message above.
pause
