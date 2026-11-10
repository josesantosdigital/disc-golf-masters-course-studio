# Architecture

## Runtime path

`Plugin.Load` creates the course registry from the plugin `Courses` directory, loads and validates JSON definitions, and installs Harmony patches. DGM remains responsible for the built-in course scene, hole flow, and scoring.

The loader hooks the offline course selector to add custom course entries and PNG cards. It discovers the selector through `CourseSelectionViewController.Awake`, `SetupCourseSelection`, and built-in `SetupCourseEntry` view hooks. It avoids patching `SoloConfiguration.GetCourses`; that IL2CPP wrapper crashed even when the postfix only queued work. `CourseLoadingDriver` defers selector preparation until the loading overlay has had time to render; `CourseSelectionLoadGate` waits for the rebuilt list height to stabilize, then releases the overlay, with bounded timeout and error paths. Player scrolling ends automatic scroll correction. `MapSceneRuntime` loads imported scene AssetBundles and every declared dependency, maintains opted-in map camera settings while the round is active, and cleans it up afterward, without displaying the custom selector loading overlay. A missing or rejected dependency aborts setup so the map cannot silently load with incomplete assets. Imported-map setup requires a DGM hole entry and usable tee/basket objects for every configured hole; an ungrounded placement or incomplete layout fails the setup. If setup fails after changing scene state, it restores snapshots and unloads the failed scene and bundles. When a custom course starts, patches apply its active hole settings: par, nominal length, tee and basket offsets/positions, OB zones, marker visibility, shot limits, and tee-side card images. Imported-map placement refreshes the in-round hole information panel after changing the DGM hole objects, so the displayed par and distance follow the placed tee and basket.

For completed, in-bounds throws on imported maps, the lie-position patch checks whether DGM's safety search selected a surface well above the settled disc. When the selected lie is nearby and imported physical ground is immediately below the disc, it returns that ground height. Water, penalty lies, unsupported discs, and ordinary course placement continue through DGM's normal logic. Runtime and multiplayer behavior require player verification.

In offline rounds, tee pad and tee-sign renderers on original courses are hidden and their original renderer states are restored on cleanup. Custom-course holes control tee artwork and the generated tee-side card independently, with both defaulting off; the tee's transform and gameplay objects remain active. The editor always displays its draggable tee position, even when the in-game pad is hidden.

Custom-course best scores are kept separately from DGM's built-in course progression. `CustomPersonalBests` stores the best score by layout ID under BepInEx's config directory; the round-completion patch only records a fully scored round and enumerates the game's hole-score collection. The selector patch serves that stored value and hides the empty reward widgets for custom courses, which have no DGM reward item; it restores those widgets when the selection changes or custom-course detection fails. The built-in course save/progression path remains in control of original courses.

## Main components

