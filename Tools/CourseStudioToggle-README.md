# Disc Golf Masters Mod Toggle 2.0.0

Temporarily disable or re-enable BepInEx and **all** BepInEx mods, including Course Studio and Aim Elevation. Other mod-loading systems are not controlled.

1. Close Disc Golf Masters.
2. Extract this ZIP and double-click `Toggle-AllMods.cmd`.
3. If asked, choose the folder containing `Disc Golf Masters.exe`.
4. Choose **Yes** to disable all BepInEx mods, **No** to re-enable them, or **Cancel** to leave them unchanged. Wait for the success message, then start the game.

The tool switches the game folder's `winhttp.dll` BepInEx Doorstop hook to `winhttp.dll.course-studio-disabled`, and reverses it to re-enable. This preserves mod files, maps and courses. Individually disabled mods remain disabled. If the old Course Studio-only toggle 1.0.0 has a recorded disable, choosing enable restores its recorded loader files before restoring BepInEx startup.

The tool requires the expected BepInEx IL2CPP Doorstop configuration and refuses linked startup files, conflicting enabled/disabled copies, missing hooks, or an open game. It never overwrites an existing startup hook. A different mod-loading setup may need its own instructions. Do not reinstall BepInEx while disabled without first re-enabling; if both hook files exist, reconcile them before retrying.

For advanced use: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Toggle-AllMods.ps1 -GameRoot 'C:\Games\Disc Golf Masters' -Action Status`. Replace the example game path; use `-Action Disable` or `-Action Enable` to change state. This requires Windows PowerShell. The exact packaged files passed disposable fixture tests; startup in the real game with BepInEx disabled has not yet been verified.
