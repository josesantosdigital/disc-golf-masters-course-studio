using Dgm;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DgmCustomCourses;

internal static partial class CourseScenePatcher
{
    private const float CameraOcclusionRefreshIntervalSeconds = 0.5f;
    private static float _nextCameraOcclusionRefreshAt;
    private static bool _cameraOcclusionWarningLogged;

    private static void ApplyImportedCameraOcclusionSetting(MapLightingDefinition lighting, string courseName, bool logWhenUnchanged = true)
    {
        if (lighting?.DisableCameraOcclusionCulling != true)
            return;

        try
        {
            if (logWhenUnchanged)
                _nextCameraOcclusionRefreshAt = Time.realtimeSinceStartup + CameraOcclusionRefreshIntervalSeconds;

            var cameras = UnityEngine.Object.FindObjectsOfType<Camera>(true);
            var changedCount = 0;
            var trackedCount = 0;
            const int MaxExamples = 8;
            var examples = new List<string>();
            for (var i = 0; cameras != null && i < cameras.Length; i++)
            {
                var camera = cameras[i];
                if (camera == null)
                    continue;

                var id = camera.GetInstanceID();
                if (!CameraOcclusionSnapshots.ContainsKey(id))
                    CameraOcclusionSnapshots[id] = new CameraOcclusionSnapshot(camera);
                trackedCount++;
                if (!camera.useOcclusionCulling)
                    continue;

                camera.useOcclusionCulling = false;
                changedCount++;
                if (examples.Count < MaxExamples)
                    examples.Add($"{camera.name}@{camera.gameObject.scene.name}[active={camera.enabled && camera.gameObject.activeInHierarchy}]");
            }
            if (changedCount == 0 && !logWhenUnchanged)
            {
                _cameraOcclusionWarningLogged = false;
                return;
            }

            var action = logWhenUnchanged ? "Disabled" : "Re-disabled";
            Plugin.LogSource.LogInfo($"{action} occlusion culling for imported map '{courseName}' on {changedCount} camera(s), tracking {trackedCount}; original camera settings will be restored during course cleanup. Examples: {string.Join(" || ", examples)}.");
            _cameraOcclusionWarningLogged = false;
        }
        catch (Exception ex)
        {
            if (!_cameraOcclusionWarningLogged)
            {
                Plugin.LogSource.LogWarning($"Could not disable camera occlusion culling for imported map '{courseName}': {ex.Message}");
                _cameraOcclusionWarningLogged = true;
            }
        }
    }

    internal static void MaintainImportedCameraOcclusionSetting(MapLightingDefinition lighting, string courseName)
    {
        if (lighting?.DisableCameraOcclusionCulling != true || Time.realtimeSinceStartup < _nextCameraOcclusionRefreshAt)
            return;

        _nextCameraOcclusionRefreshAt = Time.realtimeSinceStartup + CameraOcclusionRefreshIntervalSeconds;
        ApplyImportedCameraOcclusionSetting(lighting, courseName, logWhenUnchanged: false);
    }

    private static void LogImportedCameraOcclusionContext(Scene importedScene, string courseName)
    {
        try
        {
            var activeScene = SceneManager.GetActiveScene();
            var cameras = UnityEngine.Object.FindObjectsOfType<Camera>(true);
            var activeCameraCount = 0;
            var activeOcclusionCameraCount = 0;
            const int MaxCameraExamples = 12;
            var cameraExamples = new List<string>();
            for (var i = 0; cameras != null && i < cameras.Length; i++)
            {
                var camera = cameras[i];
                if (camera == null)
                    continue;
                var isActive = camera.enabled && camera.gameObject.activeInHierarchy;
                if (isActive)
                {
                    activeCameraCount++;
                    if (camera.useOcclusionCulling)
                        activeOcclusionCameraCount++;
                }
                if (cameraExamples.Count < MaxCameraExamples)
                {
                    var position = camera.transform.position;
                    cameraExamples.Add($"{camera.name}@{camera.gameObject.scene.name}[enabled={camera.enabled},active={isActive},occlusion={camera.useOcclusionCulling},position=({position.x:F1},{position.y:F1},{position.z:F1})]");
                }
            }
            Plugin.LogSource.LogInfo(
                $"Imported-map occlusion context for '{courseName}': importedScene='{importedScene.name}', activeScene='{activeScene.name}', loadedScenes={SceneManager.sceneCount}, cameras={cameras?.Length ?? 0}, activeCameras={activeCameraCount}, activeCamerasWithOcclusion={activeOcclusionCameraCount}; examples={string.Join(" || ", cameraExamples)}.");
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Could not inspect camera occlusion state for imported map '{courseName}': {ex.Message}");
        }
    }

    private static bool IsFiniteBounds(Bounds bounds)
    {
        var min = bounds.min;
        var max = bounds.max;
        return float.IsFinite(min.x) && float.IsFinite(min.y) && float.IsFinite(min.z) &&
               float.IsFinite(max.x) && float.IsFinite(max.y) && float.IsFinite(max.z);
    }

    private static bool ContainsBounds(Bounds outer, Bounds inner, float tolerance)
    {
        var outerMin = outer.min;
        var outerMax = outer.max;
        var innerMin = inner.min;
        var innerMax = inner.max;
        return outerMin.x <= innerMin.x + tolerance && outerMin.y <= innerMin.y + tolerance && outerMin.z <= innerMin.z + tolerance &&
               outerMax.x >= innerMax.x - tolerance && outerMax.y >= innerMax.y - tolerance && outerMax.z >= innerMax.z - tolerance;
    }

    private static string FormatBounds(Bounds bounds) =>
        $"center=({bounds.center.x:F1},{bounds.center.y:F1},{bounds.center.z:F1}),size=({bounds.size.x:F1},{bounds.size.y:F1},{bounds.size.z:F1})";

    private static bool RepairRendererLocalBounds(Renderer renderer, out string diagnostic, out bool skippedStaticBatch)
    {
        diagnostic = null;
        skippedStaticBatch = false;
        if (renderer == null || renderer.GetComponent<MeshRenderer>() == null)
            return false;
        if (ImportedMapPhysicsSupport.HasWaterNameToken(renderer.name))
            return false;
        var materials = renderer.sharedMaterials;
        for (var i = 0; materials != null && i < materials.Length; i++)
            if (materials[i] != null && ImportedMapPhysicsSupport.HasWaterNameToken(materials[i].name))
                return false;

        var meshFilter = renderer.GetComponent<MeshFilter>();
        var mesh = meshFilter == null ? null : meshFilter.sharedMesh;
        if (mesh == null)
            return false;

        if (renderer.isPartOfStaticBatch)
        {
            skippedStaticBatch = true;
            diagnostic = $"{DescribeTransformPath(renderer.transform)} [mesh={mesh.name}; renderer={FormatBounds(renderer.localBounds)}; skipped=shared static-batch mesh bounds are not per-renderer local bounds]";
            return false;
        }

        var meshBounds = mesh.bounds;
        var rendererBounds = renderer.localBounds;
        if (!IsFiniteBounds(meshBounds) || !IsFiniteBounds(rendererBounds) || ContainsBounds(rendererBounds, meshBounds, 0.01f))
            return false;

        var expandedBounds = rendererBounds;
        expandedBounds.Encapsulate(meshBounds);
        var rendererId = renderer.GetInstanceID();
        if (!RendererLocalBoundsSnapshots.ContainsKey(rendererId))
            RendererLocalBoundsSnapshots[rendererId] = new RendererLocalBoundsSnapshot(renderer);
        renderer.localBounds = expandedBounds;
        diagnostic = $"{DescribeTransformPath(renderer.transform)} [renderer {FormatBounds(rendererBounds)}; mesh {FormatBounds(meshBounds)}; expanded {FormatBounds(expandedBounds)}]";
        return true;
    }

