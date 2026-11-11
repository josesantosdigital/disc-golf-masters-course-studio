# Supported environment

## Confirmed development baseline

This baseline is specific to the development PC and is not a minimum-version or compatibility promise. Version 0.5.117 is the public experimental beta. The 0.5.116 run-up camera correction was confirmed on one imported course; the 0.5.117 OB overview alignment passed automated checks but has not been verified in game. Clean-game installation and broader in-game checks remain pending. Offline courses hide tee and card artwork by default, with independent per-hole options for custom layouts. A local 711 lighting configuration improved the reported glare but did not fully eliminate it.

## Historical baseline observations

Historical 0.5.48/0.5.49 observation: the source included the imported-map HUD refresh and course-loading flow. At that point, all four target BONELAB layouts were nine-hole `autoLayout: long` starters with zero explicit tee/basket pairs, and Teeny Tiny Isle, Southside, and Milsim Forest omitted `mapWorldBounds`; the Milsim round was the only runtime check. The later 0.5.49 diagnostic candidate logged renderer paths/bounds for unresolved textureless materials when `ignoreSourceTint` was enabled.

The 0.5.24 Tree Land round produced user-confirmed shrub/leaf reactions and foliage sound. That result is version-specific; current-round disc slowdown, performance, branch movement, shadows, landing audio, chain visibility, and tee-card readability have not all been verified together. Selector scrolling, personal-best completion/display/persistence, and clean-game installation also remain open. These are observed versions, not minimum-version promises:

## Observed platform and current evidence

| Component | Observed baseline |
| --- | --- |
| Operating system | Windows 10, 64-bit |
| Disc Golf Masters | Steam App ID 2819400, build 26127348 |
| Unity player | 6000.3.26f1 |
| Imported BONELAB bundle versions | All 11 scene/dependency bundles for Teeny Tiny Isle, 711 DGC, Milsim Forest, and Southside report Unity 2021.3.16f1 in their UnityFS headers, while DGM uses 6000.3.26f1. This is the older-bundle/newer-player direction; Unity documents TypeTree-based backward compatibility for many assets, but substantial feature changes can still fail and per-course runtime compatibility remains unverified. |
| BepInEx | 6.0.0-be.791, IL2CPP |
| Managed runtime reported by BepInEx | .NET 6.0.7; the plugin target remains net6.0 for this host |
| SDK for current contributor checks | CI is configured for the supported .NET 10 SDK. The local .NET 6 SDK remains a legacy fallback for pure checks; the .NET 10 CI run has not yet been observed for this candidate |
| Node.js used for the local editor server | 24.22.1 |
| Public beta | 0.5.117, target framework net6.0. Release build, coordinate, converter and disposable installer checks passed; the OB fix has not been verified in game. |
| Earlier 711 lighting observation | Glare persisted on 0.5.106. A later local configuration improved it but did not fully eliminate it. Results vary by imported map and settings. |
| Built-in catalog evidence | 0.5.22 startup exported 3 original courses with 11 layouts |
| Failed map-load log | 0.5.18 |

The game installation's `BepInEx/LogOutput.log` records the OS, game build environment, Unity, BepInEx, and runtime versions. A 0.5.16 imported-map run loaded Tree Land and all nine holes, but its sparse proxies did not affect throws. The user reported very dark rendering, no trunk/rock/shrub shadows, foliage without wind or impact response, and chains appearing only after first impact; performance was not measured. Version 0.5.18 crashed during imported-map setup with an `AccessViolationException` in `Dgm.FoliageSweep.Classify` from `ImportedTreeCollisionProxies.AddCanopyShapes`. Version 0.5.19 removed that classifier call; the user confirmed a Tree Land round started without a crash. Its selector log recorded `Method unstripping failed` at `RectTransformUtility.CalculateRelativeRectTransformBounds`. The generated built-in catalog contains three original courses and eleven layouts. On 0.5.20, the user reported audible ground impacts, harsh lighting, a floating/transparent tee sign, and no branch/shrub response or foliage sounds. On 0.5.22, the runtime log shows selector layout sizing completed without that error, Tree Land loaded, all nine hole positions were applied, the base sun and ambient settings were applied, the audio fallback ran, and basket chain rigs were awakened. The user confirmed that Tree Land's shadows now look good. Tee-sign loading failed through the IL2CPP `ReadOnlySpan<T>.GetPinnableReference` path; 0.5.24 includes the Course Studio PNG decoder replacement, but the sign has not been visually confirmed. Version 0.5.24 adds an opt-in approximation for terrain-tree prefab meshes whose materials have leaf-named identifiers, with a 3,000-instance cap. The user confirmed shrub/leaf reactions and foliage sound in Tree Land; 0.5.24 logs record 342 prepared foliage volumes and entry into the disc-slowing path, and the latest 0.5.25 run loaded Tree Land with all nine hole positions. Performance, foliage appearance, and actual disc slowdown remain unmeasured; the approximation does not animate branches and may produce false hits. Native canopy-trigger generation remains disabled, and the editor marks that separate control unavailable. This is a development install with player data, not a clean BepInEx install; see the [release checklist](RELEASE_CHECKLIST.md).

