# Disc Golf Masters Course Studio

Course Studio is a local course authoring tool and BepInEx 6 IL2CPP plugin for offline rounds in **Disc Golf Masters**. It edits layouts on original DGM courses and can load compatible Unity scene bundles as imported map terrain.

## Get started

- Players: [installation, editing, and course-sharing guide](docs/PLAYER_GUIDE.md)
- Course authors: [course JSON format](docs/COURSE_FORMAT.md) and [map porting guide](docs/PORTING_GUIDE.md)
- Contributors: [architecture](docs/ARCHITECTURE.md), [AI project guide](docs/AI_PROJECT_GUIDE.md), and the root [AGENTS.md](AGENTS.md)
- Maintainers: [release checklist](docs/RELEASE_CHECKLIST.md), [troubleshooting](docs/TROUBLESHOOTING.md), and [third-party notices](THIRD_PARTY_NOTICES.md) with the exact referenced [third-party license texts](ThirdPartyLicenses/)
- Project license: [MIT-0](LICENSE)
- Verified build target and limitations: [supported environment](docs/SUPPORTED_ENVIRONMENT.md)
- Exact local compiler references: [build reference baseline](docs/BUILD_REFERENCE_BASELINE.md)
- Tools: [converter guide](Tools/README.md)
- Adapter authors: [adding a course converter](docs/CONVERTER_ADAPTERS.md)

## What it supports

- Custom offline course layouts based on the game's built-in courses.
- Hole par, nominal length, shot limit, tee and basket placement, marker visibility, OB polygons, and per-hole tee cards.
- Custom Unity scene bundles when their scene path, dependencies, platform, and Unity build are compatible with the installed game.
- BONELAB PC packed-pallet folder or ZIP conversion through `Tools/ConvertBonelabPallet.ps1`.
- Generic Unity AssetBundle packaging through `Tools/ConvertUnitySceneBundle.ps1`.
- Cross-game course-layout conversion through the normalized JSON format in `Tools/Schemas/course-layout-interchange.schema.json`.

The loader uses DGM's gameplay objects for hole behavior and scoring. It does not author terrain, convert Unity assets between engine versions, or add multiplayer course support. See the [porting guide](docs/PORTING_GUIDE.md) for format and compatibility limits.

## Local build

Build against a local Disc Golf Masters installation with BepInEx 6 IL2CPP interop assemblies:

~~~powershell
$gameRoot = 'C:\Games\Disc Golf Masters'
dotnet build .\CustomCourseLoader.csproj -c Release "-p:GameRoot=$gameRoot"
~~~

Run the command from the source project folder and replace `$gameRoot` with your Disc Golf Masters installation. A release archive stores source under `Source/CustomCourseLoader`, separate from the game folder, so pass the actual game path explicitly. Players should run the archive-root `Install-CourseStudio.ps1`; it updates the plugin/editor while preserving player courses, maps, generated catalogs/previews, and personal-best data. Contributors may use `.\build.ps1 -GameRoot $gameRoot` to build and install from source. Use `.\New-ReleasePackage.ps1 -Draft -GameRoot $gameRoot` to build, inspect, and audit a review archive. Public packaging remains gated on the open release checks and third-party distribution review. See [release checklist](docs/RELEASE_CHECKLIST.md).

## Release status

Course Studio is in active development. The current 0.5.96 build is a release candidate, not a public release. Draft packages are for local review only and must not be redistributed; remaining gates include qualified dependency-license review, clean-install and editor-flow acceptance, and user acceptance of imported-map rendering and gameplay. The 0.5.96 DRAFT archive is for local review only and is not redistributable. MIT-0 applies to original project code and documentation. Generated game files, imported map assets, original game assets, and player courses are excluded from release packages.
