using Dgm;
using HarmonyLib;

namespace DgmCustomCourses;

[HarmonyPatch(typeof(SoloConfiguration), nameof(SoloConfiguration.GetCourses))]
internal static class SoloCourseListPatch
{
    private static void Postfix()
    {
        Plugin.Registry.OnSoloCourseListRequested();
    }
}
