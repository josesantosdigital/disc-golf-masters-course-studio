using Dgm;
using HarmonyLib;

namespace DgmCustomCourses;

[HarmonyPatch(typeof(CourseLayout), nameof(CourseLayout.GetAllLayouts))]
internal static class GetAllLayoutsPatch
{
    private static void Postfix(ref Il2CppSystem.Collections.Generic.List<CourseLayout> __result)
    {
        Plugin.Registry.AppendLayouts(__result);
    }
}

[HarmonyPatch(typeof(CourseLayout), nameof(CourseLayout.GetAllCourses))]
internal static class GetAllCoursesPatch
{
    private static void Postfix(ref Il2CppSystem.Collections.Generic.List<CourseLayout.CourseID> __result)
    {
        Plugin.Registry.AppendCourseIds(__result);
    }
}

[HarmonyPatch(typeof(CourseLayout), nameof(CourseLayout.GetLayoutsForCourse), new[] { typeof(CourseLayout.CourseID) })]
internal static class GetLayoutsForCoursePatch
{
    private static void Postfix(CourseLayout.CourseID courseID, ref Il2CppSystem.Collections.Generic.List<CourseLayout> __result)
    {
        var custom = Plugin.Registry.FindByCourseId(courseID);
        if (custom == null)
            return;

        _ = CourseLayout.GetAllLayouts();
        __result = Plugin.Registry.CreateLayoutList(custom);
    }
}

[HarmonyPatch(typeof(CourseLayout), nameof(CourseLayout.GetLayout), new[] { typeof(string) })]
internal static class GetLayoutPatch
{
    private static bool Prefix(string layoutID, ref CourseLayout __result)
    {
        var custom = Plugin.Registry.FindByLayoutId(layoutID);
        if (custom?.Layout == null)
            return true;

        __result = custom.Layout;
        return false;
    }
}

[HarmonyPatch(typeof(CourseLayout), nameof(CourseLayout.GetCourseDisplayName), new[] { typeof(CourseLayout.CourseID) })]
internal static class GetCourseDisplayNamePatch
{
    private static bool Prefix(CourseLayout.CourseID courseID, ref string __result)
    {
        var custom = Plugin.Registry.FindByCourseId(courseID);
        if (custom == null)
            return true;

        __result = custom.Definition.Name;
        return false;
    }
}

[HarmonyPatch(typeof(CourseLayout), nameof(CourseLayout.IsEditorOnlyCourse), new[] { typeof(CourseLayout.CourseID) })]
internal static class IsEditorOnlyCoursePatch
{
    private static bool Prefix(CourseLayout.CourseID courseID, ref bool __result)
    {
        if (Plugin.Registry.FindByCourseId(courseID) == null)
            return true;

        __result = false;
        return false;
    }
}
