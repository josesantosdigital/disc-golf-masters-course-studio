# Disc Golf Masters Mod Toggle 3.0.1

Temporarily remove BepInEx and all its plugins from the game folder, then restore them later. This includes the Course Studio loader and Aim Elevation mod. The tool preserves installed mods, course data, maps and settings.

1. Close Disc Golf Masters and save any Course Studio work.
2. Extract this ZIP and double-click `Toggle-CleanMods.cmd`.
3. If prompted, select the folder containing `Disc Golf Masters.exe`.
4. Choose **Yes** to disable, **No** to restore and enable, or **Cancel**. Wait for the success message before starting the game.

Disabling stops the Course Studio local editor server, then moves BepInEx and recognised Doorstop files into a `Disc Golf Masters - Mod Backup` folder next to the game folder on the same drive. Do not delete or modify that backup. Re-enabling restores the exact saved files, turns on the BepInEx startup hook, and restores the previous Course Studio 1.0.0 toggle's recorded changes if present. It also recognises the earlier `Disc Golf Masters Mod Backup Safe` backup in the Steam library's `steamapps` folder. Other independently disabled plugins stay disabled.

Version 3.0.1 fixes a false conflict after an earlier backup has been fully restored but its instructions file remains. A backup that still contains loader files is still treated as a conflict when BepInEx is installed.

The tool refuses an open game, conflicting files, unexpected loader configuration and linked loader roots. It will not overwrite files. Close the game and editor before any change; do not reinstall BepInEx while disabled. If an operation reports a conflict or an incomplete rollback, keep both locations intact and seek help before retrying.

This only handles the standard BepInEx IL2CPP Doorstop loader. Other mod loaders or modified game files remain as they were. The game may still report modifications; this tool does not change the game's detection or guarantee online access. It passed disposable folder tests but a complete cycle has not been confirmed in the game.

For advanced use: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Toggle-CleanMods.ps1 -GameRoot 'C:\Games\Disc Golf Masters' -Action Status`. Replace the example game path; use `-Action Disable` or `-Action Enable` to change state. Windows PowerShell is required.