    private static Light FindBaseCourseSun(Scene importedScene, string preferredSceneName)
    {
        try
        {
            var sun = RenderSettings.sun;
            if (sun != null && sun.enabled && sun.gameObject.activeInHierarchy && sun.gameObject.scene.handle != importedScene.handle)
                return sun;
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded || scene.handle == importedScene.handle ||
                    (!string.IsNullOrWhiteSpace(preferredSceneName) && !string.Equals(scene.name, preferredSceneName, StringComparison.OrdinalIgnoreCase))) continue;
                var roots = scene.GetRootGameObjects();
                for (var rootIndex = 0; roots != null && rootIndex < roots.Length; rootIndex++)
                {
                    if (roots[rootIndex] == null) continue;
                    var lights = roots[rootIndex].GetComponentsInChildren<Light>(true);
                    for (var j = 0; lights != null && j < lights.Length; j++)
                    {
                        var light = lights[j];
                        if (light != null && light.enabled && light.gameObject.activeInHierarchy && light.type == LightType.Directional) return light;
                    }
                }
            }
        }
        catch (Exception ex) { Plugin.LogSource.LogWarning($"Could not find the base-course sun: {ex.Message}"); }
        return null;
    }

    private static void RepairImportedMapRendering(Scene scene, string courseName, MapLightingDefinition lighting, Light baseSun)
    {
        var adjustedLocalLightCount = ApplyImportedLocalLightIntensity(scene, courseName, lighting);
        LogImportedTextMeshReferences(scene, courseName);
        var fallbackShader = FindSupportedFallbackShader();
        var unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
        var nativeLitShader = Shader.Find(ImportedLitMaterials.TargetShaderName);
        var nativeLitMaterialCount = 0;
        var nativeLitExamples = new List<string>();
        var nativeLitWarningCount = 0;
        // DGM uses lit cutout materials on its own courses; their alpha-test variant
        // may be present even when the unlit cutout variant was stripped at build time.
        var litFoliageShader = lighting.UseLitFoliageMaterials || lighting.RealtimeMapShadows ? Shader.Find("Universal Render Pipeline/Lit") : null;
        if (litFoliageShader != null && !litFoliageShader.isSupported)
            litFoliageShader = null;
        var gameFoliageShader = lighting.UseGameFoliageShader ? Shader.Find("DGM/Foliage") : null;
        if (gameFoliageShader != null && !gameFoliageShader.isSupported)
            gameFoliageShader = null;
        var useAlbedoOnly = lighting.ForceUnlitMaterials && unlitShader != null && unlitShader.isSupported;
        var textureProperties = new[]
        {
            Shader.PropertyToID("_BaseMap"),
            Shader.PropertyToID("_MainTex"),
            Shader.PropertyToID("_BaseColorMap"),
            Shader.PropertyToID("_ColorMap"),
            Shader.PropertyToID("_Albedo"),
            Shader.PropertyToID("_AlbedoMap"),
            Shader.PropertyToID("_BaseColorTexture"),
            Shader.PropertyToID("_BaseColorTex"),
            Shader.PropertyToID("_DiffuseMap"),
            Shader.PropertyToID("_Diffuse")
        };
        var colorProperties = new[]
        {
            Shader.PropertyToID("_BaseColor"),
            Shader.PropertyToID("_Color"),
            Shader.PropertyToID("_Tint")
        };
        var shaderCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var processedMaterialIds = new HashSet<int>();
        const int MaxTexturelessRendererExamples = 8;
        const int MaxTexturelessMaterialUsageLogs = 24;
        var texturelessMaterialUses = new Dictionary<int, (string Name, int RendererCount, List<string> RendererPaths)>();
        var roots = scene.GetRootGameObjects();
        var renderRoots = new List<GameObject>();
        var renderRootIds = new HashSet<int>();
        for (var i = 0; i < roots.Length; i++)
            if (roots[i] != null && renderRootIds.Add(roots[i].GetInstanceID()))
                renderRoots.Add(roots[i]);
        var sceneRootCount = renderRoots.Count;
        if (lighting.RepairTerrainMaterials)
            AddTerrainAssets(scene, roots, renderRoots, renderRootIds, lighting);
        else if (lighting.ExperimentalFoliageInteractions)
        {
            foreach (var root in roots)
            {
                if (root == null) continue;
                foreach (var terrain in root.GetComponentsInChildren<Terrain>(true))
                    if (terrain != null && terrain.terrainData != null)
                        ImportedFoliageInteraction.AddTerrain(terrain, terrain.terrainData.treePrototypes);
            }
        }
        var siblingTextureAliases = BuildSiblingTextureAliases(renderRoots, textureProperties);
        if (lighting.ExperimentalFoliageInteractions)
            ImportedFoliageInteraction.AddSceneRenderers(scene, courseName);
        var rendererCount = 0;
        var materialCount = 0;
        var texturedMaterialCount = 0;
        var unsupportedMaterialCount = 0;
        var repairedMaterialCount = 0;
        var albedoOnlyMaterialCount = 0;
        var albedoConversionWarningLogged = false;
        var directionalLightCount = 0;
        var pointLightCount = 0;
        var spotLightCount = 0;
        const int MaxLocalLightExamples = 12;
        var localLightExamples = new List<string>();
        var disabledCameraCount = 0;
        var lightmappedRendererCount = 0;
        var clearedLightmapCount = 0;
        Light importedSun = null;
        var importedDirectionalLights = new List<Light>();
        var shadowRendererCount = 0;
        var preservedParticleCount = 0;
        var hiddenParticleCount = 0;
        var hiddenLightVolumeCount = 0;
        var hiddenSkyboxMeshCount = 0;
        // Do not access LightmapSettings.lightmaps here. On the game's IL2CPP
        // runtime, constructing its Il2CppReferenceArray<LightmapData> wrapper
        // can throw before a try/catch in a helper is entered, aborting all
        // remaining material repairs. When lightmaps are not preserved, every
        // imported renderer's lightmap indices are cleared below regardless, so
        // the count is only diagnostic. When preserved, avoid judging indices
        // without a safely readable active-lightmap count.
        var activeLightmapCount = -1;
        var lightmapRendererReadWarningLogged = false;
        var invalidLightmapRendererCount = 0;
        var invalidLightmapExamples = new List<string>();
        var widenedRendererBoundsCount = 0;
        var widenedRendererBoundsExamples = new List<string>();
        var skippedStaticBatchBoundsCount = 0;
        var skippedStaticBatchBoundsExamples = new List<string>();
        var repairedInvalidLightmapRendererCount = 0;

        for (var rootIndex = 0; rootIndex < renderRoots.Count; rootIndex++)
        {
            var root = renderRoots[rootIndex];
            if (root == null)
                continue;

            var lights = rootIndex < sceneRootCount ? root.GetComponentsInChildren<Light>(true) : null;
            for (var lightIndex = 0; lights != null && lightIndex < lights.Length; lightIndex++)
            {
                var light = lights[lightIndex];
                if (light == null || !light.enabled || !light.gameObject.activeInHierarchy)
                    continue;
                if (light.type == LightType.Directional)
                {
                    directionalLightCount++;
                    importedDirectionalLights.Add(light);
                    if (importedSun == null) importedSun = light;
                }
                else if (light.type is LightType.Point or LightType.Spot)
                {
                    if (light.type == LightType.Point)
                        pointLightCount++;
                    else
                        spotLightCount++;
                    if (localLightExamples.Count < MaxLocalLightExamples)
                        localLightExamples.Add($"{light.type}:{DescribeTransformPath(light.transform)}[intensity={light.intensity:F1},range={light.range:F1},shadows={light.shadows}]");
                }
            }

            var cameras = rootIndex < sceneRootCount ? root.GetComponentsInChildren<Camera>(true) : null;
            for (var cameraIndex = 0; cameras != null && cameraIndex < cameras.Length; cameraIndex++)
            {
                var camera = cameras[cameraIndex];
                if (camera == null || !camera.enabled)
                    continue;
                camera.enabled = false;
                disabledCameraCount++;
            }

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (var rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                var renderer = renderers[rendererIndex];
                if (renderer == null || !renderer.enabled)
                    continue;
                rendererCount++;
                try
                {
                    if (RepairRendererLocalBounds(renderer, out var boundsDiagnostic, out var skippedStaticBatch))
                    {
                        widenedRendererBoundsCount++;
                        if (widenedRendererBoundsExamples.Count < 8)
                            widenedRendererBoundsExamples.Add(boundsDiagnostic);
                    }
                    else if (skippedStaticBatch)
                    {
                        skippedStaticBatchBoundsCount++;
                        if (skippedStaticBatchBoundsExamples.Count < 8)
                            skippedStaticBatchBoundsExamples.Add(boundsDiagnostic);
                    }
                }
                catch (Exception ex)
                {
                    Plugin.LogSource.LogDebug($"Could not repair imported renderer bounds for '{renderer.name}': {ex.Message}");
                }
                var canInspectRendererLightmaps = false;
                var bakedLightmapIndex = -1;
                var realtimeLightmapIndex = -1;
                if (!lighting.PreserveLightmaps)
                {
                    try
                    {
                        bakedLightmapIndex = renderer.lightmapIndex;
                        realtimeLightmapIndex = renderer.realtimeLightmapIndex;
                        canInspectRendererLightmaps = true;
                    }
                    catch (Exception ex)
                    {
                        if (!lightmapRendererReadWarningLogged)
                        {
                            Plugin.LogSource.LogWarning($"Could not inspect lightmap indices on imported renderers in '{courseName}'; continuing other rendering repairs: {ex.Message}");
                            lightmapRendererReadWarningLogged = true;
                        }
                    }
                }

                if (canInspectRendererLightmaps && (bakedLightmapIndex != -1 || realtimeLightmapIndex != -1))
                {
                    lightmappedRendererCount++;
                    var invalidBakedLightmap = bakedLightmapIndex < -1 ||
                        (activeLightmapCount >= 0 && bakedLightmapIndex >= activeLightmapCount);
                    var invalidRealtimeLightmap = realtimeLightmapIndex < -1 ||
                        (activeLightmapCount >= 0 && realtimeLightmapIndex >= activeLightmapCount);
                    if (invalidBakedLightmap || invalidRealtimeLightmap)
                    {
                        invalidLightmapRendererCount++;
                        if (invalidLightmapExamples.Count < 10)
                            invalidLightmapExamples.Add($"{DescribeTransformPath(renderer.transform)}[lightmap={bakedLightmapIndex},realtime={realtimeLightmapIndex}]");
                    }
                    if (!lighting.PreserveLightmaps || invalidBakedLightmap || invalidRealtimeLightmap)
                    {
                        try
                        {
                            if (lighting.PreserveLightmaps)
                            {
                                var rendererId = renderer.GetInstanceID();
                                if (!RendererLightmapSnapshots.ContainsKey(rendererId))
                                    RendererLightmapSnapshots[rendererId] = new RendererLightmapSnapshot(renderer);
                                if (invalidBakedLightmap) renderer.lightmapIndex = -1;
                                if (invalidRealtimeLightmap) renderer.realtimeLightmapIndex = -1;
                                repairedInvalidLightmapRendererCount++;
                            }
                            else
                            {
                                renderer.lightmapIndex = -1;
                                renderer.realtimeLightmapIndex = -1;
                                clearedLightmapCount++;
                            }
                        }
                        catch (Exception ex)
                        {
                            Plugin.LogSource.LogDebug($"Couldn't clear an imported renderer lightmap: {ex.Message}");
                        }
                    }
                }
                var materials = renderer.sharedMaterials;
                if (materials == null)
                    continue;

                if (lighting.UseBaseSkybox && IsImportedSkyboxRenderer(renderer, materials))
                {
                    var rendererId = renderer.GetInstanceID();
                    if (!RendererSnapshots.ContainsKey(rendererId))
                        RendererSnapshots[rendererId] = new RendererSnapshot(renderer, renderer.enabled);
                    renderer.enabled = false;
                    hiddenSkyboxMeshCount++;
                    continue;
                }

                if (lighting.HideImportedParticles && materials.Any(m => m != null && m.shader != null &&
                    m.shader.name.IndexOf("/Particles/", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    var rendererId = renderer.GetInstanceID();
                    if (!RendererSnapshots.ContainsKey(rendererId))
                        RendererSnapshots[rendererId] = new RendererSnapshot(renderer, renderer.enabled);
                    renderer.enabled = false;
                    hiddenParticleCount++;
                    if (hiddenParticleCount <= 24)
                    {
                        var particleShaders = string.Join(", ", materials.Where(m => m != null && m.shader != null &&
                            m.shader.name.IndexOf("/Particles/", StringComparison.OrdinalIgnoreCase) >= 0).Select(m => m.shader.name).Distinct());
                        Plugin.LogSource.LogInfo($"Hidden imported particle renderer '{renderer.name}' under '{renderer.transform.parent?.name ?? "<root>"}' using shader(s): {particleShaders}.");
                    }
                    continue;
                }

                if (materials.Any(m => m != null && m.shader != null &&
                    (m.shader.name.IndexOf("Luminous Light Volumes", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     m.shader.name.IndexOf("Volumetric Lightbeam", StringComparison.OrdinalIgnoreCase) >= 0)))
                {
                    var rendererId = renderer.GetInstanceID();
                    if (!RendererSnapshots.ContainsKey(rendererId))
                        RendererSnapshots[rendererId] = new RendererSnapshot(renderer, renderer.enabled);
                    renderer.enabled = false;
                    hiddenLightVolumeCount++;
                    continue;
                }

                if (lighting.RealtimeMapShadows && renderer.GetComponent<MeshRenderer>() != null)
                {
                    try
                    {
                        var rendererId = renderer.GetInstanceID();
                        if (!ShadowRendererSnapshots.ContainsKey(rendererId))
                            ShadowRendererSnapshots[rendererId] = new ShadowRendererSnapshot(renderer);
                        renderer.shadowCastingMode = ShadowCastingMode.On;
                        renderer.receiveShadows = true;
                        shadowRendererCount++;
                    }
                    catch (Exception ex) { Plugin.LogSource.LogDebug($"Could not enable shadows on '{renderer.name}': {ex.Message}"); }
                }

                if (lighting.HideHoloWalls && rootIndex < sceneRootCount && materials.Length == 1 &&
                    materials[0] != null && materials[0].shader != null &&
                    materials[0].shader.name.IndexOf("Holowall", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var rendererId = renderer.GetInstanceID();
                    if (!RendererSnapshots.ContainsKey(rendererId))
                        RendererSnapshots[rendererId] = new RendererSnapshot(renderer, renderer.enabled);
                    renderer.enabled = false;
                    Plugin.LogSource.LogInfo($"Hidden imported holographic wall renderer '{renderer.name}' to reveal the skybox.");
                    continue;
                }

                for (var materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    var material = materials[materialIndex];
                    if (material == null)
                        continue;
                    var materialId = material.GetInstanceID();
                    if (!processedMaterialIds.Add(materialId))
                    {
                        if (texturelessMaterialUses.TryGetValue(materialId, out var usage))
                        {
                            usage.RendererCount++;
                            if (usage.RendererPaths.Count < MaxTexturelessRendererExamples)
                                usage.RendererPaths.Add(DescribeTransformPath(renderer.transform));
                            texturelessMaterialUses[materialId] = usage;
                        }
                        continue;
                    }
                    materialCount++;

                    var shader = material.shader;
                    var shaderName = shader == null ? "<missing>" : shader.name;
                    shaderCounts[shaderName] = shaderCounts.TryGetValue(shaderName, out var currentCount) ? currentCount + 1 : 1;

                    // Particle shaders depend on their own blending and vertex streams;
                    // converting them to opaque map materials produces floating squares.
                    if (shaderName.IndexOf("/Particles/", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        preservedParticleCount++;
                        continue;
                    }

                    Texture texture = null;
                    foreach (var propertyId in textureProperties)
                    {
                        if (!material.HasProperty(propertyId))
                            continue;
                        var candidateTexture = material.GetTexture(propertyId);
                        if (candidateTexture == null || ImportedMaterialTextures.IsPlaceholderTexture(candidateTexture))
                            continue;
                        texture = candidateTexture;
                        break;
                    }
                    var sourceAlphaClip = material.IsKeywordEnabled("_ALPHATEST_ON") ||
                        (material.renderQueue >= 2450 && material.renderQueue < 2500) ||
                        (material.HasProperty(Shader.PropertyToID("_AlphaClip")) && material.GetFloat(Shader.PropertyToID("_AlphaClip")) > 0.5f) ||
                        (material.HasProperty(Shader.PropertyToID("_AlphaCutoff")) && material.GetFloat(Shader.PropertyToID("_AlphaCutoff")) > 0f);
                    var isFoliage = shaderName.IndexOf("Leaf", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        shaderName.EndsWith("/Bil", StringComparison.OrdinalIgnoreCase) ||
                        shaderName.IndexOf("Foliage", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        material.name.IndexOf("leaf", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        material.name.IndexOf("billboard", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        material.name.IndexOf("plant", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        material.name.IndexOf("bush", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        material.name.IndexOf("branch", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        material.name.IndexOf("spruce", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        material.name.IndexOf("cvet", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (texture != null && (texture.name.IndexOf("plant", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            texture.name.IndexOf("bush", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            texture.name.IndexOf("branch", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            texture.name.IndexOf("spruce", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            texture.name.IndexOf("cvet", StringComparison.OrdinalIgnoreCase) >= 0)) ||
                        sourceAlphaClip;
                    if (texture == null)
                        texture = ImportedMaterialTextures.FindAdditional(material, isFoliage || lighting.IgnoreSourceTint);
                    if (texture == null && TryGetSiblingTexture(material, siblingTextureAliases, out var siblingTexture))
                    {
                        texture = siblingTexture;
                        Plugin.LogSource.LogInfo($"Reused matching sibling texture '{texture.name}' for textureless imported material '{material.name}'.");
                    }
                    var texturelessRendererPath = texture == null ? DescribeTransformPath(renderer.transform) : null;
                    if (texturelessRendererPath != null)
                        texturelessMaterialUses[materialId] = (material.name, 1, new List<string> { texturelessRendererPath });
                    if (texture != null)
                        texturedMaterialCount++;

                    if (lighting.ExportFoliageDiagnostics && texture != null &&
                        (isFoliage || (rootIndex >= sceneRootCount && string.Equals(material.name, "Tree", StringComparison.OrdinalIgnoreCase))))
                        FoliageDiagnostics.Capture(courseName, material, texture);

                    if (lighting.IgnoreSourceTint)
                    {
                        var rendererPath = texturelessRendererPath ?? DescribeTransformPath(renderer.transform);
                        Plugin.LogSource.LogInfo($"Imported material '{material.name}' on renderer '{renderer.name}' at '{rendererPath}': shader='{shaderName}', selected texture='{(texture == null ? "<none>" : texture.name)}'.");
                        if (texture == null)
                        {
                            var bounds = renderer.bounds;
                            Plugin.LogSource.LogInfo($"Imported renderer '{rendererPath}' uses material '{material.name}' with no discovered color texture (bounds center=({bounds.center.x:F1},{bounds.center.y:F1},{bounds.center.z:F1}), size=({bounds.size.x:F1},{bounds.size.y:F1},{bounds.size.z:F1})); this may be intentional vertex-color or tint-only geometry.");
                        }
                    }

                    if (useAlbedoOnly && (lighting.RealtimeMapShadows || shader == null || !string.Equals(shader.name, unlitShader.name, StringComparison.Ordinal)))
                    {
                        if (isFoliage && texture == null)
                        {
                            Plugin.LogSource.LogWarning($"Kept foliage material '{material.name}' unchanged because no color texture was found.");
                            continue;
                        }
                        try
                        {
                            var albedoTint = Color.white;
                            foreach (var propertyId in colorProperties)
                            {
                                if (lighting.IgnoreSourceTint)
                                    break;
                                if (!material.HasProperty(propertyId))
                                    continue;
                                albedoTint = material.GetColor(propertyId);
                                break;
                            }
                            var renderQueue = material.renderQueue;
                    var alphaClip = isFoliage || sourceAlphaClip;
                            var transparent = !isFoliage && material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT");
                            var surfaceId = Shader.PropertyToID("_Surface");
                            var surface = isFoliage ? 0f : (material.HasProperty(surfaceId) ? material.GetFloat(surfaceId) : 0f);
                            var cullId = Shader.PropertyToID("_Cull");
                            var cull = isFoliage ? 0f : (material.HasProperty(cullId) ? material.GetFloat(cullId) : 2f);

                            var useGameFoliage = gameFoliageShader != null && rootIndex >= sceneRootCount &&
                                material.name.IndexOf("leaf", StringComparison.OrdinalIgnoreCase) >= 0;
                            var replacementShader = useGameFoliage ? gameFoliageShader :
                                ((isFoliage || lighting.RealtimeMapShadows) && litFoliageShader != null ? litFoliageShader : unlitShader);
                            material.shader = replacementShader;
                            if (texture != null)
                            {
                                foreach (var propertyId in new[] { Shader.PropertyToID("_BaseMap"), Shader.PropertyToID("_BaseColorMap"), Shader.PropertyToID("_MainTex") })
                                    if (material.HasProperty(propertyId))
                                        material.SetTexture(propertyId, texture);
                            }
                            var brightness = lighting.AlbedoBrightness;
                            var boostedTint = new Color(albedoTint.r * brightness, albedoTint.g * brightness, albedoTint.b * brightness, albedoTint.a);
                            foreach (var propertyId in new[] { Shader.PropertyToID("_BaseColor"), Shader.PropertyToID("_Color") })
                                if (material.HasProperty(propertyId))
                                    material.SetColor(propertyId, boostedTint);
                            if (material.HasProperty(surfaceId))
                                material.SetFloat(surfaceId, surface);
                            if (material.HasProperty(cullId))
                                material.SetFloat(cullId, cull);
                            if (useGameFoliage)
                            {
                                foreach (var propertyId in new[] { Shader.PropertyToID("_HealthyColor"), Shader.PropertyToID("_DryColor") })
                                    if (material.HasProperty(propertyId)) material.SetColor(propertyId, boostedTint);
                                var tilingId = Shader.PropertyToID("_Tiling_and_Offset");
                                if (material.HasProperty(tilingId)) material.SetVector(tilingId, new Vector4(1f, 1f, 0f, 0f));
                                var impactId = Shader.PropertyToID("_ImpactResponse");
                                if (material.HasProperty(impactId)) material.SetFloat(impactId, 1f);
                                try { material.enableInstancing = true; }
                                catch (Exception ex) { Plugin.LogSource.LogWarning($"Could not enable game foliage instancing: {ex.Message}"); }
                                Plugin.LogSource.LogInfo($"Using DGM foliage shader for '{material.name}' with imported leaf atlas.");
                            }
                            if (replacementShader == litFoliageShader && litFoliageShader != null)
                            {
                                // Do not carry shader-specific PBR state from the source shader.
                                var smoothnessId = Shader.PropertyToID("_Smoothness");
                                var metallicId = Shader.PropertyToID("_Metallic");
                                if (material.HasProperty(smoothnessId)) material.SetFloat(smoothnessId, 0f);
                                if (material.HasProperty(metallicId)) material.SetFloat(metallicId, 0f);
                                material.DisableKeyword("_NORMALMAP");
                                material.DisableKeyword("_METALLICSPECGLOSSMAP");
                                material.DisableKeyword("_OCCLUSIONMAP");
                                if (lighting.RealtimeMapShadows && rootIndex >= sceneRootCount)
                                {
                                    try { material.enableInstancing = true; }
                                    catch (Exception ex) { Plugin.LogSource.LogWarning($"Could not enable tree material instancing on '{material.name}': {ex.Message}"); }
                                }
                                if (isFoliage) Plugin.LogSource.LogInfo($"Using lit cutout shader '{replacementShader.name}' for foliage material '{material.name}'.");
                            }
                            if (alphaClip)
                            {
                                var alphaClipId = Shader.PropertyToID("_AlphaClip");
                                var cutoffId = Shader.PropertyToID("_Cutoff");
                                var gameCutoffId = Shader.PropertyToID("_AlphaCutoff");
                                if (material.HasProperty(alphaClipId)) material.SetFloat(alphaClipId, 1f);
                                if (isFoliage && material.HasProperty(cutoffId)) material.SetFloat(cutoffId, 0.35f);
                                if (isFoliage && material.HasProperty(gameCutoffId)) material.SetFloat(gameCutoffId, 0.35f);
                                material.EnableKeyword("_ALPHATEST_ON");
                            }
                            if (transparent)
                                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                            else if (isFoliage)
                                material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                            material.renderQueue = isFoliage ? 2450 : renderQueue;
                            if (lighting.RealtimeMapShadows && !transparent)
                            {
                                try
                                {
                                    if (material.FindPass("ShadowCaster") >= 0)
                                        material.SetShaderPassEnabled("ShadowCaster", true);
                                }
                                catch (Exception ex) { Plugin.LogSource.LogDebug($"Could not enable shadow pass for '{material.name}': {ex.Message}"); }
                            }
                            albedoOnlyMaterialCount++;
                            continue;
                        }
                        catch (Exception ex)
                        {
                            if (!albedoConversionWarningLogged)
                            {
                                Plugin.LogSource.LogWarning($"Could not convert imported map materials to albedo-only rendering: {ex.Message}");
                                albedoConversionWarningLogged = true;
                            }
                        }
                    }

                    if (shader != null && shader.isSupported && !lighting.ForceFallbackMaterials)
                    {
                        // Supported source shaders may still be compiled against a
                        // different URP lighting ABI. Port this known PBR family;
                        // explicit unlit/fallback modes retain their existing behavior.
                        if (!lighting.ForceUnlitMaterials)
                        {
                            if (ImportedLitMaterials.TryMigrate(material, nativeLitShader, out var migration))
                            {
                                nativeLitMaterialCount++;
                                if (nativeLitExamples.Count < 8) nativeLitExamples.Add(migration);
                            }
                            else if (migration != null && nativeLitWarningCount++ < 3)
                                Plugin.LogSource.LogWarning(migration);
                        }
                        continue;
                    }

                    if (shader == null || !shader.isSupported)
                        unsupportedMaterialCount++;
                    if (fallbackShader == null)
                        continue;

                    var tint = Color.white;
                    var hasTint = false;
                    foreach (var propertyId in colorProperties)
                    {
                        if (!material.HasProperty(propertyId))
                            continue;
                        tint = material.GetColor(propertyId);
                        hasTint = true;
                        break;
                    }

                    var fallbackRenderQueue = material.renderQueue;
                    var fallbackAlphaClip = material.IsKeywordEnabled("_ALPHATEST_ON");
                    var fallbackTransparent = material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT");
                    var fallbackSurfaceId = Shader.PropertyToID("_Surface");
                    var fallbackSurface = material.HasProperty(fallbackSurfaceId) ? material.GetFloat(fallbackSurfaceId) : 0f;
                    var fallbackCullId = Shader.PropertyToID("_Cull");
                    var fallbackCull = material.HasProperty(fallbackCullId) ? material.GetFloat(fallbackCullId) : 2f;
                    var fallbackCutoffId = Shader.PropertyToID("_Cutoff");
                    var fallbackCutoff = material.HasProperty(fallbackCutoffId) ? material.GetFloat(fallbackCutoffId) : 0.5f;

                    material.shader = fallbackShader;
                    if (texture != null)
                    {
                        foreach (var propertyId in new[] { Shader.PropertyToID("_BaseMap"), Shader.PropertyToID("_MainTex") })
                            if (material.HasProperty(propertyId))
                                material.SetTexture(propertyId, texture);
                    }
                    if (hasTint)
                    {
                        foreach (var propertyId in new[] { Shader.PropertyToID("_BaseColor"), Shader.PropertyToID("_Color") })
                            if (material.HasProperty(propertyId))
                                material.SetColor(propertyId, tint);
                    }
                    if (material.HasProperty(fallbackSurfaceId))
                        material.SetFloat(fallbackSurfaceId, fallbackTransparent ? 1f : fallbackSurface);
                    if (material.HasProperty(fallbackCullId))
                        material.SetFloat(fallbackCullId, fallbackCull);
                    if (material.HasProperty(fallbackCutoffId))
                        material.SetFloat(fallbackCutoffId, fallbackCutoff);
                    if (fallbackAlphaClip)
                        material.EnableKeyword("_ALPHATEST_ON");
                    if (fallbackTransparent)
                        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    material.renderQueue = fallbackRenderQueue;
                    repairedMaterialCount++;
                }
            }
        }

        foreach (var entry in texturelessMaterialUses
            .Where(entry => entry.Value.RendererCount > 1)
            .OrderBy(entry => entry.Value.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxTexturelessMaterialUsageLogs))
        {
            var usage = entry.Value;
            var omittedCount = usage.RendererCount - usage.RendererPaths.Count;
            var omitted = omittedCount > 0 ? $"; {omittedCount} additional renderer(s) omitted" : string.Empty;
            Plugin.LogSource.LogInfo($"Imported material '{usage.Name}' has no discovered color texture and is used by {usage.RendererCount} renderers: {string.Join(" || ", usage.RendererPaths)}{omitted}.");
        }

        if (lighting.RepairTerrainMaterials)
        {
            for (var rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                if (roots[rootIndex] == null)
                    continue;
                var terrains = roots[rootIndex].GetComponentsInChildren<Terrain>(true);
                for (var i = 0; i < terrains.Length; i++)
                {
                    var terrain = terrains[i];
                    if (terrain == null || terrain.terrainData == null)
                        continue;
                    try { terrain.terrainData.RefreshPrototypes(); terrain.Flush(); }
                    catch (Exception ex) { Plugin.LogSource.LogWarning($"Could not refresh terrain prototypes for '{terrain.name}': {ex.Message}"); }
                }
            }
        }

        var activeLightmapSummary = activeLightmapCount < 0
            ? (lighting.PreserveLightmaps ? "preserved; not inspected" : "unavailable")
            : activeLightmapCount.ToString();
        var summary = string.Join(
            "; ",
            shaderCounts.OrderByDescending(entry => entry.Value).Take(12).Select(entry => $"{entry.Key} ({entry.Value})"));
        Plugin.LogSource.LogInfo(
            $"Imported map render scan for '{courseName}': {rendererCount} renderers, {materialCount} materials, " +
            $"{texturedMaterialCount} with texture maps, {unsupportedMaterialCount} unsupported shaders, " +
            $"{repairedMaterialCount} repaired, {albedoOnlyMaterialCount} rendered albedo-only; native Lit migrations={nativeLitMaterialCount}, examples={string.Join(" || ", nativeLitExamples)}; found lightmaps on {lightmappedRendererCount} renderers, cleared {clearedLightmapCount}, preserved={lighting.PreserveLightmaps}; " +
            $"active lightmap data={activeLightmapSummary}, renderers with out-of-range baked/realtime indices={invalidLightmapRendererCount}, repaired invalid indices={repairedInvalidLightmapRendererCount}, examples={string.Join(" || ", invalidLightmapExamples)}; " +
            $"widened non-batched mesh renderer bounds={widenedRendererBoundsCount}, examples={string.Join(" || ", widenedRendererBoundsExamples)}; " +
            $"static-batch renderer bounds repairs skipped={skippedStaticBatchBoundsCount}, examples={string.Join(" || ", skippedStaticBatchBoundsExamples)}; " +
            $"directional lights={directionalLightCount}; point lights={pointLightCount}; spot lights={spotLightCount}; adjusted point/spot lights={adjustedLocalLightCount} (intensity x{lighting.LocalLightIntensityMultiplier:F2}); local-light examples={string.Join(" || ", localLightExamples)}; disabled imported cameras={disabledCameraCount}; shadow renderers={shadowRendererCount}; preserved particle materials={preservedParticleCount}; hidden particle renderers={hiddenParticleCount}; hidden incompatible volumetric light renderers={hiddenLightVolumeCount}; hidden source sky meshes={hiddenSkyboxMeshCount}; shaders: {summary}");

        if (lighting.PreferBaseCourseSun && baseSun != null && baseSun.enabled && baseSun.gameObject.activeInHierarchy)
        {
            foreach (var light in importedDirectionalLights)
            {
                try
                {
                    var id = light.GetInstanceID();
                    if (!ShadowLightSnapshots.ContainsKey(id)) ShadowLightSnapshots[id] = new ShadowLightSnapshot(light);
                    light.enabled = false;
                }
                catch (Exception ex) { Plugin.LogSource.LogWarning($"Could not disable an imported directional light: {ex.Message}"); }
            }
            importedSun = baseSun;
            Plugin.LogSource.LogInfo($"Using base-course sun '{baseSun.name}' instead of {importedDirectionalLights.Count} imported directional light(s).");
        }
        else if (lighting.PreferBaseCourseSun)
            Plugin.LogSource.LogWarning($"No active base-course sun found for '{courseName}'; keeping its imported lighting.");

        if (lighting.RealtimeMapShadows && importedSun != null)
        {
            try
            {
                var id = importedSun.GetInstanceID();
                if (!ShadowLightSnapshots.ContainsKey(id)) ShadowLightSnapshots[id] = new ShadowLightSnapshot(importedSun);
                if (!HasSunSnapshot) { OriginalSun = RenderSettings.sun; HasSunSnapshot = true; }
                importedSun.shadows = LightShadows.Soft;
                if (importedSun.shadowStrength <= 0f) importedSun.shadowStrength = 1f;
                RenderSettings.sun = importedSun;
                try
                {
                    var baking = importedSun.bakingOutput;
                    baking.lightmapBakeType = LightmapBakeType.Realtime;
                    baking.isBaked = false;
                    importedSun.bakingOutput = baking;
                }
                catch (Exception ex) { Plugin.LogSource.LogWarning($"Could not change the imported light bake mode: {ex.Message}"); }
                Plugin.LogSource.LogInfo($"Enabled realtime shadows on directional light '{importedSun.name}'.");
            }
            catch (Exception ex) { Plugin.LogSource.LogWarning($"Could not enable imported directional light shadows: {ex.Message}"); }
        }

        if (directionalLightCount == 0 || lighting.SupplementLighting || lighting.RealtimeMapShadows)
        {
            if (!HasRenderSettingsSnapshot)
            {
                OriginalAmbientMode = RenderSettings.ambientMode;
                OriginalAmbientColor = RenderSettings.ambientLight;
                OriginalAmbientIntensity = RenderSettings.ambientIntensity;
                HasRenderSettingsSnapshot = true;
            }
            RenderSettings.ambientMode = AmbientMode.Flat;
            var ambient = lighting.AmbientColor ?? new MapColorDefinition();
            RenderSettings.ambientLight = new Color(ambient.R, ambient.G, ambient.B, 1f);
            RenderSettings.ambientIntensity = lighting.AmbientIntensity;
            Plugin.LogSource.LogInfo($"Applied imported-map ambient light for '{courseName}' (intensity {lighting.AmbientIntensity:F2}).");
        }

        if ((directionalLightCount == 0 && importedSun == null) || lighting.SupplementLighting)
        {
            var lightObject = new GameObject("DGM_ImportedMap_KeyLight");
            SceneManager.MoveGameObjectToScene(lightObject, scene);
            lightObject.transform.rotation = Quaternion.Euler(lighting.KeyLightPitchDegrees, lighting.KeyLightYawDegrees, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            var keyColor = lighting.KeyLightColor ?? new MapColorDefinition();
            light.color = new Color(keyColor.R, keyColor.G, keyColor.B, 1f);
            light.intensity = lighting.KeyLightIntensity;
            light.cullingMask = -1;
            light.shadows = directionalLightCount == 0 ? LightShadows.Soft : LightShadows.None;
            if (lighting.RealtimeMapShadows && directionalLightCount == 0)
            {
                try
                {
                    if (!HasSunSnapshot) { OriginalSun = RenderSettings.sun; HasSunSnapshot = true; }
                    RenderSettings.sun = light;
                }
                catch (Exception ex) { Plugin.LogSource.LogWarning($"Could not set the imported map key light as sun: {ex.Message}"); }
            }
            TemporaryObjects.Add(lightObject);
            Plugin.LogSource.LogWarning(directionalLightCount == 0
                ? $"'{courseName}' has no active directional light; added a temporary DGM key light."
                : $"Added a DGM fill light and ambient lighting to '{courseName}' alongside its {directionalLightCount} imported directional light(s).");
        }

        if ((unsupportedMaterialCount > 0 || lighting.ForceFallbackMaterials) && fallbackShader == null)
            Plugin.LogSource.LogWarning($"'{courseName}' needs fallback materials, but no compatible DGM fallback shader was found.");
        if (lighting.ForceUnlitMaterials && !useAlbedoOnly)
            Plugin.LogSource.LogWarning($"'{courseName}' requested albedo-only materials but DGM's URP Unlit shader is unavailable.");
        if (lighting.RealtimeMapShadows && litFoliageShader == null)
            Plugin.LogSource.LogWarning($"'{courseName}' requested lit shadow-receiving materials but DGM's URP Lit shader is unavailable.");
        if (lighting.RealtimeMapShadows)
        {
            ImportedShadowDiagnostics.Log(importedSun);
            ImportedShadowDiagnostics.LogSceneLayers(importedSun, renderRoots, sceneRootCount);
        }
    }

    private static int ApplyImportedLocalLightIntensity(Scene scene, string courseName, MapLightingDefinition lighting)
    {
        if (lighting == null || Mathf.Approximately(lighting.LocalLightIntensityMultiplier, 1f))
            return 0;

        var adjustedCount = 0;
        Il2CppReferenceArray<GameObject> roots;
        try
        {
            roots = scene.GetRootGameObjects();
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Could not enumerate imported lights in '{courseName}': {ex.Message}");
            return 0;
        }

        for (var rootIndex = 0; rootIndex < roots.Length; rootIndex++)
        {
            var root = roots[rootIndex];
            if (root == null)
                continue;

            Il2CppArrayBase<Light> lights;
            try
            {
                lights = root.GetComponentsInChildren<Light>(true);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"Could not enumerate lights below imported root '{root.name}' in '{courseName}': {ex.Message}");
                continue;
            }

            for (var lightIndex = 0; lightIndex < lights.Length; lightIndex++)
            {
                var light = lights[lightIndex];
                if (light == null || light.type is not (LightType.Point or LightType.Spot))
                    continue;

                try
                {
                    var lightId = light.GetInstanceID();
                    if (!ShadowLightSnapshots.TryGetValue(lightId, out var snapshot))
                    {
                        snapshot = new ShadowLightSnapshot(light);
                        ShadowLightSnapshots[lightId] = snapshot;
                    }
                    light.intensity = snapshot.Intensity * lighting.LocalLightIntensityMultiplier;
                    adjustedCount++;
                }
                catch (Exception ex)
                {
                    Plugin.LogSource.LogWarning($"Could not adjust imported local light '{light.name}' in '{courseName}': {ex.Message}");
                }
            }
        }

        Plugin.LogSource.LogInfo($"Applied imported local-light multiplier before material repair in '{courseName}': adjusted {adjustedCount} point/spot light(s), intensity x{lighting.LocalLightIntensityMultiplier:F2}.");
        return adjustedCount;
    }

    private static void LogImportedTextMeshReferences(Scene scene, string courseName)
    {
        try
        {
            var roots = scene.GetRootGameObjects();
            var componentCount = 0;
            var nullFontCount = 0;
            var nullMaterialCount = 0;
            var nullAtlasCount = 0;
            var examples = new List<string>();

            for (var rootIndex = 0; roots != null && rootIndex < roots.Length; rootIndex++)
            {
                var root = roots[rootIndex];
                if (root == null)
                    continue;

                var textMeshes = root.GetComponentsInChildren<TMP_Text>(true);
                for (var i = 0; textMeshes != null && i < textMeshes.Length; i++)
                {
                    var textMesh = textMeshes[i];
                    if (textMesh == null)
                        continue;

                    componentCount++;
                    var font = textMesh.font;
                    var sharedMaterial = font == null ? null : textMesh.fontSharedMaterial;
                    var atlas = font == null ? null : font.atlasTexture;
                    if (font == null)
                        nullFontCount++;
                    if (font != null && sharedMaterial == null)
                        nullMaterialCount++;
                    if (font != null && atlas == null)
                        nullAtlasCount++;

                    if ((font != null && sharedMaterial != null && atlas != null) || examples.Count >= 32)
                        continue;

                    var fontName = font == null ? "<null>" : font.name;
                    var materialName = sharedMaterial == null ? "<null>" : sharedMaterial.name;
                    examples.Add($"{DescribeImportedTransform(textMesh.transform)}|font={fontName}|material={materialName}|atlas={(atlas == null ? "<null>" : atlas.name)}");
                }
            }

            Plugin.LogSource.LogInfo($"Imported TextMeshPro reference scan for '{courseName}': components={componentCount}, nullFont={nullFontCount}, nullSharedMaterial={nullMaterialCount}, nullAtlas={nullAtlasCount}; incomplete examples={string.Join(" || ", examples)}.");
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogDebug($"Could not inspect imported TextMeshPro references for '{courseName}': {ex.Message}");
        }
    }

    private static string DescribeImportedTransform(Transform transform)
    {
        var names = new Stack<string>();
        for (var current = transform; current != null; current = current.parent)
            names.Push(current.name);
        return string.Join("/", names);
    }

    private static Dictionary<string, Texture> BuildSiblingTextureAliases(List<GameObject> roots, int[] textureProperties)
    {
        var aliases = new Dictionary<string, Texture>(StringComparer.OrdinalIgnoreCase);
        var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            if (root == null) continue;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;
                var materials = renderer.sharedMaterials;
                for (var materialIndex = 0; materials != null && materialIndex < materials.Length; materialIndex++)
                {
                    var material = materials[materialIndex];
                    if (material == null) continue;
                    var key = NormalizeSiblingMaterialName(material.name);
                    if (key.Length < 4 || ambiguous.Contains(key)) continue;

                    Texture texture = null;
                    foreach (var propertyId in textureProperties)
                    {
                        if (!material.HasProperty(propertyId)) continue;
                        texture = material.GetTexture(propertyId);
                        if (texture != null && !ImportedMaterialTextures.IsPlaceholderTexture(texture)) break;
                        texture = null;
                    }
                    texture ??= ImportedMaterialTextures.FindAdditional(material, false);
                    if (texture == null) continue;

                    if (aliases.TryGetValue(key, out var existing) && existing != texture)
                    {
                        aliases.Remove(key);
                        ambiguous.Add(key);
                    }
                    else
                        aliases[key] = texture;
                }
            }
        }
        if (aliases.Count > 0)
            Plugin.LogSource.LogInfo($"Built {aliases.Count} unambiguous texture aliases from matching imported materials.");
        return aliases;
    }

    private static string DescribeTransformPath(Transform transform)
    {
        try
        {
            var names = new List<string>();
            while (transform != null && names.Count < 32)
            {
                names.Add(transform.name);
                transform = transform.parent;
            }
            names.Reverse();
            return names.Count == 0 ? "<unknown>" : string.Join("/", names);
        }
        catch
        {
            return "<unavailable>";
        }
    }

    private static bool TryGetSiblingTexture(Material material, Dictionary<string, Texture> aliases, out Texture texture)
    {
        texture = null;
        if (material == null || aliases == null) return false;
        var key = NormalizeSiblingMaterialName(material.name);
        return key.Length >= 4 && aliases.TryGetValue(key, out texture) && texture != null;
    }

    private static string NormalizeSiblingMaterialName(string name)
    {
        var key = (name ?? string.Empty).Trim();
        var suffixStart = key.Length;
        while (suffixStart > 0 && char.IsDigit(key[suffixStart - 1])) suffixStart--;
        if (suffixStart < key.Length && suffixStart > 0 &&
            (char.IsWhiteSpace(key[suffixStart - 1]) || key[suffixStart - 1] == '.'))
            key = key.Substring(0, suffixStart - 1).TrimEnd();
        return key;
    }

    private static bool IsImportedSkyboxRenderer(Renderer renderer, Material[] materials = null)
    {
        if (renderer == null) return false;
        if (renderer.name.IndexOf("skybox", StringComparison.OrdinalIgnoreCase) >= 0 ||
            renderer.gameObject.name.IndexOf("skybox", StringComparison.OrdinalIgnoreCase) >= 0 ||
            renderer.name.IndexOf("sky dome", StringComparison.OrdinalIgnoreCase) >= 0 ||
            renderer.gameObject.name.IndexOf("sky dome", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        materials ??= renderer.sharedMaterials;
        for (var i = 0; materials != null && i < materials.Length; i++)
        {
            var material = materials[i];
            if (material == null) continue;
            var shaderName = material.shader == null ? "" : material.shader.name;
            if (material.name.IndexOf("customskybox", StringComparison.OrdinalIgnoreCase) >= 0 ||
                shaderName.IndexOf("skybox", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    private static void AddTerrainAssets(Scene scene, Il2CppReferenceArray<GameObject> roots, List<GameObject> renderRoots, HashSet<int> renderRootIds, MapLightingDefinition lighting)
    {
        var terrainShader = FindCompatibleTerrainShader(scene);
        var initialRootCount = renderRoots.Count;

        for (var rootIndex = 0; rootIndex < roots.Length; rootIndex++)
        {
            var root = roots[rootIndex];
            if (root == null)
                continue;
            var terrains = root.GetComponentsInChildren<Terrain>(true);
            for (var i = 0; i < terrains.Length; i++)
            {
                var terrain = terrains[i];
                if (terrain == null || terrain.terrainData == null)
                    continue;

                if (lighting.RealtimeMapShadows)
                {
                    try
                    {
                        var terrainId = terrain.GetInstanceID();
                        if (!TerrainShadowSnapshots.ContainsKey(terrainId)) TerrainShadowSnapshots[terrainId] = new TerrainShadowSnapshot(terrain);
                        terrain.shadowCastingMode = ShadowCastingMode.On;
                        Plugin.LogSource.LogInfo($"Enabled tree/terrain shadow casting on '{terrain.name}' (previous={TerrainShadowSnapshots[terrainId].CastingMode}).");
                    }
                    catch (Exception ex) { Plugin.LogSource.LogWarning($"Could not enable terrain shadow casting: {ex.Message}"); }
                }

                if (!lighting.PreserveLightmaps)
                {
                    try
                    {
                        var terrainId = terrain.GetInstanceID();
                        if (!TerrainLightmapSnapshots.ContainsKey(terrainId)) TerrainLightmapSnapshots[terrainId] = new TerrainLightmapSnapshot(terrain);
                        var snapshot = TerrainLightmapSnapshots[terrainId];
                        terrain.lightmapIndex = -1;
                        terrain.realtimeLightmapIndex = -1;
                        Plugin.LogSource.LogInfo($"Cleared imported terrain lightmaps on '{terrain.name}' (previous={snapshot.LightmapIndex}/{snapshot.RealtimeLightmapIndex}).");
                    }
                    catch (Exception ex) { Plugin.LogSource.LogWarning($"Could not clear imported terrain lightmaps: {ex.Message}"); }
                }

                var data = terrain.terrainData;
                var treeCount = 0;
                var detailCount = 0;
                try
                {
                    var trees = data.treePrototypes;
                    treeCount = trees == null ? 0 : trees.Length;
                    for (var treeIndex = 0; treeIndex < treeCount; treeIndex++)
                    {
                        var prefab = trees[treeIndex] == null ? null : trees[treeIndex].prefab;
                        if (prefab != null && renderRootIds.Add(prefab.GetInstanceID()))
                        {
                            renderRoots.Add(prefab);
                            if (lighting.ForceTreeMeshLods)
                                RepairTreeMeshLods(prefab);
                        }
                    }
                    if (lighting.GenerateTreeCanopyColliders)
                        Plugin.LogSource.LogWarning("Experimental canopy collision is temporarily disabled after an IL2CPP crash; existing map collision remains active.");
                    if (lighting.ExperimentalFoliageInteractions)
                        ImportedFoliageInteraction.AddTerrain(terrain, trees);
                }
                catch (Exception ex) { Plugin.LogSource.LogWarning($"Could not inspect trees for terrain '{terrain.name}': {ex.Message}"); }

                if (lighting.ForceTreeMeshLods)
                {
                    try
                    {
                        var snapshot = new TerrainTreeSnapshot(terrain);
                        TerrainTreeSnapshots[terrain.GetInstanceID()] = snapshot;
                        terrain.treeBillboardDistance = Mathf.Max(terrain.treeDistance, snapshot.BillboardDistance);
                        terrain.treeMaximumFullLODCount = Mathf.Max(snapshot.MaximumFullLodCount, 5000);
                        terrain.treeCrossFadeLength = 0f;
                        Plugin.LogSource.LogInfo($"Set terrain '{terrain.name}' to use tree meshes to its tree draw distance ({terrain.treeDistance:F1}).");
                    }
                    catch (Exception ex) { Plugin.LogSource.LogWarning($"Could not adjust terrain tree distances for '{terrain.name}': {ex.Message}"); }
                }

                try
                {
                    var details = data.detailPrototypes;
                    detailCount = details == null ? 0 : details.Length;
                    for (var detailIndex = 0; detailIndex < detailCount; detailIndex++)
                    {
                        var prefab = details[detailIndex] == null ? null : details[detailIndex].prototype;
                        if (prefab != null && renderRootIds.Add(prefab.GetInstanceID()))
                            renderRoots.Add(prefab);
                    }
                    if (lighting.RepairTerrainTextureDetails && details != null)
                        RepairTerrainTextureDetails(scene, data, details);
                }
                catch (Exception ex) { Plugin.LogSource.LogWarning($"Could not inspect details for terrain '{terrain.name}': {ex.Message}"); }

                try
                {
                    var previousMaterial = terrain.materialTemplate;
                    if (terrainShader != null)
                    {
                        var replacement = new Material(terrainShader) { name = "DGM_Imported_Terrain" };
                        terrain.materialTemplate = replacement;
                        TerrainMaterialSnapshots[terrain.GetInstanceID()] = new TerrainMaterialSnapshot(terrain, previousMaterial);
                        TemporaryMaterials.Add(replacement);
                    }
                    Plugin.LogSource.LogInfo($"Imported terrain '{terrain.name}': tree prototypes={treeCount}, detail prototypes={detailCount}, original shader='{(previousMaterial == null || previousMaterial.shader == null ? "<default>" : previousMaterial.shader.name)}', replacement='{(terrainShader == null ? "<unavailable>" : terrainShader.name)}'.");
                }
                catch (Exception ex)
                {
                    Plugin.LogSource.LogWarning($"Could not fully repair imported terrain '{terrain.name}': {ex.Message}");
                }
            }
        }
        Plugin.LogSource.LogInfo($"Collected {renderRoots.Count - initialRootCount} imported terrain tree/detail prefab root(s) for material repair.");
    }

    private static void RepairTreeMeshLods(GameObject prefab)
    {
        var groups = prefab.GetComponentsInChildren<LODGroup>(true);
        var repairedGroups = 0;
        for (var groupIndex = 0; groupIndex < groups.Length; groupIndex++)
        {
            var group = groups[groupIndex];
            if (group == null)
                continue;
            try
            {
                var originalLods = group.GetLODs();
                if (originalLods == null || originalLods.Length == 0)
                    continue;
                var retained = new List<LOD>();
                var removedBillboard = false;
                for (var lodIndex = 0; lodIndex < originalLods.Length; lodIndex++)
                {
                    var sourceLod = originalLods[lodIndex];
                    if (sourceLod == null || sourceLod.renderers == null)
                        continue;
                    var meshes = new List<Renderer>();
                    var renderers = sourceLod.renderers;
                    for (var rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                    {
                        var renderer = renderers[rendererIndex];
                        if (renderer == null)
                            continue;
                        if (IsTreeBillboardRenderer(renderer))
                            removedBillboard = true;
                        else
                            meshes.Add(renderer);
                    }
                    if (meshes.Count == 0)
                        continue;
                    var meshArray = new Il2CppReferenceArray<Renderer>(meshes.Count);
                    for (var i = 0; i < meshes.Count; i++) meshArray[i] = meshes[i];
                    retained.Add(new LOD
                    {
                        screenRelativeTransitionHeight = sourceLod.screenRelativeTransitionHeight,
                        fadeTransitionWidth = 0f,
                        renderers = meshArray
                    });
                }

                if (!removedBillboard || retained.Count == 0)
                    continue;
                retained[retained.Count - 1].screenRelativeTransitionHeight = 0.0001f;
                var replacements = new Il2CppReferenceArray<LOD>(retained.Count);
                for (var i = 0; i < retained.Count; i++) replacements[i] = retained[i];
                TreeLodSnapshots[group.GetInstanceID()] = new TreeLodSnapshot(group, originalLods);
                group.SetLODs(replacements);
                group.lastLODBillboard = false;
                group.animateCrossFading = false;
                group.fadeMode = LODFadeMode.None;
                group.RecalculateBounds();
                repairedGroups++;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"Could not replace billboard LODs in '{prefab.name}': {ex.Message}");
            }
        }

        var allRenderers = prefab.GetComponentsInChildren<Renderer>(true);
        var hidden = 0;
        for (var i = 0; i < allRenderers.Length; i++)
        {
            var renderer = allRenderers[i];
            if (renderer == null || !IsTreeBillboardRenderer(renderer))
                continue;
            var id = renderer.GetInstanceID();
            if (!RendererSnapshots.ContainsKey(id))
                RendererSnapshots[id] = new RendererSnapshot(renderer, renderer.enabled);
            renderer.enabled = false;
            hidden++;
        }
        Plugin.LogSource.LogInfo($"Tree prefab '{prefab.name}': repaired {repairedGroups} LOD group(s), hid {hidden} incompatible billboard renderer(s).");
    }

    private static bool IsTreeBillboardRenderer(Renderer renderer)
    {
        var materials = renderer == null ? null : renderer.sharedMaterials;
        if (materials == null)
            return false;
        for (var i = 0; i < materials.Length; i++)
        {
            var material = materials[i];
            if (material == null)
                continue;
            var shaderName = material.shader == null ? "" : material.shader.name;
            if (material.name.IndexOf("billboard", StringComparison.OrdinalIgnoreCase) >= 0 ||
                shaderName.EndsWith("/Bil", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static void RepairTerrainTextureDetails(Scene scene, TerrainData data, Il2CppReferenceArray<DetailPrototype> originals)
    {
        if (TerrainDetailSnapshots.ContainsKey(data.GetInstanceID()))
            return;
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        var useLitCutout = shader != null && shader.isSupported;
        if (!useLitCutout)
        {
            shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader != null && shader.isSupported)
                Plugin.LogSource.LogWarning("URP Lit is unavailable for terrain detail cutouts; falling back to URP Unlit, which may not preserve some source foliage alpha masks.");
        }
        if (shader == null || !shader.isSupported)
        {
            Plugin.LogSource.LogWarning("Cannot repair texture-based terrain details because neither URP Lit nor URP Unlit is available.");
            return;
        }

        var updated = new Il2CppReferenceArray<DetailPrototype>(originals.Length);
        Mesh grassMesh = null;
        var converted = 0;
        for (var i = 0; i < originals.Length; i++)
        {
            var source = originals[i];
            updated[i] = source;
            if (source == null)
                continue;
            try
            {
                if (source.usePrototypeMesh)
                {
                    Plugin.LogSource.LogInfo($"Terrain detail {i} already uses a mesh prefab and is included in prefab material repair.");
                    continue;
                }
                var texture = source.prototypeTexture;
                if (texture == null)
                {
                    Plugin.LogSource.LogWarning($"Terrain detail {i} has no texture or mesh to repair.");
                    continue;
                }

                if (grassMesh == null)
                    grassMesh = CreateTerrainGrassMesh();
                var material = new Material(shader) { name = $"DGM_TerrainDetail_{i}", renderQueue = 2450 };
                TemporaryMaterials.Add(material);
                try { material.enableInstancing = true; }
                catch (Exception ex) { Plugin.LogSource.LogWarning($"Could not enable detail material instancing: {ex.Message}"); }
                var baseMap = Shader.PropertyToID("_BaseMap");
                var mainTex = Shader.PropertyToID("_MainTex");
                if (material.HasProperty(baseMap)) material.SetTexture(baseMap, texture);
                if (material.HasProperty(mainTex)) material.SetTexture(mainTex, texture);
                var baseColor = Shader.PropertyToID("_BaseColor");
                if (material.HasProperty(baseColor)) material.SetColor(baseColor, Color.white);
                var metallic = Shader.PropertyToID("_Metallic");
                if (material.HasProperty(metallic)) material.SetFloat(metallic, 0f);
                var smoothness = Shader.PropertyToID("_Smoothness");
                if (material.HasProperty(smoothness)) material.SetFloat(smoothness, 0f);
                var cutoff = Shader.PropertyToID("_Cutoff");
                if (material.HasProperty(cutoff)) material.SetFloat(cutoff, 0.35f);
                var alphaClip = Shader.PropertyToID("_AlphaClip");
                if (material.HasProperty(alphaClip)) material.SetFloat(alphaClip, 1f);
                var cull = Shader.PropertyToID("_Cull");
                if (material.HasProperty(cull)) material.SetFloat(cull, 0f);
                material.EnableKeyword("_ALPHATEST_ON");

                var prefab = new GameObject($"DGM_TerrainDetailPrefab_{i}");
                SceneManager.MoveGameObjectToScene(prefab, scene);
                TemporaryObjects.Add(prefab);
                prefab.AddComponent<MeshFilter>().sharedMesh = grassMesh;
                prefab.AddComponent<MeshRenderer>().sharedMaterial = material;
                prefab.SetActive(false);

                var replacement = new DetailPrototype
                {
                    prototype = prefab,
                    usePrototypeMesh = true,
                    renderMode = DetailRenderMode.VertexLit,
                    minWidth = source.minWidth,
                    maxWidth = source.maxWidth,
                    minHeight = source.minHeight,
                    maxHeight = source.maxHeight,
                    noiseSpread = source.noiseSpread,
                    bendFactor = source.bendFactor,
                    healthyColor = Color.white,
                    dryColor = Color.white
                };
                try { replacement.useInstancing = true; }
                catch (Exception ex) { Plugin.LogSource.LogWarning($"Could not enable detail {i} instancing: {ex.Message}"); }
                try
                {
                    replacement.noiseSeed = source.noiseSeed;
                    replacement.density = source.density;
                    replacement.holeEdgePadding = source.holeEdgePadding;
                    replacement.targetCoverage = source.targetCoverage;
                    replacement.useDensityScaling = source.useDensityScaling;
                    replacement.alignToGround = source.alignToGround;
                    replacement.positionJitter = source.positionJitter;
                }
                catch (Exception ex) { Plugin.LogSource.LogDebug($"Some terrain detail options could not be copied: {ex.Message}"); }
                updated[i] = replacement;
                converted++;
                Plugin.LogSource.LogInfo($"Rebuilt terrain texture detail {i} with '{texture.name}' as an instanced cutout mesh using {shader.name}.");
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"Could not rebuild terrain texture detail {i}: {ex.Message}");
            }
        }

        if (converted == 0)
            return;
        TerrainDetailSnapshots[data.GetInstanceID()] = new TerrainDetailSnapshot(data, originals);
        data.detailPrototypes = updated;
        Plugin.LogSource.LogInfo($"Rebuilt {converted} of {originals.Length} terrain detail prototype(s); existing mesh details were retained.");
    }

    private static Mesh CreateTerrainGrassMesh()
    {
        var mesh = new Mesh { name = "DGM_TerrainDetailCross" };
        var positions = new[]
        {
            new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f), new Vector3(-0.5f, 1f, 0f), new Vector3(0.5f, 1f, 0f),
            new Vector3(0f, 0f, -0.5f), new Vector3(0f, 0f, 0.5f), new Vector3(0f, 1f, -0.5f), new Vector3(0f, 1f, 0.5f)
        };
        var uvs = new[]
        {
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f)
        };
        var indices = new[] { 0, 2, 1, 2, 3, 1, 4, 6, 5, 6, 7, 5 };
        var vertexArray = new Il2CppStructArray<Vector3>(positions.Length);
        var uvArray = new Il2CppStructArray<Vector2>(uvs.Length);
        var indexArray = new Il2CppStructArray<int>(indices.Length);
        for (var i = 0; i < positions.Length; i++) { vertexArray[i] = positions[i]; uvArray[i] = uvs[i]; }
        for (var i = 0; i < indices.Length; i++) indexArray[i] = indices[i];
        mesh.vertices = vertexArray;
        mesh.uv = uvArray;
        mesh.triangles = indexArray;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        TemporaryMeshes.Add(mesh);
        return mesh;
    }

    private static Shader FindCompatibleTerrainShader(Scene importedScene)
    {
        var shader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
        if (shader != null && shader.isSupported)
            return shader;
        for (var sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
        {
            var candidateScene = SceneManager.GetSceneAt(sceneIndex);
            if (!candidateScene.isLoaded || candidateScene.name == importedScene.name)
                continue;
            var roots = candidateScene.GetRootGameObjects();
            for (var rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                if (roots[rootIndex] == null)
                    continue;
                var terrains = roots[rootIndex].GetComponentsInChildren<Terrain>(true);
                for (var i = 0; i < terrains.Length; i++)
                {
                    var material = terrains[i] == null ? null : terrains[i].materialTemplate;
                    if (material != null && material.shader != null && material.shader.isSupported)
                        return material.shader;
                }
            }
        }
        return null;
    }

    private static Shader FindSupportedFallbackShader()
    {
        var candidates = new[]
        {
            "Universal Render Pipeline/Lit",
            "Universal Render Pipeline/Simple Lit",
            "Universal Render Pipeline/Unlit",
            "Standard",
            "Unlit/Texture"
        };
        foreach (var candidate in candidates)
        {
            var shader = Shader.Find(candidate);
            if (shader != null && shader.isSupported)
                return shader;
        }
        return null;
    }
}
