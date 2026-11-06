using Dgm;
using HarmonyLib;

namespace DgmCustomCourses;

[HarmonyPatch(typeof(SurfaceMapper), nameof(SurfaceMapper.OnEnable))]
internal static class ImportedHiddenTerrainReactivationPatch
{
    private static void Postfix(SurfaceMapper __instance)
    {
        if (CourseScenePatcher.IsImportedMapActive)
            ImportedTerrainIsolation.SuspendMapper(__instance);
    }
}

[HarmonyPatch(typeof(DiscBehaviour), nameof(DiscBehaviour.Throw))]
internal static class ImportedThrowTerrainSourcePatch
{
    private static void Postfix(DiscBehaviour __instance) => ImportedMapPhysicsSupport.UpdateImportedGroundState(__instance);
}

[HarmonyPatch(typeof(DiscBehaviour), nameof(DiscBehaviour.GroundAction))]
internal static class ImportedGroundActionPatch
{
    // FixedUpdate inlines GroundAction in the native binary, so its prefix also
    // writes the fields and clears the hidden Terrain reference. Getter patches
    // cannot intercept those native field accesses.
    private static bool Prefix(DiscBehaviour __instance) => !ImportedMapPhysicsSupport.UpdateImportedGroundState(__instance);
}

[HarmonyPatch(typeof(DiscGroundContact), nameof(DiscGroundContact.FixedUpdate))]
internal static class ImportedGroundModelSourcePatch
{
    private static void Prefix(DiscGroundContact __instance) => ImportedTerrainIsolation.RemoveHiddenGroundModel(__instance);
}
