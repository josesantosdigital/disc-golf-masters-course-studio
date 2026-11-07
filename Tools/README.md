# Import and validation tools

The dotnet-run checks below require an SDK. Use the current .NET 10 SDK for contributor work; .NET 6 remains a legacy fallback because the plugin targets the game's observed .NET 6 host. These pure-code check projects also target net6.0. With .NET 10, the scripts suppress only the SDK's out-of-support target warning and use major runtime roll-forward if .NET 6 is not installed; this does not run the game plugin on .NET 10. Maintainer checkouts with .tools\dotnet\dotnet.exe should use the project-local SDK and CLI-home setup in the AI project guide. The release archive omits .tools; a runtime-only dotnet on PATH cannot run SDK commands.

Run Tools/TestOfflineChecks.ps1 from the source root to execute the contributor checks without a local DGM installation. It prefers a stable .NET 10-or-newer SDK and falls back to the project-local .NET 6 SDK, uses isolated temporary .NET/NuGet state, and checks converters, package validation, editor scripts, PowerShell syntax, and all seven pure C# check projects. The Windows GitHub Actions workflow runs the same command. This does not compile the game plugin against DGM assemblies or replace the full release preflight below.

Run the complete offline release preflight from the source root with `.\Tools\TestReleasePreflight.ps1 -ReferenceGameRoot 'C:\Games\Disc Golf Masters'`. It checks build/install preservation, converter and package-validation fixtures, the tracked synthetic sample, blocked/allowed release-asset policy examples, Git ignore boundaries in a disposable repository, Course Editor Node tests, and all pure C# check projects. Add `-DraftArchivePath '<path to the exact DRAFT ZIP>'` to audit and install-test that archive. The script uses disposable fixtures and does not install into or start the reference game; it does not replace a clean real-game launch.

Run `dotnet run --project .\Tools\CourseLoadingChecks\CourseLoadingChecks.csproj` to check selector loading delays, layout stability and timeout behavior without Unity. This does not test the in-game overlay.

Run `dotnet run --project .\Tools\CoursePreviewChecks\CoursePreviewChecks.csproj` to check course-definition fingerprints and UTF-8 BOM handling. This does not test camera rendering or the badge in-game.

Run `dotnet run --project .\Tools\ImportedCoursePlannerChecks\ImportedCoursePlannerChecks.csproj` to check the pure long-hole selection on synthetic samples. It does not test Unity physics, fairway reachability or water.

For authors building a native importer for another game, see [the converter adapter guide](../docs/CONVERTER_ADAPTERS.md) for the coordinate contract, shared package flow, fixture requirements, and support-claim boundaries.

All tools are Windows PowerShell scripts. Run commands from the source project folder or release archive root, and always pass the game's `PluginDirectory` explicitly. The scene converters refuse existing outputs. The layout converter only replaces existing JSON/card files when you pass `-Overwrite`; it restores both if validation fails.

## BONELAB PC pallet adapter

Use `ConvertBonelabPallet.ps1` with the PC pallet folder produced by Marrow SDK or a ZIP containing that folder's files. A mod.io page URL is a source reference, not converter input; download and extract the PC mod first, or point the tool at the locally installed packed pallet folder. The source must contain one `catalog_*.json`, one `.pallet.json`, and the bundle files referenced by the catalog. This converter targets PC pallets; Quest-only bundles have not been verified in DGM's Windows player. It extracts one selected Unity scene plus common shared bundles and explicit dependencies, then calls `Modules/CoursePackage.psm1` to write the DGM course package.

