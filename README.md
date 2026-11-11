# Disc Golf Masters Course Studio

Course Studio is a local course editor and custom course loader for offline rounds in **Disc Golf Masters**. Create layouts on original courses or play compatible imported maps. Windows PC only; custom multiplayer is not supported.

## Install and play

1. Close Disc Golf Masters and download the latest Course Studio **Beta setup EXE** from [GitHub Releases](https://github.com/josesantosdigital/disc-golf-masters-course-studio/releases). Run it and select your game folder if asked. This installs the editor and loader together.
2. Start the game once and open its offline course selector, then use the **Disc Golf Course Studio** desktop shortcut.
3. Install a separate custom course package or create and export a layout in the editor. Restart the game, open the offline course selector, and choose its custom course card.

Follow the [player guide](docs/PLAYER_GUIDE.md) for step-by-step setup, map installation, editing, exporting, updates, and troubleshooting. For converting a BONELAB PC map, see the [conversion guide](docs/PORTING_GUIDE.md#convert-a-bonelab-pc-map). The installer and loader do not include imported maps; obtain them separately.

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
- Separate per-course ZIP packaging for layouts and authorized imported map bundles through `Tools/New-CourseModPackage.ps1`.

The loader uses DGM's gameplay objects for hole behavior and scoring. It does not author terrain, convert Unity assets between engine versions, or add multiplayer course support. See the [porting guide](docs/PORTING_GUIDE.md) for format and compatibility limits.

## Local build

Build against a local Disc Golf Masters installation with BepInEx 6 IL2CPP interop assemblies. The plugin stays on `net6.0` to match the game's observed .NET 6 host. Use the current .NET 10 SDK for source work; the SDK can target this older framework. The explicit property below suppresses only the SDK's out-of-support target warning and does not change the plugin's runtime target.

~~~powershell
$gameRoot = 'C:\Games\Disc Golf Masters'
dotnet build .\CustomCourseLoader.csproj -c Release "-p:GameRoot=$gameRoot" "-p:CheckEolTargetFramework=false"
~~~

Run the command from the source project folder and replace `$gameRoot` with your Disc Golf Masters installation. A release archive stores source under `Source/CustomCourseLoader`, separate from the game folder, so pass the actual game path explicitly. Players can use the one-click setup or the archive-root `Install-CourseStudio.ps1`; both preserve player courses, maps, generated catalogs/previews, and personal-best data. Contributors may use `.\build.ps1 -GameRoot $gameRoot` to build and install from source. Use `.\New-ReleasePackage.ps1 -Draft -GameRoot $gameRoot` for a local review archive or `-Prerelease` for an explicitly experimental beta. Stable packaging remains gated on the open checks in the [release checklist](docs/RELEASE_CHECKLIST.md).

## Release status

Course Studio is in active development. Version 0.5.117 is an experimental beta. The run-up camera correction has been confirmed by a player on one imported course; the OB overview-alignment update passed automated checks but has not been verified in game. Clean real-game installation, broader course/gameplay acceptance, and review of LGPL-linked dependency distribution questions remain open. MIT-0 applies to original project code and documentation. Generated game files, imported map assets, original game assets, and player courses are excluded from release packages. DRAFT archives remain for local review; BETA archives identify the experimental distribution separately.
