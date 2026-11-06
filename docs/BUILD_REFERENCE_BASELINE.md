# Build reference baseline

This is the exact local reference set used to compile the 0.5.18 through 0.5.24 development candidates on 2026-11-03 and reused for 0.5.25 and 0.5.26 builds on 2026-11-04. A current read-only check matched all 15 reference-assembly SHA-256 values below. These identities improve troubleshooting and reproducibility; they do not define the minimum supported versions. Paths are relative to the game root, and binaries remain external game/modding dependencies. The package contains none of these files.

Build context: Disc Golf Masters Steam build `26127348`, Unity `6000.3.26f1`, Windows 10 x64, target framework `net6.0`, BepInEx IL2CPP `6.0.0-be.791`.

| Reference assembly | Observed file/product version | SHA-256 |
| --- | --- | --- |
| `BepInEx/core/BepInEx.Core.dll` | `6.0.0-be.791+b782da4ac54e1072d2857f4a2f77c80c3ef08005` | `A9C55B457259672EEFC34417E86E627BEAF90ACB11A8AE889191A7FF70F72143` |
| `BepInEx/core/BepInEx.Unity.IL2CPP.dll` | `6.0.0-be.791+b782da4ac54e1072d2857f4a2f77c80c3ef08005` | `2060949CA0DB6FDBF47F4A9C5D9AEB3535292EFA79C05C438661F406A40B30AC` |
| `BepInEx/core/0Harmony.dll` | `2.10.2` | `6C898933B52149E8BCF6722305C7E4D94ADD47C9E53B364096F91721624C0877` |
| `BepInEx/core/Il2CppInterop.Runtime.dll` | `1.5.3+dbda1cb353b0f4253345dc45136d170b9e50a5a0` | `65AB051A681C2C1E52DF7596FF738BE401AAE72CFF882FF18241CF54A3EA38A4` |
| `BepInEx/core/Il2CppInterop.HarmonySupport.dll` | `1.5.3+dbda1cb353b0f4253345dc45136d170b9e50a5a0` | `F7612EA2E5A4D473B771F34EE3B20CF32DC0C4ECEA0185416474999A17FBCC07` |
| `BepInEx/interop/Il2Cppmscorlib.dll` | No Windows file version reported | `F159CDFDECC9D2A30EDFA36C32613E3153FD941BCF17B5AAADA2544DEAF2AE4F` |
| `BepInEx/interop/Assembly-CSharp.dll` | No Windows file version reported | `0DA9514A0C6FAE1CCCE3C9C3C0D69078F88F84AA51015B24B93DDAEF174C2407` |
| `BepInEx/interop/UnityEngine.CoreModule.dll` | No Windows file version reported | `862D4BFE5F9A1E67DBE28D95CD9E02A4B831A58B2E935DBCCF4258950053B813` |
| `BepInEx/interop/UnityEngine.ImageConversionModule.dll` | No Windows file version reported | `D24A771F1E7857BC756C037F4DDA0425860FB586811D45C0DBF6FE2869706613` |
| `BepInEx/interop/UnityEngine.AssetBundleModule.dll` | No Windows file version reported | `70DEA2F397EB5608C45DD0360A2E57F1F8D91744B0F368F7D3CD300FF32613E2` |
| `BepInEx/interop/UnityEngine.PhysicsModule.dll` | No Windows file version reported | `48625E063D6D1B0A76273FC03F648B8A63E3967A13F1BA56DD4F907A0833086` |
| `BepInEx/interop/UnityEngine.TerrainModule.dll` | No Windows file version reported | `ECE4E7CD6971AAB08CC9A2B76992DB7379FCEFB672EC3AB8CA964C98365B6963` |
| `BepInEx/interop/Unity.RenderPipelines.Core.Runtime.dll` | No Windows file version reported | `34C06A998150330FAF3A221182A2B9997E955B9CD03FB893670E650998E646D4` |
| `BepInEx/interop/Unity.RenderPipelines.Universal.Runtime.dll` | No Windows file version reported | `295743780709B9EB51DA055F09037AA42CE792D50DE2794034C79C354C4A26F2` |
| `BepInEx/interop/UnityEngine.UI.dll` | No Windows file version reported | `FD172F9CF893FEE146411CA4605EE5CEA0D81E3E1116877C7A545598D445977E` |
| `BepInEx/interop/UnityEngine.UIModule.dll` | No Windows file version reported | `39536F9C4A35946A63CD1ACCCC6AAE6C243C011D5CCCEAC5D3E274237FC41C10` |

The file hashes identify this machine's build inputs; they are not authenticity signatures. A matching hash does not grant rights to redistribute the referenced assembly. Contributors should build against the installed game and mod loader, and report their own game, Unity, BepInEx, Harmony, and Il2CppInterop versions when results differ.
