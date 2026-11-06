# Course and map porting guide

Course Studio separates course layout data from imported terrain. A layout converter can map par, distance, tee/basket coordinates, shot limits, and OB into the DGM course format. A map adapter must also provide a scene bundle, the exact `.unity` asset path, required dependencies, and a DGM-compatible Unity build.

## Current import tools

| Tool | Input | Output | What it does |
| --- | --- | --- | --- |
| `Tools/ConvertBonelabPallet.ps1` | BONELAB **PC** packed pallet folder or ZIP plus a scene name | DGM `Courses/<id>.json`, card image, and `Maps/<id>` bundles | Reads the pallet catalog, selects a scene bundle, extracts common MonoScripts/Unity built-in shader bundles and requested dependencies, then creates a starter DGM course definition. |
| `Tools/ConvertUnitySceneBundle.ps1` | UnityFS scene AssetBundle, its internal `.unity` path, and optional dependency bundles | Same DGM package structure | Packages an already-extracted compatible scene from another Unity game. It leaves bundle data unchanged. |
| `Tools/ConvertCourseLayoutJson.ps1` | Normalized course-layout interchange JSON | DGM `Courses/<id>.json` and generated card | Converts hole settings and either DGM local offsets or imported-scene world coordinates. Can update a starter definition after explicit `-Overwrite`; it does not parse source-game files or copy bundles. |
| Course Studio **Open JSON…** | DGM course JSON v1/v2 | Editable draft | Opens an existing DGM course definition. Game-specific adapters can translate a game's exported layout to this format. |

