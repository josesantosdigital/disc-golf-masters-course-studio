using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Dgm;
using UnityEngine;

namespace DgmCustomCourses;

// Opt-in static proxies: imported terrain trees may not regenerate prototype
// colliders after runtime changes. Build sparse scene colliders once per round.
internal static class ImportedTreeCollisionProxies
{
    private const int MaxTerrainTreeInstances = 3000;
    private static readonly List<GameObject> Roots = new();

    public static void AddCanopyShapes(Terrain terrain, Il2CppReferenceArray<TreePrototype> prototypes)
    {
        if (terrain == null || terrain.terrainData == null || prototypes == null) return;
        GameObject root = null;
        try
        {
            var data = terrain.terrainData;
            var count = data.treeInstanceCount;
            if (count > MaxTerrainTreeInstances)
            {
                Plugin.LogSource.LogWarning($"Canopy collision skipped for '{terrain.name}': {count} trees exceed the {MaxTerrainTreeInstances} safety limit.");
                return;
            }

            var shapes = new Dictionary<int, Bounds>();
            for (var i = 0; i < prototypes.Length; i++)
            {
                var prefab = prototypes[i] == null ? null : prototypes[i].prefab;
                if (prefab == null || !TryFindFoliageBounds(prefab, out var bounds)) continue;
                var size = bounds.size;
                if (size.y < 4f || size.y > 100f || size.x < 0.5f || size.z < 0.5f) continue;
                shapes[i] = bounds;
                Plugin.LogSource.LogInfo($"Canopy collision prototype '{prefab.name}': bounds={size}.");
            }
            if (shapes.Count == 0)
            {
                Plugin.LogSource.LogWarning($"Canopy collision skipped for '{terrain.name}': no compatible foliage tree prototypes.");
                return;
            }

            var instances = data.treeInstances;
            var treeLayerName = DiscCollision.k_TreeLayerName;
            var treeLayer = LayerMask.NameToLayer(treeLayerName);
            if (treeLayer < 0)
            {
                Plugin.LogSource.LogWarning($"Could not resolve game tree layer '{treeLayerName}'; canopy collision skipped.");
                return;
            }
            root = new GameObject("DGM_ImportedTreeCanopies");
            Roots.Add(root);
            root.layer = treeLayer;
            root.transform.SetParent(terrain.transform, false);
            var created = 0;
            SphereCollider firstProbe = null;
            for (var i = 0; instances != null && i < instances.Length; i++)
            {
                var instance = instances[i];
                if (!shapes.TryGetValue(instance.prototypeIndex, out var bounds)) continue;
                var size = bounds.size;
                var radius = Mathf.Clamp(Mathf.Min(size.x, size.z) * 0.16f, 0.25f, 1.5f);
                var tree = new GameObject($"DGM_Canopy_{i}");
                tree.layer = treeLayer;
                tree.transform.SetParent(root.transform, false);
                tree.transform.localPosition = Vector3.Scale(instance.position, data.size);
                tree.transform.localRotation = Quaternion.Euler(0f, instance.rotation * Mathf.Rad2Deg, 0f);
                tree.transform.localScale = new Vector3(instance.widthScale, instance.heightScale, instance.widthScale);
                tree.AddComponent<FoliageCollider>();

                var first = tree.AddComponent<SphereCollider>();
                first.isTrigger = true;
                first.radius = radius;
                first.center = new Vector3(bounds.center.x + size.x * 0.13f, bounds.min.y + size.y * 0.22f, bounds.center.z);
                if (firstProbe == null) firstProbe = first;
                var second = tree.AddComponent<SphereCollider>();
                second.isTrigger = true;
                second.radius = radius * 0.8f;
                second.center = new Vector3(bounds.center.x - size.x * 0.13f, bounds.min.y + size.y * 0.48f, bounds.center.z + size.z * 0.1f);
                created++;
            }
            Physics.SyncTransforms();
            Plugin.LogSource.LogInfo($"Created sparse foliage trigger proxies for {created} of {count} trees on '{terrain.name}', layer='{treeLayerName}' ({treeLayer}), queriesHitTriggers={Physics.queriesHitTriggers}.");
            if (firstProbe != null)
            {
                try
                {
                    var probeBounds = firstProbe.bounds;
                    var origin = probeBounds.center + Vector3.up * (probeBounds.extents.y + 0.5f);
                    var hits = Physics.RaycastAll(origin, Vector3.down, probeBounds.extents.y * 2f + 1f);
                    var found = hits != null && hits.Any(hit => hit.collider != null && hit.collider.GetInstanceID() == firstProbe.GetInstanceID());
                    Plugin.LogSource.LogInfo($"Canopy collision physics probe: first proxy raycast hit={found}, world bounds={probeBounds.size}.");
                }
                catch (Exception ex) { Plugin.LogSource.LogWarning($"Could not probe canopy collisions: {ex.Message}"); }
            }
        }
        catch (Exception ex)
        {
            if (root != null) { root.SetActive(false); UnityEngine.Object.Destroy(root); Roots.Remove(root); }
            Plugin.LogSource.LogWarning($"Could not build terrain tree collisions: {ex.Message}");
        }
    }

    public static void Restore()
    {
        foreach (var root in Roots)
        {
            if (root == null) continue;
            try { root.SetActive(false); UnityEngine.Object.Destroy(root); }
            catch (Exception ex) { Plugin.LogSource.LogWarning($"Could not remove temporary canopy collisions: {ex.Message}"); }
        }
        Roots.Clear();
    }

    internal static bool TryFindFoliageBounds(GameObject prefab, out Bounds bounds)
    {
        bounds = default;
        var found = false;
        var filters = prefab.GetComponentsInChildren<MeshFilter>(true);
        for (var i = 0; filters != null && i < filters.Length; i++)
        {
            var filter = filters[i];
            if (filter == null || filter.sharedMesh == null) continue;
            var renderer = filter.GetComponent<Renderer>();
            if (renderer == null || !renderer.enabled) continue;
            var materials = renderer.sharedMaterials;
            if (materials == null || !materials.Any(m => m != null && m.name.IndexOf("leaf", StringComparison.OrdinalIgnoreCase) >= 0)) continue;
            var meshBounds = filter.sharedMesh.bounds;
            for (var x = -1; x <= 1; x += 2)
                for (var y = -1; y <= 1; y += 2)
                    for (var z = -1; z <= 1; z += 2)
                    {
                        var corner = meshBounds.center + Vector3.Scale(meshBounds.extents, new Vector3(x, y, z));
                        var local = prefab.transform.InverseTransformPoint(filter.transform.TransformPoint(corner));
                        if (!found) { bounds = new Bounds(local, Vector3.zero); found = true; }
                        else bounds.Encapsulate(local);
                    }
        }
        return found;
    }
}
