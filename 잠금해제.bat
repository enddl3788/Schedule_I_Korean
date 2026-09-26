@echo off
chcp 65001 >nul
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0unlock.ps1"
if errorlevel 1 echo [error] script failed - see message above
pause
