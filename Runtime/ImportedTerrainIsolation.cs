using Dgm;
using UnityEngine;

namespace DgmCustomCourses;

/// <summary>
/// Disabling a Terrain/Collider does not unregister DGM's separate heightfield
/// solver. Keep hidden base terrain out of that solver and the disc's cached
/// Terrain.SampleHeight path, without changing disc positions or throw results.
/// </summary>
internal static class ImportedTerrainIsolation
{
    private static readonly HashSet<int> HiddenTerrains = new();
    private static readonly Dictionary<int, (SurfaceMapper Mapper, bool Enabled)> Mappers = new();
    private static readonly Dictionary<int, (DiscBehaviour Disc, Terrain Terrain)> Discs = new();

    public static void RememberHiddenTerrain(Terrain terrain)
    {
        if (terrain != null)
            HiddenTerrains.Add(terrain.GetInstanceID());
    }

    public static bool IsHidden(Terrain terrain) => terrain != null && HiddenTerrains.Contains(terrain.GetInstanceID());

    public static void SuspendMapper(SurfaceMapper mapper)
    {
        if (mapper == null || !IsHidden(mapper.terrain))
            return;
        var id = mapper.GetInstanceID();
        if (!Mappers.ContainsKey(id))
        {
            Mappers[id] = (mapper, mapper.enabled);
            Plugin.LogSource.LogInfo($"Isolating hidden base terrain physics: mapper='{mapper.name}', terrain='{mapper.terrain.name}', wasEnabled={mapper.enabled}. Native OnDisable will unregister this heightfield.");
        }
        // Use Unity's normal lifecycle; never edit SurfaceMapper.s_Active directly.
        mapper.enabled = false;
    }

    public static void DetachHiddenTerrain(DiscBehaviour disc)
    {
        if (!CourseScenePatcher.IsImportedMapActive || disc == null)
            return;

        var terrain = disc.terrain;
        if (IsHidden(terrain))
        {
            var id = disc.GetInstanceID();
            if (!Discs.ContainsKey(id))
            {
                Discs[id] = (disc, terrain);
                var position = disc.transform.position;
                Plugin.LogSource.LogInfo($"Detached hidden Terrain.SampleHeight source: disc={id}, terrain='{terrain.name}', sampledY={terrain.SampleHeight(position):F2}, discY={position.y:F2}.");
            }
            // Throw() can reacquire even a disabled Terrain. Do this after Throw
            // and before every FixedUpdate, including its inlined GroundAction.
            disc.terrain = null;
        }

        RemoveHiddenGroundModel(disc.groundContact);
    }

    public static void RemoveHiddenGroundModel(DiscGroundContact contact)
    {
        if (!CourseScenePatcher.IsImportedMapActive || contact == null || contact.mapper == null ||
            !IsHidden(contact.mapper.terrain))
            return;

        SuspendMapper(contact.mapper);
        // A cached solver also runs after physics via PushOut. Drop it before
        // either callback can use the now-unregistered heightfield again.
        contact.mapper = null;
        contact.solver = null;
        contact.onGround = false;
        contact.stepsOffGround = int.MaxValue;
        Plugin.LogSource.LogInfo("Cleared a cached disc ground model owned by hidden base terrain.");
    }

    public static void Restore()
    {
        foreach (var saved in Discs.Values)
        {
            // Preserve a different valid terrain selected since detachment.
            if (saved.Disc != null && saved.Disc.terrain == null && saved.Terrain != null)
                saved.Disc.terrain = saved.Terrain;
        }
        Discs.Clear();
        foreach (var saved in Mappers.Values)
            if (saved.Mapper != null)
                saved.Mapper.enabled = saved.Enabled;
        Mappers.Clear();
        HiddenTerrains.Clear();
    }
}
