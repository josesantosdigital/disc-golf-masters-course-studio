# AI project guide

This is the compact handoff for coding assistants working on Course Studio. Read `AGENTS.md` for the hard safety rules and `ARCHITECTURE.md` for component boundaries.

## Current importer handoff (2026-11-06)


Historical failure evidence: the 0.5.94 user-run log confirmed continued city snaps in Southside and Tiny Isle. Southside disc -190314 jumps from Y=7.41 to Y=30.08; its .94 endpoint correction moves it to Y=4.6 while the native ground model still reports contact at Y=29.99. Isle disc -204600 jumps from Y=4.02 to Y=30.08. No normal collision contact is required. Prior position clamps/getter filters did not fix this; .93 completion suppression also caused a hardlock.

Offline native-code analysis on 2026-11-06 found a source-level isolation gap. Current GameAssembly SHA-256 is `97AF84B21BC22D8B457C06DE5CBB42752BEB6BE085B8E9F191945704CBA3B98E` (Unity 6000.3.26f1, IL2CPP metadata v39). `SurfaceMapper.Under` (0x180667D70) chooses the first registered terrain covering X/Z without checking Terrain/Collider enabled state. `DiscGroundContact.FixedUpdate` (0x1809A3250) constructs a `DiscGroundSolver` from that mapper; Step and post-physics PushOut (0x180637FF0) can write Rigidbody.position using its heightfield, outside normal collision callbacks. `SurfaceMapper.OnDisable` removes itself through the native registry. Separately, DiscBehaviour.Throw reacquires even disabled terrain through FindFirstObjectByType; FixedUpdate inlines GroundAction and writes GroundDistance/IsGrounded backing fields from Terrain.SampleHeight. Getter hooks cannot intercept those accesses. Native audit helper/reports remain local under `.tools/native-city-audit`; do not distribute game-native disassembly or dependencies.

Version 0.5.95 suspends mappers whose base Terrain is hidden, through the normal enabled/OnDisable lifecycle, and guards later mapper reactivation. It detaches hidden cached disc terrains after Throw and before FixedUpdate, drops any cached ground model owned by them, and supplies actual imported mesh ground fields when no valid Terrain remains. Native imported terrains/models are preserved. Restoration is centralized in CourseScenePatcher after base Terrain/Collider restoration. Mesh support selects the nearest upward-facing physical imported collider, including a real roof/bridge, without old name/size/layer preference rules. The position clamps, grounded getter filters, and endpoint relocations are removed. Diagnostics only observe jumps and preserve native completion; completed traces no longer restart every tick. Source is `Runtime/ImportedTerrainIsolation.cs`, patches in `Runtime/Patches/ImportedGroundDistancePatch.cs` plus CourseScenePatch, and field sampling in ImportedMapPhysicsSupport.

The 0.5.82 log shows `RepairImportedMapRendering` aborting on all four maps because `LightmapSettings.lightmaps` fails IL2CPP's `Il2CppReferenceArray<LightmapData>` generic constraint. This occurs before the renderer/material/light repair loop, so the subsequent render-scan summary is absent. Version 0.5.83 catches only this optional lightmap-count read, preserves nonnegative baked indices when the count is unavailable, and allows the rest of the repair pass to continue.

Versions 0.5.84–0.5.94 used speculative height clamps, getter overrides, and completion corrections. These were superseded by 0.5.95 source isolation after user testing showed the snaps persisted. Do not reintroduce those corrections without identifying a separate measured cause.

The installed game's `level15` file contains the string `Canon_Park`, but UnityDataTool reports no TypeTrees for it; its object list exposes types and offsets without names or transforms. This does not identify a built-in collider at the 711 jump point, so the 0.5.77 all-scene throw snapshot remains necessary.

Version 0.5.80 introduced complete-hole setup requirements: every configured hole needs a DGM hole entry, usable tee/basket objects, and a grounded placement. Failures trigger the 0.5.79 rollback instead of leaving a partial layout or using the scene-bound fallback height. These guards remain in the installed 0.5.90 build; prior DLLs are kept in `.tools/recovery/`.

Keep all in-game checks for the user's manual verification; do not launch or control DGM. The user confirmed on 2026-11-06 that the 0.5.95 city-top snap fix works. The latest .95 log shows the hidden SurfaceMapper disabled, cached Terrain.SampleHeight source detached, Southside Hole 2 settling at (106.0,4.7,60.4), and Tiny Isle city throws completing. This fix applies automatically to future imported scenes through terrain ownership; it does not guarantee unrelated map collision or material correctness. Do not reinstate the superseded teleport clamps.

