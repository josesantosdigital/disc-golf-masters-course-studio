# Course JSON format

Course definitions are UTF-8 JSON files stored in `BepInEx/plugins/CustomCourseLoader/Courses`. The loader accepts `schemaVersion` 1 and 2; Course Studio exports version 2. A course needs an ID, name, base course, and one to eighteen holes.

## Minimal course

~~~json
{
  "schemaVersion": 2,
  "id": "sample-meadow-nine",
  "name": "Sample Meadow Nine",
  "baseCourse": "CanonPark",
  "baseLayout": "",
  "description": "A nine-hole example course.",
  "holes": [
    {
      "par": 3,
      "lengthMeters": 75,
      "shotLimit": 0,
      "teeOffset": { "right": 0, "up": 0, "forward": 0 },
      "basketOffset": { "right": 2, "up": 0, "forward": 62 },
      "teeYawDegrees": 0,
      "basketYawDegrees": 15,
      "teeEnabled": true,
      "basketEnabled": true,
      "obZones": []
    }
  ]
}
~~~

The example has one hole for readability. A playable course file contains its full layout. For imported maps, include a `mapScene` object and use `mapTeePosition` plus `mapBasketPosition` for each hole, or omit map positions on every hole and let the loader place them automatically.

When the selector first adds custom courses, a loading screen covers the list until the entries are registered and its layout has settled, or a safety timeout expires. For example, selecting the list containing Sample Meadow Nine shows “Loading custom courses” during preparation. The loader does not show this screen when entering a course. This behavior does not change the course JSON.

For a scenic selector image, the loader attempts to capture the current hole from a separate camera after a custom round has loaded. After a successful capture, the next course selection can use that scenery instead of the configured course-card image. Captures are cached under `BepInEx/config/CustomCourseLoader/Previews` using the course ID and a fingerprint of its JSON; editing the JSON causes a new capture attempt on the next play. The original card files are preserved and used when no rendered capture is available. Per-hole tee signs continue to use the configured card images. Selector badges use a shared custom disc-golf emblem. Capture appearance and timing depend on the loaded scene and camera and still need in-game verification.

## Top-level fields

| Field | Required | Meaning |
| --- | --- | --- |
| `schemaVersion` | yes | Supported values: `1` and `2`. |
| `id` | yes | Stable file/course key, 1–48 ASCII letters, numbers, `_` or `-`. |
| `name` | yes | Display name, 1–64 characters. |
| `baseCourse` | yes | A DGM course enum ID, for example `CanonPark`, `MapleHill`, or `NokiaDiscGolfPark`. |
| `baseLayout` | no | Optional built-in layout ID used to seed editor defaults. |
| `description` | no | Short description. |
| `courseCardImage` | no | PNG filename beside the JSON, used as selector fallback until a scenic preview is captured and as an imported tee-sign fallback. |
| `autoLayout` | no | `long` enables experimental terrain-sampled imported holes; requires `mapScene` and no explicit map positions. Each hole's `lengthMeters` is its preferred target distance, allowing each course to define its own distance profile. The loader snapshots those configured targets when it loads the course definition, so the actual tee/basket distance measured after placement does not change the profile used for a later round. Targets scale to the map's usable span; the placed tee/basket distance and par are recalculated in memory. The loader prefers grounded points with nearby and overhead clearance and rejects candidate surfaces whose collider or parent names indicate a roof, building, house, or sky. These objects remain collidable obstacles; they are not retagged as ground. If the coarse sample finds fewer than six grounded points, it retries with a denser grid (up to 5 m spacing). If normal distance/elevation targets cannot complete the layout, remaining holes may use any available grounded pair at least 8 m apart; resulting holes can be much shorter or longer than the preferred targets. Placement is skipped when there are fewer than two usable points or no pair at least 8 m apart. |
| `layoutGroundColliderExclusions` | no | Up to eight case-insensitive name fragments used by `autoLayout: long`. A candidate ground collider is skipped if its object or any parent name contains one of these values. This is useful when a large decorative terrain collider covers playable streets or a city floor. It changes hole sampling only; it does not disable collision during play. |
| `waterLandingClearanceMeters` | `4` | Imported maps only. Detected water meshes are registered with the active DGM out-of-bounds manager and checked against their mesh footprint and local triangle height. A thrown disc that is grounded within this many meters above a detected water surface is treated as a water hazard. Readable ground MeshColliders can have triangles removed where they sit at or below the waterline. Keep this close to the surface: large values can mark a disc as wet while it is still at an elevated tee. Range: 0–25 m. Does not move visible water or change automatic tee/basket sampling. |
| `holes` | yes | 1–18 hole definitions. |
| `mapScene` | no | Imported Unity scene bundle configuration. |
| `mapWorldBounds` | no | Imported map X/Z bounds set editor coordinates, constrain automatic hole sampling, and define the fallback normal-play rectangle. The play rectangle expands beyond the configured bounds by 5% of the larger span, clamped to 10–40 m; too-wide bounds can put holes in empty outskirts, while too-tight bounds can make nearby fairway throws OB. `groundY` guides default marker snapping and sampled-ground selection. Bounds do not rescale the scene. |
| `mapLighting` | no | Optional imported-scene rendering adjustments, including map-specific camera occlusion handling. |

  The course format recognizes `CanonPark`, `MinokaSprings`, `BaldurDgc`, `MapleHill`, `ChampionsLanding`, `WinthropGold`, `NokiaDiscGolfPark`, and `Terminalen` IDs. This list describes accepted identifiers, not courses available in every game build. For a playable base scene, choose a course exported by the installed game; the Course Studio Original courses tab is generated from that build's loaded course catalog.

