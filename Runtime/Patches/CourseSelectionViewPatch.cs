using Dgm;
using HarmonyLib;
using UnityEngine.EventSystems;
using UnityEngine;
using UnityEngine.UI;

namespace DgmCustomCourses;

// Discover the selector through its Unity lifecycle, avoiding SoloConfiguration.GetCourses,
// whose IL2CPP wrapper crashes even when its postfix only queues work.
[HarmonyPatch(typeof(CourseSelectionViewController), "Awake")]
internal static class CourseSelectionViewAwakePatch
{
    private static bool _loggedAwakeHook;

    private static void Postfix(CourseSelectionViewController __instance)
    {
        if (__instance == null)
            return;

        if (!_loggedAwakeHook)
        {
            _loggedAwakeHook = true;
            Plugin.LogSource.LogInfo("Offline course selector Awake hook ran; preparing custom course entries.");
        }
        Plugin.Registry.SetUpCourseSelection(__instance);
    }
}

[HarmonyPatch(typeof(CourseSelectionViewController), "SetupCourseEntry")]
internal static class CourseEntryViewPatch
{
    private static bool _loggedEntryHook;

    private static void Postfix(CourseSelectionViewController __instance, SoloConfiguration.SoloCourse course)
    {
        if (course == null)
            return;

        var courseId = course.courseId;
        if (!string.IsNullOrEmpty(courseId) && courseId.StartsWith("custom_", StringComparison.OrdinalIgnoreCase))
            return;

        if (!_loggedEntryHook)
        {
            _loggedEntryHook = true;
            Plugin.LogSource.LogInfo("Offline course card hook ran; appending custom courses after a built-in card.");
        }

        Plugin.Registry.SetActiveCourseSelectionView(__instance);
        Plugin.Registry.AppendCourseEntries(__instance);
    }
}

[HarmonyPatch(typeof(CourseSelectionViewController), "SetupCourseSelection")]
internal static class CourseSelectionViewPatch
{
    private static void Postfix(CourseSelectionViewController __instance)
    {
        if (__instance == null)
            return;

        Plugin.Registry.SetUpCourseSelection(__instance);
    }
}

[HarmonyPatch(typeof(ScrollRect), "LateUpdate")]
internal static class CourseSelectionScrollRectPatch
{
    private static void Postfix(ScrollRect __instance)
    {
        Plugin.Registry.ResetCourseSelectionScrollIfPending(__instance);
    }
}

[HarmonyPatch(typeof(ScrollRect), "OnScroll")]
internal static class CourseSelectionScrollWheelPatch
{
    private static bool Prefix(ScrollRect __instance, PointerEventData __0)
    {
        if (Plugin.Registry.IsCourseSelectionScrollBlocked(__instance)) return false;
        Plugin.Registry.ReleaseCourseSelectionScrollPin(__instance);
        return true;
    }
}

[HarmonyPatch(typeof(ScrollRect), "OnBeginDrag")]
internal static class CourseSelectionScrollDragPatch
{
    private static bool Prefix(ScrollRect __instance, PointerEventData __0)
    {
        if (Plugin.Registry.IsCourseSelectionScrollBlocked(__instance)) return false;
        Plugin.Registry.ReleaseCourseSelectionScrollPin(__instance);
        return true;
    }
}

[HarmonyPatch(typeof(ScrollRect), "OnDrag")]
internal static class CourseSelectionScrollWhileLoadingPatch
{
    private static bool Prefix(ScrollRect __instance) => !Plugin.Registry.IsCourseSelectionScrollBlocked(__instance);
}
