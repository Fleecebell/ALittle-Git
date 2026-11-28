@echo off
title Uninstall VALORANT (ACLOS) + ACE AntiCheat
echo ================================================
echo   Uninstall VALORANT (ACLOS) + ACE AntiCheat
echo ================================================
echo.
net session >nul 2>&1
if errorlevel 1 (
    echo [!] Not running as Administrator. Requesting elevation...
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)
echo [OK] Administrator privileges confirmed
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "D:\game\ALittle-Git\uninstall_valorant.ps1"
echo.
echo Done. Full log:
echo   D:\game\ALittle-Git\valorant_uninstall_log.txt
echo.
pause