| Component | Responsibility |
| --- | --- |
| `Plugin.cs` | BepInEx entry point, logger, registry creation, Harmony bootstrap. |
| `Domain/CourseDefinition.cs` | JSON models and input validation for schema versions 1 and 2. |
| `Services/CourseRegistry.cs` | Loads definitions, matches built-in IDs/layouts, creates runtime course data, and coordinates selector registration. |
| `Services/CourseRegistry.Selection.cs` | Adds custom course entries to the offline selector and manages its scroll/reset state. It is a partial of `CourseRegistry` so it shares the registry's private selection state. |
| `Services/CourseCardFactory.cs` | Loads and downsamples supported PNG course cards or generates a fallback card; caches textures for the registry lifetime. |
| `Runtime/CourseSceneCardCapture.cs`, `Services/CustomCourseBadge.cs` | Capture and cache a wider, elevated selector card after a custom round loads, using a standalone camera if the game has no tagged main camera; revisioned cache names invalidate older compositions. Also supply a shared custom disc-golf badge. Existing card files remain fallback artwork. |
| `Services/CustomPersonalBests.cs` | Stores the lowest completed custom solo-round score relative to par, keyed by custom layout ID, in a separate local configuration file. |
| `Runtime/Patches/CustomPersonalBestPatches.cs` | Records fully scored custom solo rounds, supplies their local personal best to the solo UI, and suppresses the missing custom reward item in that UI. |
| `Runtime/Patches/CourseScenePatch.cs` | Applies hole settings, imported positions, OB zones, tee cards, and scene lifecycle patches; owns snapshots and cleanup for temporary scene changes. |
| `Runtime/Patches/ImportedMapRendering.cs` | Rendering half of the partial `CourseScenePatcher`: imported-scene lights, materials, terrain, tree LODs, and terrain-detail repair. |
| `Runtime/ImportedMapPhysicsSupport.cs` | Connects imported ground and water to DGM collision/hazard behavior, samples nearest physical imported support for disc ground fields, maps upward imported contacts to DGM's Terrain layer before its collision handler, and captures bounded contact, jump, and final-surface diagnostics. Does not clamp flight positions or relocate completed throws. |
| `Runtime/ImportedTerrainIsolation.cs`, `Runtime/Patches/ImportedGroundDistancePatch.cs` | Suspend hidden base Terrain mappers through their native lifecycle, detach hidden cached disc terrains/solvers, guard reactivation, and restore source ownership at cleanup. This shared source-isolation fix applies to future imports. |
| `Runtime/ImportedLitMaterials.cs` | Rebind the recognized source PBR Lit shader to DGM's supported URP Lit shader while retaining common material properties and surface behavior; restore original materials at course cleanup. Explicit unlit/fallback modes take precedence. Does not convert arbitrary custom shaders. |
| `Runtime/Patches/CourseLayoutPatches.cs` | Hooks layout creation and course metadata needed by custom entries. |
| `Runtime/Patches/CourseSelectionViewPatch.cs` | Offline course selector lifecycle discovery, card hooks, and scroll behavior. |
| `Runtime/CourseLoadingDriver.cs` | Unity-frame driver for selector preparation, imported-map loading, and loading-overlay updates. |
| `Runtime/CourseLoadingOverlay.cs` | Selector loading panel and temporary navigation control while its layout settles. |
| `Runtime/CourseSelectionLoadGate.cs` | Pure scheduling and layout-stability policy for the selector loading overlay; covered by isolated checks. |
| `Runtime/MapSceneRuntime.cs` | Deferred imported bundle loading, scene setup, positioning, map preview capture, and cleanup. |
| `Runtime/ImportedCoursePlanner.cs`, `Runtime/Patches/CourseScenePatch.cs` | Experimental opt-in selection of varied long holes from grounded scene samples. Each course record snapshots its configured `lengthMeters` targets when loaded; in-round measured distances cannot overwrite those future planning targets. Prefer points without nearby colliders or low overhead obstructions when at least two clear points exist; otherwise logs and falls back to all grounded samples. If the coarse grid finds fewer than six, retry at up to five-metre spacing. If distance/elevation windows leave holes unplanned, complete them from available grounded pairs at least 8 m apart; distances can depart from the preferred targets. Recalculate nominal distance and par from DGM's tee-pad front-middle and selected basket positions. This does not establish fairway reachability or water behavior. |
| `Services/CourseCatalogExporter.cs` | Exports built-in course/layout metadata for Course Studio. |
| `Services/CourseMapPreviewExporter.cs`, `Services/TexturePngEncoder.cs` | Exports map overview images as PNG. |
| `Runtime/ImportedMaterialTextures.cs` | Resolves textures from imported Unity materials for map rendering. |
| `Runtime/ImportedTreeCollisionProxies.cs` | Retained experimental proxy builder. The loader currently skips canopy-trigger generation after an IL2CPP crash; do not reconnect it until a safe integration and throw interaction are verified. |
| `Runtime/ImportedFoliageGeometry.cs`, `Runtime/ImportedFoliageInteraction.cs` | Opt-in approximate terrain foliage volumes and pure segment-intersection math; the disc physics hook slows a disc on entry and requests game foliage audio. This is independent of the disabled native proxy builder. On Tree Land, the user confirmed foliage reactions and audible foliage sound. The runtime log records the foliage interaction path, but actual disc-speed change, accuracy, performance, and broader compatibility remain unverified. |
| `Runtime/Patches/ImportedFoliageInteractionPatch.cs` | Runs the opt-in imported foliage approximation after the game's disc physics update. |
| `Diagnostics/FoliageDiagnostics.cs` | Optional diagnostics for imported foliage rendering; captures are grouped under `Diagnostics/Foliage/<course name>/` and logs include the course name. |
| `Diagnostics/ImportedShadowDiagnostics.cs` | Logs URP, quality, camera, light, terrain, and renderer shadow settings for imported maps; it does not change the game's render settings. |
| `CourseEditor/` | Browser course studio split into model/state, library, inspector, map canvas, file import/export, settings, event, and utility scripts. `app.js` is the final bootstrap; `index.html` loads classic scripts in dependency order. The folder also contains the local Node server and installed-map explorer; the source server can target another plugin data root and block direct API writes for review. |
| `CourseEditor/map-catalog.js` | Read-only discovery of installed imported course definitions, their scene bundles, dependency health, map-bounds status, and authored tee/basket counts. |
| `Tools/` | BONELAB and generic Unity scene import, cross-game layout interchange, and validation scripts. |