For example, add `"autoLayout": "long"` to an imported course with `mapScene` and holes that omit `mapTeePosition` and `mapBasketPosition`, as shown in [course-v2-auto-layout.example.json](../Tools/Samples/course-v2-auto-layout.example.json). At round setup the loader samples level ground, attempts to avoid separately named water surfaces, and ignores tall combined renderers whose materials happen to include a water shader. It rejects candidate colliders under objects named as buildings, houses, roofs, or sky, while leaving those source colliders intact as obstacles. It also filters points with nearby or overhead obstructions when at least two clear locations remain, then chooses varied hole directions around each hole's configured target length. Sparse island maps receive a finer retry. If the preferred distance/elevation windows leave holes unplanned, the loader fills them from available grounded pairs at least 8 m apart, logs that fallback, and recalculates nominal length and par in memory. After placing an imported layout, it refreshes the in-round hole information panel so its displayed par and distance reflect the resulting DGM tee and basket positions. Such fallback holes can cross water or miss the preferred targets, so a complete placement log does not prove that the map is playable. JSON values shown before the first load are estimates. These checks reduce placements inside buildings but cannot guarantee reachable fairways, safe water behavior, or suitable positions on every map; check each map in game. Exporting from Course Studio supplies explicit coordinates and replaces the automatic layout.

## Hole fields

| Field | Default | Constraints / meaning |
| --- | --- | --- |
| `par` | `3` | Integer 1–8. |
| `lengthMeters` | `60` | Nominal integer distance, 1–2000 meters. |
| `shotLimit` | `0` | Integer 0–20. Zero is unlimited. |
| `pinVariant` | `Default` | DGM basket pin variant name. |
| `teeVariant` | `Default` | DGM tee variant name. |
| `teeOffset` | zero | Local right/up/forward offsets in meters on original course terrain. |
| `basketOffset` | zero | Local right/up/forward offsets in meters on original course terrain. |
| `teeYawDegrees` | `0` | Additional tee yaw in degrees. |
| `basketYawDegrees` | `0` | Basket yaw in degrees. |
| `teeEnabled` | `true` | Show/hide the tee art. |
| `basketEnabled` | `true` | Show/hide basket art; scoring trigger behavior remains managed by DGM. |
| `obZones` | `[]` | Up to 12 polygon zones, 3–64 points each. `right` and `forward` are meters in the hole's tee-relative horizontal frame. |
| `cardImage` | empty | Optional per-hole PNG filename beside the course JSON. |
| `mapTeePosition` | absent | Imported-map absolute Unity world position `{x,y,z}`. Supply alongside basket position. If `y` matches `mapWorldBounds.groundY` (or the scene minimum when bounds are omitted), the loader snaps the marker to nearby valid ground at that X/Z; use a different Y for an intentional elevated placement. |
| `mapBasketPosition` | absent | Imported-map absolute Unity world position `{x,y,z}`. Supply alongside tee position. If `y` matches `mapWorldBounds.groundY` (or the scene minimum when bounds are omitted), the loader snaps the marker to nearby valid ground at that X/Z; use a different Y for an intentional elevated placement. |