~~~powershell
.\Tools\ConvertBonelabPallet.ps1 `
  -ArchivePath 'C:\Maps\Author.MapName' `
  -SceneName 'Gm_HoodCorner Day' `
  -Id 'bonelab-hoodcorner' `
  -CourseName 'Hood Corner DGC' `
  -PluginDirectory 'C:\Games\Disc Golf Masters\BepInEx\plugins\CustomCourseLoader'
~~~

You can pass a ZIP instead, for example `-ArchivePath 'C:\Maps\Pallet.zip'`. The folder form works directly with the packed PC output described by the [official Marrow SDK packing guide](https://github.com/StressLevelZero/MarrowSDK/blob/main/Docs/BuildPallet.md).

The ID must be unique. Review the source's sharing terms before distributing converted files. The converter creates default hole records; it does not extract BONELAB tee/basket placements or scoring rules.

## Normalized course-layout converter

`ConvertCourseLayoutJson.ps1` converts a portable layout description to DGM course JSON. It handles either local offsets on a built-in DGM course (`courseOffsets`) or absolute Unity coordinates on an already-installed imported scene (`unityWorld`). Any source game can use this path if its exporter or a separate mapping step can produce the documented interchange JSON; the converter does not parse proprietary formats itself.

The examples are in `Tools/Samples`. To convert a built-in-course layout:

~~~powershell
.\Tools\ConvertCourseLayoutJson.ps1 `
  -LayoutPath '.\Tools\Samples\course-layout-offsets.example.json' `
  -PluginDirectory 'C:\Games\Disc Golf Masters\BepInEx\plugins\CustomCourseLoader'
~~~

For the example BONELAB flow, first run the pallet converter with ID `bonelab-hoodcorner`. Then edit `course-layout-unityworld.example.json` so its internal scene path and coordinates match the selected scene. If you used a different ID, update `id` and the bundle/dependency paths too. Pass `-Overwrite` to replace the pallet converter's default hole definition while keeping the imported bundle files. Existing JSON and card files are backed up during the update and restored if package validation fails.

Coordinates in `courseOffsets` are meters in DGM local right/up/forward axes. `unityWorld` uses absolute Unity X/Y/Z positions in meters. OB polygon points always use local right/forward coordinates relative to each hole's tee. See `Schemas/course-layout-interchange.schema.json` for the input contract.

## Generic Unity scene adapter

Use `ConvertUnitySceneBundle.ps1` when a source game provides a UnityFS scene bundle as a file. The caller supplies the exact scene path inside that bundle and any external dependencies:

~~~powershell
.\Tools\ConvertUnitySceneBundle.ps1 `
  -SceneBundlePath 'C:\Maps\scene.bundle' `
  -ScenePath 'Assets/Levels/Map.unity' `
  -Id 'ported-map' `
  -CourseName 'Ported Map DGC' `
  -DependencyBundlePaths @('C:\Maps\MonoScripts.bundle') `
  -PluginDirectory 'C:\Games\Disc Golf Masters\BepInEx\plugins\CustomCourseLoader'
~~~

The bundle must have a UnityFS header. The tool leaves its bytes unchanged. Unity version, platform, source game's scripts, shaders, and scene conventions determine whether the DGM player can load it.

## Validate a course package

~~~powershell
.\Tools\ValidateCoursePackage.ps1 `
  -CoursePath 'C:\Games\Disc Golf Masters\BepInEx\plugins\CustomCourseLoader\Courses\ported-map.json' `
  -PluginDirectory 'C:\Games\Disc Golf Masters\BepInEx\plugins\CustomCourseLoader'
~~~

The validator checks course schema limits, image references, safe paths, imported scene and dependency bundle existence and UnityFS headers, and per-hole imported positions. It does not launch the game or prove visual/physics compatibility.

Run the isolated validation smoke checks with `.\Tools\TestCoursePackageValidation.ps1`. They create temporary fixtures outside the game folder and cover valid packages, malformed JSON, missing bundle/dependency/image files, invalid scene/dependency bundle headers, unsafe paths, and duplicate IDs.

## Share a course as a separate ZIP

`New-CourseModPackage.ps1` creates a versioned, checksummed ZIP for one course JSON and the card images it references. For a layout on a built-in DGM course:

~~~powershell
.\Tools\New-CourseModPackage.ps1 `
  -CoursePath 'C:\Games\Disc Golf Masters\BepInEx\plugins\CustomCourseLoader\Courses\my-layout.json' `
  -PluginDirectory 'C:\Games\Disc Golf Masters\BepInEx\plugins\CustomCourseLoader' `
  -OutputPath 'C:\CoursePackages\my-layout-1.0.0.zip' `
  -PackageVersion '1.0.0' `
  -Author 'Your name'
~~~

For an imported course, add `-IncludeImportedAssets -AssetPermissionReference '<source URL or written permission record>'` only when redistribution of every included scene and dependency bundle is allowed. The manifest records the author's reference; it does not grant rights or establish that redistribution is permitted. If permission is absent or unclear, distribute the layout separately and point players to the original map source so they obtain compatible assets themselves.

Recipients install Course Studio at the minimum version shown in the package README or newer, close DGM, extract the ZIP, and run `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install-CourseModPackage.ps1 -GameRoot 'C:\Games\Disc Golf Masters'`. The installer checks hashes, course IDs, referenced images and bundles, refuses collisions unless `-Overwrite` is specified, and refuses to replace a bundle or image referenced by another course with different bytes. The archive includes only the selected course package and its referenced assets; it does not contain the loader or original DGM files. `Tools/TestCourseModPackage.ps1` runs synthetic ZIP creation, installation, collision, overwrite, shared-asset, hash-tampering, and imported-asset permission-gate checks.