The source tree follows domain, runtime, service, and diagnostic responsibilities. Large responsibilities are kept in focused files; `CourseRegistry.Selection.cs` is a partial of `CourseRegistry`, and `CourseScenePatcher` is split between lifecycle/placement and imported-map rendering files. Its snapshots and temporary Unity objects remain shared so restore and cleanup stay centralized in `CourseScenePatch.cs`. Card image decoding and fallback rendering live in `CourseCardFactory`. All C# files use the `DgmCustomCourses` namespace; the SDK project compiles source recursively.

The installed-map explorer uses `GET /api/maps` to join course JSON files under `Courses` with bundles under `Maps`. It reports scene, bundle, and dependency health but does not inspect bundle contents. Its authoring check separately flags default/invalid overview bounds and holes that still need explicit tee/basket positions; `READY TO EDIT` means files and paths are present, not that a course is playable. Course JSON with an invalid or missing bundle path remains visible when it declares `mapScene`, so the author can open and repair its settings. The `server.js` scanner must stay read-only; only the explicit `PUT /api/install/...` route writes allowlisted course, image, and bundle files.

## Course data flow

~~~mermaid
flowchart LR
  JSON[Courses/*.json] --> Validate[CourseDefinition validation]
  Validate --> Registry[CourseRegistry]
  Registry --> Cards[Offline course selector]
  Round --> Capture[CourseSceneCardCapture]
  Capture --> LocalPreview[Config/CustomCourseLoader/Previews]
  LocalPreview --> Cards
  Registry --> Round[Course and hole patches]
  Round --> Map[MapSceneRuntime for imported scenes]
  Round --> Best[Local custom personal-best JSON]
  Best --> Cards
  Editor[Course Studio] --> Export[JSON + PNG + bundle files]
  Export --> JSON
  Game[Built-in selector] --> Catalog[CourseCatalogExporter]
  Catalog --> Editor
~~~

## Development boundaries

- Runtime compatibility is tied to DGM's game version, Unity version, BepInEx IL2CPP and generated interop assemblies.
- The editor binds its Node server to loopback. The server reads and writes only within its plugin package root.
- Imported map files are opaque Unity bundles. The loader does not translate source-game object types or rebuild assets.
- Game-generated `CourseEditor/catalog.js` and `CourseEditor/assets/maps` are local runtime outputs. Preserve them on install and omit them from public packages.
- The tool currently patches offline rounds only.

When moving code, preserve the namespace and let the SDK's recursive compile include follow the source directories. Keep this file, `AGENTS.md`, and build/package scripts aligned with the source tree.
