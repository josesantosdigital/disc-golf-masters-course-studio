using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace DgmCustomCourses;

// Scoped to imported rounds: do not change the player's pipeline or quality settings.
internal static class ImportedShadowDiagnostics
{
    private static bool _loggedAfterRender;

    public static void ResetRound() => _loggedAfterRender = false;

    // The map is set up before gameplay cameras render. Inspect once after a throw,
    // using an existing gameplay hook rather than adding an Update loop.
    public static void LogAfterRenderOnce()
    {
        if (_loggedAfterRender) return;
        _loggedAfterRender = true;
        try
        {
            var texture = Shader.GetGlobalTexture("_MainLightShadowmapTexture");
            var sun = RenderSettings.sun;
            Plugin.LogSource.LogInfo($"Shadow diagnostics after throw: sun={(sun == null ? "<none>" : sun.name)}, main={Shader.IsKeywordEnabled("_MAIN_LIGHT_SHADOWS")}, cascades={Shader.IsKeywordEnabled("_MAIN_LIGHT_SHADOWS_CASCADE")}, screen={Shader.IsKeywordEnabled("_MAIN_LIGHT_SHADOWS_SCREEN")}, shadowmap={(texture == null ? "<none>" : texture.name + " " + texture.width + "x" + texture.height)}.");
        }
        catch (Exception ex) { Plugin.LogSource.LogWarning($"Could not inspect post-render shadow globals: {ex.Message}"); }

        try
        {
            for (var sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                var scene = SceneManager.GetSceneAt(sceneIndex);
                if (!scene.isLoaded) continue;
                var roots = scene.GetRootGameObjects();
                for (var rootIndex = 0; roots != null && rootIndex < roots.Length; rootIndex++)
                {
                    var root = roots[rootIndex];
                    if (root == null) continue;
                    var lights = root.GetComponentsInChildren<Light>(true);
                    for (var i = 0; lights != null && i < lights.Length; i++)
                    {
                        var light = lights[i];
                        if (light == null || !light.enabled || !light.gameObject.activeInHierarchy || light.type != LightType.Directional) continue;
                        Plugin.LogSource.LogInfo($"Shadow diagnostics after throw: scene='{scene.name}', directional light='{light.name}', intensity={light.intensity:F2}, shadows={light.shadows}, layer={light.renderingLayerMask}.");
                    }
                }
            }
        }
        catch (Exception ex) { Plugin.LogSource.LogWarning($"Could not list active directional lights: {ex.Message}"); }
    }

    public static void Log(Light light)
    {
        try
        {
            var asset = GraphicsSettings.currentRenderPipeline?.TryCast<UniversalRenderPipelineAsset>();
            Plugin.LogSource.LogInfo(asset == null
                ? "Shadow diagnostics: active render pipeline is not a URP asset."
                : $"Shadow diagnostics: URP main-light shadows={asset.supportsMainLightShadows}, distance={asset.shadowDistance:F1}; quality shadows={QualitySettings.shadows}, distance={QualitySettings.shadowDistance:F1}.");
        }
        catch (Exception ex) { Plugin.LogSource.LogWarning($"Could not inspect render-pipeline shadow settings: {ex.Message}"); }

        try
        {
            var camera = Camera.main;
            var data = camera == null ? null : camera.GetComponent<UniversalAdditionalCameraData>();
            Plugin.LogSource.LogInfo(data == null
                ? "Shadow diagnostics: no main camera URP data available at map setup."
                : $"Shadow diagnostics: main camera '{camera.name}' renderShadows={data.renderShadows}.");
        }
        catch (Exception ex) { Plugin.LogSource.LogWarning($"Could not inspect camera shadow settings: {ex.Message}"); }

        try
        {
            var cameras = Camera.allCameras;
            for (var i = 0; cameras != null && i < cameras.Length; i++)
            {
                var camera = cameras[i];
                if (camera == null || !camera.enabled) continue;
                var data = camera.GetComponent<UniversalAdditionalCameraData>();
                Plugin.LogSource.LogInfo($"Shadow diagnostics: active camera '{camera.name}', depth={camera.depth}, URP renderShadows={(data == null ? "<default>" : data.renderShadows.ToString())}.");
            }
        }
        catch (Exception ex) { Plugin.LogSource.LogWarning($"Could not list active camera shadow settings: {ex.Message}"); }

        if (light == null) return;
        try
        {
            Plugin.LogSource.LogInfo($"Shadow diagnostics: sun='{light.name}', strength={light.shadowStrength:F2}, shadows={light.shadows}, cullingMask={light.cullingMask}, renderingLayerMask={light.renderingLayerMask}, intensity={light.intensity:F2}, bakeMode={light.bakingOutput.lightmapBakeType}.");
        }
        catch (Exception ex) { Plugin.LogSource.LogWarning($"Could not inspect imported sun shadow settings: {ex.Message}"); }
    }

