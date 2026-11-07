# Third-party notices and distribution boundary

## Current archive contents

The release packager selects the Course Studio plugin, browser editor, a generic sample course JSON, documentation, converter utilities, pinned third-party license texts, and package metadata. Its archive audit rejects DGM game assemblies, BepInEx/Harmony/Il2CppInterop runtime DLLs, Unity assemblies, map bundles, generated previews, images, fonts, third-party implementation source files, and player course files. Players install Disc Golf Masters and BepInEx separately.

The source project builds against local Disc Golf Masters assemblies, BepInEx 6 IL2CPP, HarmonyX, Il2CppInterop, and Unity interop assemblies. These are build/runtime references and are not copied into the package. The dependency names below identify the upstream projects used by the build; a release owner must confirm the applicable distribution terms for the plugin before publication.

| Referenced project | Exact observed build reference | Upstream license record and included copy | Runtime library bundled |
| --- | --- | --- | --- |
| BepInEx Core and Unity IL2CPP | `6.0.0-be.791`, commit `b782da4ac54e1072d2857f4a2f77c80c3ef08005` | The pinned [BepInEx license](https://github.com/BepInEx/BepInEx/blob/b782da4ac54e1072d2857f4a2f77c80c3ef08005/LICENSE) identifies LGPL-2.1-or-later. Verbatim copy: `ThirdPartyLicenses/BepInEx-LICENSE.txt`. | No |
| HarmonyX / Harmony | `0Harmony` 2.10.2 | The [HarmonyX 2.10.2 license](https://github.com/BepInEx/HarmonyX/blob/v2.10.2/LICENSE) and its [Harmony license](https://github.com/BepInEx/HarmonyX/blob/v2.10.2/LICENSE.Harmony) identify MIT terms. Copies: `ThirdPartyLicenses/HarmonyX-LICENSE.txt` and `ThirdPartyLicenses/Harmony-LICENSE.txt`. | No |
| Il2CppInterop Runtime and HarmonySupport | `1.5.3`, commit `dbda1cb353b0f4253345dc45136d170b9e50a5a0` | The pinned [Il2CppInterop license](https://github.com/BepInEx/Il2CppInterop/blob/dbda1cb353b0f4253345dc45136d170b9e50a5a0/LICENSE) is LGPL-3.0 and includes the GPL-3.0 text incorporated by LGPL-3.0. Verbatim copy: `ThirdPartyLicenses/Il2CppInterop-LICENSE.txt`. | No |
| Unity, URP, and Disc Golf Masters assemblies | Unity `6000.3.26f1`, DGM build `26127348` | Supplied through the player's installed game/modding environment; this project does not grant rights to those products. | No |

The four license copies are taken from the exact upstream revisions listed above. Their license notices and terms are included at the archive root and in the source tree. Assembly hashes and the full build reference list are in [BUILD_REFERENCE_BASELINE.md](docs/BUILD_REFERENCE_BASELINE.md). Runtime dependency DLLs remain external and are not bundled. The plugin source and build instructions are included so maintainers can rebuild against locally installed references. This inventory does not determine whether plugin linking or distribution creates additional obligations: a release owner must still review whether the plugin's use of the LGPL libraries and runtime loader meets the terms applicable to the exact builds.

## LGPL review handoff

These are technical distribution facts for a qualified reviewer, not a legal conclusion:

- `CustomCourseLoader.dll` is a managed plugin loaded by BepInEx. `CustomCourseLoader.csproj` references BepInEx Core/IL2CPP, Harmony, Il2CppInterop Runtime/HarmonySupport, and game/Unity assemblies with `Private=false`; those references are not copied beside the build output.
- The release archive contains the plugin DLL and the original Course Studio source, but not the referenced runtime DLLs. The player installs Disc Golf Masters and BepInEx separately. The archive includes the exact pinned dependency license texts listed above.
- The pinned BepInEx license is LGPL-2.1-or-later. The pinned Il2CppInterop license contains LGPL-3.0 and its incorporated GPL-3.0 text. The GNU project's [LGPL FAQ on static versus dynamic linking](https://www.gnu.org/licenses/gpl-faq.en.html#LGPLStaticVsDynamic) describes how distribution of a library can affect the analysis; consult the [LGPL 2.1 text](https://www.gnu.org/licenses/lgpl-2.1.html) and [LGPL 3.0 text](https://www.gnu.org/licenses/lgpl-3.0.html) for the exact versions referenced here.

### Proposed distribution surface for review

The planned public release has two project-owned distribution forms: the GitHub source repository, and a versioned Course Studio ZIP containing the plugin DLL, editor, converter tools, documentation, project source, and notices. Neither form includes BepInEx, Il2CppInterop, Harmony, Unity, or DGM runtime assemblies; users obtain the game and BepInEx separately. The repository is currently private and the ZIP remains marked DRAFT until both release gates close.

Course authors can separately create per-course ZIPs. A layout-only package contains course JSON and its card; an imported-map package can additionally contain the scene and dependency bundles only when the author records redistribution permission and a source reference. These course packages do not include the Course Studio plugin or its runtime dependencies. Course-sharing links may also be posted through the Discord workflow documented in `docs/PORTING_GUIDE.md`; the author remains responsible for rights to each map and its assets.

The reviewer should confirm which of these channels and package forms are in scope, and whether each needs separate notices, source delivery, or other conditions. Do not treat the proposed list as legal approval or as permission to distribute any imported course assets.

The release reviewer should determine how the BepInEx plugin loading and assembly-reference model applies under these exact LGPL versions, and whether the archive's source, notices, separately installed runtime, and user ability to replace or modify the referenced libraries meet all applicable requirements. The package audit verifies file contents and license-copy presence; it cannot decide this legal question.

### Qualified review record

Complete this record only after a qualified reviewer has assessed the intended distribution. No public-release approval is recorded until the decision applies to the exact candidate archive and its distribution channels.

- Reviewer and qualification:
- Review date and applicable jurisdiction(s):
- Course Studio version and candidate archive SHA-256:
- Distribution channels reviewed (source, plugin binary, or both):
- Written conclusion for the BepInEx LGPL-2.1-or-later and Il2CppInterop LGPL-3.0 references:
- Required source, notice, relinking/replacement, or distribution changes:
- Approval reference or location of the full written opinion:

## Project license

The repository's original Course Studio source and documentation are provided under the MIT No Attribution License (MIT-0) in `LICENSE`. This license does not cover Disc Golf Masters, BepInEx, Harmony, Unity, BONELAB, or other third-party content.

The BONELAB converter reads PC pallet archives supplied by the user. A converted bundle remains BONELAB-derived content. The converter does not grant redistribution rights; authors must follow the source game's modding and content-sharing terms and obtain permission for third-party content. List any third-party source, code, or assets added in a future archive here before publishing.
