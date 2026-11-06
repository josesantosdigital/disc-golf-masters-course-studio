using Dgm;
using HarmonyLib;

namespace DgmCustomCourses;

[HarmonyPatch(typeof(DiscCollision), nameof(DiscCollision.FixedUpdate))]
internal static class ImportedFoliageInteractionPatch
{
    private static void Postfix(DiscCollision __instance) => ImportedFoliageInteraction.OnDiscPhysicsStep(__instance);
}