Run `.\Tools\TestCourseConverters.ps1` for repeatable converter fixtures. It converts both layout examples, checks current imported-map lighting settings, packages a synthetic Unity scene bundle, rejects invalid dependency headers, simulates a locked dependency during copy to confirm partial staging cleanup, exercises BONELAB PC pallet folder and ZIP inputs with nested Marrow catalogs, `PALLET_BARCODE` bundle IDs and scene variants, validates each output, and verifies JSON/card rollback after an invalid overwrite. Fixtures are synthetic and do not establish compatibility with real game maps or redistribution rights.

Maintainer smoke checks also converted two user-provided, locally authorized BONELAB PC pallets (Tree Land and Hood Corner Day) in temporary folders. Their selected scene and dependency bundles matched the source bytes, starter cards were generated, package validation passed, and the temporary outputs were removed. This result covers those two pallets only; their assets are not included here or in release packages, and it does not test BONELAB course-layout extraction.

The generic Unity adapter was separately exercised with the authorized Tree Land scene bundle and dependencies. It preserved the scene bytes and generated a valid temporary package; those exact map assets had already loaded in DGM through the BONELAB package. The generic-adapter output itself was not launched as a separate course.

## Build/install smoke check

The archive-root `Install-CourseStudio.ps1` installs a built package into an existing BepInEx 6 IL2CPP game folder. Run it from the extracted archive with `-GameRoot 'C:\Games\Disc Golf Masters'`. It refuses DRAFT archives unless `-AllowDraft` is explicit, requires the game to be closed, and preserves existing courses, maps, generated catalog data, previews, and personal-best data during updates. Run `.\Tools\TestCourseStudioInstaller.ps1` to check fresh install, update preservation, and default DRAFT refusal using temporary fixtures. To verify a built archive end to end, pass its path with `-ArchivePath` so the test extracts that archive and installs it into a disposable game folder while checking existing data sentinels.

Run `.\Tools\TestBuildInstall.ps1 -ReferenceGameRoot 'C:\Games\Disc Golf Masters'` to exercise `build.ps1` without installing into the live game folder. The check copies only the project's referenced compiler assemblies into two temporary fake game roots. A fresh install verifies the plugin hash, the synthetic sample from Tools/Samples, and editor files; it also verifies no ignored local course files leak from the source Courses folder. An update case starts with a same-named sample course and existing course/card/map/catalog/preview data, then verifies those user files survive while the DLL and editor update. It removes both temporary roots afterward. This check does not launch DGM or prove startup from a clean BepInEx installation.

Run `dotnet run --project .\Tools\PersonalBestChecks\PersonalBestChecks.csproj` to check local custom personal-best persistence, reloads, score comparisons, incomplete rounds, and preservation of unreadable data. It does not invoke the game hooks or prove that the selector displays the result.

Run the installed-map discovery checks with `node --test .\Tools\TestMapCatalog.cjs`. This uses temporary fixture folders to cover configured maps, missing files and invalid paths, and standalone scene bundles. It does not read or change the live game map folders.

To serve the current source editor against an existing installed plugin folder without copying editor files into the game, set `DGM_COURSE_EDITOR_PLUGIN_DIR` to that absolute folder path before running `node .\CourseEditor\server.js`. The editor then reads its course JSON, map bundles, generated original-course catalog, and map previews from that folder; its `/api/install` endpoint writes there by default. Set `DGM_COURSE_EDITOR_READ_ONLY=1` as well when reviewing a live installation to block editor API writes. The server binds only to `127.0.0.1`.

Run the Course Editor export-flow check with `node --test .\Tools\TestCourseEditorExport.cjs`. It executes the actual `app-files.js` export branches against the actual local-server request handler and temporary plugin folders, checking direct installation, map rescanning, selected data-root routing for courses/catalogs/previews, read-only API rejection, path allowlists, folder-picker writes, and multi-file download names. Browser permission prompts are mocked; verify those in a real browser before release.

## Shared module

`Modules/CoursePackage.psm1` provides safe ID/path checks, UnityFS header inspection, preview card generation, staging, refusal to overwrite, and rollback for partial package installation. New source-game adapters should extract or translate into this module's input shape instead of duplicating the DGM package writer.

`TestReleasePackage.ps1` verifies a generated archive's SHA-256 sidecar, manifest, required player/contributor files (including the foliage geometry checks, both `CourseScenePatcher` source files, the release asset-policy module/fixtures, and the Git ignore fixture), third-party dependency/license notices, Markdown relative links, editor script load order, draft/license status, the seven supplied BONELAB source URLs in both packaged porting-guide copies, required `.gitignore` rules for local courses/maps/previews/backups/build outputs, exclusions for build outputs, game binaries, Unity bundles/scenes/materials, pallet manifests, images, textures, fonts, map previews, and user course files, and text-file rejection of machine-specific Windows profile paths. `New-ReleasePackage.ps1` runs this audit automatically before reporting success.
