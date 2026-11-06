using Dgm;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DgmCustomCourses;

/// <summary>
/// Connects imported map terrain and water meshes to the collision layers and
/// hazard components used by Disc Golf Masters.
/// </summary>
internal static class ImportedMapPhysicsSupport
{
    private sealed class WaterSurface
    {
        private readonly List<(Vector3 A, Vector3 B, Vector3 C)> _footprintTriangles = new();

        public string Name { get; }
        public Bounds Bounds { get; }
        public float SurfaceY { get; }
        public WaterHazard Hazard { get; set; }
        public OBArea ObArea { get; set; }
        public int FootprintTriangleCount => _footprintTriangles.Count;

        public WaterSurface(Bounds bounds, string name)
        {
            Name = name;
            Bounds = bounds;
            SurfaceY = bounds.max.y;
        }

        public void AddFootprintMesh(Mesh mesh, Transform meshTransform)
        {
            if (mesh == null || meshTransform == null || mesh.vertexCount < 3 || mesh.vertexCount > 250000)
                return;

            try
            {
                var vertices = mesh.vertices;
                var indices = mesh.triangles;
                for (var i = 0; i + 2 < indices.Length; i += 3)
                {
                    var ia = indices[i];
                    var ib = indices[i + 1];
                    var ic = indices[i + 2];
                    if (ia < 0 || ib < 0 || ic < 0 || ia >= vertices.Length || ib >= vertices.Length || ic >= vertices.Length)
                        continue;

                    var a = meshTransform.TransformPoint(vertices[ia]);
                    var b = meshTransform.TransformPoint(vertices[ib]);
                    var c = meshTransform.TransformPoint(vertices[ic]);
                    var minY = Mathf.Min(a.y, b.y, c.y);
                    var maxY = Mathf.Max(a.y, b.y, c.y);
                    var cross = Cross(new Vector2(b.x - a.x, b.z - a.z), new Vector2(c.x - a.x, c.z - a.z));
                    if (maxY - minY > 0.75f || Mathf.Abs(cross) < 0.01f)
                        continue;

                    _footprintTriangles.Add((a, b, c));
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogDebug($"Could not read the imported water footprint mesh '{mesh.name}': {ex.Message}");
            }
        }

        public bool ContainsXZ(Vector3 point) => TryGetSurfaceY(point, out _);

        public bool TryGetSurfaceY(Vector3 point, out float surfaceY)
        {
            surfaceY = SurfaceY;
            if (!ImportedMapPhysicsSupport.ContainsXZ(Bounds, point))
                return false;
            if (_footprintTriangles.Count == 0)
                return true;

            var p = new Vector2(point.x, point.z);
            for (var i = 0; i < _footprintTriangles.Count; i++)
            {
                var triangle = _footprintTriangles[i];
                var a = new Vector2(triangle.A.x, triangle.A.z);
                var b = new Vector2(triangle.B.x, triangle.B.z);
                var c = new Vector2(triangle.C.x, triangle.C.z);
                var ab = b - a;
                var ac = c - a;
                var ap = p - a;
                var denominator = Cross(ab, ac);
                if (Mathf.Abs(denominator) < 0.01f)
                    continue;
                var weightB = Cross(ap, ac) / denominator;
                var weightC = Cross(ab, ap) / denominator;
                var weightA = 1f - weightB - weightC;
                if (weightA < -0.01f || weightB < -0.01f || weightC < -0.01f)
                    continue;

                surfaceY = triangle.A.y * weightA + triangle.B.y * weightB + triangle.C.y * weightC;
                return true;
            }
            return false;
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    }

    private sealed class ColliderSnapshot
    {
        public Collider Collider { get; }
        public bool Enabled { get; }
        public bool IsTrigger { get; }
        public int Layer { get; }
        public bool HasMeshCollider { get; }
        public Mesh SharedMesh { get; }
        public bool Convex { get; }

        public ColliderSnapshot(Collider collider)
        {
            Collider = collider;
            Enabled = collider.enabled;
            IsTrigger = collider.isTrigger;
            Layer = collider.gameObject.layer;
            var meshCollider = GetSameColliderComponent<MeshCollider>(collider);
            if (meshCollider != null)
            {
                HasMeshCollider = true;
                SharedMesh = meshCollider.sharedMesh;
                Convex = meshCollider.convex;
            }
        }
    }

    private static T GetSameColliderComponent<T>(Collider collider) where T : Collider
    {
        if (collider == null)
            return null;

        var component = collider.GetComponent<T>();
        return component != null && component.GetInstanceID() == collider.GetInstanceID()
            ? component
            : null;
    }

    private sealed class ImportedThrowTrace
    {
        public float StartedAt { get; set; }
        public float LastProgressAt { get; set; }
        public float LastPositionSampleAt { get; set; }
        public Vector3 LastObservedPosition { get; set; }
        public Vector3 LastObservedVelocity { get; set; }
        public bool HasPositionSample { get; set; }
        public int PositionJumpLogCount { get; set; }
        public float LastGroundProfileAt { get; set; }
        public Vector3 LastGroundProfilePosition { get; set; }
        public bool HasGroundProfile { get; set; }
        public float LastCollisionLogAt { get; set; }
        public Vector3 LastCollisionLogPosition { get; set; }
        public Vector3 LastCollisionLogVelocity { get; set; }
        public int CollisionLogCount { get; set; }
        public int ActiveSurfaceSnapshotCount { get; set; }
        public bool CompletionLogged { get; set; }
        public float CompletedAt { get; set; }
    }

    private sealed class GroundDistanceSample
    {
        public float FixedTime { get; set; }
        public Vector3 Position { get; set; }
        public float Distance { get; set; }
        public string Support { get; set; }
    }

    private sealed class PhysicsStepSample
    {
        public float CapturedAt { get; set; }
        public Vector3 Position { get; set; }
        public Vector3 Velocity { get; set; }
    }

    private static readonly Dictionary<int, ColliderSnapshot> ColliderSnapshots = new();
    private static readonly List<GameObject> TemporaryObjects = new();
    private static readonly List<Mesh> TemporaryMeshes = new();
    private static readonly List<WaterSurface> WaterSurfaces = new();
    private static readonly HashSet<int> SeenWaterMeshes = new();
    private static readonly HashSet<int> WaterCutColliders = new();
    private static readonly HashSet<int> HandledDiscs = new();
    private static readonly HashSet<int> ThrownDiscs = new();
    private static readonly Dictionary<int, Vector3> ThrowOrigins = new();
    private static readonly Dictionary<int, ImportedThrowTrace> ImportedThrowTraces = new();
    private static readonly Dictionary<int, GroundDistanceSample> GroundDistanceSamples = new();
    private static readonly Dictionary<int, PhysicsStepSample> DiscMotionStarts = new();
    private static readonly HashSet<int> GroundDistanceOverridesLogged = new();
    private static readonly Dictionary<int, HashSet<int>> CollisionCollidersLoggedByThrow = new();
    private static readonly HashSet<int> ReportedWaterAreas = new();
    private static readonly HashSet<int> UnreadableMeshesLogged = new();
    private static readonly HashSet<int> StaticBatchColliderSkipsLogged = new();
    private static Scene _preparedScene;
    private static int _terrainLayer = -1;
    private static int _waterLayer = -1;
    private static float _waterLandingClearanceMeters = DefaultWaterLandingClearanceMeters;
    private static int _groundColliderCount;
    private static int _waterHazardMarkerCount;
    private static int _waterObAreaCount;

    public static void Prepare(Scene scene, string courseName, float waterLandingClearanceMeters = DefaultWaterLandingClearanceMeters)
    {
        Restore();
        if (!scene.isLoaded)
            return;

        try
        {
            _preparedScene = scene;
            _waterLandingClearanceMeters = Mathf.Clamp(waterLandingClearanceMeters, 0f, 25f);
            _terrainLayer = LayerMask.NameToLayer(DiscCollision.k_TerrainLayerName);
            _waterLayer = ResolveWaterLayer(scene);

            var roots = scene.GetRootGameObjects();
            var rejectedWaterCandidateCount = 0;
            var rejectedWaterCandidateLogs = 0;
            for (var rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                var root = roots[rootIndex];
                if (root == null)
                    continue;

                var renderers = root.GetComponentsInChildren<Renderer>(true);
                for (var rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                {
                    var renderer = renderers[rendererIndex];
                    if (renderer == null || !renderer.enabled || !IsWaterSurface(renderer))
                        continue;

                    var bounds = renderer.bounds;
                    if (bounds.size.sqrMagnitude > 100000000f)
                        continue;

                    var hasExplicitWaterName = HasWaterNameToken(renderer.gameObject.name);
                    var isNarrowNamedWater = hasExplicitWaterName && Mathf.Max(bounds.size.x, bounds.size.z) >= 4f;
                    if ((bounds.size.x < 4f || bounds.size.z < 4f) && !isNarrowNamedWater)
                    {
                        rejectedWaterCandidateCount++;
                        if (rejectedWaterCandidateLogs < 8)
                        {
                            Plugin.LogSource.LogInfo($"Skipped small imported water candidate '{renderer.name}' at '{DescribeTransformPath(renderer.transform)}': bounds size=({bounds.size.x:F1},{bounds.size.y:F1},{bounds.size.z:F1})m; it is not a long, explicitly named water surface.");
                            rejectedWaterCandidateLogs++;
                        }
                        continue;
                    }

                    var surface = new WaterSurface(bounds, renderer.name);
                    WaterSurfaces.Add(surface);
                    surface.ObArea = AddWaterObArea(scene, bounds, courseName, renderer.name);
                    var isStaticBatch = renderer.isPartOfStaticBatch;
                    if (isStaticBatch)
                        Plugin.LogSource.LogInfo($"Imported water surface '{renderer.name}' is static-batched; using its renderer bounds instead of the shared MeshFilter mesh for the water footprint.");
                    var filters = isStaticBatch ? null : renderer.GetComponents<MeshFilter>();
                    var hasTrigger = false;
                    var foundMesh = false;
                    if (filters != null)
                    {
                        foreach (var filter in filters)
                        {
                            if (filter == null || filter.sharedMesh == null)
                                continue;
                            foundMesh = true;
                            if (!SeenWaterMeshes.Add(filter.GetInstanceID()))
                                continue;
                            surface.AddFootprintMesh(filter.sharedMesh, filter.transform);
                            Plugin.LogSource.LogInfo($"Imported water footprint '{renderer.name}': bounds X {bounds.min.x:F1}..{bounds.max.x:F1}, Z {bounds.min.z:F1}..{bounds.max.z:F1}, surface Y={bounds.max.y:F1}, mesh triangles={surface.FootprintTriangleCount}.");
                            ConvertWaterColliders(renderer);
                            surface.Hazard = AddMeshWaterHazardMarker(scene, filter, renderer, courseName);
                            hasTrigger = true;
                        }
                    }

                    if (!hasTrigger && renderer is SkinnedMeshRenderer skinned && skinned.sharedMesh != null && SeenWaterMeshes.Add(renderer.GetInstanceID()))
                    {
                        surface.AddFootprintMesh(skinned.sharedMesh, skinned.transform);
                        ConvertWaterColliders(renderer);
                        surface.Hazard = AddWaterHazardMarker(scene, renderer.transform.position, renderer.transform.rotation, courseName, renderer.name);
                    }
                    else if (!hasTrigger && !foundMesh)
                    {
                        ConvertWaterColliders(renderer);
                        surface.Hazard = AddWaterHazardMarker(scene, bounds.center, Quaternion.identity, courseName, renderer.name);
                    }
                }
            }

            if (rejectedWaterCandidateCount > rejectedWaterCandidateLogs)
                Plugin.LogSource.LogInfo($"Skipped {rejectedWaterCandidateCount - rejectedWaterCandidateLogs} additional small imported water candidates for '{courseName}' after logging the first {rejectedWaterCandidateLogs}.");

            Plugin.LogSource.LogInfo($"Imported-map physics support for '{courseName}': {WaterSurfaces.Count} water surfaces, {_waterHazardMarkerCount} sampled water hazards, {_waterObAreaCount} water OB checks; waterline ground cutoff={WaterGroundClearance:F2}m, grounded-disc water landing allowance={_waterLandingClearanceMeters:F1}m above the surface, disc water layer={_waterLayer}, terrain layer={_terrainLayer}.");
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Imported-map physics setup for '{courseName}' was incomplete: {ex.Message}");
        }
    }

    public static bool UseDiscTerrainLayer(Collider collider)
    {
        if (collider == null || !collider.enabled || collider.isTrigger || _terrainLayer < 0)
            return false;

        var gameObject = collider.gameObject;
        var id = collider.GetInstanceID();
        var collisionMeshChanged = WaterCutColliders.Add(id) && RemoveShallowGroundTriangles(collider);
        if (gameObject.layer == _terrainLayer)
            return collisionMeshChanged;

        RememberCollider(collider);
        gameObject.layer = _terrainLayer;
        _groundColliderCount++;
        if (_groundColliderCount <= 12)
            Plugin.LogSource.LogInfo($"Mapped imported ground collider '{collider.name}' from layer {LayerMask.LayerToName(ColliderSnapshots[id].Layer)} to Disc Golf Masters terrain layer '{DiscCollision.k_TerrainLayerName}'.");
        return true;
    }

    public static void PrepareImportedGroundContact(DiscCollision collision, Collision hit)
    {
        if (!CourseScenePatcher.IsImportedMapActive || !_preparedScene.IsValid() || !_preparedScene.isLoaded || collision == null || hit == null)
            return;

        try
        {
            var disc = collision.disc;
            var collider = hit.collider;
            if (disc == null || (!disc.IsThrown && !HasRecentImportedThrowTrace(disc.GetInstanceID())) || collider == null || collider.isTrigger ||
                collider.gameObject.scene.handle != _preparedScene.handle || hit.contactCount == 0)
                return;

            var mostUpwardNormal = -1f;
            for (var i = 0; i < hit.contactCount; i++)
                mostUpwardNormal = Mathf.Max(mostUpwardNormal, hit.GetContact(i).normal.y);

            if (mostUpwardNormal < 0.65f)
                return;

            UseDiscTerrainLayer(collider);
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogDebug($"Could not classify an upward imported-map contact as terrain: {ex.Message}");
        }
    }

    public static bool IsBelowWater(Vector3 point)
    {
        foreach (var water in WaterSurfaces)
        {
            if (water.TryGetSurfaceY(point, out var surfaceY) && point.y <= surfaceY + WaterGroundClearance)
                return true;
        }
        return false;
    }

    public static bool TryGetWaterObArea(Vector3 point, out OBArea area)
    {
        foreach (var water in WaterSurfaces)
        {
            if (water.ObArea == null || !water.TryGetSurfaceY(point, out var surfaceY) || point.y > surfaceY + 0.2f)
                continue;
            if (ReportedWaterAreas.Add(water.ObArea.GetInstanceID()))
                Plugin.LogSource.LogInfo($"Water OB check matched '{water.ObArea.name}' at ({point.x:F1},{point.y:F1},{point.z:F1}), local surface Y={surfaceY:F1}.");
            area = water.ObArea;
            return true;
        }
        area = null;
        return false;
    }

    public static bool UpdateImportedGroundState(DiscBehaviour disc)
    {
        if (!CourseScenePatcher.IsImportedMapActive || !_preparedScene.IsValid() || !_preparedScene.isLoaded || disc == null)
            return false;
        try
        {
            ImportedTerrainIsolation.DetachHiddenTerrain(disc);
            // A real imported Unity Terrain keeps its native heightfield path.
            if (disc.terrain != null)
                return false;
            if (!TryGetImportedGroundDistance(disc, disc.GroundDistance, out var distance, out _))
                return false;

            // Write the real fields: native DGM code reads them directly and
            // bypasses managed get_GroundDistance/get_IsGrounded hooks.
            disc.GroundDistance = distance;
            var contact = disc.groundContact;
            disc.IsGrounded = contact != null && contact.HasGroundModel
                ? contact.OnGround
                : distance <= 0.05f && distance >= -0.05f;
            return true;
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Could not isolate imported disc ground state: {ex.Message}");
            return false;
        }
    }

    public static bool TryGetImportedGroundDistance(DiscBehaviour disc, float gameGroundDistance, out float groundDistance, out string support)
    {
        groundDistance = 0f;
        support = "<none>";
        if (!CourseScenePatcher.IsImportedMapActive || !_preparedScene.IsValid() || !_preparedScene.isLoaded || disc == null)
            return false;

        try
        {
            var discId = disc.GetInstanceID();
            var position = disc.transform.position;
            var fixedTime = Time.fixedTime;
            if (GroundDistanceSamples.TryGetValue(discId, out var cached) &&
                cached.FixedTime == fixedTime && (cached.Position - position).sqrMagnitude < 0.000001f)
            {
                groundDistance = cached.Distance;
                support = cached.Support;
                return true;
            }

            var origin = position + Vector3.up * GroundDistanceRayStartMeters;
            var hits = Physics.RaycastAll(origin, Vector3.down, GroundDistanceRayLengthMeters, ~0, QueryTriggerInteraction.Ignore);
            var nearestDistance = float.PositiveInfinity;
            var groundY = 0f;
            var groundName = string.Empty;
            for (var i = 0; i < hits.Length; i++)
            {
                var hit = hits[i];
                var collider = hit.collider;
                if (collider == null || collider.isTrigger ||
                    collider.gameObject.scene.handle != _preparedScene.handle ||
                    IsDiscCollider(disc, collider) || IsWaterCollider(collider))
                    continue;

                if (hit.normal.y < MinimumSupportNormalY || hit.point.y > position.y + GroundDistanceRayStartMeters)
                    continue;

                // The closest physical surface below the disc wins. A legitimate
                // roof or bridge is valid support; a street underneath it must not
                // win by layer/name. Surface size does not make a collider false.
                if (hit.distance < nearestDistance)
                {
                    nearestDistance = hit.distance;
                    groundY = hit.point.y;
                    groundName = collider.name;
                }
            }

            if (!float.IsPositiveInfinity(nearestDistance))
            {
                groundDistance = position.y - groundY;
                support = $"imported:{groundName}@Y={groundY:F2}";
            }
            else
            {
                // No imported ground exists below this point (for example, over
                // water or beyond a shoreline). Treat it as unsupported instead of
                // falling back to the hidden Canon Park ground at Y≈30 m.
                groundDistance = NoImportedGroundDistanceMeters;
                support = "<no imported ground below disc>";
            }

            GroundDistanceSamples[discId] = new GroundDistanceSample
            {
                FixedTime = fixedTime,
                Position = position,
                Distance = groundDistance,
                Support = support
            };

            if (GroundDistanceOverridesLogged.Add(discId))
                Plugin.LogSource.LogInfo($"Imported-map ground-distance override: map='{_preparedScene.name}', disc={discId}, position=({position.x:F1},{position.y:F1},{position.z:F1}), gameDistance={gameGroundDistance:F2}m, importedDistance={groundDistance:F2}m, support={support}.");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogDebug($"Could not sample imported ground below disc {disc.GetInstanceID()}: {ex.Message}");
            return false;
        }
    }

    private static bool IsWaterCollider(Collider collider)
    {
        if (collider == null)
            return false;

        return HasWaterNameToken(collider.gameObject.name) || (_waterLayer >= 0 && collider.gameObject.layer == _waterLayer);
    }

    private static bool IsNamedBuildingSurface(Collider collider)
    {
        for (var transform = collider == null ? null : collider.transform; transform != null; transform = transform.parent)
        {
            var name = transform.name;
            if (name.IndexOf("sky", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("roof", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("building", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("house", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    public static void HandleDiscPhysicsStep(DiscBehaviour disc)
    {
        if (!CourseScenePatcher.IsImportedMapActive || disc == null)
            return;
        try
        {
            var id = disc.GetInstanceID();
            SeedThrowTraceFromPhysicsStep(disc, id);
            if (disc.IsThrown)
            {
                if (!ThrowOrigins.ContainsKey(id))
                    ThrowOrigins[id] = disc.transform.position;
                HandledDiscs.Remove(id);
                ThrownDiscs.Add(id);
            }
            if (!ThrownDiscs.Contains(id))
                return;

            // Keep tracking after IsThrown turns false so the trace can capture the
            // completion transition that should advance the round.
            TraceImportedThrow(disc, id);
            if (disc.ThrowComplete)
            {
                ThrownDiscs.Remove(id);
                return;
            }
            if (disc.State == DiscBehaviour.DiscState.InWaterHazard)
            {
                ThrownDiscs.Remove(id);
                return;
            }
            if (HandledDiscs.Contains(id))
                return;

            var position = disc.transform.position;
            if (disc.IsGrounded && ThrowOrigins.TryGetValue(id, out var throwOrigin) &&
                Vector3.Distance(position, throwOrigin) > 12f && ImportedThrowTraces.TryGetValue(id, out var trace))
            {
                var movedSinceProfile = trace.HasGroundProfile
                    ? Vector3.Distance(position, trace.LastGroundProfilePosition)
                    : float.PositiveInfinity;
                if (!trace.HasGroundProfile ||
                    (Time.realtimeSinceStartup - trace.LastGroundProfileAt >= 1f && movedSinceProfile >= 3f))
                    LogNearbyImportedGround(disc, throwOrigin, "grounded-progress", trace);
            }

            foreach (var water in WaterSurfaces)
            {
                if (water.Hazard == null || !water.TryGetSurfaceY(position, out var surfaceY))
                    continue;

                var hasReachedWater = position.y <= surfaceY + 0.2f;
                var restingAboveWater = disc.IsGrounded && position.y <= surfaceY + _waterLandingClearanceMeters;
                if (!hasReachedWater && !restingAboveWater)
                    continue;

                HandledDiscs.Add(id);
                ThrownDiscs.Remove(id);
                disc.OnLandedInWater(water.Hazard);
                if (ImportedThrowTraces.TryGetValue(id, out var waterTrace))
                    waterTrace.CompletionLogged = true;
                Plugin.LogSource.LogInfo($"Applied the imported water hazard to disc {id} at ({position.x:F1},{position.y:F1},{position.z:F1}); local water surface Y={surfaceY:F1}, grounded={disc.IsGrounded}.");
                return;
            }
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Could not apply the imported water hazard to a disc: {ex.Message}");
        }
    }

    public static void CaptureDiscPhysicsStepStart(DiscBehaviour disc)
    {
        if (!CourseScenePatcher.IsImportedMapActive || disc == null)
            return;

        try
        {
            var body = disc.GetRigidbody();
            DiscMotionStarts[disc.GetInstanceID()] = new PhysicsStepSample
            {
                CapturedAt = Time.realtimeSinceStartup,
                Position = disc.transform.position,
                Velocity = body == null ? Vector3.zero : body.velocity
            };
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogDebug($"Could not capture disc physics-step start: {ex.Message}");
        }
    }

    public static void CaptureDiscCollisionStart(DiscCollision collision, Collision hit)
    {
        if (!CourseScenePatcher.IsImportedMapActive || collision == null || hit == null)
            return;

        try
        {
            var disc = collision.disc;
            if (disc == null)
                return;

            var discId = disc.GetInstanceID();
            if (!disc.IsThrown && !HasRecentImportedThrowTrace(discId))
                return;

            var body = collision.rb != null ? collision.rb : disc.GetRigidbody();
            DiscMotionStarts[discId] = new PhysicsStepSample
            {
                CapturedAt = Time.realtimeSinceStartup,
                Position = disc.transform.position,
                Velocity = body == null ? Vector3.zero : body.velocity
            };
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogDebug($"Could not capture disc position before collision handling: {ex.Message}");
        }
    }

    private static bool HasRecentImportedThrowTrace(int discId)
    {
        if (!ImportedThrowTraces.TryGetValue(discId, out var trace))
            return false;
        return !trace.CompletionLogged ||
               Time.realtimeSinceStartup - trace.CompletedAt <= RecentCompletedThrowCollisionWindowSeconds;
    }

    private static void SeedThrowTraceFromPhysicsStep(DiscBehaviour disc, int discId)
    {
        if (!DiscMotionStarts.TryGetValue(discId, out var stepStart))
            return;

        DiscMotionStarts.Remove(discId);
        SeedThrowTraceFromSample(disc, discId, stepStart);
    }

    private static void SeedThrowTraceFromSample(DiscBehaviour disc, int discId, PhysicsStepSample stepStart, bool forceExistingSample = false)
    {
        if (disc == null || !disc.IsThrown || disc.ThrowComplete || stepStart == null)
            return;

        if (!ImportedThrowTraces.TryGetValue(discId, out var trace) || trace.CompletionLogged)
        {
            BeginImportedThrowTrace(disc, discId, stepStart.CapturedAt, stepStart.Position, stepStart.Velocity);
            return;
        }

        if (!forceExistingSample)
            return;

        // Seed from immediately before DGM's physics or collision handler. This
        // collision sample exposes a reset before the callback could initialize
        // diagnostics at the snapped point. FixedUpdate samples leave an active
        // flight trace untouched so movement between callbacks is still compared.
        trace.LastObservedPosition = stepStart.Position;
        trace.LastObservedVelocity = stepStart.Velocity;
        trace.LastPositionSampleAt = stepStart.CapturedAt;
        trace.HasPositionSample = true;
    }

    public static void LogImportedDiscCollision(DiscCollision collision, Collision hit)
    {
        if (!CourseScenePatcher.IsImportedMapActive || !_preparedScene.IsValid() || !_preparedScene.isLoaded || collision == null || hit == null)
            return;

        try
        {
            var disc = collision.disc;
            var other = hit.collider;
            if (disc == null || other == null)
                return;

            var discId = disc.GetInstanceID();
            if (!disc.IsThrown && !HasRecentImportedThrowTrace(discId))
                return;

            if (DiscMotionStarts.TryGetValue(discId, out var collisionStart))
            {
                DiscMotionStarts.Remove(discId);
                SeedThrowTraceFromSample(disc, discId, collisionStart, forceExistingSample: true);
            }
            if (!ImportedThrowTraces.TryGetValue(discId, out var trace) ||
                (trace.CompletionLogged && Time.realtimeSinceStartup - trace.CompletedAt > RecentCompletedThrowCollisionWindowSeconds))
            {
                if (!disc.IsThrown || disc.ThrowComplete)
                    return;
                trace = BeginImportedThrowTrace(disc, discId);
            }

            if (!CollisionCollidersLoggedByThrow.TryGetValue(discId, out var loggedColliders))
                CollisionCollidersLoggedByThrow[discId] = loggedColliders = new HashSet<int>();
            var firstForCollider = loggedColliders.Add(other.GetInstanceID());
            var now = Time.realtimeSinceStartup;
            var discPosition = disc.transform.position;
            var velocity = collision.rb != null ? collision.rb.velocity : hit.relativeVelocity;
            var movedSinceLog = trace.CollisionLogCount == 0
                ? 0f
                : Vector3.Distance(discPosition, trace.LastCollisionLogPosition);
            var velocityChange = trace.CollisionLogCount == 0
                ? 0f
                : Vector3.Distance(velocity, trace.LastCollisionLogVelocity);
            var shouldLog = trace.CollisionLogCount == 0 || firstForCollider ||
                (now - trace.LastCollisionLogAt >= 0.5f && (movedSinceLog >= 1f || velocityChange >= 1.5f));
            if (!shouldLog || trace.CollisionLogCount >= 32)
                return;

            var bounds = other.bounds;
            var contact = hit.contactCount > 0 ? hit.GetContact(0) : default;
            var contactPoint = hit.contactCount > 0 ? contact.point : disc.transform.position;
            var contactNormal = hit.contactCount > 0 ? contact.normal : Vector3.zero;
            var renderer = other.GetComponent<Renderer>();
            var renderBounds = renderer == null ? "<none>" : $"center=({renderer.bounds.center.x:F1},{renderer.bounds.center.y:F1},{renderer.bounds.center.z:F1}),size=({renderer.bounds.size.x:F1},{renderer.bounds.size.y:F1},{renderer.bounds.size.z:F1})";
            var meshInfo = DescribeColliderMesh(other);
            var shapeInfo = DescribeColliderShape(other);
            var transformPath = DescribeTransformPath(other.transform);
            var colliderScene = other.gameObject.scene;
            var importedSceneContact = colliderScene.handle == _preparedScene.handle;
            var physicsMaterial = DescribeColliderPhysicsMaterial(other);
            var deltaPosition = trace.CollisionLogCount == 0 ? Vector3.zero : discPosition - trace.LastCollisionLogPosition;
            var deltaVelocity = trace.CollisionLogCount == 0 ? Vector3.zero : velocity - trace.LastCollisionLogVelocity;
            Plugin.LogSource.LogInfo($"Imported-map disc collision: map='{_preparedScene.name}', disc={disc.GetInstanceID()}, elapsed={Mathf.Max(0f, now - trace.StartedAt):F2}s, contact={trace.CollisionLogCount + 1}, deltaPosition=({deltaPosition.x:F1},{deltaPosition.y:F1},{deltaPosition.z:F1}), deltaVelocity=({deltaVelocity.x:F1},{deltaVelocity.y:F1},{deltaVelocity.z:F1}), position=({discPosition.x:F1},{discPosition.y:F1},{discPosition.z:F1}), velocity=({velocity.x:F1},{velocity.y:F1},{velocity.z:F1}), contactPoint=({contactPoint.x:F1},{contactPoint.y:F1},{contactPoint.z:F1}), normal=({contactNormal.x:F2},{contactNormal.y:F2},{contactNormal.z:F2}); collisionScene='{colliderScene.name}', importedSceneContact={importedSceneContact}, hit='{other.name}#{other.GetInstanceID()}', root='{other.transform.root.name}', collider={other.GetType().FullName}, shape={shapeInfo}, physicsMaterial={physicsMaterial}, enabled={other.enabled}, trigger={other.isTrigger}, layer={other.gameObject.layer}('{LayerMask.LayerToName(other.gameObject.layer)}'), center=({bounds.center.x:F1},{bounds.center.y:F1},{bounds.center.z:F1}), size=({bounds.size.x:F1},{bounds.size.y:F1},{bounds.size.z:F1}), transformPath={transformPath}, transformPosition=({other.transform.position.x:F1},{other.transform.position.y:F1},{other.transform.position.z:F1}), transformScale=({other.transform.lossyScale.x:F2},{other.transform.lossyScale.y:F2},{other.transform.lossyScale.z:F2}), mesh={meshInfo}, rendererBounds={renderBounds}, {DescribeThrowState(disc)}.");
            trace.LastCollisionLogAt = now;
            trace.LastCollisionLogPosition = discPosition;
            trace.LastCollisionLogVelocity = velocity;
            trace.CollisionLogCount++;
            TraceImportedThrow(disc, discId);
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogDebug($"Could not inspect an imported-map disc collision: {ex.Message}");
        }
    }

    private static string DescribeColliderShape(Collider collider)
    {
        var box = GetSameColliderComponent<BoxCollider>(collider);
        if (box != null)
            return $"box localCenter=({box.center.x:F2},{box.center.y:F2},{box.center.z:F2}), localSize=({box.size.x:F2},{box.size.y:F2},{box.size.z:F2})";
        var sphere = GetSameColliderComponent<SphereCollider>(collider);
        if (sphere != null)
            return $"sphere localCenter=({sphere.center.x:F2},{sphere.center.y:F2},{sphere.center.z:F2}), radius={sphere.radius:F2}";
        var capsule = GetSameColliderComponent<CapsuleCollider>(collider);
        if (capsule != null)
            return $"capsule localCenter=({capsule.center.x:F2},{capsule.center.y:F2},{capsule.center.z:F2}), radius={capsule.radius:F2}, height={capsule.height:F2}, direction={capsule.direction}";
        var meshCollider = GetSameColliderComponent<MeshCollider>(collider);
        if (meshCollider != null)
            return $"mesh convex={meshCollider.convex}";
        return "other";
    }

    private static string DescribeColliderPhysicsMaterial(Collider collider)
    {
        try
        {
            var material = collider == null ? null : collider.sharedMaterial;
            return material == null
                ? "<default>"
                : $"{material.name}[dynamicFriction={material.dynamicFriction:F2},staticFriction={material.staticFriction:F2},bounciness={material.bounciness:F2},frictionCombine={material.frictionCombine},bounceCombine={material.bounceCombine}]";
        }
        catch (Exception ex)
        {
            return $"<unavailable:{ex.GetType().Name}>";
        }
    }

    private static string DescribeColliderMesh(Collider collider)
    {
        var meshCollider = GetSameColliderComponent<MeshCollider>(collider);
        var source = meshCollider != null ? "MeshCollider" : "MeshFilter-fallback";
        var mesh = meshCollider != null ? meshCollider.sharedMesh : collider.GetComponent<MeshFilter>()?.sharedMesh;
        if (mesh == null)
            return "<none>";

        var bounds = mesh.bounds;
        return $"{source}:{mesh.name}#{mesh.GetInstanceID()}/{mesh.vertexCount}v/{mesh.subMeshCount}submeshes/localBounds=({bounds.center.x:F1},{bounds.center.y:F1},{bounds.center.z:F1};{bounds.size.x:F1},{bounds.size.y:F1},{bounds.size.z:F1})";
    }

    private static string DescribeTransformPath(Transform transform)
    {
        var parts = new List<string>();
        for (var depth = 0; transform != null && depth < 7; depth++, transform = transform.parent)
        {
            var local = transform.localPosition;
            var angles = transform.localEulerAngles;
            var scale = transform.localScale;
            parts.Add($"{transform.name}[p=({local.x:F1},{local.y:F1},{local.z:F1}),r=({angles.x:F0},{angles.y:F0},{angles.z:F0}),s=({scale.x:F2},{scale.y:F2},{scale.z:F2})]");
        }
        return string.Join(" <- ", parts);
    }

    public static void Restore()
    {
        foreach (var snapshot in ColliderSnapshots.Values)
        {
            var collider = snapshot.Collider;
            if (collider == null)
                continue;
            try
            {
                if (snapshot.HasMeshCollider)
                {
                    var meshCollider = GetSameColliderComponent<MeshCollider>(collider);
                    if (meshCollider != null)
                    {
                        meshCollider.sharedMesh = snapshot.SharedMesh;
                        meshCollider.convex = snapshot.Convex;
                    }
                }
                collider.gameObject.layer = snapshot.Layer;
                collider.isTrigger = snapshot.IsTrigger;
                collider.enabled = snapshot.Enabled;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogDebug($"Could not restore an imported map collider: {ex.Message}");
            }
        }
        ColliderSnapshots.Clear();
        foreach (var mesh in TemporaryMeshes)
        {
            if (mesh != null)
                UnityEngine.Object.Destroy(mesh);
        }
        TemporaryMeshes.Clear();
        foreach (var gameObject in TemporaryObjects)
        {
            if (gameObject == null)
                continue;
            try
            {
                gameObject.SetActive(false);
                UnityEngine.Object.Destroy(gameObject);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogDebug($"Could not remove an imported water hazard marker: {ex.Message}");
            }
        }
        TemporaryObjects.Clear();
        WaterSurfaces.Clear();
        SeenWaterMeshes.Clear();
        WaterCutColliders.Clear();
        HandledDiscs.Clear();
        ThrownDiscs.Clear();
        ThrowOrigins.Clear();
        ImportedThrowTraces.Clear();
        GroundDistanceSamples.Clear();
        DiscMotionStarts.Clear();
        GroundDistanceOverridesLogged.Clear();
        CollisionCollidersLoggedByThrow.Clear();
        ReportedWaterAreas.Clear();
        StaticBatchColliderSkipsLogged.Clear();
        _preparedScene = default;
        _terrainLayer = -1;
        _waterLayer = -1;
        _waterLandingClearanceMeters = DefaultWaterLandingClearanceMeters;
        _groundColliderCount = 0;
        _waterHazardMarkerCount = 0;
        _waterObAreaCount = 0;
        UnreadableMeshesLogged.Clear();
    }

    private static void TraceImportedThrow(DiscBehaviour disc, int discId)
    {
        var now = Time.realtimeSinceStartup;
        ImportedThrowTraces.TryGetValue(discId, out var trace);
        if (trace != null && !trace.CompletionLogged)
            LogLargeImportedDiscPositionJump(disc, discId, trace, now);
        if (disc.ThrowComplete)
        {
            if (trace != null && !trace.CompletionLogged)
            {
                var throwOrigin = ThrowOrigins.TryGetValue(discId, out var savedOrigin)
                    ? savedOrigin
                    : disc.transform.position;
                if (!trace.HasGroundProfile || Vector3.Distance(disc.transform.position, trace.LastGroundProfilePosition) >= 1f)
                    LogNearbyImportedGround(disc, throwOrigin, "throw-complete", trace);
                var body = disc.GetRigidbody();
                var velocity = body == null ? Vector3.zero : body.velocity;
                Plugin.LogSource.LogInfo($"Imported-map throw completed: disc={discId}, elapsed={Mathf.Max(0f, now - trace.StartedAt):F1}s, position=({disc.transform.position.x:F1},{disc.transform.position.y:F1},{disc.transform.position.z:F1}), velocity=({velocity.x:F1},{velocity.y:F1},{velocity.z:F1}), state={disc.State}, grounded={disc.IsGrounded}, groundDistance={disc.GroundDistance:F2}m, collisionContactsLogged={trace.CollisionLogCount}, {DescribeThrowState(disc)}, {DescribeGroundContact(disc)}.");
                trace.CompletionLogged = true;
                trace.CompletedAt = now;
            }
            return;
        }

        if (trace == null || trace.CompletionLogged)
        {
            BeginImportedThrowTrace(disc, discId, now);
            return;
        }

        if (now - trace.LastProgressAt < 10f)
            return;

        trace.LastProgressAt = now;
        var rigidbody = disc.GetRigidbody();
        var currentVelocity = rigidbody == null ? Vector3.zero : rigidbody.velocity;
        if (trace.ActiveSurfaceSnapshotCount < 4)
        {
            var throwOrigin = ThrowOrigins.TryGetValue(discId, out var savedOrigin)
                ? savedOrigin
                : disc.transform.position;
            LogNearbyImportedGround(disc, throwOrigin, "active-progress", trace, recordGroundProfile: false);
            trace.ActiveSurfaceSnapshotCount++;
        }
        Plugin.LogSource.LogInfo($"Imported-map throw still active: disc={discId}, elapsed={Mathf.Max(0f, now - trace.StartedAt):F1}s, position=({disc.transform.position.x:F1},{disc.transform.position.y:F1},{disc.transform.position.z:F1}), velocity=({currentVelocity.x:F1},{currentVelocity.y:F1},{currentVelocity.z:F1}), state={disc.State}, grounded={disc.IsGrounded}, groundDistance={disc.GroundDistance:F2}m, collisionContactsLogged={trace.CollisionLogCount}, {DescribeThrowState(disc)}.");
    }

    private static void LogLargeImportedDiscPositionJump(DiscBehaviour disc, int discId, ImportedThrowTrace trace, float now)
    {
        if (disc == null || trace == null)
            return;

        var position = disc.transform.position;
        var body = disc.GetRigidbody();
        var velocity = body == null ? Vector3.zero : body.velocity;
        if (!trace.HasPositionSample)
        {
            trace.LastObservedPosition = position;
            trace.LastObservedVelocity = velocity;
            trace.LastPositionSampleAt = now;
            trace.HasPositionSample = true;
            return;
        }

        var previousPosition = trace.LastObservedPosition;
        var previousVelocity = trace.LastObservedVelocity;
        var elapsed = Mathf.Max(0f, now - trace.LastPositionSampleAt);
        var delta = position - previousPosition;
        if (delta.sqrMagnitude < LargeDiscPositionJumpThresholdMeters * LargeDiscPositionJumpThresholdMeters)
        {
            trace.LastObservedPosition = position;
            trace.LastObservedVelocity = velocity;
            trace.LastPositionSampleAt = now;
            return;
        }

        var throwOrigin = ThrowOrigins.TryGetValue(discId, out var savedOrigin) ? savedOrigin : previousPosition;
        trace.LastObservedPosition = position;
        trace.LastObservedVelocity = velocity;
        trace.LastPositionSampleAt = now;
        if (trace.PositionJumpLogCount >= MaxPositionJumpLogsPerThrow)
            return;

        trace.PositionJumpLogCount++;
        Plugin.LogSource.LogWarning(
            $"Imported-map disc position jump: disc={discId}, jump={trace.PositionJumpLogCount}, elapsed={Mathf.Max(0f, now - trace.StartedAt):F2}s, sampleInterval={elapsed:F3}s, delta=({delta.x:F2},{delta.y:F2},{delta.z:F2})m, position=({position.x:F2},{position.y:F2},{position.z:F2}), velocity=({velocity.x:F2},{velocity.y:F2},{velocity.z:F2}), state={disc.State}, grounded={disc.IsGrounded}, groundDistance={disc.GroundDistance:F2}m, {DescribeThrowState(disc)}.");

        LogNearbyImportedGround(disc, throwOrigin, "position-jump", trace);
    }

    private static bool IsDiscCollider(DiscBehaviour disc, Collider collider)
    {
        if (disc == null || collider == null)
            return false;

        var discTransform = disc.transform;
        var colliderTransform = collider.transform;
        return colliderTransform == discTransform || colliderTransform.IsChildOf(discTransform) || discTransform.IsChildOf(colliderTransform);
    }

    private static ImportedThrowTrace BeginImportedThrowTrace(
        DiscBehaviour disc,
        int discId,
        float now = -1f,
        Vector3? initialPosition = null,
        Vector3? initialVelocity = null)
    {
        if (now < 0f)
            now = Time.realtimeSinceStartup;
        var body = disc.GetRigidbody();
        var position = initialPosition ?? disc.transform.position;
        var velocity = initialVelocity ?? (body == null ? Vector3.zero : body.velocity);
        var trace = new ImportedThrowTrace
        {
            StartedAt = now,
            LastProgressAt = now,
            LastPositionSampleAt = now,
            LastObservedPosition = position,
            LastObservedVelocity = velocity,
            HasPositionSample = true
        };
        ImportedThrowTraces[discId] = trace;
        ThrowOrigins[discId] = position;
        CollisionCollidersLoggedByThrow[discId] = new HashSet<int>();
        Plugin.LogSource.LogInfo($"Imported-map throw started: disc={discId}, position=({position.x:F1},{position.y:F1},{position.z:F1}), map='{_preparedScene.name}'.");
        return trace;
    }

    private static string DescribeThrowState(DiscBehaviour disc)
    {
        var currentThrow = disc.currentThrow;
        var throwInfo = currentThrow == null
            ? "currentThrow=<null>"
            : $"throwComplete={currentThrow.complete}, throwDuration={currentThrow.duration:F1}s, throwDistance={currentThrow.distance:F1}m, collided={currentThrow.hasCollidedWithObject}, penalized={currentThrow.wasPenalized}, penalties={currentThrow.throwPenalties}";
        var obArea = disc.currentOBArea;
        return $"isThrown={disc.IsThrown}, flightTime={disc.flightTime:F1}s, stationaryTimer={disc.stationaryTimer:F1}s, resetHeight={disc.resetHeight:F1}m, obArea={(obArea == null ? "<none>" : obArea.name)}, {throwInfo}";
    }

    private static string DescribeGroundContact(DiscBehaviour disc)
    {
        try
        {
            var groundContact = disc == null ? null : disc.groundContact;
            if (groundContact == null)
                return "groundContact=<none>";

            var solver = groundContact.solver;
            var state = groundContact.state;
            var point = solver == null ? Vector3.zero : solver.ContactPoint;
            var normal = solver == null ? Vector3.zero : solver.ContactNormal;
            var impactSpeed = solver == null ? 0f : solver.ImpactSpeed;
            var stateNormal = state == null ? Vector3.zero : state.groundNormal;
            var mapper = groundContact.mapper;
            var source = mapper == null ? "<none>" : $"{mapper.gameObject.scene.name}/{mapper.name}(enabled={mapper.enabled})";
            var terrain = disc.terrain;
            var cachedTerrain = terrain == null ? "<none>" : $"{terrain.gameObject.scene.name}/{terrain.name}";
            return $"groundContact[onGround={groundContact.OnGround},hasModel={groundContact.HasGroundModel},mapper={source},cachedTerrain={cachedTerrain},point=({point.x:F2},{point.y:F2},{point.z:F2}),normal=({normal.x:F2},{normal.y:F2},{normal.z:F2}),impactSpeed={impactSpeed:F2},stateNormal=({stateNormal.x:F2},{stateNormal.y:F2},{stateNormal.z:F2})]";
        }
        catch (Exception ex)
        {
            return $"groundContact=<unavailable:{ex.Message}>";
        }
    }

    private static void LogNearbyImportedGround(DiscBehaviour disc, Vector3 throwOrigin, string phase, ImportedThrowTrace trace, bool recordGroundProfile = true)
    {
        if (disc == null || trace == null || !_preparedScene.IsValid() || !_preparedScene.isLoaded)
            return;

        try
        {
            var position = disc.transform.position;
            var rayOrigin = position + Vector3.up * 0.5f;
            var hits = Physics.RaycastAll(rayOrigin, Vector3.down, 300f, ~0, QueryTriggerInteraction.Ignore);
            var nearestImportedDistance = float.PositiveInfinity;
            var nearestSceneDistance = float.PositiveInfinity;
            for (var i = 0; i < hits.Length; i++)
            {
                var collider = hits[i].collider;
                if (collider == null || collider.isTrigger)
                    continue;
                nearestSceneDistance = Mathf.Min(nearestSceneDistance, hits[i].distance);
                if (collider.gameObject.scene.handle == _preparedScene.handle)
                    nearestImportedDistance = Mathf.Min(nearestImportedDistance, hits[i].distance);
            }

            var examples = new List<string>();
            for (var i = 0; i < hits.Length && examples.Count < 6; i++)
            {
                var hit = hits[i];
                var collider = hit.collider;
                if (collider == null || collider.isTrigger || collider.gameObject.scene.handle != _preparedScene.handle ||
                    hit.distance > nearestImportedDistance + 2f)
                    continue;

                var bounds = collider.bounds;
                examples.Add($"{collider.name}#{collider.GetInstanceID()}|type={collider.GetType().Name}|scene={collider.gameObject.scene.name}|root={collider.transform.root.name}|layer={collider.gameObject.layer}('{LayerMask.LayerToName(collider.gameObject.layer)}')|physicsMaterial={DescribeColliderPhysicsMaterial(collider)}|hitY={hit.point.y:F1}|normalY={hit.normal.y:F2}|distance={hit.distance:F1}|triangle={hit.triangleIndex}|center=({bounds.center.x:F1},{bounds.center.y:F1},{bounds.center.z:F1})|size=({bounds.size.x:F1},{bounds.size.y:F1},{bounds.size.z:F1})|mesh={DescribeColliderMesh(collider)}|path={DescribeTransformPath(collider.transform)}");
            }

            var sceneExamples = new List<string>();
            for (var i = 0; i < hits.Length && sceneExamples.Count < 6; i++)
            {
                var hit = hits[i];
                var collider = hit.collider;
                if (collider == null || collider.isTrigger || hit.distance > nearestSceneDistance + 2f)
                    continue;
                sceneExamples.Add($"{collider.name}#{collider.GetInstanceID()}|type={collider.GetType().Name}|scene={collider.gameObject.scene.name}|layer={collider.gameObject.layer}('{LayerMask.LayerToName(collider.gameObject.layer)}')|physicsMaterial={DescribeColliderPhysicsMaterial(collider)}|hitY={hit.point.y:F1}|normalY={hit.normal.y:F2}|distance={hit.distance:F1}|triangle={hit.triangleIndex}");
            }

            var nearbyExamples = new List<string>();
            var nearby = Physics.OverlapSphere(position, 1.5f, ~0, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < nearby.Length && nearbyExamples.Count < 8; i++)
            {
                var collider = nearby[i];
                if (collider == null)
                    continue;
                var bounds = collider.bounds;
                var meshInfo = DescribeColliderMesh(collider);
                nearbyExamples.Add($"{collider.name}#{collider.GetInstanceID()}|scene={collider.gameObject.scene.name}|{collider.GetType().Name}|layer={collider.gameObject.layer}('{LayerMask.LayerToName(collider.gameObject.layer)}')|physicsMaterial={DescribeColliderPhysicsMaterial(collider)}|trigger={collider.isTrigger}|center=({bounds.center.x:F1},{bounds.center.y:F1},{bounds.center.z:F1})|size=({bounds.size.x:F1},{bounds.size.y:F1},{bounds.size.z:F1})|mesh={meshInfo}");
            }

            Plugin.LogSource.LogInfo($"Imported-map disc ground profile: disc={disc.GetInstanceID()}, phase='{phase}', map='{_preparedScene.name}', throw=({throwOrigin.x:F1},{throwOrigin.y:F1},{throwOrigin.z:F1}), position=({position.x:F1},{position.y:F1},{position.z:F1}), grounded={disc.IsGrounded}, gameGroundDistance={disc.GroundDistance:F2}m, travelled={Vector3.Distance(position, throwOrigin):F1}m; water at disc={DescribeWaterAtPoint(position)}; nearest imported hits={string.Join(" || ", examples)}; nearest loaded-scene hits={string.Join(" || ", sceneExamples)}; nearby colliders={string.Join(" || ", nearbyExamples)}.");
            if (recordGroundProfile)
            {
                trace.LastGroundProfileAt = Time.realtimeSinceStartup;
                trace.LastGroundProfilePosition = position;
                trace.HasGroundProfile = true;
            }
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogDebug($"Could not inspect imported surfaces near disc {disc.GetInstanceID()}: {ex.Message}");
        }
    }

    private static string DescribeWaterAtPoint(Vector3 point)
    {
        var matches = new List<string>();
        for (var i = 0; i < WaterSurfaces.Count && matches.Count < 4; i++)
        {
            var water = WaterSurfaces[i];
            if (!water.TryGetSurfaceY(point, out var surfaceY))
                continue;
            matches.Add($"{water.Name}[surfaceY={surfaceY:F2},discDeltaY={point.y - surfaceY:F2}m,triangles={water.FootprintTriangleCount}]");
        }
        return matches.Count == 0 ? "<none>" : string.Join(" || ", matches);
    }

    public static bool TryUseVisibleGroundMesh(Collider collider)
    {
        if (collider == null || _terrainLayer < 0 || collider.gameObject.scene.name == "")
            return false;

        try
        {
            var renderer = collider.GetComponent<Renderer>();
            if (renderer != null && renderer.isPartOfStaticBatch)
            {
                var sourceMesh = collider.GetComponent<MeshFilter>()?.sharedMesh;
                var colliderId = collider.GetInstanceID();
                if (StaticBatchColliderSkipsLogged.Count < 12 && StaticBatchColliderSkipsLogged.Add(colliderId))
                    Plugin.LogSource.LogInfo($"Kept the authored collider on static-batched renderer '{collider.name}': shared mesh '{(sourceMesh == null ? "<none>" : sourceMesh.name)}' contains batch geometry, not an isolated object mesh.");
                return false;
            }

            var filter = collider.GetComponent<MeshFilter>();
            var mesh = filter == null ? null : filter.sharedMesh;
            if (mesh == null || mesh.vertexCount < 3 || mesh.vertexCount > 250000)
                return false;
            if (!mesh.isReadable)
            {
                if (UnreadableMeshesLogged.Add(mesh.GetInstanceID()))
                    Plugin.LogSource.LogInfo($"Skipped visible-mesh collision replacement for '{collider.name}': mesh '{mesh.name}' is not readable; keeping the imported collider.");
                return false;
            }

            var meshCollider = GetSameColliderComponent<MeshCollider>(collider);
            if (meshCollider != null)
            {
                if (meshCollider.sharedMesh == mesh)
                    return false;
                RememberCollider(meshCollider);
                var oldVertices = meshCollider.sharedMesh == null ? 0 : meshCollider.sharedMesh.vertexCount;
                meshCollider.sharedMesh = null;
                meshCollider.sharedMesh = mesh;
                meshCollider.convex = false;
                meshCollider.gameObject.layer = _terrainLayer;
                Plugin.LogSource.LogInfo($"Aligned imported ground collider '{collider.name}' to its visible mesh ({oldVertices} to {mesh.vertexCount} vertices).");
                return true;
            }

            RememberCollider(collider);
            collider.enabled = false;
            var proxy = new GameObject($"DGM_VisibleGroundMesh_{collider.name}");
            TemporaryObjects.Add(proxy);
            SceneManager.MoveGameObjectToScene(proxy, collider.gameObject.scene);
            proxy.layer = _terrainLayer;
            proxy.transform.SetParent(filter.transform, false);
            var accurateCollider = proxy.AddComponent<MeshCollider>();
            accurateCollider.sharedMesh = mesh;
            accurateCollider.convex = false;
            _groundColliderCount++;
            Plugin.LogSource.LogInfo($"Replaced imported ground collider '{collider.name}' with its visible mesh ({mesh.vertexCount} vertices).");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Could not align imported ground collider '{collider.name}' to its visible mesh: {ex.Message}");
            return false;
        }
    }

    private static int ResolveWaterLayer(Scene scene)
    {
        try
        {
            var discs = UnityEngine.Object.FindObjectsOfType<DiscBehaviour>(true);
            for (var i = 0; discs != null && i < discs.Length; i++)
            {
                var layer = discs[i] == null ? -1 : discs[i].waterLayer;
                if (layer >= 0 && layer < 32)
                    return layer;
            }
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogDebug($"Could not read the game's disc water layer: {ex.Message}");
        }

        var waterLayer = LayerMask.NameToLayer("Water");
        return waterLayer >= 0 ? waterLayer : 0;
    }

    private static bool IsWaterSurface(Renderer renderer)
    {
        var name = renderer.gameObject.name;
        if (HasWaterNameToken(name))
            return true;

        // A material on a static-batched world renderer can be shared by the
        // whole combined mesh. It is not a reliable per-surface water marker.
        if (renderer.isPartOfStaticBatch)
            return false;

        // Imported combined world meshes can contain a water material alongside
        // buildings and terrain. Their tall bounds are not a water surface and
        // must not become a course-wide water trigger.
        if (renderer.bounds.size.y > 8f)
            return false;

        var materials = renderer.sharedMaterials;
        for (var i = 0; materials != null && i < materials.Length; i++)
        {
            var materialName = materials[i] == null ? "" : materials[i].name;
            if (HasWaterNameToken(materialName))
                return true;
        }
        return false;
    }

    internal static bool HasWaterNameToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var tokens = value.Split(new[] { ' ', '_', '-', '|', ':', '/', '\\', '.', '(', ')', '[', ']', '{', '}', ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
        {
            if (token.Equals("bath", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("cooler", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("decal", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("decals", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("fountain", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("foam", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("heater", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("pipe", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("splash", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("tank", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("tree", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("trees", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("tower", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("border", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("seat", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("diner", StringComparison.OrdinalIgnoreCase))
                return false;
        }

        foreach (var token in tokens)
        {
            if (token.Equals("water", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("ocean", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("sea", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("lake", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static WaterHazard AddMeshWaterHazardMarker(Scene scene, MeshFilter filter, Renderer renderer, string courseName)
    {
        if (filter == null || filter.sharedMesh == null)
            return null;
        var marker = CreateWaterHazardObject(scene, renderer.transform.position, renderer.transform.rotation, courseName, renderer.name);
        marker.transform.SetParent(filter.transform, false);
        marker.transform.localPosition = Vector3.zero;
        marker.transform.localRotation = Quaternion.identity;
        marker.transform.localScale = Vector3.one;
        _waterHazardMarkerCount++;
        Plugin.LogSource.LogInfo($"Added a sampled Disc Golf Masters water hazard for imported surface '{renderer.name}' ({filter.sharedMesh.vertexCount} vertices, layer '{LayerMask.LayerToName(_waterLayer)}', Y {renderer.bounds.min.y:F1}..{renderer.bounds.max.y:F1}); no unsupported concave trigger was created.");
        return marker.GetComponent<WaterHazard>();
    }

    private static WaterHazard AddWaterHazardMarker(Scene scene, Vector3 position, Quaternion rotation, string courseName, string sourceName)
    {
        var marker = CreateWaterHazardObject(scene, position, rotation, courseName, sourceName);
        _waterHazardMarkerCount++;
        return marker.GetComponent<WaterHazard>();
    }

    private static OBArea AddWaterObArea(Scene scene, Bounds bounds, string courseName, string sourceName)
    {
        var areaObject = new GameObject($"DGM_Custom_OB_Water_{sourceName}");
        TemporaryObjects.Add(areaObject);
        SceneManager.MoveGameObjectToScene(areaObject, scene);
        var area = areaObject.AddComponent<OBArea>();
        area.insideIsOB = true;
        area.isHazard = false;
        area.hideLineAndFlagpoles = true;
        area.bounds = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector2>(4);
        area.bounds[0] = new Vector2(bounds.min.x, bounds.min.z);
        area.bounds[1] = new Vector2(bounds.min.x, bounds.max.z);
        area.bounds[2] = new Vector2(bounds.max.x, bounds.max.z);
        area.bounds[3] = new Vector2(bounds.max.x, bounds.min.z);
        CourseScenePatcher.RegisterImportedWaterObArea(area);
        _waterObAreaCount++;
        return area;
    }

    private static GameObject CreateWaterHazardObject(Scene scene, Vector3 position, Quaternion rotation, string courseName, string sourceName)
    {
        var hazardObject = new GameObject($"DGM_{courseName}_WaterHazard_{sourceName}");
        TemporaryObjects.Add(hazardObject);
        SceneManager.MoveGameObjectToScene(hazardObject, scene);
        hazardObject.layer = _waterLayer;
        hazardObject.transform.SetPositionAndRotation(position, rotation);
        var hazard = hazardObject.AddComponent<WaterHazard>();
        hazard.useDropZone = false;
        hazard.layerMask = -1;
        return hazardObject;
    }

    private static void ConvertWaterColliders(Renderer renderer)
    {
        var colliders = renderer.GetComponentsInChildren<Collider>(true);
        for (var i = 0; colliders != null && i < colliders.Length; i++)
        {
            var collider = colliders[i];
            if (collider == null || collider.isTrigger)
                continue;
            RememberCollider(collider);
            var meshCollider = GetSameColliderComponent<MeshCollider>(collider);
            if (meshCollider != null && !meshCollider.convex)
            {
                meshCollider.enabled = false;
                Plugin.LogSource.LogInfo($"Disabled imported concave water collider '{collider.name}'; disc water contact and OB are handled by the sampled water surface.");
                continue;
            }
            collider.isTrigger = true;
            if (_waterLayer >= 0)
                collider.gameObject.layer = _waterLayer;
        }
    }

    private static bool RemoveShallowGroundTriangles(Collider collider)
    {
        if (WaterSurfaces.Count == 0)
            return false;
        var meshCollider = GetSameColliderComponent<MeshCollider>(collider);
        if (meshCollider == null)
        {
            Plugin.LogSource.LogInfo($"Waterline mesh audit skipped '{collider.name}': collider type={collider.GetType().Name}, bounds center=({collider.bounds.center.x:F1},{collider.bounds.center.y:F1},{collider.bounds.center.z:F1}), size=({collider.bounds.size.x:F1},{collider.bounds.size.y:F1},{collider.bounds.size.z:F1}).");
            return false;
        }

        var sourceMesh = meshCollider.sharedMesh;
        if (sourceMesh == null || sourceMesh.vertexCount < 3 || sourceMesh.vertexCount > 250000)
        {
            Plugin.LogSource.LogInfo($"Waterline mesh audit skipped '{collider.name}': mesh={(sourceMesh == null ? "<none>" : sourceMesh.name)}, vertices={(sourceMesh == null ? 0 : sourceMesh.vertexCount)}.");
            return false;
        }
        if (!sourceMesh.isReadable)
        {
            if (UnreadableMeshesLogged.Add(sourceMesh.GetInstanceID()))
                Plugin.LogSource.LogInfo($"Waterline mesh audit skipped '{collider.name}': source mesh '{sourceMesh.name}' is not readable; original collision is retained.");
            return false;
        }

        try
        {
            var vertices = sourceMesh.vertices;
            var triangles = sourceMesh.triangles;
            if (triangles.Length < 3)
                return false;

            var keptTriangles = new List<int>(triangles.Length);
            var removedCount = 0;
            var projectedWaterTriangles = 0;
            var aboveClearanceTriangles = 0;
            var lowestProjectedHeight = float.PositiveInfinity;
            var highestProjectedHeight = float.NegativeInfinity;
            for (var i = 0; i + 2 < triangles.Length; i += 3)
            {
                var ia = triangles[i];
                var ib = triangles[i + 1];
                var ic = triangles[i + 2];
                if (ia < 0 || ib < 0 || ic < 0 || ia >= vertices.Length || ib >= vertices.Length || ic >= vertices.Length)
                {
                    keptTriangles.Add(triangles[i]);
                    keptTriangles.Add(triangles[i + 1]);
                    keptTriangles.Add(triangles[i + 2]);
                    continue;
                }

                var a = meshCollider.transform.TransformPoint(vertices[ia]);
                var b = meshCollider.transform.TransformPoint(vertices[ib]);
                var c = meshCollider.transform.TransformPoint(vertices[ic]);
                var highestVertex = Mathf.Max(a.y, Mathf.Max(b.y, c.y));
                var center = (a + b + c) / 3f;
                var submerged = false;
                var projectedOverWater = false;
                for (var waterIndex = 0; waterIndex < WaterSurfaces.Count; waterIndex++)
                {
                    var water = WaterSurfaces[waterIndex];
                    if (!water.TryGetSurfaceY(center, out var localSurfaceY))
                        continue;
                    projectedOverWater = true;
                    if (highestVertex > localSurfaceY + WaterGroundClearance)
                        break;
                    submerged = true;
                    break;
                }

                if (projectedOverWater)
                {
                    projectedWaterTriangles++;
                    lowestProjectedHeight = Mathf.Min(lowestProjectedHeight, center.y);
                    highestProjectedHeight = Mathf.Max(highestProjectedHeight, center.y);
                    if (!submerged)
                        aboveClearanceTriangles++;
                }

                if (submerged)
                {
                    removedCount++;
                    continue;
                }
                keptTriangles.Add(ia);
                keptTriangles.Add(ib);
                keptTriangles.Add(ic);
            }

            var originalTriangleCount = triangles.Length / 3;
            if (removedCount == 0)
            {
                Plugin.LogSource.LogInfo($"Waterline mesh audit '{collider.name}': {originalTriangleCount} triangles, {projectedWaterTriangles} centroids over imported water, {aboveClearanceTriangles} above the {WaterGroundClearance:F1}m shoreline cutoff; projected triangle heights {(float.IsPositiveInfinity(lowestProjectedHeight) ? "none" : $"{lowestProjectedHeight:F1}..{highestProjectedHeight:F1}m")}, collider bounds center=({collider.bounds.center.x:F1},{collider.bounds.center.y:F1},{collider.bounds.center.z:F1}), size=({collider.bounds.size.x:F1},{collider.bounds.size.y:F1},{collider.bounds.size.z:F1}).");
                return false;
            }

            RememberCollider(meshCollider);
            if (keptTriangles.Count == 0)
            {
                meshCollider.enabled = false;
            }
            else
            {
                var clippedMesh = new Mesh
                {
                    name = $"{sourceMesh.name}_DGM_WaterlineClipped",
                    indexFormat = sourceMesh.indexFormat,
                    vertices = vertices,
                    subMeshCount = 1,
                };
                var triangleArray = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int>(keptTriangles.Count);
                for (var i = 0; i < keptTriangles.Count; i++)
                    triangleArray[i] = keptTriangles[i];
                clippedMesh.triangles = triangleArray;
                clippedMesh.RecalculateBounds();
                TemporaryMeshes.Add(clippedMesh);
                meshCollider.sharedMesh = null;
                meshCollider.sharedMesh = clippedMesh;
                meshCollider.convex = false;
            }

            Plugin.LogSource.LogInfo($"Removed {removedCount}/{originalTriangleCount} shallow ground collision triangles from '{collider.name}' within the imported water footprint (kept {keptTriangles.Count / 3}); visible terrain was left intact.");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Could not remove submerged ground collision from '{collider.name}': {ex.Message}");
            return false;
        }
    }

    private static void RememberCollider(Collider collider)
    {
        if (collider == null)
            return;
        var id = collider.GetInstanceID();
        if (!ColliderSnapshots.ContainsKey(id))
            ColliderSnapshots[id] = new ColliderSnapshot(collider);
    }

    // Keep the terrain collision and layout sampler aligned with the actual water plane.
    // A multi-metre margin classified the island itself as submerged and pushed layouts
    // onto distant decorative mountain colliders.
    private const float WaterGroundClearance = 0.25f;
    private const float DefaultWaterLandingClearanceMeters = 4f;
    private const float LargeDiscPositionJumpThresholdMeters = 5f;
    private const int MaxPositionJumpLogsPerThrow = 8;
    private const float MinimumSupportNormalY = 0.25f;
    private const float GroundDistanceRayStartMeters = 0.05f;
    private const float GroundDistanceRayLengthMeters = 2000f;
    private const float NoImportedGroundDistanceMeters = 1000f;
    private const float RecentCompletedThrowCollisionWindowSeconds = 1f;

    internal static bool ContainsXZ(Bounds bounds, Vector3 point) =>
        point.x >= bounds.min.x && point.x <= bounds.max.x && point.z >= bounds.min.z && point.z <= bounds.max.z;
}