The BONELAB adapter was also run against two user-provided, locally authorized PC pallet ZIPs. Tree Land and Hood Corner Day scenes were selected from their real Marrow catalogs; the converter copied scene and dependency bundle bytes unchanged, generated starter course cards, validated the packages, and removed temporary output. The installed Tree Land package from those sources loaded in the DGM round described above. A later 0.5.24 log records Hood Corner loading, all nine hole placements, and a post-throw hook. The map had no active directional light, so the loader added a temporary DGM key light; a base-terrain collision-audio fallback also ran. These logs do not confirm that all holes were reached or describe user-visible lighting/audio quality. This demonstrates those two PC pallets only; it does not make BONELAB course-layout extraction native support or grant redistribution rights. The generic Unity adapter was separately exercised with the Tree Land UnityFS scene and dependencies, preserving its bytes and validating a temporary package. That exact scene was loaded through the BONELAB adapter, but the generic adapter's output was not separately launched. Neither converter includes source or output map assets in public archives.

A read-only review of the installed Course Studio found three original courses and two installed map packages; both map packages reported ready with their scene bundles and dependencies present. Search narrowed the installed list to Tree Land, Ready/Missing status filters returned the expected results, and rescan rediscovered both packages. Canon Park and Tree Land nine-hole overviews rendered; zoom changed from 100% to 122% and Fit returned to 100%. The tee/basket position, OB, and card panels were available. This confirms installed-editor discovery and display on this PC, not end-to-end editing/export or in-game selector reachability.

The project currently documents this one verified build target. It does not claim compatibility with older BepInEx builds, other DGM builds, Linux/Proton, or non-PC platforms. Recheck the baseline after DGM, Unity, or BepInEx updates.

## Player requirements

- Windows PC copy of Disc Golf Masters.
- BepInEx 6 IL2CPP in the game folder. One-click setup uses an existing installation or downloads the pinned IL2CPP build when missing. Course Studio does not bundle BepInEx, Harmony, Unity interop assemblies, or game files.
- A desktop browser to use the local Course Editor.
- Node.js to start the editor's local loopback server. One-click setup uses an existing Node.js or downloads a portable runtime when missing. Node 24.22.1 was observed on the development PC; older versions are unverified.
- For authoring/building from source: .NET 10 SDK recommended (or .NET 6 SDK as a legacy fallback), Windows PowerShell, and a local DGM/BepInEx installation whose assemblies are used as build references. The plugin remains targeted at net6.0 to match the observed game host; using the .NET 10 SDK does not change that runtime target.
- For imported maps: a PC Unity scene AssetBundle, its exact `.unity` scene path, dependencies, and platform/Unity serialization that the installed DGM player can load.

The BONELAB adapter targets **PC** pallets. It packages the source scene and selected dependencies; it does not rebuild the content for DGM or translate BONELAB course data. Quest-only bundles have not been verified in DGM's Windows player; other game-specific archive formats need a separate adapter.

Unity does not support forward compatibility for AssetBundles: content built with a newer Unity version may not load in an older player. A matching or older bundle version is not a guarantee because serialized types, scripts, shaders, rendering pipelines, and platform settings can still differ. Test every imported map in DGM. See Unity's [AssetBundle compatibility guidance](https://docs.unity.com/en-us/engine/7000.0/manual/assets-and-media/assets-managing-runtime/assetbundles-section/asset-bundles-intro).

## Support report details

When reporting a compatibility issue, include the DGM Steam build, Windows version, BepInEx version, Course Studio version, and relevant error excerpt from `BepInEx/LogOutput.log`. Do not include game files, imported bundles, or private course content unless the maintainer asks and the author has permission to share it.
