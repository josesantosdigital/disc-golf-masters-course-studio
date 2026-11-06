using Dgm;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.SceneManagement;
using NumericsVector = System.Numerics.Vector3;

namespace DgmCustomCourses;

// Optional approximation for imported terrain trees and bushes. It uses only
// loader-owned data and never creates native foliage colliders or calls Classify.
internal static class ImportedFoliageInteraction
{
    private const float CellSize = 16f;
    private const int MaxTrees = 3000;
    private const int MaxSceneVolumes = 12000;
    private const float ImpactCooldown = 0.2f;
    private readonly record struct Volume(NumericsVector Center, NumericsVector Radii);
    private sealed class DiscState { public Vector3 Previous; public float NextImpact; public bool Valid; }
    private static readonly List<Volume> Volumes = new();
    private static readonly Dictionary<(int, int), List<int>> Cells = new();
    private static readonly Dictionary<int, DiscState> Discs = new();
    private static readonly HashSet<int> Visited = new();
    private static bool _reportedHit;
    private static bool _reportedAudioFailure;
    private static bool _warnedMaxVolumes;

    internal static void Reset()
    {
        Volumes.Clear(); Cells.Clear(); Discs.Clear(); Visited.Clear();
        _reportedHit = false; _reportedAudioFailure = false; _warnedMaxVolumes = false;
    }

    internal static void AddTerrain(Terrain terrain, Il2CppReferenceArray<TreePrototype> prototypes)
    {
        if (terrain == null || terrain.terrainData == null || prototypes == null) return;
        try
        {
            var data = terrain.terrainData;
            if (data.treeInstanceCount > MaxTrees)
            {
                Plugin.LogSource.LogWarning($"Imported foliage interaction skipped: more than {MaxTrees} terrain trees.");
                return;
            }
            var boundsByPrototype = new Dictionary<int, Bounds>();
            for (var i = 0; i < prototypes.Length; i++)
            {
                var prefab = prototypes[i] == null ? null : prototypes[i].prefab;
                if (prefab != null && ImportedTreeCollisionProxies.TryFindFoliageBounds(prefab, out var bounds) &&
                    bounds.size.x > 0.1f && bounds.size.y > 0.1f && bounds.size.z > 0.1f)
                    boundsByPrototype[i] = bounds;
            }
            var trees = data.treeInstances;
            var countBefore = Volumes.Count;
            var terrainScale = terrain.transform.lossyScale;
            for (var i = 0; trees != null && i < trees.Length; i++)
            {
                var tree = trees[i];
                if (!boundsByPrototype.TryGetValue(tree.prototypeIndex, out var bounds)) continue;
                var instanceScale = new Vector3(tree.widthScale, tree.heightScale, tree.widthScale);
                var rotation = Quaternion.Euler(0f, tree.rotation * Mathf.Rad2Deg, 0f);
                var localCenter = Vector3.Scale(tree.position, data.size) + rotation * Vector3.Scale(bounds.center, instanceScale);
                var worldCenter = terrain.transform.TransformPoint(localCenter);
                var extents = Vector3.Scale(bounds.extents, instanceScale);
                var horizontal = Mathf.Clamp(Mathf.Max(Mathf.Abs(extents.x * terrainScale.x), Mathf.Abs(extents.z * terrainScale.z)) * 0.7f, 0.25f, 8f);
                var vertical = Mathf.Clamp(Mathf.Abs(extents.y * terrainScale.y) * 0.7f, 0.25f, 20f);
                AddVolume(worldCenter, horizontal, vertical);
            }
            Plugin.LogSource.LogInfo($"Prepared {Volumes.Count - countBefore} experimental imported foliage interaction volumes for '{terrain.name}'.");
        }
        catch (Exception ex) { Plugin.LogSource.LogWarning($"Could not prepare imported foliage interaction: {ex.Message}"); }
    }

