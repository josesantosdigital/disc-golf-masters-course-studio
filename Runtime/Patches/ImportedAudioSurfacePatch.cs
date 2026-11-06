using Dgm;
using HarmonyLib;
using UnityEngine;

namespace DgmCustomCourses;

// The game's audio mapper retains the base course's terrain after an imported
// scene is loaded. Its alphamap lookup throws if a contact lies outside that
// terrain. Use a safe base-terrain sample as a fallback for those contacts.
[HarmonyPatch(typeof(SurfaceMapper), nameof(SurfaceMapper.GetTextureMix))]
internal static class ImportedAudioSurfacePatch
{
    private static bool _reportedFallback;

    internal static void Reset() => _reportedFallback = false;

    private static void Prefix(SurfaceMapper __instance, ref Vector3 __0)
    {
        if (!CourseScenePatcher.IsImportedMapActive || __instance == null)
            return;

        try
        {
            var terrain = __instance.terrain;
            var data = terrain == null ? null : terrain.terrainData;
            if (data == null || data.alphamapWidth < 2 || data.alphamapHeight < 2)
                return;

            var origin = __instance.terrainPos;
            var size = data.size;
            if (size.x <= 0f || size.z <= 0f || float.IsNaN(__0.x) || float.IsNaN(__0.z) ||
                float.IsInfinity(__0.x) || float.IsInfinity(__0.z))
                return;

            // Keep at least two texels inside the map, including at the upper
            // edge, since the game may sample adjacent alphamap cells.
            var marginX = Mathf.Min(size.x * 0.25f, Mathf.Max(0.01f, size.x * 2f / data.alphamapWidth));
            var marginZ = Mathf.Min(size.z * 0.25f, Mathf.Max(0.01f, size.z * 2f / data.alphamapHeight));
            var x = Mathf.Clamp(__0.x, origin.x + marginX, origin.x + size.x - marginX);
            var z = Mathf.Clamp(__0.z, origin.z + marginZ, origin.z + size.z - marginZ);
            if (x == __0.x && z == __0.z)
                return;

            __0.x = x;
            __0.z = z;
            if (!_reportedFallback)
            {
                _reportedFallback = true;
                Plugin.LogSource.LogInfo("Imported-map collision audio is using a base-terrain fallback for an out-of-range surface sample.");
            }
        }
        catch (Exception ex)
        {
            if (!_reportedFallback)
            {
                _reportedFallback = true;
                Plugin.LogSource.LogWarning($"Could not prepare the imported-map audio surface fallback: {ex.Message}");
            }
        }
    }
}