The BONELAB pallet converter packages the scene; it does not extract a BONELAB course layout. The official [MarrowSDK level documentation](https://github.com/StressLevelZero/MarrowSDK/blob/main/Docs/Levels.md) describes levels as one or more Unity scenes with environment and gameplay components; it does not document a DGM-style export for tee/basket positions, OB, par, or shot limits. Combine the pallet converter with `ConvertCourseLayoutJson.ps1` when you have a layout export or have mapped the source coordinates into the interchange format. The layout converter supports multiple source games through this common format, but does not parse their native files. Native proprietary exports still need a game-specific adapter that produces interchange JSON. The generic bundle tool only works when a compatible scene AssetBundle is available.

## BONELAB PC pallet

Use the packed PC pallet folder created by BONELAB's Marrow SDK, or a ZIP containing that folder's files. A mod.io page URL is a source reference only: the converter does not download from mod.io. Download the PC version of the mod and extract it first, or point the converter at the locally installed packed pallet folder. The input must contain one `catalog_*.json`, one `.pallet.json`, and the bundles referenced by the catalog. This converter targets PC pallets; Quest-only bundles have not been verified in DGM's Windows player. Marrow SDK's [packing guide](https://github.com/StressLevelZero/MarrowSDK/blob/main/Docs/BuildPallet.md) describes PC packing and says the generated folder can be shared through sites such as mod.io. The converter selects a scene by its Unity scene asset name. It reports available scene names if the selection is wrong. Example:

~~~powershell
.\Tools\ConvertBonelabPallet.ps1 `
  -ArchivePath 'C:\Users\you\AppData\LocalLow\Stress Level Zero\BONELAB\MODS\Author.MapName' `
  -SceneName 'Gm_HoodCorner Day' `
  -Id 'bonelab-hoodcorner' `
  -CourseName 'Hood Corner DGC' `
  -PluginDirectory 'C:\Games\Disc Golf Masters\BepInEx\plugins\CustomCourseLoader'
~~~

The converter reads the packed pallet's catalog and bundle files in place, then copies only the chosen scene bundle and selected dependencies into DGM's plugin data directory. BONELAB's official [pallet packing guide](https://github.com/StressLevelZero/MarrowSDK/blob/main/Docs/BuildPallet.md) documents the packed PC output and local MODS folder.

The converter defaults to `CanonPark`, nine par-3 holes, and 60 meters per hole. Use `-BaseCourse`, `-HoleCount`, `-SceneBundlePath`, and `-DependencyBundlePaths` when required. Choose a unique ID; the converter refuses to overwrite existing output. Its generated PNG is a labeled selector card, not a rendered map preview. Pass `-PluginDirectory` on every run so imported data goes to the intended game install.

The tool copies only selected scene/dependency bundles. Review the source pallet's distribution terms before sharing any converted bundle.

## Generic Unity scene bundle

~~~powershell
.\Tools\ConvertUnitySceneBundle.ps1 `
  -SceneBundlePath 'C:\Port\MyLevel.bundle' `
  -ScenePath 'Assets/Scenes/MyLevel.unity' `
  -Id 'my-level' `
  -CourseName 'My Level DGC' `
  -DependencyBundlePaths @('C:\Port\MonoScripts.bundle','C:\Port\Shaders.bundle') `
  -PluginDirectory 'C:\Games\Disc Golf Masters\BepInEx\plugins\CustomCourseLoader'
~~~

The bundle and dependencies must be built for a platform and Unity runtime that the installed DGM player can load. The importer checks a UnityFS header and records the source version when it can read it. It cannot rebuild or upgrade assets, restore missing shaders, repair mod scripts, infer scene paths, or convert game-specific tee/basket formats.

The four BONELAB maps currently under review have 10 selected scene/dependency bundles with Unity 2021.3.16f1 headers; the installed DGM player reports Unity 6000.3.26f1. Their bundles have been copied unchanged and do load. This is the older-bundle/newer-player direction: Unity's TypeTrees support backward-compatible deserialization across many version changes, although substantial feature changes can still produce incorrect results. The version gap is therefore a diagnostic lead, not proof that these maps require rebuilding. No Unity Editor or AssetRipper/AssetStudio/UABEA executable was found at the usual local install paths or on `PATH`, so scene reconstruction is not available in the current local toolchain. The next user-controlled runtime pass should distinguish bundle compatibility from course-specific scene data and DGM collision behavior.

Unity's [AssetBundle documentation](https://docs.unity.com/en-us/engine/6000.3/manual/assets-and-media/assets-managing-runtime/assetbundles-section/asset-bundles-intro) describes TypeTree-based backward compatibility and notes that significant feature changes can still require a rebuild. It does not support forward compatibility (a newer bundle loaded by an older player). Check the game's Unity version and test each map in DGM before sharing.

## Porting course layout data

1. Export or record each source hole's par, nominal length, shot limit, tee, basket, yaw, and OB geometry.
2. Determine whether the coordinates are relative to a source tee, local scene origin, or global scene origin. Convert explicitly to DGM local `teeOffset`/`basketOffset` or imported `mapTeePosition`/`mapBasketPosition` values.
3. Convert distance and rotation units. DGM uses meters and degrees; Unity scene coordinates are X/Y/Z meters.
4. Map the source game's basket/tee variants to DGM variants where a meaningful equivalent exists. Use `Default` otherwise.
5. Validate the generated JSON with `ValidateCoursePackage.ps1` and open it in Course Studio.
6. Test all holes in an offline DGM round, including OB, shot limits, tees, baskets, and tee cards.

There is no universal native course format across golf games. The interchange converter is the common target; a separate adapter is required for each game that exposes a documented or otherwise legally usable export format. Provide the next target game and a sample export to scope a native adapter.

If you are implementing an adapter, follow [the converter adapter guide](CONVERTER_ADAPTERS.md) for the coordinate contract, shared package writer, fixture rules, and release documentation updates.

## Rights and attribution

Only port maps and assets you created or are authorized to use. A local conversion does not grant the right to redistribute the source game's scene bundles, textures, music, scripts, or logos. Public release packages from this project omit imported map bundles and generated game assets. Review the source game's modding and redistribution terms before sharing a converted course.

BONELAB's official Marrow SDK docs describe PC/Quest pallet packing and recommend distributing packed content through its supported channels: [Build a pallet](https://github.com/StressLevelZero/MarrowSDK/blob/main/Docs/BuildPallet.md), [Pallets and crates](https://github.com/StressLevelZero/MarrowSDK/blob/main/Docs/PalletsAndCrates.md).

## BONELAB map source references

These Discord-ready links are retained as source references for sharing and course-porting notes. Only the URLs and titles are recorded here; original maps, extracted bundles, and converted map assets are not committed to Git or included in release packages. The references do not grant permission to redistribute the source content. Check each page and the author's terms before sharing converted assets.

- [TeenyTiny Isle](https://mod.io/g/bonelab/m/teenytiny-isle#description)
- [RP_SouthSide (V4)](https://mod.io/g/bonelab/m/rp-southside#description)
- [Gm_HoodCorner (V2)](https://mod.io/g/bonelab/m/gm-hoodcorner-v2#description)
- [7/11 | SANDBOX](https://mod.io/g/bonelab/m/711-alpha#description)
- [gm-milsim-forest](https://mod.io/g/bonelab/m/gm-milsim-forest#description)
- [Tree Land](https://mod.io/g/bonelab/m/tree-land#description)
- [The Simpsons Pack](https://mod.io/g/bonelab/m/the-simpsons-pack#description)
