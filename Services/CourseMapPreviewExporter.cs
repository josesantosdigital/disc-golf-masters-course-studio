using UnityEngine;
using UnityEngine.SceneManagement;

namespace DgmCustomCourses;

internal static class CourseMapPreviewExporter
{
    private const int PreviewWidth = 1280;
    private const int PreviewHeight = 800;

    public static void CaptureIfMissing(Scene mapScene, Bounds bounds, string courseId)
    {
        var safeId = SafeId(courseId);
        if (string.IsNullOrEmpty(safeId) || !mapScene.IsValid() || !mapScene.isLoaded)
            return;

        var mapDirectory = Path.Combine(BepInEx.Paths.PluginPath, "CustomCourseLoader", "CourseEditor", "assets", "maps");
        var outputPath = Path.Combine(mapDirectory, $"imported-{safeId}.png");
        if (File.Exists(outputPath))
            return;

        var disabledRenderers = new List<(Renderer Renderer, bool Enabled)>();
        var disabledTerrains = new List<(Terrain Terrain, bool Enabled)>();
        GameObject cameraObject = null;
        RenderTexture target = null;
        try
        {
            Directory.CreateDirectory(mapDirectory);
            for (var sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                var scene = SceneManager.GetSceneAt(sceneIndex);
                if (!scene.IsValid() || scene.handle == mapScene.handle)
                    continue;

                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                    {
                        if (renderer == null || !renderer.enabled)
                            continue;
                        disabledRenderers.Add((renderer, renderer.enabled));
                        renderer.enabled = false;
                    }
                    foreach (var terrain in root.GetComponentsInChildren<Terrain>(true))
                    {
                        if (terrain == null || !terrain.enabled)
                            continue;
                        disabledTerrains.Add((terrain, terrain.enabled));
                        terrain.enabled = false;
                    }
                }
            }

            cameraObject = new GameObject("DGM_CourseStudio_MapCapture");
            SceneManager.MoveGameObjectToScene(cameraObject, mapScene);
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.aspect = PreviewWidth / (float)PreviewHeight;
            camera.orthographicSize = Mathf.Max(bounds.size.z * 0.5f, bounds.size.x * 0.5f / camera.aspect) * 1.06f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = Mathf.Max(1000f, bounds.size.y + 500f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.18f, 0.22f, 0.24f, 1f);
            camera.useOcclusionCulling = false;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.cullingMask = ~0;
            var heightAboveMap = Mathf.Max(20f, bounds.size.y * 0.25f);
            camera.transform.SetPositionAndRotation(
                new Vector3(bounds.center.x, bounds.max.y + heightAboveMap, bounds.center.z),
                Quaternion.Euler(90f, 0f, 0f));

            target = RenderTexture.GetTemporary(PreviewWidth, PreviewHeight, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = target;
            camera.Render();
            var png = TexturePngEncoder.Encode(target, PreviewWidth);
            if (png == null || png.Length == 0)
                throw new InvalidDataException("The map capture returned an empty image.");

            var temporaryPath = outputPath + ".tmp";
            File.WriteAllBytes(temporaryPath, png);
            File.Move(temporaryPath, outputPath, true);
            Plugin.LogSource.LogInfo($"Captured a {PreviewWidth}x{PreviewHeight} Course Studio overview for imported map '{courseId}'.");
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Could not capture an automatic Course Studio overview for '{courseId}': {ex.Message}");
        }
        finally
        {
            foreach (var item in disabledRenderers)
                if (item.Renderer != null)
                    item.Renderer.enabled = item.Enabled;
            foreach (var item in disabledTerrains)
                if (item.Terrain != null)
                    item.Terrain.enabled = item.Enabled;
            if (cameraObject != null)
                UnityEngine.Object.Destroy(cameraObject);
            if (target != null)
                RenderTexture.ReleaseTemporary(target);
        }
    }

    private static string SafeId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        return new string(value.ToLowerInvariant().Where(character =>
            character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_').ToArray());
    }
}
