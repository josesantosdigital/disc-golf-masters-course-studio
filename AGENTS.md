# Contributor instructions

Read `README.md`, `docs/ARCHITECTURE.md`, and `docs/AI_PROJECT_GUIDE.md` before changing loader or converter behavior.

## Project safety

- Treat `BepInEx/plugins/CustomCourseLoader` as a live player data folder. Preserve existing `Courses`, `Maps`, `CourseEditor/catalog.js`, and `CourseEditor/assets/maps` content.
- `build.ps1` installs into the game folder. Use `dotnet build` for compile-only work and `New-ReleasePackage.ps1 -Draft` for a release candidate. Prefer `.tools\dotnet\dotnet.exe` when present and keep CLI first-use data under `.tools\cli-home`; see `docs/AI_PROJECT_GUIDE.md` for the PowerShell setup.
- Never package the user's game directory, original game assets, BONELAB pallet contents, generated map previews, logs, or installed course bundles.
- Keep user-provided BONELAB mod pages as source URLs and attribution notes in `docs/PORTING_GUIDE.md`; never commit original pallets, converted user map bundles/assets, or generated map previews. Converter fixtures in the source tree must remain synthetic.
- Do not change a course JSON schema or runtime behavior without updating `docs/COURSE_FORMAT.md` and an example.
- Import tools must validate IDs, paths, and bundle headers, refuse accidental overwrites, and clean up only their own staging directories.
- The normalized layout converter may replace an existing course only with explicit `-Overwrite`; preserve and restore its JSON/card files if validation fails.
- Avoid direct mutation of game-owned IL2CPP collections. The game has crashed on unsafe list access before; use the existing safe course entry hooks and verify changes in game.
- Never directly call `Dgm.FoliageSweep.Classify` from the loader: the 0.5.18 diagnostic call caused an uncatchable `System.AccessViolationException` while loading Tree Land. Canopy proxies are disabled pending a verified safe integration.
- The game's Unity/interop references are local build dependencies. Do not copy them into a release archive.
- Keep `ThirdPartyLicenses/` as verbatim copies of the pinned upstream license texts named in `THIRD_PARTY_NOTICES.md`. When a dependency reference changes, refresh its copy and provenance; the release archive audit checks the identifiers and requires identical copies in the root and source tree.
- Keep runtime code organized under `Domain/`, `Runtime/`, `Services/`, and `Diagnostics/`; `Plugin.cs` is the BepInEx entry point.
- Keep `CourseScenePatcher` split between `Runtime/Patches/CourseScenePatch.cs` (course lifecycle, placements, shared snapshots and cleanup) and `Runtime/Patches/ImportedMapRendering.cs` (imported scene rendering repairs). When adding temporary render resources, register and restore them through the shared lifecycle cleanup.
- Keep the CourseEditor browser code divided by feature. `index.html` owns the classic-script order (`app-core.js` through `app-utils.js`, then `app.js` bootstrap); update it when adding or moving a feature script. Preserve loopback binding in `server.js`; the optional `DGM_COURSE_EDITOR_PLUGIN_DIR` must be an absolute existing plugin data folder, and `DGM_COURSE_EDITOR_READ_ONLY=1` must block `/api/install` writes.
- Do not add `Courses/` or `CourseEditor/` as build-output content. The install and release scripts explicitly select safe distributable files; build output must not collect player maps or generated previews.

## Validation

- Build with the installed game's interop assemblies when changing C# code.
- Run `Tools/ValidateCoursePackage.ps1` on example and converted course packages.
- Run `Tools/TestCourseConverters.ps1` for synthetic layout, Unity scene, BONELAB adapter, and rollback coverage.
- Run `Tools/TestBuildInstall.ps1 -ReferenceGameRoot <DGM game root>` to exercise the install script against a disposable fake game folder; this does not substitute for clean-game startup verification.
- Use the project-local .NET CLI setup in `docs/AI_PROJECT_GUIDE.md` before running isolated `dotnet` checks; a runtime-only `dotnet` on `PATH` is not enough to run SDK commands.
- Run `Tools/TestCourseStudioInstaller.ps1` to verify fresh installs, update preservation, and default DRAFT refusal; pass `-ArchivePath <candidate.zip>` to extract and install the actual archive into a disposable fake game folder.
- Run `Tools/TestGitIgnore.ps1` after changing `.gitignore`; it uses a disposable repository to check nested build-output, local-map, generated-catalog, and source inclusion rules.
- Run `node --test .\Tools\TestCourseEditorExport.cjs` for direct-install and fallback export paths against an isolated Course Editor server fixture.
- Parse JavaScript with `node --check`; parse PowerShell scripts before packaging.
- For Course Studio map-catalog changes, run `node --test .\Tools\TestMapCatalog.cjs` and report the result separately from in-game verification.
- For imported foliage sweep geometry changes, run `dotnet run --project .\Tools\FoliageGeometryChecks\FoliageGeometryChecks.csproj`; this checks only pure geometry and does not establish Unity collision or audio behavior.
- For custom personal-best storage changes, run `dotnet run --project .\Tools\PersonalBestChecks\PersonalBestChecks.csproj`; this covers persistence and round-completeness rules in isolation, not Unity hooks or in-game UI.
- Before reporting a release archive complete, run `Tools/TestReleasePackage.ps1` or use `New-ReleasePackage.ps1`, which runs the archive audit automatically.
- Run import and course-selection flows in game before describing a change as release verified.
- Coordinate player-visible runtime checks into one consolidated acceptance pass after importer changes settle; do not request separate test sessions for individual maps or features.
- Report separately what was compiled, package-validated, and tested in game.