Version 0.5.96 adds a lighting candidate in Runtime/ImportedLitMaterials.cs, called from the shared imported render pass with original-material restoration in CourseScenePatcher. Southside still has a large white glare patch with 175 active imported local lights at zero, 185 base lights disabled, 175 source light-volume meshes hidden, and no source lightmaps on renderers. Its 1,484 source PBR materials report supported and were previously left unchanged. Rebind only the exact source shader name Universal Render Pipeline/Lit (PBR Workflow) to the game's supported URP Lit shader. Keep common textures/UVs/tint/PBR/emission/alpha/culling/queue state and remap compatible keywords; do not reuse the incompatible packed _DetailMap layer. Honor explicit ForceUnlit/ForceFallback modes. Water/custom shader families are not migrated. This is a pipeline-compatibility candidate, not a proven cause until visual testing.

Local source audit is under .tools/lighting-audit-0.5.96. Source candle_light has emission red=1024, but its eight uses are far from the screenshot position; no emission clamp was added on that evidence. Southside's live course settings remain unchanged (local light multiplier 0). Backup of the working .95 DLL, changed sources, Southside definition and log: .tools/recovery/lighting-source-0.5.96-20261106/. The .95 rollback SHA-256 is AAC6347469D86D6447FAAAE50C7964FBF71A11D1D280DAD4840786B1A84DBB00.

Installed 0.5.96: SHA-256 9425D15237CE877FEA68D2474C755DE74445B1F75E9C5B5C78B9E16B8D85D5E7 (287,744 bytes). Installed by atomic DLL replacement with .95 backup after confirming DGM was closed; live Southside JSON hash is unchanged. No game launch or controls. Build: zero warnings/errors. Production material migration passed 37 transfer/scope/rollback assertions; terrain isolation passed 18 assertions (both use component doubles). Seven converter scenarios passed. Package validation passed for Southside, Tiny Isle and the updated synthetic island example. TestBuildInstall and TestCourseStudioInstaller still fail because the pre-existing source Courses/SampleMeadowNine.json fixture is absent; no sample was restored into the player course list. Logs/evidence: .tools/lighting-audit-0.5.96/. Visual verification remains pending.

Consolidated manual check after .96 installation: load Southside Hole 2, approach the bright storefront/gas-station area shown in the screenshot, confirm whether glare disappears while textures, cutout leaves and glass remain correct. Log should report native Lit migrations (expected 1,484 for this source) and no migration errors. Then return to a built-in course to check cleanup. Do not open or control the game; ask the user for that pass. Offline material tests exercise production migration against stateful doubles and do not prove Unity shader variant availability or visual output. The separate city-fix acceptance is already confirmed.

The old 0.5.59 log shows 711 layout bounds of X -25..115 and Z -95..85 (140×180 m); it generated 67–185 m holes against 120–335 m targets. The current installed 711 definition expands the placement bounds to X -153..247 and Z -183..217 (400×400 m), so those short distances are not evidence for the current layout. Retest the current placements before changing grid density or course targets.

An offline 711 source-geometry audit on 2026-11-05 transformed the scene's 5,050 mesh colliders and 5,587 box, 114 capsule, and 24 sphere colliders into world space around both Y=30 m resting positions in the 0.5.59 log. The (68.7, 30.0, -17.2) point is about 7.5 m above the nearby `Building1_B (1)` collision bounds; the (82.1, 30.0, 2.6) point has no nearby imported mesh or primitive collider. The only intersecting mesh AABB is the enormous `Panorama Sphere`, whose actual spherical surface is far outside the playable area. This supports a false-ground/contact-state or other-scene explanation, rather than a city collision mesh physically supporting the disc at Y=30. The 0.5.77 trace adds a bounded log when a disc moves at least 5 m between physics updates, then captures nearby loaded-scene hits and colliders at that position; this should distinguish a transient vertical jump from the final resting surface. No game or test was run for this audit.

