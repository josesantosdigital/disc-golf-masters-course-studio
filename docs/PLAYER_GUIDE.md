# Player guide

Course Studio adds a **custom course loader** and a browser-based **course editor** to the Windows version of Disc Golf Masters. It is an experimental beta for offline play. The editor and loader are installed together; imported maps are separate.

## 1. Install or update Course Studio

1. Close Disc Golf Masters.
2. Open [GitHub Releases](https://github.com/josesantosdigital/disc-golf-masters-course-studio/releases) and download the newest `DiscGolfMasters-CourseStudio-Setup-…-BETA.exe` asset. Run it. The installer is unsigned.
3. Setup looks for the Steam installation. If asked, select the **game's folder**: the folder containing `Disc Golf Masters.exe`, not the `steamapps` folder. To find it in Steam, right-click Disc Golf Masters in your library, choose **Manage → Browse local files**, and copy the folder location.
4. When setup finishes, start the game and open the **offline course selector** once. This lets the loader prepare built-in course information and previews. The first launch after a fresh BepInEx installation may take longer.
5. Use the **Disc Golf Course Studio** shortcut on your desktop to open the editor.

Setup installs the loader and editor, uses an existing BepInEx installation where possible, and downloads the pinned BepInEx 6 IL2CPP and Node.js from their official sources when needed; downloads are checksum-verified. Internet is needed if a dependency is missing. If it finds an incomplete BepInEx installation or conflicting files, it stops and reports the problem. Existing courses, maps, generated previews and personal-best data are preserved on update. Close the game before updating.

The release ZIP is an alternative for manual or advanced use. Extract it outside the game folder and run `Setup-CourseStudio.cmd`; this uses the same setup. The lower-level `Install-CourseStudio.ps1` requires BepInEx 6 IL2CPP to be installed already. The release does not contain the game, BepInEx, or imported map bundles. See the [supported environment](SUPPORTED_ENVIRONMENT.md) for tested versions and limits.

## 2. Install a separate course or map package

First install Course Studio as above. A Course Studio course package is a ZIP containing `Install-CourseModPackage.ps1`, `manifest.json`, `Payload` and a README. It is different from a raw BONELAB map ZIP; use the [conversion guide](PORTING_GUIDE.md#convert-a-bonelab-pc-map) for that.

1. Close the game and extract the course ZIP to its own folder.
2. In that extracted folder, open PowerShell or Windows Terminal. Run the following, replacing the example with the folder containing **your** `Disc Golf Masters.exe`:

   ~~~powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install-CourseModPackage.ps1 -GameRoot 'C:\Games\Disc Golf Masters'
   ~~~

3. Read the output for errors. Start the game and choose the course in the **offline course selector**. If Course Studio was already open, select **Installed maps → Scan maps** to refresh its list.

The package installer verifies the package, paths and hashes. It refuses existing files by default; `-Overwrite` explicitly permits replacement, subject to shared-file checks. Back up your courses and maps first if replacing anything. Read the package README for its required loader version and any additional instructions. A layout-only package may require the recipient to obtain and convert the source map separately; follow its README and ensure the bundle paths match.

To install files manually, the plugin folder is `<game folder>\BepInEx\plugins\CustomCourseLoader`. Put the course JSON and referenced card/preview images directly in its `Courses` folder. Put scene and dependency bundles at the exact relative paths given in the JSON's `mapScene`, normally under `Maps`. Preserve all folder names; merely placing the original source ZIP in `Maps` does not convert it. Restart the game.

## 3. Open the editor and make a course

Open the **Disc Golf Course Studio** desktop shortcut. It starts a local server on `127.0.0.1` using an available port and opens a browser. Use the URL opened by the shortcut: the port can change each time. The server reads and writes your selected local game installation; it does not publish courses online. If you installed manually, run `CourseEditor\Launch-Course-Editor.ps1` from the plugin folder.

1. Choose **Original courses** to make a layout on a built-in map, or **Installed maps** to use a converted or installed map. If a map was added while the editor was open, use **Scan maps**.
2. Select **Edit course** to modify an installed layout or **New layout** to make a separate variation. Give it a distinctive course name and ID. Reusing an ID on export replaces that course's JSON and generated cards.
3. Select each hole and drag its tee and basket markers. Set their positions and height in the inspector, along with par, distance, and shot limit. A shot limit of `0` means no limit. Review every hole, particularly automatically planned holes.
4. To draw OB, open **OB limits**, choose **Draw OB zone**, click at least three boundary points on the map and choose **Finish zone**. In version 0.5.117, generated imported-map overviews account for the capture's world bounds. For older drawings, if **Align earlier OB zones to preview** appears, use it only if the zones should remain where you originally drew them; it adjusts zones on all holes. Review the results and export again. Custom screenshots without known world bounds may still need calibration. Test OB in game.
5. The tee and hole card can be turned on per hole in their inspector panels; they default to off. Hiding basket art retains the scoring trigger. For imported maps, **Map settings** includes bounds and optional rendering/lighting controls.
6. Select **Export to game** and look for the success message. Restart the game, open the offline course selector and select your custom card. A browser-local draft is not a substitute for exporting to the game; export or back up your course before changing browsers or clearing browser data.

**READY TO EDIT** in Installed maps means the scene path, scene bundle and listed dependencies exist; it does not establish in-game compatibility or correct hole placement. **AUTHORING CHECK** calls out settings that need review. Use **Delete map** only when you intend to remove its course and unshared map assets; back them up first. Shared assets referenced by other courses are kept.

### If export asks for a folder

The local editor normally installs directly. If it instead opens a folder picker, choose `<game folder>\BepInEx\plugins\CustomCourseLoader`, not the game root. The editor creates `Courses` and needed `Maps` paths beneath it. If it downloads individual files instead, place them as described in section 2. Check for the final success message and restart the game.

## 4. Convert a map

The included tools can package a compatible **BONELAB PC pallet ZIP or folder**, or an already-extracted compatible Unity scene bundle. Follow the [step-by-step BONELAB conversion guide](PORTING_GUIDE.md#convert-a-bonelab-pc-map). Conversion requires PowerShell and the tools in the extracted Course Studio release ZIP or a source checkout; it is not a one-click conversion in the editor.

The converter copies selected scene/dependency bundles and creates a **starter** course. It does not extract BONELAB hole layouts or rebuild shaders, scripts, lighting or physics. Review and position the holes, tee/basket heights, bounds and OB in Course Studio, then test them in an offline round. Not every Unity map is compatible.

If you already have a compatible Unity scene bundle and know its internal `.unity` scene path, **Installed maps → Add map package…** opens a draft where you can select the scene bundle, enter its scene path and add dependency bundles under **Map settings**. Set a unique ID, preview and world bounds, review each hole, then export. This editor action does not unpack or convert a BONELAB ZIP.

## 5. Share a course

Built-in-course layouts need their JSON and referenced images; recipients need the loader and corresponding built-in course. Imported courses additionally reference map bundles. Share imported assets only when the creator and applicable terms permit it; otherwise share the source page and instructions so recipients can obtain and convert the map themselves. A source link is not the map itself.

Authors can create a versioned course ZIP with [New-CourseModPackage.ps1](../Tools/README.md#share-a-course-as-a-separate-zip). An imported-map package requires explicit asset inclusion and a recorded permission reference. The source reference is not itself proof of redistribution permission. Include the required Course Studio version, course ID, map source, and any known issues in your post.

## Disable or re-enable mods

1. Close Disc Golf Masters.
2. Download `CourseStudio-ModToggle-2.0.0.zip` from the [0.5.117 Beta release](https://github.com/josesantosdigital/disc-golf-masters-course-studio/releases/tag/v0.5.117-beta) and extract it.
3. Double-click `Toggle-AllMods.cmd`. If prompted, choose the folder containing `Disc Golf Masters.exe`. The window shows the current status: choose **Yes** to disable all BepInEx mods, **No** to re-enable BepInEx, or **Cancel** to leave it unchanged. Start the game again after the success message.

This renames the BepInEx Doorstop startup hook `winhttp.dll` in the game folder, so BepInEx and all its plugins—including the course loader and Aim Elevation mod—do not start on the next launch. It preserves your courses, maps, editor and mod files. On re-enable it restores the hook and, if present, reverses the prior 1.0.0 toggle's course-loader changes. Mods that were independently disabled remain disabled. The editor can still open, but custom courses do not load while BepInEx is disabled. Re-running an installer while disabled can create conflicting hook files; the toggle stops rather than overwriting them. Other mod-loading systems outside BepInEx are not controlled.

For a manual disable on a standard BepInEx Doorstop installation, close the game and rename `<game folder>\winhttp.dll` to `winhttp.dll.course-studio-disabled`; reverse that rename to re-enable. Do not overwrite either file if both exist. If you previously used the 1.0.0 course-only toggle, restoring the hook alone will not re-enable its loader: use the 2.0.0 toggle to restore its recorded changes. Game startup with BepInEx disabled has not yet been verified in-game.

## Backup and uninstall

Close the game, then back up `<game folder>\BepInEx\plugins\CustomCourseLoader\Courses` and `Maps`. Custom-course personal bests are stored in `BepInEx\config\CustomCourseLoader.personal-bests.json`; back that up too if needed. To uninstall the loader/editor, remove the `BepInEx\plugins\CustomCourseLoader` folder after making backups. Remove BepInEx only if no other mod needs it.

For common problems, see [troubleshooting](TROUBLESHOOTING.md). When reporting an issue, include the loader version, steps, course/map name and relevant log lines; remove private paths before sharing logs.
