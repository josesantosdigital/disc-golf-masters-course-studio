@echo off
title Course Studio Mod Toggle
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Toggle-CourseStudio.ps1" %*
if errorlevel 1 (echo. & echo Toggle did not complete. Read the message above. & pause & exit /b 1)