The 711 bundle contains one `OcclusionCullingSettings` with 5,431 static-renderer references and one 4.5 MiB `OcclusionCullingData` asset. The loader loads the course additively, then sets it as the active scene. Unity 2021.1 documentation says only one occlusion-data asset is loaded at runtime and additive scenes need shared occlusion data for intended culling. Because Canon Park and the user-provided 711 scene were not baked together, mismatched camera/PVS state is a plausible contributor to disappearing city geometry, but not yet proven. Version 0.5.81 disables occlusion culling on existing cameras at setup and rechecks every 0.5 seconds, then restores each camera's captured setting on cleanup; this remains in 0.5.83. The user still reports broken/missing 711 rendering after that bypass; 0.5.82's aborted renderer repair is the newly identified lead. [Unity 2021.1 additive-scene occlusion guidance](https://docs.unity3d.com/2021.1/Documentation/Manual/occlusion-culling-scene-loading.html).

The same 0.5.59 log shows a Building5_B contact at (90.9, 14.8, -2.7), velocity (-3.8, -7.2, 3.3) m/s, and near-horizontal normal (-0.87, -0.00, -0.49). The disc was then observed stationary at (82.1, 30.0, 2.6), and the game marked the throw complete 10.2 seconds after release. The old log therefore captures a large unexplained upward position change but not a round hardlock; preserve that distinction when comparing the next trace.

An additional Milsim material audit confirms `HAZARD_SIGN_ATLAS` is not the large black wall from the user's screenshot: its only submesh in `world_geometry` has four triangles and a transformed extent of about 0.61 m by 0.30 m. The adjacent `EP2_WOODSIGN01` slot is a different textured submesh. `Lit` has no `_BaseMap` or other color texture; its only populated texture slot is `_MetallicGlossMap`, and the material is used by `bunker_door_1`, `bunker_door_3`, `vhs_door`, and `func_button_78139`. These asset facts identify candidates for a user visual check but do not establish which surface looked black in-game; do not substitute textures without that evidence. This was a read-only bundle audit; no game or tests were run.


A follow-up read-only Milsim material/reference audit on 2026-11-05 found three placeholder-only slots on the combined `world_geometry` mesh: `HAZARD_SIGN_ATLAS` (slot 24), `CEILING01 1` (slot 58), and `RUBBERROOF002A 1` (slot 72). The latter two normalize to `ceiling01` and `rubberroof002a` peers with actual primary color textures, which match the loader's sibling-texture fallback; the sign slot is only four triangles. The other placeholder-only materials either have matching-family peers or are on small device props. This does not account for the large black wall, so keep texture substitutions evidence-led and inspect the 0.5.77 runtime alias/renderer diagnostics and the user's reported surface location. No tests or game interaction were run for this audit.

A cross-map occlusion-data inventory on 2026-11-05 found one `OcclusionCullingSettings` and one baked data asset in 711 DGC, Teeny Tiny Isle, and Southside, but none in Milsim Forest. The asset-database views contain 5,430, 5,769, and 12,309 static-renderer references respectively; their data objects are about 4.5 MiB, 0.55 MiB, and 1.0 MiB. This makes camera/PVS state a possible visibility factor for those three maps, but does not establish a rendering fault. The 0.5.77 context logger records active scene/camera occlusion state for every imported map; compare that evidence during the consolidated manual pass before enabling the 711-only bypass on another course. No course settings were changed from this audit.
The installed play-mod selection matches the user's request: Aim Elevation Mod remains enabled at 89.5 degrees; Lateral Throw Mod's DLL remains disabled and its dormant config now also has `Enabled=false`. The Course Loader remains installed. No game launch was performed.

The legacy 0.5.59 collision trace shows the disc hit the side of `Building5_B` at about (90.9, 14.8, -2.7), with a near-horizontal contact normal, then ended at (82.1, 30.0, 2.6) marked grounded with `groundDistance=0.01 m`. The old trace's `Combined Mesh (7)` label came from a MeshFilter fallback because it could not identify the wrapped collider as a MeshCollider; it is not evidence that the 2.23-million-vertex visual mesh was the physics mesh. Version 0.5.66 added exact collider-component identification, and 0.5.72 adds a final-position raycast across all loaded scenes. Capture that trace before changing imported collision or disc physics.

The 20 out-of-range serialized lightmap indices in 711 are attached to 19 `Decal_Water_*` renderers and one `TowerLOD_00` renderer. They do not account for broad building flicker by themselves. Version 0.5.70 clears only those invalid indices while preserving the 14-entry source lightmap set; the current 0.5.89 build carries the subsequent renderer-bounds, lightmap, and material-diagnostic changes. User verification of the render scan is pending.

Historical Southside lighting lead: source audit identified 175 active intensity-110 spotlights and 175 light-beam renderers. Later runtime logs confirm all beam renderers hidden, all imported local lights at zero and 185 base-course lights disabled, yet the glare remains. Do not repeat the old intensity reductions; the current candidate and evidence are recorded above.

Collision note: `HideBaseCourse` disables colliders and `Terrain` components outside the protected active-hole, player, basket, and tee-pad subtrees in the selected base course scene. The hidden base terrain is therefore not expected to remain as a collision surface, though other loaded scenes and protected gameplay objects still need the 0.5.83 all-scene position-jump and ground snapshot check.

## Commands

For the full offline preflight, run the project script below before release. It requires the local game assemblies only for disposable build/install fixtures; it never installs into the reference game. Individual commands remain useful for focused checks.

~~~powershell
.\Tools\TestReleasePreflight.ps1 -ReferenceGameRoot 'C:\Games\Disc Golf Masters'

# Optionally include a DRAFT archive for content and exact-extraction/install checks.
.\Tools\TestReleasePreflight.ps1 `
  -ReferenceGameRoot 'C:\Games\Disc Golf Masters' `
  -DraftArchivePath '.\dist\review\DiscGolfMasters-CourseStudio-0.5.34-DRAFT.zip'
~~~

~~~powershell
# Compile only; references the local game installation.
# Prefer the ignored project-local .NET 6 SDK when present, otherwise use .NET 6 on PATH.
# Keep first-use CLI files under ignored .tools instead of the host user's profile.
$dotnetCliDataRoot = Join-Path (Get-Location) '.tools\cli-home'
New-Item -ItemType Directory -Path $dotnetCliDataRoot -Force | Out-Null
$env:DOTNET_CLI_HOME = $dotnetCliDataRoot
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$localDotnet = Join-Path (Get-Location) '.tools\dotnet\dotnet.exe'
$dotnetCommand = if (Test-Path $localDotnet) { $localDotnet } else { (Get-Command dotnet -ErrorAction Stop).Source }
$gameRoot = 'C:\Games\Disc Golf Masters'
& $dotnetCommand build .\CustomCourseLoader.csproj -c Release "-p:GameRoot=$gameRoot"

# Install into the selected game folder. Preserves installed courses and generated catalog/previews.
.\build.ps1 -GameRoot $gameRoot

# Create a local candidate archive without installing it. Each run gets a fresh review directory.
$reviewDirectory = Join-Path '.\dist' ('review-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
.\New-ReleasePackage.ps1 -Draft -GameRoot $gameRoot -OutputDirectory $reviewDirectory

# Audit an already-created candidate archive and its checksum/content boundaries.
$version = [regex]::Match((Get-Content .\Plugin.cs -Raw), 'PluginVersion\s*=\s*"([^"]+)"').Groups[1].Value
.\Tools\TestReleasePackage.ps1 -ArchivePath (Join-Path $reviewDirectory "DiscGolfMasters-CourseStudio-$version-DRAFT.zip") -ExpectedVersion $version -Draft

# Test the exact archive's extraction and install behavior in a disposable game fixture.
.\Tools\TestCourseStudioInstaller.ps1 -ArchivePath (Join-Path $reviewDirectory "DiscGolfMasters-CourseStudio-$version-DRAFT.zip")

# Validate course packages before sharing.
.\Tools\ValidateCoursePackage.ps1 -CoursePath .\Courses\SampleMeadowNine.json -PluginDirectory 'C:\Games\Disc Golf Masters\BepInEx\plugins\CustomCourseLoader'

# Run isolated package-validation failure-path smoke checks.
# Includes missing and invalid scene/dependency UnityFS bundles.
.\Tools\TestCoursePackageValidation.ps1

# Smoke-test layout conversion, Unity/BONELAB packaging, validation, and rollback using synthetic fixtures.
.\Tools\TestCourseConverters.ps1

# Check fresh-install and update paths in disposable fake game folders; live player data is not changed.
# This checks file placement and preservation, not clean-game BepInEx startup.
.\Tools\TestBuildInstall.ps1 -ReferenceGameRoot $gameRoot

# Exercise direct install and folder/download export code against an isolated server fixture.
node --test .\Tools\TestCourseEditorExport.cjs

# Test installed-map discovery with isolated temporary fixture folders.
node --test .\Tools\TestMapCatalog.cjs

# Test imported lighting and foliage setting serialization/validation.
node --test .\Tools\TestMapLightingSettings.cjs

# Serve the source editor against an installed plugin folder for a read-only map review.
$env:DGM_COURSE_EDITOR_PLUGIN_DIR = 'C:\Games\Disc Golf Masters\BepInEx\plugins\CustomCourseLoader'
$env:DGM_COURSE_EDITOR_READ_ONLY = '1' # Blocks /api/install writes to that folder.
$env:DGM_COURSE_EDITOR_PORT = '4171'
try { node .\CourseEditor\server.js } finally {
    Remove-Item Env:DGM_COURSE_EDITOR_PLUGIN_DIR, Env:DGM_COURSE_EDITOR_READ_ONLY, Env:DGM_COURSE_EDITOR_PORT -ErrorAction SilentlyContinue
}

# Check the pure imported-foliage segment geometry without loading Unity or IL2CPP.
& $dotnetCommand run --project .\Tools\FoliageGeometryChecks\FoliageGeometryChecks.csproj

# Check custom personal-best persistence and complete-round qualification in isolation.
& $dotnetCommand run --project .\Tools\PersonalBestChecks\PersonalBestChecks.csproj

# Check deterministic imported hole placement and distance planning.
& $dotnetCommand run --project .\Tools\ImportedCoursePlannerChecks\ImportedCoursePlannerChecks.csproj

# Check selector preview cache invalidation and automatic-layout validation.
& $dotnetCommand run --project .\Tools\CoursePreviewChecks\CoursePreviewChecks.csproj

# Check selector loading delays, layout stability, and timeout release without Unity.
& $dotnetCommand run --project .\Tools\CourseLoadingChecks\CourseLoadingChecks.csproj
~~~

## Key contracts

- JSON v1/v2 is defined and validated in `Domain/CourseDefinition.cs`. Update `docs/COURSE_FORMAT.md` and sample JSON with any schema change.
- Game patches should keep the game in control of its IL2CPP-owned collections. Avoid indexing or mutating IL2CPP lists unless a proven safe wrapper is used.
- `Services/CourseRegistry.cs` is the authority for resolving built-in IDs/layouts and course asset paths. `Services/CourseCardFactory.cs` owns course-card loading and fallback generation.
- `Runtime/ImportedCoursePlanner.cs` chooses deterministic long imported holes from grounded scene samples. `CourseScenePatch` prefers samples with no nearby non-ground colliders or overhead obstructions when at least two clear points exist; otherwise it logs and uses all grounded samples. If Unity's clearance query throws, it logs once and disables that heuristic for the current plan. This is a local clearance heuristic, not navmesh or fairway reachability. When preferred distance/elevation windows cannot fill the course, the planner now completes remaining holes using any available grounded pair at least 8 m apart; expect target-distance deviations and verify water crossings/shot routes in game. For auto-planned holes, measure the displayed nominal length and par from `TeePadBehaviour.GetFrontMiddlePosition()` to the selected basket after moving both transforms. The four imported BONELAB courses remain under consolidated in-game acceptance. On Milsim Forest, the user reports improved branch collisions and generally better appearance but still sees a couple of missing textures. The 0.5.48 render log finds five materials without a color texture; the 0.5.49 diagnostic candidate adds renderer paths/bounds to those log entries, so use its next round to localize the surfaces before changing asset matching. Version 0.5.50 logs the first thrown-disc collision with each imported collider, including its type, mesh, layer, and bounds; 0.5.51 adds contact normals, primitive shape details, and transform paths. The user now reports that Teeny Isle Hole 2's city collision works, but one wall impact left a throw unresolved. Version 0.5.52 adds sampled throw lifecycle logs and removes unsupported concave water triggers, skips generated hidden `OBArea.Start` visual setup that produced null references, and keeps existing colliders when visible meshes are unreadable. Verify water OB and throw completion in game; these changes are not yet runtime-confirmed.
- `Runtime/Patches/ImportedMapRendering.cs` is the rendering half of partial `CourseScenePatcher`; it repairs imported-scene lights, materials, terrain, tree LODs, and terrain details. Runtime snapshots and temporary Unity resource ownership remain in `CourseScenePatch.cs`, where restore/cleanup is centralized. Coordinate changes to materials, meshes, and lights with that cleanup path; never write rendering changes into installed source bundles.
- Version 0.5.60 extends imported-throw diagnosis to keep a bounded sequence of repeat contacts when the disc moves or changes velocity, plus nearby colliders and downward ground hits across loaded scenes at changed resting positions and completion. The 0.5.59 711 trace captured a Building5_B side impact and a later Y=30 m grounded completion, but the final surface was not captured; the prior logger only recorded the first contact per collider.
- Version 0.5.61 additionally reports the active lightmap-data count and up to ten renderer paths with out-of-range baked/realtime indices. This is intended to distinguish invalid lightmap references from other causes of 711's flickering/missing buildings while preserving source materials/lightmaps.
- Version 0.5.62 captures up to four nearby-collider and downward-ground snapshots during prolonged throws that remain unresolved, even if DGM marks the disc grounded. Version 0.5.63 adds a bounded imported-scene TextMeshPro reference inventory after the last 711 session produced thousands of repeated TextMeshPro null-reference errors. It compiled with zero warnings/errors and is installed as SHA-256 `782E867D8A65E64B63A1236720239C83C7D1D41148920BDBB4FA370BAD02631A`, backed up 0.5.62 under `.tools/recovery/tmpro-scan-0.5.63-20261105/`; it has not been loaded in game. Do not launch or control DGM on the user's behalf.
- Version 0.5.64 expands a static imported renderer's local bounds to include its assigned mesh when the existing bounds omit geometry; it snapshots and restores the original value during scene cleanup. This targets the 711 combined-city-mesh flicker lead and does not change colliders or disc physics. The Release source compiled with zero warnings/errors (SHA-256 `B74EB334AA256302127D609260A32C71648363253B603C01D3537CA40697F548`); it is not installed or runtime-checked. Save all player-visible checks for the user's return.
- Version 0.5.65 carries forward the renderer-bounds repair but excludes water renderers from it; water bounds determine fallback water height. When a water mesh can be sampled, hazard checks and shallow-ground trimming now interpolate the local water surface height from the mesh triangle at each X/Z point. The Release source compiled with zero warnings/errors (SHA-256 `94E43A6BE7088924987A27A176F722E1FD37715BF33B7F128FA64DAB23C1F77E`); it is not installed or runtime-checked. Save all player-visible checks for the user's return.
- The Teeny Tiny Isle city on Hole 2 now has working collisions, but one wall impact was followed by a round hardlock. Version 0.5.53 continues the imported-disc trace after `IsThrown` clears and logs the player's `OnThrowComplete` callback with the active course, hole, and throw count. Version 0.5.54 adds the native flight/stationary timers, reset height, OBArea, and current throw's completion/collision/penalty fields. Version 0.5.57 resets the trace origin and logs the first contact with each collider on every throw, so a repeated city-wall collision is retained. These diagnostics do not change physics, scoring, or throw completion. The 0.5.58 startup is confirmed, but it has not loaded an imported map; reproduce the wall impact to capture the trace.
- Version 0.5.55 addresses Southside's screenshot with oversized white street-light cones by enabling the existing `hideImportedParticles` option and logging the first 24 hidden renderers and their shaders. The 0.5.58 Southside render scan later confirmed that 175 incompatible volumetric-light renderers were hidden; its generated preview is still very dark, so final lighting acceptance remains open.
- Version 0.5.56 gives each imported hole its configured `lengthMeters` target and snapshots that profile when the course definition loads, so post-placement measured distances cannot skew a later layout. Distinct target profiles were set for all four imported maps; their package validation and all 11 planner checks pass. Version 0.5.57 retains these profiles. The 0.5.58 Southside log shows all nine holes planned and placed. Teeny Isle's city-wall hardlock still needs an Isle-specific runtime trace.
- Version 0.5.57 resets the trace origin and records the first contact with each collider on every throw, so a repeated city-wall collision is retained. Version 0.5.58 adds raw imported-scene content bounds before configured gameplay bounds are applied. The installed 0.5.58 DLL matches SHA-256 `8B26EEC6769FC6EE54A491415FC3BBE6AFDBF0108D0CB39D0A2362E198D45BDA`. A subsequent 0.5.58 Southside session loaded all nine holes and logged seven completed throws across holes 1 and 2, including player completion callbacks. The Southside raw bounds were X -355.7..295.6 and Z -241.5..416.7, while its configured bounds remain X -140..190 and Z -150..150. The 175 point/spot lights were reduced to 20%, and the generated preview was very dark; the installed Southside multiplier is now restored to 1.0 with a backup at `BepInEx/plugins/CustomCourseLoader/backups/bonelab-southside-20261105-before-light-reset.json`, pending a reload check. This session did not reproduce the Teeny Isle city-wall hardlock or verify the other three maps on 0.5.58.
- `Runtime/CourseSceneCardCapture.cs` captures a selector preview only after an actual custom round has loaded; `Services/CustomCourseBadge.cs` supplies the generated selector badge. Capture uses the tagged main camera when available and a standalone camera otherwise. Capture revision 2 uses a wider, elevated tee-to-basket view and a revisioned cache filename so old compositions are not reused. Scheduling, missing tee/basket objects, deadline cancellation, and capture failures should be logged. Keep generated selector captures separate from authored per-hole `cardImage` tee signs and supplied `courseCardImage` artwork. Captures are local player data: never include them in archives or converter outputs. Image readability and capture cost require an in-game check.
- `Services/CustomPersonalBests.cs` owns only local custom-course score persistence. `Runtime/Patches/CustomPersonalBestPatches.cs` records complete rounds and exposes those values to DGM's custom-course selector. Enumerate game-owned IL2CPP score lists rather than indexing them, and restore a reward widget if it was hidden by the patch when the selection changes or detection fails. Keep original-course progression on DGM's native path; run `Tools/PersonalBestChecks/PersonalBestChecks.csproj` when changing the storage contract and verify selector/round behavior in game before release.
- Imported scenes are Unity AssetBundles; `Runtime/MapSceneRuntime.cs` owns loading and cleanup. Scene paths and dependency paths in course JSON are relative to their own bundle/plugin root respectively.
- `Runtime/CourseLoadingDriver.cs` advances selector and imported-map setup on Unity frames; `CourseLoadingOverlay.cs` reports progress and temporarily blocks selector input while registration completes. Keep `CourseSelectionLoadGate.cs` free of Unity calls so its delay, layout-stability, and timeout policy remains isolated and testable. Timeouts and failures must release the overlay/navigation state; synchronous AssetBundle calls may still pause animation, and the checks do not prove in-game rendering or responsiveness.
- `CourseEditor/` is a single-page editor split into classic scripts: `app-core.js`, `app-library.js`, `app-inspector.js`, `app-map.js`, `app-files.js`, `app-settings.js`, `app-events.js`, `app-utils.js`, then the `app.js` bootstrap. `index.html` owns their load order; keep bootstrap last and preserve the no-build-tool workflow. `server.js` serves the editor and exposes local install/scan APIs. Preserve loopback binding and path allowlists. `DGM_COURSE_EDITOR_PLUGIN_DIR` may point the source editor at another existing plugin data root; `DGM_COURSE_EDITOR_READ_ONLY=1` blocks direct `/api/install` writes during review.
- `CourseEditor/map-catalog.js` owns read-only installed-map discovery and authoring readiness facts (bounds status and explicit tee/basket counts); keep HTTP behavior in `server.js` and use `Tools/TestMapCatalog.cjs` for isolated scan and warning fixtures. `READY TO EDIT` reports file/path health only; do not present it as a guarantee of playability.
- The Installed maps view is fed by `GET /api/maps`. Keep scan results useful for recovery: include imported `mapScene` course files even when bundle/scene paths are invalid, and report missing dependency files without hiding their course cards. `CourseEditor/catalog.js` and `CourseEditor/assets/maps` are game-generated local data; never replace them during refactors or release packaging.
- The Original courses tab merges only the three known baseline IDs as an offline fallback with the generated game catalog. Do not add guessed/future courses to the fallback list; live generated layouts are the authority for courses available in the installed game build.
- `ConvertBonelabPallet.ps1` is a source-specific PC pallet folder/ZIP adapter; `ConvertUnitySceneBundle.ps1` writes a generic package through `Tools/Modules/CoursePackage.psm1`.
- Keep source mod URLs and attribution in `docs/PORTING_GUIDE.md`. Do not commit original or converted user map assets, bundles, or generated previews; tests and converter fixtures must use synthetic map data.
- The shared package writer and validator require UnityFS headers for both scene and dependency bundles; fixture coverage includes invalid dependency rejection and cleanup. BONELAB folder/ZIP fixtures model nested Marrow catalogs, `PALLET_BARCODE` bundle references, and scene variants without bundling real pallets.
- `ConvertCourseLayoutJson.ps1` converts normalized hole layouts, not native game formats. Update its input schema and both examples when changing the interchange contract.
- Canopy collision proxy generation is intentionally skipped after the 0.5.18 IL2CPP access violation. The `generateTreeCanopyColliders` field is accepted for compatibility, but currently produces only a warning; treat the editor control as unavailable until it is removed or safely reimplemented and verified.
- `experimentalFoliageInteractions` is a separate opt-in approximation using loader-owned terrain foliage volumes and disc-step interception. It does not call `FoliageSweep.Classify` or enable the legacy native proxy builder. On Tree Land, the user confirmed foliage reactions and audible foliage sound. The runtime log records the foliage interaction path, but actual disc-speed change, hit accuracy, performance, and behavior on other maps remain unverified.
- Custom solo personal bests are stored separately under `BepInEx/config/CustomCourseLoader.personal-bests.json`. The completion hook checks for a score on every custom-layout hole before writing; official course progress is untouched. `Tools/PersonalBestChecks` covers storage, comparisons, and incomplete rounds, but gameplay completion and UI display still need in-game verification.
- `SUPPORTED_ENVIRONMENT.md` records the observed development build target, and `BUILD_REFERENCE_BASELINE.md` fingerprints its compiler references; do not turn that single machine into broader compatibility claims without runtime evidence.
- Keep C# under `Domain/`, `Runtime/`, `Services/`, and `Diagnostics/`; preserve the `DgmCustomCourses` namespace when moving files.
- Keep service files scoped to one responsibility. `CourseRegistry.Selection.cs` is a partial of `CourseRegistry` for selector hooks/state; `CourseCardFactory.cs` owns PNG card loading, fallback drawing, and its texture cache. Put new course-card behavior in the factory rather than growing the registry again.
- Release packages must omit the installed game tree, game DLL references, BepInEx/Harmony binaries, imported maps, generated map catalog/previews, and user course files. Keep `.gitattributes` as the authority for LF text files and binary assets. Keep the seven user-supplied BONELAB source URLs in both packaged copies of `docs/PORTING_GUIDE.md`; `Tools/TestReleasePackage.ps1` enforces their presence, checks the packaged `.gitignore` exclusions and `.gitattributes` rules, rejects non-placeholder Windows user-profile paths in text files, and enforces the map-asset exclusions. Include the pinned license copies from `ThirdPartyLicenses/` at the archive root and in the source tree; keep the manifest's MIT-0 scope limited to original Course Studio code and documentation. License copies do not settle the LGPL linkage review tracked in `RELEASE_CHECKLIST.md`. The package may contain only the empty `CourseEditor/catalog.js` placeholder.

## Safe workflow

1. Inspect current source, runtime data, and docs before editing.
2. Keep changes within the plugin source. Treat installed `Courses` and `Maps` as player data.
3. Compile to the local build output; do not run the install script just to validate syntax.
4. Validate sample/imported course JSON and converter outputs.
5. For runtime changes, report the game version and whether an offline round was actually tested.
6. Coordinate player-visible runtime acceptance into one combined pass after importer work settles. Cover all target maps and product flows together; do not ask the player to run separate rounds for each feature or map.
7. Before public release, close every item in `RELEASE_CHECKLIST.md` and confirm redistribution rights for all third-party assets.

## Adding a source-game converter

Before adding a game-specific parser, check whether the source can export a UnityFS scene bundle and whether the layout can be mapped to `Tools/Schemas/course-layout-interchange.schema.json`. Keep bundle packaging in `Tools/Modules/CoursePackage.psm1`; adapters should only locate source files, validate their paths/IDs, and translate metadata. Do not claim native course-data support unless the adapter parses real source data and converted output has passed package validation and an in-game import. Update `Tools/README.md`, `docs/PORTING_GUIDE.md`, samples, and the release checklist when support changes.

For the full adapter workflow, coordinate semantics, fixture rules, and the distinction between layout and scene conversion, read [CONVERTER_ADAPTERS.md](CONVERTER_ADAPTERS.md) before changing a converter.
