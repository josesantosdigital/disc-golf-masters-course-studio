# Troubleshooting

## Course Studio does not open

- Use the **Disc Golf Course Studio** desktop shortcut. It starts a local server and opens the correct address; the port is selected automatically and can change, so a previously bookmarked `127.0.0.1` address may no longer work.
- If the shortcut is missing, run `BepInEx/plugins/CustomCourseLoader/CourseEditor/Launch-Course-Editor.ps1` from the game folder. The launcher prints the local URL if it cannot open the browser.
- The one-click setup installs a private Node runtime if necessary; the launcher also accepts Node.js installed on PATH. Re-run the current setup with the game closed if the editor or its runtime is missing.
- A DRAFT or outdated installer may install an older loader/editor. Check the installed version in `BepInEx/LogOutput.log` and compare it with the beta you intended to install.

## Setup or a course-package installer fails

- Close Disc Golf Masters, extract the relevant ZIP completely, and run the installer from that extracted folder. Choose the folder containing `Disc Golf Masters.exe` when asked for the game folder.
- The Course Studio setup downloads BepInEx and Node.js when missing, so it needs internet access in that case. It leaves an existing BepInEx installation in place, and stops on incomplete or conflicting files.
- For a separate map package, install Course Studio first and use that package's `Install-CourseModPackage.ps1`; a raw BONELAB ZIP is a converter input, not an installable course package. See [installation](PLAYER_GUIDE.md#2-install-a-separate-course-or-map-package) and [conversion](PORTING_GUIDE.md#convert-a-bonelab-pc-map).
- A course-package collision is refused unless `-Overwrite` is explicitly provided. Back up your course/map files first and read the error before retrying.

## Original course maps are missing

Open the DGM offline course selector once with the loader installed. The plugin exports built-in metadata and top-down previews. The editor can show schematic maps until those generated assets exist.

## Installed map is absent or says setup required

- Confirm a course JSON exists in `Courses` and its `mapScene.bundlePath` points to a `.bundle` beneath `Maps`.
- Confirm the scene and dependency files exist and match the JSON paths exactly.
- For a bundle-only map, enter the exact `.unity` asset path inside the bundle.
- Use **Installed maps → Scan maps** after installing or moving files; reopen Course Studio if the list still does not refresh.

## Course does not appear in game

- Read `BepInEx/LogOutput.log` for `CustomCourseLoader` validation warnings.
- Confirm the course JSON is directly inside the plugin's `Courses` folder and the ID/name/hole count are valid.
- Confirm `baseCourse` matches a course ID in the installed DGM build.
- Validate course and referenced files with `Tools/ValidateCoursePackage.ps1`.
- Restart the game after exporting course files.
- When export falls back to the folder picker, select the `BepInEx/plugins/CustomCourseLoader` folder, not the game root. Confirm the editor reports success and the JSON appears directly in `Courses`.

## Imported map fails to load

- Check that the bundle is a UnityFS AssetBundle for the correct PC platform and that `scenePath` names a scene inside it.
- Add every required dependency bundle and verify the dependency paths.
- Compare the source bundle's Unity version with the DGM player. Unity does not promise forward compatibility for AssetBundles.
- Check BepInEx logs for asset, scene, material, and missing dependency errors.
- Some source shaders, scripted components, lightmaps, colliders, and game-specific systems may not be supported. The importer copies bundles; it does not convert their internals.

## Tees/baskets or OB look wrong

- Original terrain uses local right/up/forward offsets based on the built-in tee. Check tee calibration and map width before placing markers.
- Imported maps use absolute world X/Y/Z. Check map bounds, ground height, and world positions.
- OB polygon points use the local horizontal frame from the hole tee.
- For an older imported course on a generated overview, check **OB limits** for **Align earlier OB zones to preview**, review the adjusted zones, export and restart the game. The button affects zones across all holes. Generated overviews use their recorded or inferred image bounds; manually supplied screenshots still require matching calibration. Version 0.5.117's fix has automated coverage but needs in-game verification.
- Use the per-hole reset buttons to restore original positions.

## Report a reproducible issue

Include the plugin version, DGM build, BepInEx version, course JSON, imported map source/version (if relevant), exact steps, and relevant log lines. Remove account names and unrelated personal paths from logs before posting. Do not upload game files or large map bundles unless the author and applicable licenses allow sharing them.