Offsets and positions must be finite. PNG names must be simple `.png` filenames in the course folder; paths to parent folders are rejected. OB polygons and coordinates are bounded by loader validation.

Custom solo rounds use a local personal-best file at `BepInEx/config/CustomCourseLoader.personal-bests.json`, separate from official-course progress. A result is recorded only after every hole in the custom layout has a score, and lower scores relative to par replace higher ones. Records use the stable custom layout ID derived from the course `id`; for example, a course with `id` `forest-nine` uses `custom_forest-nine`. Changing a layout while retaining its ID retains its existing local best. An unreadable personal-best file is preserved rather than overwritten. In-game save and display behavior requires validation for the installed game version.

## Imported scene fields

~~~json
{
  "mapScene": {
    "scenePath": "Assets/Levels/MyLevel.unity",
    "bundlePath": "Maps/my-level/scene.bundle",
    "dependencyBundlePaths": [
      "Maps/my-level/dependencies/MonoScripts.bundle"
    ]
  },
  "mapWorldBounds": {
    "minX": -100,
    "maxX": 100,
    "minZ": -100,
    "maxZ": 100,
    "groundY": 0
  },
  "waterLandingClearanceMeters": 0.25,
  "mapLighting": {
    "preserveLightmaps": true,
    "disableCameraOcclusionCulling": false,
    "supplementLighting": true,
    "forceUnlitMaterials": false,
    "localLightIntensityMultiplier": 1,
    "useBaseSkybox": true,
    "ambientIntensity": 1.2,
    "keyLightIntensity": 2.5
  }
}
~~~

In this example, the imported map uses the base-course collision-audio fallback described below; no additional audio field is needed. Imported tees show a sign with the hole's `cardImage`, or `courseCardImage` when the hole has none; that fallback is course artwork, not a hole layout. `scenePath` is an asset path inside the AssetBundle. Bundle paths are relative to the plugin root and may not contain `.` or `..` path segments. Every declared dependency bundle is required: if one is missing or Unity rejects it, map setup aborts and rolls back instead of loading a partial scene. All explicit map positions must be supplied for every hole or omitted for every hole. `mapWorldBounds` maps the editor canvas to Unity coordinates, constrains automatic placements, and defines the fallback in-bounds play area with a 10–40 m margin; it does not rescale the scene.

For collision setup, the loader resolves BoxCollider and MeshCollider components by instance identity when Unity exposes a contact through a base `Collider` wrapper. Readable non-static-batched render meshes may be used to improve collision alignment. Static-batched renderers keep their authored colliders because their MeshFilter can reference a combined mesh containing unrelated scene geometry. Their native per-renderer culling bounds are also kept; the shared mesh bounds do not describe an individual renderer. These imported-collision and rendering paths require in-game verification for each map; a valid JSON package does not prove that objects, water, terrain, or culling behave correctly.

For imported rounds, hiding the base terrain also disables its DGM `SurfaceMapper` through Unity's component lifecycle. A disabled Terrain or TerrainCollider alone is insufficient: DGM maintains a separate active heightfield registry and can push discs onto that hidden heightfield. The loader also clears hidden cached terrain references from discs after throws and before physics updates, and invalidates any cached model belonging to that hidden terrain. Original mapper states and surviving disc terrain references are restored during cleanup. Valid imported terrain models remain available. This is based on terrain ownership, not map names or a fixed world height, and applies to future imported scenes using the same loader path.

