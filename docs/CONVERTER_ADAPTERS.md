# Course converter adapters

This guide is for contributors who want to port course layouts from another game into Course Studio. A port has two independent inputs: **layout data** (holes, tees, baskets, rules, and OB) and **map content** (an optional scene and its dependencies). One converter does not automatically provide the other.

## Available paths

| Path | Layout data | Scene content |
| --- | --- | --- |
| BONELAB PC pallet adapter | Creates a starter layout; tee and basket data must be mapped separately. | Selects a packed pallet scene and dependencies without rebuilding them. |
| Generic Unity bundle adapter | Uses a normalized layout or manual authoring. | Packages a compatible UnityFS scene bundle and caller-supplied scene path/dependencies. |
| Normalized layout adapter | Converts the shared JSON format to DGM course JSON. | None for an original DGM course; for an imported scene, the JSON points to bundle files already installed by a scene adapter. |

See [Tools/README.md](../Tools/README.md), the [porting guide](PORTING_GUIDE.md), and the [interchange schema](../Tools/Schemas/course-layout-interchange.schema.json) for commands and exact fields. The BONELAB sample is an example of the interchange shape; it is not extracted from a real BONELAB layout export.

## Coordinate contract

Choose one coordinate space for the whole layout:

- `courseOffsets`: use an existing DGM course as the scene. `teeOffset` and `basketOffset` are meters in the DGM local right/up/forward axes relative to that hole's original tee and basket. Do not include `mapScene`, imported world bounds, lighting, or absolute tee/basket positions.
- `unityWorld`: use an already-installed imported Unity scene. `teePosition` and `basketPosition` are absolute Unity X/Y/Z coordinates in meters. Include `mapScene` with the internal `.unity` scene path and plugin-relative bundle paths. Bounds and lighting settings are optional.

For both spaces, yaw values are degrees. OB polygon points are meters in right/forward coordinates local to the original tee orientation; they do not use absolute map X/Z. Convert source units and axes explicitly. Do not guess whether a source coordinate is world-relative or tee-relative.

## Adding a native source-game adapter

1. Identify a documented or otherwise legally usable export for the source game. Record the export version and the coordinates, units, rotation convention, par, distance, and OB representation it uses.
2. Add a small adapter under `Tools/` that reads that export and writes JSON compatible with `Tools/Schemas/course-layout-interchange.schema.json`. Keep source-specific parsing and coordinate conversion in the adapter.
3. If the source provides scene bundles, use `ConvertUnitySceneBundle.ps1` when they are already UnityFS files, or add a scene adapter that packages them through `Tools/Modules/CoursePackage.psm1`. Do not duplicate the shared DGM package writer.
4. Validate IDs, relative paths, ranges, duplicate holes, missing values, and coordinate conversion. Refuse existing outputs by default; require an explicit overwrite option and restore prior files if validation fails.
5. Add a small redistributable fixture, a passing example, and failure cases that run only in a temporary directory. Do not use an installed game folder or anyone's private map assets as a test fixture.
6. Update `Tools/README.md`, this guide, `PORTING_GUIDE.md`, and the release checklist. Describe exactly what the adapter parses and what it leaves for manual authoring.
7. Validate the generated course package, open it in Course Studio, then test all holes in an offline game round. Keep package validation, source-format coverage, and in-game compatibility as separate claims.

Native support should only be claimed when the adapter parses real source-game layout data. If no documented export exists, use manual mapping or request an authorized sample; do not redistribute protected data. A layout converter cannot upgrade Unity assets, recreate missing scripts or shaders, or establish redistribution rights for bundles.

## Converter contribution review

- Input and output formats and source game/version are named in the tool's help and documentation.
- Layout-only conversion and scene-bundle conversion remain separate steps unless the source format genuinely combines them.
- Output is validated by `Tools/ValidateCoursePackage.ps1` and uses the shared `CoursePackage.psm1` writer for staging and rollback.
- Fixtures contain no original game files, licensed assets, private courses, or real user paths.
- Checks cover malformed input, unsafe paths, missing dependencies, scene and dependency bundle headers, duplicate IDs, and overwrite/rollback behavior where applicable.
- The release checklist distinguishes passing fixture checks from a successful in-game round.