    public static void LogSceneLayers(Light light, List<GameObject> roots, int sceneRootCount)
    {
        try
        {
            var loggedRenderers = 0;
            for (var rootIndex = 0; rootIndex < roots.Count; rootIndex++)
            {
                var root = roots[rootIndex];
                if (root == null) continue;
                if (rootIndex >= sceneRootCount)
                {
                    var prefabRenderers = root.GetComponentsInChildren<Renderer>(true);
                    for (var i = 0; prefabRenderers != null && i < prefabRenderers.Length; i++)
                    {
                        var prefabRenderer = prefabRenderers[i];
                        if (prefabRenderer == null) continue;
                        var prefabMaterials = prefabRenderer.sharedMaterials;
                        for (var j = 0; prefabMaterials != null && j < prefabMaterials.Length; j++)
                        {
                            var prefabMaterial = prefabMaterials[j];
                            if (prefabMaterial == null) continue;
                            Plugin.LogSource.LogInfo($"Shadow diagnostics: tree prefab='{root.name}', renderer='{prefabRenderer.name}', enabled={prefabRenderer.enabled}, casting={prefabRenderer.shadowCastingMode}, layer={prefabRenderer.renderingLayerMask}, material='{prefabMaterial.name}', shader='{(prefabMaterial.shader == null ? "<null>" : prefabMaterial.shader.name)}', shadowPass={prefabMaterial.FindPass("ShadowCaster")}, passEnabled={prefabMaterial.GetShaderPassEnabled("ShadowCaster")}, instancing={prefabMaterial.enableInstancing}.");
                        }
                    }
                }
                var terrains = root.GetComponentsInChildren<Terrain>(true);
                for (var i = 0; terrains != null && i < terrains.Length; i++)
                {
                    var terrain = terrains[i];
                    if (terrain == null) continue;
                    var material = terrain.materialTemplate;
                    Plugin.LogSource.LogInfo($"Shadow diagnostics: terrain='{terrain.name}', renderingLayerMask={terrain.renderingLayerMask}, casting={terrain.shadowCastingMode}, lightmap={terrain.lightmapIndex}, material='{(material == null ? "<null>" : material.name)}', receiveShadowsOff={(material != null && material.IsKeywordEnabled("_RECEIVE_SHADOWS_OFF"))}.");
                }
                var renderers = root.GetComponentsInChildren<Renderer>(true);
                for (var i = 0; renderers != null && i < renderers.Length && loggedRenderers < 6; i++)
                {
                    var renderer = renderers[i];
                    if (renderer == null || !renderer.enabled || renderer.GetComponent<MeshRenderer>() == null) continue;
                    var material = renderer.sharedMaterial;
                    Plugin.LogSource.LogInfo($"Shadow diagnostics: renderer='{renderer.name}', renderingLayerMask={renderer.renderingLayerMask}, casting={renderer.shadowCastingMode}, receive={renderer.receiveShadows}, material='{(material == null ? "<null>" : material.name)}', shadowPass={(material == null ? -1 : material.FindPass("ShadowCaster"))}, passEnabled={(material != null && material.GetShaderPassEnabled("ShadowCaster"))}.");
                    loggedRenderers++;
                }
            }
        }
        catch (Exception ex) { Plugin.LogSource.LogWarning($"Could not inspect terrain/renderer shadow layers: {ex.Message}"); }
    }
}