When a disc has no valid native Terrain reference, its ground-distance and grounded fields are sampled from physical surfaces in the imported scene. The nearest upward-facing support wins, including a roof or bridge above a street; water and triggers are excluded. No-support locations stay unsupported. The former upward-teleport clamps, grounded getter filters, and endpoint relocations are removed in 0.5.95; jump diagnostics do not move the disc or block completion. The included imported-island example requires no new JSON setting. The user confirmed that the city-top snap is fixed on 0.5.95. This shared fix applies to future imports; it does not establish that every future map has correct authored geometry, water, or materials. Built-in-course restoration and other map-specific behavior still need acceptance coverage.

If a declared dependency bundle is missing or rejected, imported-map setup cannot place every configured hole, cannot find a usable tee/basket, or throws while applying scene changes, the loader restores the captured base-course and rendering state, then unloads the failed imported scene and its bundles. It does not report a partial layout as loaded or fall back to scene-bound height when a grid placement has no ground. This rollback does not make the map playable; correct bundle paths, grounded placements, collisions, and rendering still need validation.

The current runtime also recognizes rendering options such as `forceFallbackMaterials`, `ignoreSourceTint`, `repairTerrainMaterials`, `repairTerrainTextureDetails`, `hideHoloWalls`, `forceTreeMeshLods`, `useLitFoliageMaterials`, `exportFoliageDiagnostics`, `albedoBrightness`, ambient/key colors and intensities, key-light yaw/pitch, and `localLightIntensityMultiplier`. The local-light multiplier scales imported point and spot light intensities before material repair; its default is `1`, and values from `0` to `5` are supported. Original intensities are restored when the map scene is cleaned up. `disableCameraOcclusionCulling` defaults to `false`; when enabled, it disables occlusion culling on cameras found at map setup and rescans every 0.5 seconds while the map is active, disabling culling on cameras activated or created later. The original setting for each affected camera is restored during cleanup. This can avoid visibility errors when a map's baked occlusion data does not match the active camera/scene, at the cost of additional rendering work. Course Studio exposes this switch, the multiplier and terrain controls under **Map settings → Advanced lighting → Terrain & vegetation**. The lit foliage option tries DGM's URP Lit alpha-test variant; results depend on the shader variant present in game. Use terrain repairs selectively and verify them in game because they can change appearance or performance. `exportFoliageDiagnostics` writes selected source textures under `Diagnostics/Foliage/<course name>` so captures retain their source course; check asset permissions before sharing them. See `Domain/CourseDefinition.cs` for validation ranges and update this table and the JSON Schema when adding options.

From 0.5.96, ordinary rendering automatically ports the recognized source shader `Universal Render Pipeline/Lit (PBR Workflow)` to DGM's supported `Universal Render Pipeline/Lit`. Device support of the old compiled shader does not establish compatibility with the host lighting pipeline. The conversion retains shared textures, UV transforms, tint, normal/PBR maps, emission, cutout thresholds, culling, blend/depth state and render queue, and remaps compatible material keywords by name. The source-specific packed `_DetailMap` layer is omitted because it is not a native URP detail texture. Water, particle and other custom shader families are excluded. Explicit `forceUnlitMaterials` or `forceFallbackMaterials` modes take precedence. If the native Lit shader is unavailable, the source material is retained and a warning is logged. Original materials are restored on cleanup; no bundle is rewritten and no new JSON option is required. This lighting candidate is compiled and tested for state transfer/rollback offline, but glare and visual appearance still require a user-run check.

When an imported material has no color texture, albedo-only rendering can reuse a texture from another material with the same name after removing a trailing duplicate suffix such as ` 1` or `.001`. The known 4×4 `DummyMS_00` placeholder is ignored during color-texture discovery so matching source materials can supply their real texture. Directly assigned usable textures take precedence; ambiguous sibling matches are ignored. This is a best-effort recovery for duplicated source materials, not a replacement for missing source assets.

