using Dgm;
using HarmonyLib;
using UnityEngine;

namespace DgmCustomCourses;

// DGM's lie movement searches from high above the map. On multi-storey
// imports that search can choose a roof above an otherwise valid disc lie.
[HarmonyPatch(typeof(PlayerLiePositionFixer), nameof(PlayerLiePositionFixer.GetSafeLiePosition))]
internal static class ImportedLiePlacementPatch
{
    private static void Postfix(Transform __0, ref Vector3 __result)
    {
        if (!CourseScenePatcher.IsImportedMapActive || __0 == null)
            return;

        try
        {
            var player = __0.GetComponent<PlayerBehaviour>();
            var disc = player == null ? null : player.GetCurrentDisc();
            if (disc == null || !disc.ThrowComplete || disc.IsInAnyFormOfOutOfBounds() ||
                disc.IsInWater() || disc.IsDiscInsideBasket())
                return;

            var lie = disc.GetPositionForNextLie();
            var position = disc.transform.position;
            var dx = __result.x - lie.x;
            var dz = __result.z - lie.z;
            // Leave relocated/penalty lies and distant collision avoidance to DGM.
            if (!ImportedMapPhysicsSupport.TryGetImportedGroundDistance(disc, disc.GroundDistance, out var distance, out _) ||
                !ImportedLieHeightPolicy.TryChooseGround(dx, dz, lie.y, position.y, distance, __result.y, out var groundY))
                return;

            var oldY = __result.y;
            __result.y = groundY;
            Plugin.LogSource.LogInfo($"Corrected imported-map lie height from {oldY:F2}m to {groundY:F2}m near the settled disc.");
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Could not correct imported-map lie height: {ex.Message}");
        }
    }
}
