@echo off
title Disc Golf Masters Mod Toggle
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Toggle-AllMods.ps1" %*
if errorlevel 1 (echo. & echo Toggle did not complete. Read the message above. & pause & exit /b 1)
