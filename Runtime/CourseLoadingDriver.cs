using UnityEngine;

namespace DgmCustomCourses;

// BepInEx registers this component with IL2CPP when the plugin loads.
public sealed class CourseLoadingDriver : MonoBehaviour
{
    internal static bool Available { get; set; }
    public CourseLoadingDriver(IntPtr pointer) : base(pointer) { }

    public void Update()
    {
        try { Plugin.Registry?.TickCourseSelectionLoading(); }
        catch (Exception ex) { Plugin.LogSource.LogWarning($"Course selection loading failed: {ex}"); Plugin.Registry?.CancelCourseSelectionLoading(); }
        try { MapSceneRuntime.TickLoading(); }
        catch (Exception ex) { MapSceneRuntime.FailLoading(ex); }
        try { CourseSceneCardCapture.Tick(); }
        catch (Exception ex) { Plugin.LogSource.LogWarning($"Course card capture failed: {ex.Message}"); CourseSceneCardCapture.Cancel(); }
        try { CourseLoadingOverlay.Tick(); }
        catch (Exception ex) { Plugin.LogSource.LogWarning($"Loading screen update failed: {ex.Message}"); CourseLoadingOverlay.Cancel(); }
    }
}
