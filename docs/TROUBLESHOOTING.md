# Troubleshooting

## Course Studio does not open

- Confirm Node.js is installed and `node.exe` is on PATH.
- Run the launcher from the installed `CourseEditor` folder.
- If port `4170` is already in use, close the older Course Studio server or restart it before running the launcher again.
- The editor is local-only and should open at `http://127.0.0.1:4170/`.

## Original course maps are missing

Open the DGM offline course selector once with the loader installed. The plugin exports built-in metadata and top-down previews. The editor can show schematic maps until those generated assets exist.

## Installed map is absent or says setup required

- Confirm a course JSON exists in `Courses` and its `mapScene.bundlePath` points to a `.bundle` beneath `Maps`.
- Confirm the scene and dependency files exist and match the JSON paths exactly.
- For a bundle-only map, enter the exact `.unity` asset path inside the bundle.
- Restart Course Studio after installing or moving files so its explorer rescans the plugin folders.

## Course does not appear in game

- Read `BepInEx/LogOutput.log` for `CustomCourseLoader` validation warnings.
- Confirm the course JSON is directly inside the plugin's `Courses` folder and the ID/name/hole count are valid.
- Confirm `baseCourse` matches a course ID in the installed DGM build.
- Validate course and referenced files with `Tools/ValidateCoursePackage.ps1`.
- Restart the game after exporting course files.

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
- Use the per-hole reset buttons to restore original positions.

## Report a reproducible issue

Include the plugin version, DGM build, BepInEx version, course JSON, imported map source/version (if relevant), exact steps, and relevant log lines. Remove account names and unrelated personal paths from logs before posting. Do not upload game files or large map bundles unless the author and applicable licenses allow sharing them.
