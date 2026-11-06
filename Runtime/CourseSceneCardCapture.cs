using BepInEx;
using Dgm;
using UnityEngine;

namespace DgmCustomCourses;

// Capture only after an actual custom round has placed its holes and finished loading.
internal static class CourseSceneCardCapture
{
    private const int Width = 960;
    private const int Height = 540;
    private const int CaptureRevision = 2;
    private static CustomCourseRecord _record;
    private static Transform _tee;
    private static Transform _basket;
    private static float _readyAt;
    private static float _deadline;

    internal static string GetPath(CourseDefinition definition) => Path.Combine(
        Paths.ConfigPath, "CustomCourseLoader", "Previews",
        $"{definition.Id.ToLowerInvariant()}-capture-{CaptureRevision}-{definition.PreviewFingerprint}.png");

    internal static void Schedule(CustomCourseRecord record, HoleSettings hole)
    {
        Cancel();
        if (!CourseLoadingDriver.Available)
        {
            Plugin.LogSource.LogWarning("Could not schedule a course preview: the Unity loading driver is unavailable.");
            return;
        }
        if (record?.Definition == null || hole == null)
        {
            Plugin.LogSource.LogWarning("Could not schedule a course preview: the custom course or active hole is unavailable.");
            return;
        }
        if (record.Definition.Holes.Count == 0)
        {
            Plugin.LogSource.LogWarning($"Could not schedule a course preview for '{record.Definition.Name}': it has no holes.");
            return;
        }
        if (File.Exists(GetPath(record.Definition)))
        {
            Plugin.LogSource.LogInfo($"Using the cached course preview for '{record.Definition.Name}'.");
            return;
        }
        var index = Math.Clamp(hole.HoleNr - 1, 0, record.Definition.Holes.Count - 1);
        var definition = record.Definition.Holes[index];
        var tee = hole.GetTeepad(CourseRegistry.ParseVariant(definition.TeeVariant)) ?? hole.ActiveTeepad;
        var basket = hole.GetBasket(CourseRegistry.ParseVariant(definition.PinVariant)) ?? hole.ActiveBasket;
        if (tee == null || basket == null)
        {
            Plugin.LogSource.LogWarning($"Could not schedule a course preview for '{record.Definition.Name}': the active tee or basket is unavailable.");
            return;
        }
        _record = record;
        _tee = tee.transform;
        _basket = basket.transform;
        _readyAt = Time.unscaledTime + 3f;
        _deadline = Time.unscaledTime + 60f;
        Plugin.LogSource.LogInfo($"Scheduled a course preview for '{record.Definition.Name}' from hole {hole.HoleNr}; capture will use a tagged main camera when available.");
    }

    internal static void Cancel()
    {
        _record = null;
        _tee = null;
        _basket = null;
    }

    internal static void Tick()
    {
        if (_record == null) return;
        if (_tee == null || _basket == null)
        {
            Plugin.LogSource.LogWarning("Cancelled a course preview because its tee or basket was destroyed before capture.");
            Cancel();
            return;
        }
        if (Time.unscaledTime >= _deadline)
        {
            Plugin.LogSource.LogWarning($"Cancelled a course preview for '{_record.Definition.Name}' after its capture deadline.");
            Cancel();
            return;
        }
        if (Time.unscaledTime < _readyAt) return;

        var record = _record;
        var tee = _tee;
        var basket = _basket;
        Cancel();
        Capture(record, tee, basket);
    }

    private static void Capture(CustomCourseRecord record, Transform tee, Transform basket)
    {
        GameObject cameraObject = null;
        RenderTexture target = null;
        string temporaryPath = null;
        try
        {
            var outputPath = GetPath(record.Definition);
            if (File.Exists(outputPath)) return;
            var sourceCamera = Camera.main;
            var direction = basket.position - tee.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 25f) { direction = tee.forward; direction.y = 0f; }
            if (direction.sqrMagnitude < 0.01f) direction = Vector3.forward;
            direction.Normalize();
            var right = Vector3.Cross(Vector3.up, direction).normalized;
            var holeLength = Vector3.Distance(tee.position, basket.position);
            var viewDistance = Mathf.Clamp(holeLength * 0.55f, 32f, 100f);
            var cameraHeight = Mathf.Clamp(viewDistance * 0.65f, 22f, 70f);
            var position = tee.position - direction * viewDistance + right * (viewDistance * 0.25f) + Vector3.up * cameraHeight;
            var focus = Vector3.Lerp(tee.position, basket.position, 0.45f) + Vector3.up * 2f;

            cameraObject = new GameObject("DGM_CourseStudio_CardCapture");
            var camera = cameraObject.AddComponent<Camera>();
            if (sourceCamera != null)
                camera.CopyFrom(sourceCamera);
            else
            {
                camera.clearFlags = CameraClearFlags.Skybox;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 2000f;
                camera.cullingMask = ~0;
                camera.allowHDR = false;
                camera.allowMSAA = false;
                Plugin.LogSource.LogWarning("No tagged main camera was available; capturing the course preview with a standalone camera.");
            }
            camera.enabled = false;
            camera.orthographic = false;
            camera.fieldOfView = 55f;
            camera.aspect = Width / (float)Height;
            camera.rect = new Rect(0f, 0f, 1f, 1f);
            camera.useOcclusionCulling = false;
            camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(focus - position, Vector3.up));
            target = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = target;
            camera.Render();
            var png = TexturePngEncoder.Encode(target, Width);
            if (png == null || png.Length == 0) throw new InvalidDataException("The course capture returned an empty image.");

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            temporaryPath = outputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temporaryPath, png);
            File.Move(temporaryPath, outputPath, false);
            temporaryPath = null;
            Plugin.Registry?.SetRenderedCourseCard(record, outputPath);
            Plugin.LogSource.LogInfo($"Captured a {Width}x{Height} course preview for '{record.Definition.Name}'.");
        }
        catch (Exception ex) { Plugin.LogSource.LogWarning($"Could not capture a course preview for '{record.Definition.Name}': {ex.Message}"); }
        finally
        {
            if (temporaryPath != null) { try { File.Delete(temporaryPath); } catch { } }
            if (cameraObject != null) UnityEngine.Object.Destroy(cameraObject);
            if (target != null) RenderTexture.ReleaseTemporary(target);
        }
    }
}