`realtimeMapShadows` is an experimental opt-in for imported maps. Course Studio exposes it under **Map settings → Advanced lighting**. It switches compatible fallback materials to the game's URP lit shader, enables shadow casting and receiving on imported mesh renderers and terrain, and enables realtime shadows on an imported directional light (or the loader's fallback key light). Results depend on the map, game shadow settings and available shader variants; performance and appearance may change. Its default is `false`, preserving existing map behavior. The loader logs pipeline, camera and light shadow settings to help diagnose missing shadows; these diagnostics do not change game quality settings.

`hideImportedParticles` defaults to `false` and is also exposed under **Map settings → Advanced lighting**. When enabled, it hides imported renderers that use a particle shader, which can remove distracting effects from incompatible maps. This can also hide effects intended by the map author.

Imported-map collision audio uses the base course's terrain surface mapping as a fallback. Before DGM handles an upward-facing contact on an imported collider, the loader maps that collider to DGM's Terrain layer so unsampled ground surfaces use the terrain collision behavior. Side contacts remain on their authored layer. If audio still sounds metallic, the bounded throw diagnostics include the collider's scene and PhysicMaterial; the resulting ground sound may still differ from the imported map's original audio.

`generateTreeCanopyColliders` defaults to `false` and is temporarily unavailable while the game's foliage integration is repaired. The loader skips this option even if an existing course sets it to `true`. Earlier prototype colliders did not affect throws, and a later diagnostic call into the game's classifier caused a native crash. Do not enable or redistribute that diagnostic.

`experimentalFoliageInteractions` defaults to `false`. When enabled, the loader approximates imported Unity terrain tree instances whose prefab meshes use a material named with `leaf`. It builds simple volumes, slows the disc on entry and asks the game to play a foliage hit sound. Terrains with more than 3,000 instances are skipped. This does not create native foliage colliders, use the unsafe classifier, animate foliage, or handle every kind of imported vegetation. The user confirmed Tree Land foliage reactions and audible foliage sound. The runtime log records the foliage interaction path, but actual disc-speed change, hit accuracy, performance, and compatibility with other maps remain unverified.

`useGameFoliageShader` attempts to render compatible imported tree leaves with DGM's foliage shader, which supports wind and disc-impact effects; source mesh attributes and textures may not be compatible. `preferBaseCourseSun` uses the active base course's sun and temporarily disables imported directional lights when a base sun can be found. Both are opt-in and exposed in Course Studio. During imported rounds, enabled lights belonging to the hidden base course are disabled except for the selected base sun when this option is enabled; their previous enabled states are restored on cleanup. Realtime-shadow mode also applies the configured ambient color and intensity during an imported round; settings and imported light states are restored on cleanup.

When `repairTerrainMaterials` is enabled and `preserveLightmaps` is false, the loader clears imported terrain lightmap indices as well as mesh renderer lightmap indices; original terrain indices are restored on cleanup. Set `preserveLightmaps` to `true` when the imported scene includes its complete baked `LightmapData` and referenced textures. In this mode the loader leaves renderer/terrain lightmap indices untouched and does not query Unity's IL2CPP `LightmapData` array, which may be unavailable in some builds. With preservation off, invalid indices are cleared individually where they can be inspected. Confirm appearance in game before treating the lighting as accepted. If the bundle has no baked maps, leaving this option `false` avoids stale indices.

With `realtimeMapShadows` enabled, the loader records one additional shadow diagnostic after a throw completes; this does not add a per-frame polling loop.

Run `Tools/ValidateCoursePackage.ps1 -CoursePath <file> -PluginDirectory <plugin-folder>` before sharing manually-authored JSON.

For layouts coming from another game, see `Tools/Schemas/course-layout-interchange.schema.json` and `Tools/ConvertCourseLayoutJson.ps1`. The interchange format accepts coordinates already mapped to DGM local offsets or Unity world positions; native game exporters still need a game-specific mapping step.