    internal static void AddSceneRenderers(Scene scene, string courseName)
    {
        if (!scene.IsValid() || !scene.isLoaded) return;
        try
        {
            var roots = scene.GetRootGameObjects();
            var examined = 0;
            var added = 0;
            var examples = new List<string>(8);
            var maxHorizontalSize = 0f;
            for (var rootIndex = 0; roots != null && rootIndex < roots.Length; rootIndex++)
            {
                var root = roots[rootIndex];
                if (root == null) continue;
                var renderers = root.GetComponentsInChildren<Renderer>(true);
                for (var i = 0; renderers != null && i < renderers.Length; i++)
                {
                    var renderer = renderers[i];
                    if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy || !IsFoliageRenderer(renderer)) continue;
                    examined++;
                    var bounds = renderer.bounds;
                    var size = bounds.size;
                    if (!IsFinite(bounds.center) || !IsFinite(size) || size.sqrMagnitude < 0.01f || size.y > 80f || Mathf.Max(size.x, size.z) > 80f)
                        continue;

                    maxHorizontalSize = Mathf.Max(maxHorizontalSize, size.x, size.z);
                    var horizontal = Mathf.Clamp(Mathf.Max(size.x, size.z) * 0.35f, 0.35f, 5f);
                    var vertical = Mathf.Clamp(size.y * 0.35f, 0.65f, 12f);
                    if (!AddVolume(bounds.center, horizontal, vertical)) continue;
                    added++;
                    if (examples.Count < 8) examples.Add($"{renderer.name} [{string.Join(",", renderer.sharedMaterials.Where(material => material != null).Select(material => material.name).Distinct().Take(2))}]");
                }
            }

            var sample = examples.Count == 0 ? "none" : string.Join("; ", examples);
            Plugin.LogSource.LogInfo($"Prepared {added}/{examined} experimental scene-foliage interaction volumes for '{courseName}' (largest candidate renderer {maxHorizontalSize:F1}m; examples: {sample}).");
        }
        catch (Exception ex) { Plugin.LogSource.LogWarning($"Could not prepare scene-foliage interaction volumes for '{courseName}': {ex.Message}"); }
    }

    private static bool IsFoliageRenderer(Renderer renderer)
    {
        if (HasFoliageWord(renderer.name) || HasFoliageWord(renderer.gameObject.name)) return true;
        var materials = renderer.sharedMaterials;
        for (var i = 0; materials != null && i < materials.Length; i++)
            if (materials[i] != null && HasFoliageWord(materials[i].name))
                return true;
        return false;
    }

    private static bool HasFoliageWord(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return value.IndexOf("leaf", StringComparison.OrdinalIgnoreCase) >= 0 ||
               value.IndexOf("foliage", StringComparison.OrdinalIgnoreCase) >= 0 ||
               value.IndexOf("plant", StringComparison.OrdinalIgnoreCase) >= 0 ||
               value.IndexOf("bush", StringComparison.OrdinalIgnoreCase) >= 0 ||
               value.IndexOf("shrub", StringComparison.OrdinalIgnoreCase) >= 0 ||
               value.IndexOf("grass", StringComparison.OrdinalIgnoreCase) >= 0 ||
               value.IndexOf("branch", StringComparison.OrdinalIgnoreCase) >= 0 ||
               value.IndexOf("spruce", StringComparison.OrdinalIgnoreCase) >= 0 ||
               value.IndexOf("cvet", StringComparison.OrdinalIgnoreCase) >= 0 ||
               value.IndexOf("flower", StringComparison.OrdinalIgnoreCase) >= 0 ||
               value.IndexOf("conifer", StringComparison.OrdinalIgnoreCase) >= 0 ||
               value.IndexOf("vegetation", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool AddVolume(Vector3 center, float horizontal, float vertical)
    {
        if (Volumes.Count >= MaxSceneVolumes)
        {
            if (!_warnedMaxVolumes)
            {
                _warnedMaxVolumes = true;
                Plugin.LogSource.LogWarning($"Imported foliage interaction volume limit ({MaxSceneVolumes}) reached; remaining foliage was skipped.");
            }
            return false;
        }

        var volume = new Volume(ToNumerics(center), new NumericsVector(horizontal, vertical, horizontal));
        var index = Volumes.Count;
        Volumes.Add(volume);
        for (var x = Cell(center.x - horizontal); x <= Cell(center.x + horizontal); x++)
        for (var z = Cell(center.z - horizontal); z <= Cell(center.z + horizontal); z++)
        {
            if (!Cells.TryGetValue((x, z), out var entries)) Cells[(x, z)] = entries = new List<int>();
            entries.Add(index);
        }
        return true;
    }

    private static bool IsFinite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

    internal static void OnDiscPhysicsStep(DiscCollision collision)
    {
        if (Volumes.Count == 0 || !CourseScenePatcher.IsImportedMapActive || collision == null) return;
        try
        {
            var disc = collision.disc;
            var body = collision.rb;
            if (disc == null || body == null) return;
            var id = collision.GetInstanceID();
            if (!Discs.TryGetValue(id, out var state)) Discs[id] = state = new DiscState();
            var current = body.position;
            if (!float.IsFinite(current.x) || !float.IsFinite(current.y) || !float.IsFinite(current.z))
            {
                state.Valid = false;
                return;
            }
            if (!disc.IsThrown || disc.ThrowComplete)
            {
                state.Valid = false;
                return;
            }
            var previous = state.Previous;
            var hadPrevious = state.Valid;
            state.Previous = current;
            state.Valid = true;
            if (!hadPrevious || Time.time < state.NextImpact) return;
            var velocity = body.linearVelocity;
            var speed = velocity.magnitude;
            if (!float.IsFinite(speed) || speed < 1f || (current - previous).sqrMagnitude > 10000f) return;
            if (!TryFindEntry(previous, current, out var point)) return;

            body.linearVelocity = velocity * 0.82f;
            state.NextImpact = Time.time + ImpactCooldown;
            if (!_reportedHit)
            {
                _reportedHit = true;
                Plugin.LogSource.LogInfo("Experimental imported foliage interaction slowed the disc.");
            }
            try
            {
                var audio = collision.audioDiscComponent;
                if (audio != null) audio.OnFoliageCollision(speed, point);
            }
            catch (Exception ex)
            {
                if (!_reportedAudioFailure) { _reportedAudioFailure = true; Plugin.LogSource.LogWarning($"Imported foliage sound was unavailable: {ex.Message}"); }
            }
        }
        catch (Exception ex)
        {
            if (!_reportedHit) { _reportedHit = true; Plugin.LogSource.LogWarning($"Imported foliage interaction failed: {ex.Message}"); }
        }
    }

    private static bool TryFindEntry(Vector3 from, Vector3 to, out Vector3 point)
    {
        point = default;
        var minX = Cell(Mathf.Min(from.x, to.x)); var maxX = Cell(Mathf.Max(from.x, to.x));
        var minZ = Cell(Mathf.Min(from.z, to.z)); var maxZ = Cell(Mathf.Max(from.z, to.z));
        if ((long)(maxX - minX + 1) * (maxZ - minZ + 1) > 256) return false;
        var first = float.PositiveInfinity;
        Visited.Clear();
        for (var x = minX; x <= maxX; x++)
        for (var z = minZ; z <= maxZ; z++)
        {
            if (!Cells.TryGetValue((x, z), out var entries)) continue;
            foreach (var index in entries)
            {
                if (!Visited.Add(index)) continue;
                var volume = Volumes[index];
                if (ImportedFoliageGeometry.TryEnterEllipsoid(ToNumerics(from), ToNumerics(to), volume.Center, volume.Radii, out var t) && t < first)
                    first = t;
            }
        }
        if (float.IsPositiveInfinity(first)) return false;
        point = Vector3.Lerp(from, to, first);
        return true;
    }

    private static int Cell(float coordinate) => Mathf.FloorToInt(coordinate / CellSize);
    private static NumericsVector ToNumerics(Vector3 value) => new(value.x, value.y, value.z);
}
