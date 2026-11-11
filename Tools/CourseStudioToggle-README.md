# Course Studio Mod Toggle 1.0.0

Temporarily switch the Disc Golf Masters Course Studio loader off or on without deleting installed courses or maps.

1. Close Disc Golf Masters.
2. Extract this ZIP, then double-click `Toggle-CourseStudio.cmd`.
3. If asked, choose the game folder containing `Disc Golf Masters.exe`.
4. Choose **Yes** to disable, **No** to enable, or **Cancel** to leave it unchanged. Wait for the success message, then start the game.

The tool switches `CustomCourseLoader*.dll` files inside the Course Studio plugin folder, including old backup DLLs that BepInEx could otherwise load. It records which files it disabled and restores only those files. It preserves course/map/editor data and does not change the separate Aim Elevation mod or other plugins. Course Studio may still open while disabled, but its custom courses won't load in game.

The tool stops if the game is open, files conflict, the plugin folder contains links, or another `CustomCourseLoader*.dll` is found elsewhere under `BepInEx/plugins`. It will not overwrite an existing DLL. If you run the Course Studio installer while disabled, restore or reconcile the existing files before trying again; keep backups if in doubt.

For advanced use, run `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Toggle-CourseStudio.ps1 -GameRoot 'C:\Games\Disc Golf Masters' -Action Status`, replacing the example game path. Use `-Action Disable` or `-Action Enable` to change state. The tool requires Windows PowerShell and a valid game installation. It does not uninstall BepInEx.
