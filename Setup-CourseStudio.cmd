@echo off
title Disc Golf Course Studio Setup
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Setup-CourseStudio.ps1"
if errorlevel 1 (echo. & echo Setup did not complete. Read the message above. & pause & exit /b 1)
