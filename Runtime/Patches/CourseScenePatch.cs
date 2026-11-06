using Dgm;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DgmCustomCourses;

[HarmonyPatch(typeof(CourseIntroduction), nameof(CourseIntroduction.LoadIngameScene))]
internal static class ImportedMapPreloadPatch
{
    private static void Prefix(CourseIntroduction __instance)
    {
        try
        {
            var layout = __instance == null ? null : __instance.courseLayout;
            var custom = layout == null ? null : Plugin.Registry.FindByCourseId(layout.courseID);
            Plugin.LogSource.LogInfo($"Course introduction scene hook: course={(layout == null ? "<null>" : layout.courseID.ToString())}, layout={(layout == null ? "<null>" : layout.layoutID)}, importedMap={(custom?.Definition.MapScene != null)}.");
            if (custom?.Definition.MapScene != null)
                MapSceneRuntime.BeginPreload(custom);
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogError($"Could not start imported map preload: {ex}");
        }
    }
}

[HarmonyPatch(typeof(RoundManager), nameof(RoundManager.SceneStarted), new[] { typeof(PlayerBehaviour), typeof(CourseManager), typeof(int) })]
internal static class CourseScenePatch
{
    private static void Postfix(RoundManager __instance, PlayerBehaviour playerBehaviour, CourseManager courseManager, int startHoleNr)
    {
        var layout = __instance == null ? null : __instance.GetCurrentCourseLayout();
        var custom = layout == null ? null : Plugin.Registry.FindByCourseId(layout.courseID);
        Plugin.LogSource.LogInfo($"Round scene hook: manager={(__instance == null ? "<null>" : __instance.GetType().Name)}, course={(layout == null ? "<null>" : layout.courseID.ToString())}, layout={(layout == null ? "<null>" : layout.layoutID)}, custom={(custom?.Definition.Id ?? "<none>")}.");

        if (custom?.Definition.MapScene != null)
        {
            CourseScenePatcher.RestoreSnapshots();
            MapSceneRuntime.SetRoundContext(courseManager, playerBehaviour, startHoleNr, custom);
            return;
        }

        if (__instance is not OfflineRoundManager)
        {
            Plugin.LogSource.LogInfo($"Round scene hook saw a non-offline round ({(__instance == null ? "<null>" : __instance.GetType().Name)}); non-map custom course changes were skipped.");
            CourseScenePatcher.RestoreAll();
            return;
        }

        if (custom == null)
        {
            CourseScenePatcher.RestoreAll();
            return;
        }

        CourseScenePatcher.Apply(courseManager, playerBehaviour, startHoleNr, custom);
    }
}

[HarmonyPatch(typeof(OutOfBoundsManager), nameof(OutOfBoundsManager.IsPointInsidePlayArea))]
internal static class ImportedMapPlayAreaPatch
{
    private static void Postfix(Vector2 __0, ref bool __result)
    {
        if (CourseScenePatcher.TryGetImportedPlayAreaResult(__0, out var inside))
            __result = inside;
    }
}

[HarmonyPatch(typeof(OutOfBoundsManager), nameof(OutOfBoundsManager.IsInOBAreaCheck))]
internal static class ImportedMapOutOfBoundsPatch
{
    private static void Postfix(Vector3 __0, ref OBArea __result)
    {
        if (!CourseScenePatcher.IsImportedMapActive)
            return;

        if (ImportedMapPhysicsSupport.TryGetWaterObArea(__0, out var waterArea))
        {
            __result = waterArea;
            return;
        }

        if (__result == null)
            return;

        var areaName = __result.gameObject.name;
        if (areaName.StartsWith("DGM_Custom_OB_Water_", StringComparison.Ordinal) ||
            !areaName.StartsWith("DGM_Custom_OB_H", StringComparison.Ordinal))
            __result = null;
    }
}

[HarmonyPatch(typeof(OBArea), "Start")]
internal static class ImportedMapSyntheticOBAreaStartPatch
{
    private static bool Prefix(OBArea __instance)
    {
        if (__instance == null)
            return true;

        var name = __instance.gameObject.name;
        // Imported-map polygons are queried directly by OutOfBoundsManager. They
        // have no line, flag, or drop-zone visuals to initialize; OBArea.Start
        // assumes those game-authored objects exist and throws for our polygons.
        return !name.StartsWith("DGM_Custom_OB_", StringComparison.Ordinal) &&
               !name.EndsWith("_PlayableBounds", StringComparison.Ordinal);
    }
}

[HarmonyPatch(typeof(DiscBehaviour), nameof(DiscBehaviour.FixedUpdate))]
internal static class ImportedMapWaterCheckPatch
{
    private static void Prefix(DiscBehaviour __instance)
    {
        ImportedMapPhysicsSupport.UpdateImportedGroundState(__instance);
        ImportedMapPhysicsSupport.CaptureDiscPhysicsStepStart(__instance);
    }
    private static void Postfix(DiscBehaviour __instance)
    {
        ImportedMapPhysicsSupport.UpdateImportedGroundState(__instance);
        ImportedMapPhysicsSupport.HandleDiscPhysicsStep(__instance);
    }
}

[HarmonyPatch(typeof(DiscCollision), "OnCollisionEnter")]
internal static class ImportedMapDiscCollisionDiagnosticPatch
{
    private static void Prefix(DiscCollision __instance, Collision __0)
    {
        ImportedMapPhysicsSupport.CaptureDiscCollisionStart(__instance, __0);
        ImportedMapPhysicsSupport.PrepareImportedGroundContact(__instance, __0);
    }
    private static void Postfix(DiscCollision __instance, Collision __0) => ImportedMapPhysicsSupport.LogImportedDiscCollision(__instance, __0);
}

[HarmonyPatch(typeof(DiscCollision), "OnCollisionStay")]
internal static class ImportedMapDiscCollisionStayDiagnosticPatch
{
    private static void Prefix(DiscCollision __instance, Collision __0)
    {
        ImportedMapPhysicsSupport.CaptureDiscCollisionStart(__instance, __0);
        ImportedMapPhysicsSupport.PrepareImportedGroundContact(__instance, __0);
    }
    private static void Postfix(DiscCollision __instance, Collision __0) => ImportedMapPhysicsSupport.LogImportedDiscCollision(__instance, __0);
}

[HarmonyPatch(typeof(PlayerBehaviour), nameof(PlayerBehaviour.OnThrowComplete))]
internal static class ShotLimitPatch
{
    private static readonly HashSet<int> CompletedByLimit = new();

    private static void Postfix(PlayerBehaviour __instance)
    {
        try
        {
            var round = RoundManager.currentRound;
            var currentHole = round == null ? null : round.GetCurrentHole();
            if (currentHole == null)
                return;

            var layout = round.GetCurrentCourseLayout();
            if (layout == null)
                return;
            var custom = Plugin.Registry.FindByCourseId(layout.courseID);
            if (custom == null)
                return;

            Plugin.LogSource.LogInfo($"Imported-map player throw-complete callback: course='{custom.Definition.Name}', hole={currentHole.HoleNr}, throwCount={__instance.GetCurrentThrowCount(false, 0)}.");

            if (custom.Definition.MapScene != null && custom.Definition.MapLighting?.RealtimeMapShadows == true)
                ImportedShadowDiagnostics.LogAfterRenderOnce();

            var index = currentHole.HoleNr - 1;
            if (index < 0 || index >= custom.Definition.Holes.Count)
                return;

            var limit = custom.Definition.Holes[index].ShotLimit;
            if (limit <= 0 || __instance.GetCurrentThrowCount(false, 0) < limit)
                return;

            var holeKey = currentHole.GetInstanceID();
            if (!CompletedByLimit.Add(holeKey))
                return;

            var scores = round.GetCurrentHoleAllPlayersScores();
            if (scores != null && scores.Length > 0 && scores[0] > 0)
                return;

            Plugin.LogSource.LogInfo($"Shot limit ({limit}) reached on hole {index + 1}; completing the hole.");
            round.HoleCompleted();
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Could not apply a custom shot limit: {ex.Message}");
        }
    }
}

internal static partial class CourseScenePatcher
{
    private static readonly Dictionary<int, TransformSnapshot> TransformSnapshots = new();
    private static readonly Dictionary<int, int> BasketParSnapshots = new();
    private static readonly Dictionary<int, HoleVariantSnapshot> HoleSnapshots = new();
    private static readonly Dictionary<int, RendererSnapshot> RendererSnapshots = new();
    private static readonly Dictionary<int, RendererLocalBoundsSnapshot> RendererLocalBoundsSnapshots = new();
    private static readonly Dictionary<int, RendererLightmapSnapshot> RendererLightmapSnapshots = new();
    private static readonly Dictionary<int, CameraOcclusionSnapshot> CameraOcclusionSnapshots = new();
    private static readonly Dictionary<int, ShadowRendererSnapshot> ShadowRendererSnapshots = new();
    private static readonly Dictionary<int, ShadowLightSnapshot> ShadowLightSnapshots = new();
    private static readonly Dictionary<int, BaseCourseLightSnapshot> BaseCourseLightSnapshots = new();
    private static readonly Dictionary<int, TerrainShadowSnapshot> TerrainShadowSnapshots = new();
    private static readonly Dictionary<int, TerrainLightmapSnapshot> TerrainLightmapSnapshots = new();
    private static bool HasSunSnapshot;
    private static Light OriginalSun;
    private static readonly Dictionary<int, OutOfBoundsManagerSnapshot> OutOfBoundsSnapshots = new();
    private static readonly Dictionary<int, ColliderSnapshot> ColliderSnapshots = new();
    private static readonly Dictionary<int, TerrainSnapshot> TerrainSnapshots = new();
    private static readonly Dictionary<int, TerrainMaterialSnapshot> TerrainMaterialSnapshots = new();
    private static readonly Dictionary<int, TerrainDetailSnapshot> TerrainDetailSnapshots = new();
    private static readonly Dictionary<int, TerrainTreeSnapshot> TerrainTreeSnapshots = new();
    private static readonly Dictionary<int, TreeLodSnapshot> TreeLodSnapshots = new();
    private static readonly Dictionary<int, GameObjectActiveSnapshot> GameObjectSnapshots = new();
    private static readonly List<GameObject> TemporaryObjects = new();
    private static readonly List<Texture2D> TemporaryTextures = new();
    private static readonly Dictionary<string, Texture2D> TeeCardTextures = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<Material> TemporaryMaterials = new();
    private static readonly List<Mesh> TemporaryMeshes = new();
    private static bool LayoutClearanceUnavailable;
    private static bool LayoutClearanceWarningLogged;
    private static bool LayoutRayDiagnosticsEnabled;
    private static int LayoutRayCount;
    private static int LayoutRayHitCount;
    private static int LayoutRayMapHitCount;
    private static int LayoutRayOtherSceneHitCount;
    private static int LayoutRayWaterHitCount;
    private static int LayoutRayTriggerHitCount;
    private static int LayoutRaySlopeHitCount;
    private static int LayoutRayNamedHitCount;
    private static int LayoutRayWideHitCount;
    private static int LayoutRayHeightHitCount;
    private static int LayoutRayAcceptedCount;
    private static readonly List<string> LayoutRayExamples = new();
    private static bool HasImportedPlayBounds;
    private static float ImportedPlayMinX;
    private static float ImportedPlayMaxX;
    private static float ImportedPlayMinZ;
    private static float ImportedPlayMaxZ;
    private static bool HasRenderSettingsSnapshot;
    private static bool HasSkyboxSnapshot;
    private static Material OriginalSkybox;
    private static AmbientMode OriginalAmbientMode;
    private static Color OriginalAmbientColor;
    private static float OriginalAmbientIntensity;

    public static void Apply(CourseManager courseManager, PlayerBehaviour player, int startHoleIndex, CustomCourseRecord custom)
    {
        RestoreAll();
        if (courseManager == null || courseManager.holeList == null)
            return;

        var count = Math.Min(courseManager.holeList.Length, custom.Definition.Holes.Count);
        for (var i = 0; i < count; i++)
        {
            var holeDefinition = custom.Definition.Holes[i];
            var holeSettings = courseManager.holeList[i];
            if (holeSettings == null)
                continue;

            var pinVariant = CourseRegistry.ParseVariant(holeDefinition.PinVariant);
            var teeVariant = CourseRegistry.ParseVariant(holeDefinition.TeeVariant);
            RememberHole(holeSettings);

            var tee = holeSettings.GetTeepad(teeVariant) ?? holeSettings.ActiveTeepad;
            var basket = holeSettings.GetBasket(pinVariant) ?? holeSettings.ActiveBasket;
            if (tee == null || basket == null)
            {
                Plugin.LogSource.LogWarning($"'{custom.Definition.Name}' hole {i + 1}: the selected tee or basket variant is missing from the base course scene.");
                continue;
            }

            RememberTransform(tee.transform);
            RememberTransform(basket.transform);
            RememberBasketPar(basket);

            var teeStart = TransformSnapshots[tee.transform.GetInstanceID()];
            var basketStart = TransformSnapshots[basket.transform.GetInstanceID()];
            var right = teeStart.Rotation * Vector3.right;
            var forward = teeStart.Rotation * Vector3.forward;
            var teeOffset = ToWorldOffset(holeDefinition.TeeOffset, right, forward);
            var basketOffset = ToWorldOffset(holeDefinition.BasketOffset, right, forward);

            tee.transform.SetPositionAndRotation(
                teeStart.Position + teeOffset,
                Quaternion.AngleAxis(holeDefinition.TeeYawDegrees, Vector3.up) * teeStart.Rotation);
            basket.transform.SetPositionAndRotation(
                basketStart.Position + basketOffset,
                Quaternion.AngleAxis(holeDefinition.BasketYawDegrees, Vector3.up) * basketStart.Rotation);
            WakeMovedBasketChains(basket);

            SetVisible(tee.gameObject, holeDefinition.TeeEnabled);
            SetVisible(basket.gameObject, holeDefinition.BasketEnabled);
            holeSettings.activePinVariant = pinVariant;
            holeSettings.activeTeeVariant = teeVariant;
            basket.par = holeDefinition.Par;

            AddObZones(holeSettings, teeStart, holeDefinition.ObZones);
            if (holeDefinition.TeeEnabled && !string.IsNullOrWhiteSpace(holeDefinition.CardImage))
                AddTeeCard(custom.Definition, holeDefinition.CardImage, tee.transform, right, forward);
        }

        Physics.SyncTransforms();
        var currentHole = GetCurrentHole(courseManager, startHoleIndex, custom.Definition.Holes.Count);
        if (currentHole != null && player != null)
        {
            var index = Math.Clamp(currentHole.HoleNr - 1, 0, custom.Definition.Holes.Count - 1);
            var variant = CourseRegistry.ParseVariant(custom.Definition.Holes[index].TeeVariant);
            var tee = currentHole.GetTeepad(variant) ?? currentHole.ActiveTeepad;
            if (tee != null)
                player.transform.SetPositionAndRotation(tee.GetPositionFromTeepad(), tee.GetRotationFromTeepad());
        }

        CourseSceneCardCapture.Schedule(custom, currentHole);
        Plugin.LogSource.LogInfo($"Applied '{custom.Definition.Name}' to {count} hole(s) in the offline round.");
    }

    public static void RestoreAll()
    {
        RestoreSnapshots();
        MapSceneRuntime.Cleanup();
    }

    public static void RestoreSnapshots()
    {
        CourseSceneCardCapture.Cancel();
        ImportedAudioSurfacePatch.Reset();
        ImportedFoliageInteraction.Reset();
        ImportedMapPhysicsSupport.Restore();
        ImportedShadowDiagnostics.ResetRound();
        ImportedTreeCollisionProxies.Restore();
        HasImportedPlayBounds = false;
        foreach (var snapshot in TransformSnapshots.Values)
        {
            if (snapshot.Transform != null)
                snapshot.Transform.SetPositionAndRotation(snapshot.Position, snapshot.Rotation);
        }
        TransformSnapshots.Clear();

        foreach (var snapshot in BasketParSnapshots)
        {
            var basket = FindBasket(snapshot.Key);
            if (basket != null)
                basket.par = snapshot.Value;
        }
        BasketParSnapshots.Clear();

        foreach (var snapshot in HoleSnapshots.Values)
        {
            if (snapshot.Hole != null)
            {
                snapshot.Hole.activePinVariant = snapshot.PinVariant;
                snapshot.Hole.activeTeeVariant = snapshot.TeeVariant;
            }
        }
        HoleSnapshots.Clear();

        foreach (var snapshot in TreeLodSnapshots.Values)
        {
            if (snapshot.Group == null)
                continue;
            try { snapshot.Group.SetLODs(snapshot.Lods); snapshot.Group.lastLODBillboard = snapshot.LastLodBillboard; snapshot.Group.fadeMode = snapshot.FadeMode; snapshot.Group.animateCrossFading = snapshot.AnimateCrossFading; }
            catch (Exception ex) { Plugin.LogSource.LogWarning($"Could not restore tree LODs: {ex.Message}"); }
        }
        TreeLodSnapshots.Clear();

        foreach (var snapshot in ShadowRendererSnapshots.Values)
        {
            if (snapshot.Renderer == null) continue;
            try { snapshot.Renderer.shadowCastingMode = snapshot.CastingMode; snapshot.Renderer.receiveShadows = snapshot.ReceiveShadows; }
            catch (Exception ex) { Plugin.LogSource.LogWarning($"Could not restore imported renderer shadows: {ex.Message}"); }
        }
        ShadowRendererSnapshots.Clear();
        foreach (var snapshot in ShadowLightSnapshots.Values)
        {
            if (snapshot.Light == null) continue;
            try { snapshot.Light.enabled = snapshot.Enabled; snapshot.Light.intensity = snapshot.Intensity; snapshot.Light.shadows = snapshot.Shadows; snapshot.Light.shadowStrength = snapshot.ShadowStrength; if (snapshot.HasBakingOutput) snapshot.Light.bakingOutput = snapshot.BakingOutput; }
            catch (Exception ex) { Plugin.LogSource.LogWarning($"Could not restore imported light shadows: {ex.Message}"); }
        }
        ShadowLightSnapshots.Clear();
        foreach (var snapshot in BaseCourseLightSnapshots.Values)
        {
            if (snapshot.Light == null) continue;
            try { snapshot.Light.enabled = snapshot.Enabled; }
            catch (Exception ex) { Plugin.LogSource.LogWarning($"Could not restore base-course light '{snapshot.Light.name}': {ex.Message}"); }
        }
        BaseCourseLightSnapshots.Clear();
        foreach (var snapshot in TerrainShadowSnapshots.Values)
        {
            if (snapshot.Terrain == null) continue;
            try { snapshot.Terrain.shadowCastingMode = snapshot.CastingMode; }
            catch (Exception ex) { Plugin.LogSource.LogWarning($"Could not restore terrain shadow settings: {ex.Message}"); }
        }
        TerrainShadowSnapshots.Clear();
        foreach (var snapshot in TerrainLightmapSnapshots.Values)
        {
            if (snapshot.Terrain == null) continue;
            try { snapshot.Terrain.lightmapIndex = snapshot.LightmapIndex; snapshot.Terrain.realtimeLightmapIndex = snapshot.RealtimeLightmapIndex; }
            catch (Exception ex) { Plugin.LogSource.LogWarning($"Could not restore terrain lightmap settings: {ex.Message}"); }
        }
        TerrainLightmapSnapshots.Clear();
        foreach (var snapshot in RendererLightmapSnapshots.Values)
        {
            if (snapshot.Renderer == null) continue;
            try { snapshot.Renderer.lightmapIndex = snapshot.LightmapIndex; snapshot.Renderer.realtimeLightmapIndex = snapshot.RealtimeLightmapIndex; }
            catch (Exception ex) { Plugin.LogSource.LogWarning($"Could not restore imported renderer lightmaps: {ex.Message}"); }
        }
        RendererLightmapSnapshots.Clear();
        foreach (var snapshot in CameraOcclusionSnapshots.Values)
        {
            if (snapshot.Camera == null) continue;
            try { snapshot.Camera.useOcclusionCulling = snapshot.UseOcclusionCulling; }
            catch (Exception ex) { Plugin.LogSource.LogWarning($"Could not restore camera occlusion culling: {ex.Message}"); }
        }
        CameraOcclusionSnapshots.Clear();
        _nextCameraOcclusionRefreshAt = 0f;
        _cameraOcclusionWarningLogged = false;
        if (HasSunSnapshot)
        {
            try { RenderSettings.sun = OriginalSun; }
            catch (Exception ex) { Plugin.LogSource.LogWarning($"Could not restore the original sun: {ex.Message}"); }
            OriginalSun = null;
            HasSunSnapshot = false;
        }

        foreach (var snapshot in RendererLocalBoundsSnapshots.Values)
        {
            if (snapshot.Renderer == null) continue;
            try { snapshot.Renderer.localBounds = snapshot.LocalBounds; }
            catch (Exception ex) { Plugin.LogSource.LogWarning($"Could not restore imported renderer bounds: {ex.Message}"); }
        }
        RendererLocalBoundsSnapshots.Clear();

        foreach (var snapshot in RendererSnapshots.Values)
            if (snapshot.Renderer != null)
                snapshot.Renderer.enabled = snapshot.Enabled;
        RendererSnapshots.Clear();

        foreach (var snapshot in OutOfBoundsSnapshots.Values)
        {
            if (snapshot.Manager == null)
                continue;
            snapshot.Manager.OBAreas = snapshot.OriginalAreas;
            snapshot.Manager.normalPlayArea = snapshot.OriginalNormalPlayArea;
            snapshot.Manager.overridePlayArea = snapshot.OriginalOverridePlayArea;
        }
        OutOfBoundsSnapshots.Clear();

        foreach (var snapshot in ColliderSnapshots.Values)
            if (snapshot.Collider != null)
                snapshot.Collider.enabled = snapshot.Enabled;
        ColliderSnapshots.Clear();

        foreach (var snapshot in TerrainSnapshots.Values)
            if (snapshot.Terrain != null)
                snapshot.Terrain.enabled = snapshot.Enabled;
        TerrainSnapshots.Clear();
        ImportedTerrainIsolation.Restore();

        foreach (var snapshot in TerrainMaterialSnapshots.Values)
            if (snapshot.Terrain != null)
                snapshot.Terrain.materialTemplate = snapshot.MaterialTemplate;
        TerrainMaterialSnapshots.Clear();

        foreach (var snapshot in TerrainTreeSnapshots.Values)
        {
            if (snapshot.Terrain == null)
                continue;
            try { snapshot.Terrain.treeBillboardDistance = snapshot.BillboardDistance; snapshot.Terrain.treeMaximumFullLODCount = snapshot.MaximumFullLodCount; snapshot.Terrain.treeCrossFadeLength = snapshot.CrossFadeLength; }
            catch (Exception ex) { Plugin.LogSource.LogWarning($"Could not restore terrain tree settings: {ex.Message}"); }
        }
        TerrainTreeSnapshots.Clear();

        foreach (var snapshot in TerrainDetailSnapshots.Values)
        {
            if (snapshot.Data == null)
                continue;
            try { snapshot.Data.detailPrototypes = snapshot.Prototypes; snapshot.Data.RefreshPrototypes(); }
            catch (Exception ex) { Plugin.LogSource.LogWarning($"Could not restore terrain details: {ex.Message}"); }
        }
        TerrainDetailSnapshots.Clear();

        if (HasSkyboxSnapshot)
        {
            RenderSettings.skybox = OriginalSkybox;
            OriginalSkybox = null;
            HasSkyboxSnapshot = false;
        }

        ImportedLitMaterials.RestoreAll(message => Plugin.LogSource.LogWarning(message));

        foreach (var material in TemporaryMaterials)
            if (material != null)
                UnityEngine.Object.Destroy(material);
        TemporaryMaterials.Clear();

        foreach (var snapshot in GameObjectSnapshots.Values)
            if (snapshot.GameObject != null)
                snapshot.GameObject.SetActive(snapshot.ActiveSelf);
        GameObjectSnapshots.Clear();

        foreach (var gameObject in TemporaryObjects)
            if (gameObject != null)
                UnityEngine.Object.Destroy(gameObject);
        TemporaryObjects.Clear();

        foreach (var mesh in TemporaryMeshes)
            if (mesh != null)
                UnityEngine.Object.Destroy(mesh);
        TemporaryMeshes.Clear();

        foreach (var texture in TemporaryTextures)
            if (texture != null)
                UnityEngine.Object.Destroy(texture);
        TemporaryTextures.Clear();
        TeeCardTextures.Clear();

        if (HasRenderSettingsSnapshot)
        {
            RenderSettings.ambientMode = OriginalAmbientMode;
            RenderSettings.ambientLight = OriginalAmbientColor;
            RenderSettings.ambientIntensity = OriginalAmbientIntensity;
            HasRenderSettingsSnapshot = false;
        }
    }

    internal static bool ApplyImportedMap(CourseManager courseManager, PlayerBehaviour player, int startHoleIndex, CustomCourseRecord custom, Scene mapScene)
    {
        if (courseManager == null || courseManager.holeList == null || !mapScene.isLoaded)
            return false;

        var requiredHoleCount = custom.Definition.Holes.Count;
        if (courseManager.holeList.Length < requiredHoleCount)
        {
            Plugin.LogSource.LogError($"'{custom.Definition.Name}' requires {requiredHoleCount} holes, but DGM only created {courseManager.holeList.Length} hole entries for this round.");
            return false;
        }
        var count = requiredHoleCount;
        if (count == 0)
            return false;

        Physics.SyncTransforms();
        var hasBounds = TryGetSceneBounds(mapScene, out var mapBounds);
        if (hasBounds)
            Plugin.LogSource.LogInfo($"Imported scene content bounds for '{custom.Definition.Name}': X {mapBounds.min.x:F1}..{mapBounds.max.x:F1}, Y {mapBounds.min.y:F1}..{mapBounds.max.y:F1}, Z {mapBounds.min.z:F1}..{mapBounds.max.z:F1}; includes enabled renderers, non-trigger colliders, and terrains after large outlier filtering.");
        var configuredBounds = custom.Definition.MapWorldBounds;
        if (configuredBounds != null)
        {
            var minY = hasBounds ? mapBounds.min.y : configuredBounds.GroundY;
            var maxY = hasBounds ? mapBounds.max.y : configuredBounds.GroundY + 10f;
            var height = Mathf.Max(1f, maxY - minY);
            mapBounds = new Bounds(
                new Vector3((configuredBounds.MinX + configuredBounds.MaxX) * 0.5f, minY + height * 0.5f, (configuredBounds.MinZ + configuredBounds.MaxZ) * 0.5f),
                new Vector3(configuredBounds.MaxX - configuredBounds.MinX, height, configuredBounds.MaxZ - configuredBounds.MinZ));
            hasBounds = true;
        }
        if (!hasBounds && custom.Definition.Holes.All(hole => hole.MapTeePosition != null))
            hasBounds = TryGetConfiguredHoleBounds(custom.Definition, out mapBounds);
        if (!hasBounds && custom.Definition.Holes.Any(hole => hole.MapTeePosition == null))
        {
            Plugin.LogSource.LogError($"'{custom.Definition.Name}' loaded, but no scene geometry bounds were found to place holes.");
            return false;
        }
        if (hasBounds)
            Plugin.LogSource.LogInfo($"Imported map bounds for '{custom.Definition.Name}': X {mapBounds.min.x:F1}..{mapBounds.max.x:F1}, Y {mapBounds.min.y:F1}..{mapBounds.max.y:F1}, Z {mapBounds.min.z:F1}..{mapBounds.max.z:F1}.");

        var baseSkybox = RenderSettings.skybox;
        var mapLighting = custom.Definition.MapLighting ?? new MapLightingDefinition();
        var baseSun = custom.Definition.MapLighting?.PreferBaseCourseSun == true
            ? FindBaseCourseSun(mapScene, custom.Layout?.sceneName) : null;
        if (custom.Layout != null)
            HideBaseCourse(courseManager, custom.Layout.sceneName, count, baseSun, custom.Definition.Name);
        SceneManager.SetActiveScene(mapScene);
        LogImportedCameraOcclusionContext(mapScene, custom.Definition.Name);
        ApplyImportedCameraOcclusionSetting(mapLighting, custom.Definition.Name);
        if (mapLighting.UseBaseSkybox)
        {
            var replacementSkybox = baseSkybox;
            if (replacementSkybox == null)
            {
                var skyShader = Shader.Find("Skybox/Procedural");
                if (skyShader != null && skyShader.isSupported)
                {
                    replacementSkybox = new Material(skyShader) { name = "DGM_Imported_ProceduralSky" };
                    TemporaryMaterials.Add(replacementSkybox);
                }
            }
            if (replacementSkybox != null)
            {
                OriginalSkybox = RenderSettings.skybox;
                HasSkyboxSnapshot = true;
                RenderSettings.skybox = replacementSkybox;
                Plugin.LogSource.LogInfo($"Using skybox '{replacementSkybox.name}' for imported map '{custom.Definition.Name}'.");
            }
            else
                Plugin.LogSource.LogWarning($"No base-course or procedural skybox is available for '{custom.Definition.Name}'.");
        }
        try
        {
            RepairImportedMapRendering(mapScene, custom.Definition.Name, mapLighting, baseSun);
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Could not inspect or repair materials in '{custom.Definition.Name}': {ex.Message}");
        }

        ClearImportedMapOutOfBounds(courseManager, count, custom.Definition.Name, mapScene, mapBounds);

        ImportedMapPhysicsSupport.Prepare(mapScene, custom.Definition.Name, custom.Definition.WaterLandingClearanceMeters);

        List<ImportedCoursePlanner.Hole> autoHoles = null;
        if (string.Equals(custom.Definition.AutoLayout, "long", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                autoHoles = PlanLongImportedHoles(mapScene, mapBounds, custom.Definition.MapWorldBounds, count, custom.LayoutTargetLengths);
                if (autoHoles.Count != count)
                {
                    Plugin.LogSource.LogError($"Could not find enough grounded positions for '{custom.Definition.Name}'. Skipping course placement rather than putting tees or baskets at a scene-bound fallback height.");
                    return false;
                }
            }
            catch (Exception ex) { Plugin.LogSource.LogWarning($"Could not plan the long layout for '{custom.Definition.Name}': {ex.Message}"); }
            if (autoHoles == null)
            {
                Plugin.LogSource.LogError($"Could not build a grounded layout for '{custom.Definition.Name}'. Skipping course placement rather than putting tees or baskets at a scene-bound fallback height.");
                return false;
            }
        }

        for (var i = 0; i < count; i++)
        {
            var holeDefinition = custom.Definition.Holes[i];
            var holeSettings = courseManager.holeList[i];
            if (holeSettings == null)
            {
                Plugin.LogSource.LogError($"'{custom.Definition.Name}' hole {i + 1}: DGM did not create a hole entry; aborting imported-course setup.");
                return false;
            }

            var pinVariant = CourseRegistry.ParseVariant(holeDefinition.PinVariant);
            var teeVariant = CourseRegistry.ParseVariant(holeDefinition.TeeVariant);
            RememberHole(holeSettings);
            var tee = holeSettings.GetTeepad(teeVariant) ?? holeSettings.ActiveTeepad;
            var basket = holeSettings.GetBasket(pinVariant) ?? holeSettings.ActiveBasket;
            if (tee == null || basket == null)
            {
                Plugin.LogSource.LogError($"'{custom.Definition.Name}' hole {i + 1}: no usable tee or basket exists in the base course scene; aborting imported-course setup.");
                return false;
            }

            RememberTransform(tee.transform);
            RememberTransform(basket.transform);
            RememberBasketPar(basket);

            Vector3 teePosition;
            Vector3 basketPosition;
            if (autoHoles != null)
            {
                var planned = autoHoles[i];
                teePosition = new Vector3(planned.Tee.X, planned.Tee.Y, planned.Tee.Z);
                basketPosition = new Vector3(planned.Basket.X, planned.Basket.Y, planned.Basket.Z);
                holeDefinition.LengthMeters = Math.Clamp(Mathf.RoundToInt(planned.Distance), 1, 2000);
                holeDefinition.Par = ImportedCoursePlanner.ParFor(planned.Distance, i);
            }
            else if (holeDefinition.MapTeePosition != null && holeDefinition.MapBasketPosition != null)
            {
                teePosition = new Vector3(holeDefinition.MapTeePosition.X, holeDefinition.MapTeePosition.Y, holeDefinition.MapTeePosition.Z);
                basketPosition = new Vector3(holeDefinition.MapBasketPosition.X, holeDefinition.MapBasketPosition.Y, holeDefinition.MapBasketPosition.Z);
            }
            else if (!TryPlaceGridHole(mapScene, mapBounds, custom.Definition.MapWorldBounds?.GroundY ?? mapBounds.min.y, i, out teePosition, out basketPosition))
            {
                Plugin.LogSource.LogError($"'{custom.Definition.Name}' hole {i + 1}: no grounded tee/basket pair was found; aborting setup rather than placing objects at the map-bound fallback height.");
                return false;
            }

            var direction = basketPosition - teePosition;
            direction.y = 0f;
            var aimRotation = direction.sqrMagnitude > 0.01f
                ? Quaternion.LookRotation(direction.normalized, Vector3.up)
                : tee.transform.rotation;
            aimRotation = Quaternion.AngleAxis(holeDefinition.TeeYawDegrees, Vector3.up) * aimRotation;
            // The game uses the tee's child aim transform, which may have its
            // own rotation relative to the visible teepad.
            var localAimRotation = tee.aim == null ? Quaternion.identity : Quaternion.Inverse(tee.transform.rotation) * tee.aim.rotation;
            var teeRotation = aimRotation * Quaternion.Inverse(localAimRotation);
            var basketRotation = Quaternion.Euler(0f, holeDefinition.BasketYawDegrees, 0f);

            tee.transform.SetPositionAndRotation(teePosition, teeRotation);
            basket.transform.SetPositionAndRotation(basketPosition, basketRotation);
            if (autoHoles != null)
            {
                try
                {
                    var frontMiddle = tee.GetFrontMiddlePosition();
                    var basketPoint = basket.transform.position;
                    var measuredDistance = Vector3.Distance(frontMiddle, basketPoint);
                    if (float.IsFinite(measuredDistance) && measuredDistance >= 1f && measuredDistance <= 2000f)
                    {
                        holeDefinition.LengthMeters = Mathf.RoundToInt(measuredDistance);
                        holeDefinition.Par = ImportedCoursePlanner.ParFor(measuredDistance, i);
                        Plugin.LogSource.LogInfo($"Long imported hole {i + 1} game distance: teeFrontMiddle=({frontMiddle.x:F1},{frontMiddle.y:F1},{frontMiddle.z:F1}), basket=({basketPoint.x:F1},{basketPoint.y:F1},{basketPoint.z:F1}), measured={measuredDistance:F1}m.");
                    }
                    else
                        Plugin.LogSource.LogWarning($"Long imported hole {i + 1} returned an invalid game distance ({measuredDistance:F1}m); keeping the planned length.");
                }
                catch (Exception ex)
                {
                    Plugin.LogSource.LogWarning($"Could not measure the game distance for long imported hole {i + 1}; keeping the planned length. {ex.Message}");
                }
            }
            WakeMovedBasketChains(basket);
            SetVisible(tee.gameObject, holeDefinition.TeeEnabled);
            SetVisible(basket.gameObject, holeDefinition.BasketEnabled);
            holeSettings.activePinVariant = pinVariant;
            holeSettings.activeTeeVariant = teeVariant;
            basket.par = holeDefinition.Par;
            Plugin.LogSource.LogInfo($"Imported hole {i + 1}: tee=({teePosition.x:F1},{teePosition.y:F1},{teePosition.z:F1}), basket=({basketPosition.x:F1},{basketPosition.y:F1},{basketPosition.z:F1}), par={holeDefinition.Par}, nominalLength={holeDefinition.LengthMeters}m, OB zones={holeDefinition.ObZones.Count}.");

            var right = aimRotation * Vector3.right;
            var forward = aimRotation * Vector3.forward;
            var teeStart = new TransformSnapshot(tee.transform, teePosition, aimRotation);
            AddObZones(holeSettings, teeStart, holeDefinition.ObZones);
            var signImage = string.IsNullOrWhiteSpace(holeDefinition.CardImage)
                ? custom.Definition.CourseCardImage : holeDefinition.CardImage;
            if (holeDefinition.TeeEnabled && !string.IsNullOrWhiteSpace(signImage) &&
                AddImportedTeeSign(custom.Definition, signImage, tee.transform, right, forward))
            {
                // The stock sign belongs to the base course and can be left
                // floating after its supporting scene geometry is hidden.
                try
                {
                    var oldSign = tee.teesign;
                    if (oldSign != null)
                    {
                        var id = oldSign.GetInstanceID();
                        if (!RendererSnapshots.ContainsKey(id)) RendererSnapshots[id] = new RendererSnapshot(oldSign, oldSign.enabled);
                        oldSign.enabled = false;
                    }
                }
                catch (Exception ex) { Plugin.LogSource.LogWarning($"Could not hide the base-course tee sign: {ex.Message}"); }
            }
        }

        if (autoHoles != null)
            Plugin.Registry.RefreshAutoLayoutMetadata(custom);

        Physics.SyncTransforms();
        var currentHole = GetCurrentHole(courseManager, startHoleIndex, custom.Definition.Holes.Count);
        if (currentHole != null && player != null)
        {
            var index = Math.Clamp(currentHole.HoleNr - 1, 0, custom.Definition.Holes.Count - 1);
            var variant = CourseRegistry.ParseVariant(custom.Definition.Holes[index].TeeVariant);
            var tee = currentHole.GetTeepad(variant) ?? currentHole.ActiveTeepad;
            if (tee != null)
                player.transform.SetPositionAndRotation(tee.GetPositionFromTeepad(), tee.GetRotationFromTeepad());
        }

        if (currentHole != null)
        {
            try
            {
                var holeInfo = UnityEngine.Object.FindObjectOfType<HoleInformationUI>();
                if (holeInfo != null)
                {
                    holeInfo.SetupUI();
                    holeInfo.RefreshDistanceUI(currentHole);
                    var parText = holeInfo.GetType().GetProperty("parText")?.GetValue(holeInfo);
                    var parTextProperty = parText?.GetType().GetProperty("text");
                    parTextProperty?.SetValue(parText, $"PAR {currentHole.HolePar}");
                    var displayedPar = parTextProperty?.GetValue(parText)?.ToString() ?? "<missing>";
                    Plugin.LogSource.LogInfo($"Refreshed imported-hole HUD: hole={currentHole.HoleNr}, par={currentHole.HolePar}, basketPar={(currentHole.ActiveBasket == null ? -1 : currentHole.ActiveBasket.par)}, gameDistance={currentHole.Distance}m, displayedPar='{displayedPar}'.");
                }
                else
                    Plugin.LogSource.LogWarning($"Could not find the in-game hole-information UI to refresh imported hole {currentHole.HoleNr}.");
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"Could not refresh the imported-hole HUD: {ex.Message}");
            }
        }

        CourseSceneCardCapture.Schedule(custom, currentHole);
        Plugin.LogSource.LogInfo($"Loaded '{custom.Definition.Name}' and placed its DGM holes on the imported map scene.");
        CourseMapPreviewExporter.CaptureIfMissing(mapScene, mapBounds, custom.Definition.Id);
        return true;
    }

    internal static bool IsImportedMapActive => HasImportedPlayBounds;

    internal static bool TryGetImportedPlayAreaResult(Vector2 point, out bool inside)
    {
        if (!HasImportedPlayBounds)
        {
            inside = false;
            return false;
        }

        inside = point.x >= ImportedPlayMinX && point.x <= ImportedPlayMaxX &&
                 point.y >= ImportedPlayMinZ && point.y <= ImportedPlayMaxZ;
        return true;
    }

    private static void SetImportedPlayBounds(Bounds bounds)
    {
        var margin = GetPlayableBoundaryMargin(bounds);
        ImportedPlayMinX = bounds.min.x - margin;
        ImportedPlayMaxX = bounds.max.x + margin;
        ImportedPlayMinZ = bounds.min.z - margin;
        ImportedPlayMaxZ = bounds.max.z + margin;
        HasImportedPlayBounds = true;
    }

    private static bool TryGetConfiguredHoleBounds(CourseDefinition course, out Bounds bounds)
    {
        var positions = new List<Vector3>();
        foreach (var hole in course.Holes)
        {
            if (hole.MapTeePosition != null)
                positions.Add(new Vector3(hole.MapTeePosition.X, hole.MapTeePosition.Y, hole.MapTeePosition.Z));
            if (hole.MapBasketPosition != null)
                positions.Add(new Vector3(hole.MapBasketPosition.X, hole.MapBasketPosition.Y, hole.MapBasketPosition.Z));
        }
        if (positions.Count == 0)
        {
            bounds = default;
            return false;
        }

        var min = positions[0];
        var max = positions[0];
        foreach (var position in positions.Skip(1))
        {
            min = Vector3.Min(min, position);
            max = Vector3.Max(max, position);
        }
        var size = max - min;
        size.x = Mathf.Max(size.x, 20f);
        size.y = Mathf.Max(size.y, 10f);
        size.z = Mathf.Max(size.z, 20f);
        bounds = new Bounds((min + max) * 0.5f, size);
        return true;
    }

    private static void ClearImportedMapOutOfBounds(CourseManager courseManager, int holeCount, string courseName, Scene mapScene, Bounds mapBounds)
    {
        SetImportedPlayBounds(mapBounds);
        var playableBoundsArea = CreatePlayableMapBoundsArea(mapScene, mapBounds, courseName);
        var cleared = new HashSet<int>();
        for (var i = 0; i < holeCount; i++)
        {
            var hole = courseManager.holeList[i];
            if (hole == null)
                continue;

            hole.SetThisHoleOBManagerAsActive();
            var manager = OutOfBoundsManager.activeOBManager ?? UnityEngine.Object.FindObjectOfType<OutOfBoundsManager>();
            if (manager == null || !cleared.Add(manager.GetInstanceID()))
                continue;

            RememberOutOfBoundsManager(manager);
            if (manager.OBAreasHolder != null && playableBoundsArea.transform.parent != manager.OBAreasHolder)
                playableBoundsArea.transform.SetParent(manager.OBAreasHolder, false);
            manager.normalPlayArea = playableBoundsArea;
            manager.overridePlayArea = null;
            manager.OBAreas = new Il2CppReferenceArray<OBArea>(0);
        }

        Plugin.LogSource.LogInfo($"Set the imported map boundary as normalPlayArea for '{courseName}' across {cleared.Count} manager(s), cleared inherited OB areas, and enabled map-bounds fallback checks; editor-defined OB zones will be applied per hole.");
    }

    internal static void RegisterImportedWaterObArea(OBArea area)
    {
        if (area == null)
            return;

        var manager = OutOfBoundsManager.activeOBManager ?? UnityEngine.Object.FindObjectOfType<OutOfBoundsManager>();
        if (manager == null)
            return;

        RememberOutOfBoundsManager(manager);
        if (manager.OBAreasHolder != null && area.transform.parent != manager.OBAreasHolder)
            area.transform.SetParent(manager.OBAreasHolder, false);

        var baseAreas = manager.OBAreas ?? new Il2CppReferenceArray<OBArea>(0);
        for (var i = 0; i < baseAreas.Length; i++)
            if (baseAreas[i] == area)
                return;

        var areas = new Il2CppReferenceArray<OBArea>(baseAreas.Length + 1);
        for (var i = 0; i < baseAreas.Length; i++)
            areas[i] = baseAreas[i];
        areas[baseAreas.Length] = area;
        manager.OBAreas = areas;
    }

    private static OBArea CreatePlayableMapBoundsArea(Scene mapScene, Bounds bounds, string courseName)
    {
        var margin = GetPlayableBoundaryMargin(bounds);
        var areaObject = new GameObject($"DGM_{courseName}_PlayableBounds");
        SceneManager.MoveGameObjectToScene(areaObject, mapScene);
        var area = areaObject.AddComponent<OBArea>();
        area.insideIsOB = false;
        area.hideLineAndFlagpoles = true;
        area.bounds = new Il2CppStructArray<Vector2>(4);
        area.bounds[0] = new Vector2(bounds.min.x - margin, bounds.min.z - margin);
        area.bounds[1] = new Vector2(bounds.min.x - margin, bounds.max.z + margin);
        area.bounds[2] = new Vector2(bounds.max.x + margin, bounds.max.z + margin);
        area.bounds[3] = new Vector2(bounds.max.x + margin, bounds.min.z - margin);
        TemporaryObjects.Add(areaObject);
        Plugin.LogSource.LogInfo($"Created playable OB boundary for '{courseName}': X {bounds.min.x - margin:F1}..{bounds.max.x + margin:F1}, Z {bounds.min.z - margin:F1}..{bounds.max.z + margin:F1}, interior marked in-bounds.");
        return area;
    }

    private static float GetPlayableBoundaryMargin(Bounds bounds) => Mathf.Clamp(Mathf.Max(bounds.size.x, bounds.size.z) * 0.05f, 10f, 40f);

    private static void HideBaseCourse(CourseManager courseManager, string sceneName, int activeHoleCount, Light retainedBaseSun, string courseName)
    {
        var scene = default(Scene);
        var holesInCourse = courseManager.holeList;
        for (var i = 0; i < holesInCourse.Length; i++)
        {
            var hole = holesInCourse[i];
            if (hole != null && hole.gameObject.scene.isLoaded)
            {
                scene = hole.gameObject.scene;
                break;
            }
        }
        if (!scene.isLoaded)
        {
            Plugin.LogSource.LogWarning($"Base DGM course scene '{sceneName}' wasn't loaded; leaving its environment visible.");
            return;
        }

        var protectedIds = new HashSet<int>();
        protectedIds.Add(courseManager.transform.GetInstanceID());
        var holes = courseManager.holeList;
        for (var i = 0; i < holes.Length; i++)
        {
            var hole = holes[i];
            if (hole == null)
                continue;
            if (i >= activeHoleCount)
            {
                RememberGameObject(hole.gameObject);
                hole.gameObject.SetActive(false);
                continue;
            }
            AddProtectedSubtree(hole.transform, protectedIds);
        }

        var roots = scene.GetRootGameObjects();
        var baseLightCount = 0;
        var disabledBaseLightCount = 0;
        var retainedBaseSunCount = 0;
        var disabledLightTypes = new Dictionary<LightType, int>();
        var disabledLightExamples = new List<string>();
        for (var rootIndex = 0; rootIndex < roots.Length; rootIndex++)
        {
            var root = roots[rootIndex];
            if (root == null)
                continue;

            ProtectComponentSubtrees(root.GetComponentsInChildren<PlayerBehaviour>(true), protectedIds);
            ProtectComponentSubtrees(root.GetComponentsInChildren<BasketBehaviour>(true), protectedIds);
            ProtectComponentSubtrees(root.GetComponentsInChildren<TeePadBehaviour>(true), protectedIds);

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer != null && !protectedIds.Contains(renderer.transform.GetInstanceID()))
                {
                    var id = renderer.GetInstanceID();
                    if (!RendererSnapshots.ContainsKey(id))
                        RendererSnapshots[id] = new RendererSnapshot(renderer, renderer.enabled);
                    renderer.enabled = false;
                }
            }

            var colliders = root.GetComponentsInChildren<Collider>(true);
            for (var i = 0; i < colliders.Length; i++)
            {
                var collider = colliders[i];
                if (collider == null || protectedIds.Contains(collider.transform.GetInstanceID()))
                    continue;
                var id = collider.GetInstanceID();
                if (!ColliderSnapshots.ContainsKey(id))
                    ColliderSnapshots[id] = new ColliderSnapshot(collider, collider.enabled);
                collider.enabled = false;
            }

            var terrains = root.GetComponentsInChildren<Terrain>(true);
            for (var i = 0; i < terrains.Length; i++)
            {
                var terrain = terrains[i];
                if (terrain == null || protectedIds.Contains(terrain.transform.GetInstanceID()))
                    continue;
                var id = terrain.GetInstanceID();
                if (!TerrainSnapshots.ContainsKey(id))
                    TerrainSnapshots[id] = new TerrainSnapshot(terrain, terrain.enabled);
                ImportedTerrainIsolation.RememberHiddenTerrain(terrain);
                terrain.enabled = false;
            }

            var lights = root.GetComponentsInChildren<Light>(true);
            for (var i = 0; i < lights.Length; i++)
            {
                var light = lights[i];
                if (light == null)
                    continue;
                baseLightCount++;
                if (protectedIds.Contains(light.transform.GetInstanceID()))
                    continue;
                if (light == retainedBaseSun)
                {
                    retainedBaseSunCount++;
                    continue;
                }
                if (!light.enabled)
                    continue;

                var lightId = light.GetInstanceID();
                if (!BaseCourseLightSnapshots.ContainsKey(lightId))
                    BaseCourseLightSnapshots[lightId] = new BaseCourseLightSnapshot(light, light.enabled);
                light.enabled = false;
                disabledBaseLightCount++;
                if (!disabledLightTypes.TryGetValue(light.type, out var typeCount))
                    typeCount = 0;
                disabledLightTypes[light.type] = typeCount + 1;
                if (disabledLightExamples.Count < 8)
                    disabledLightExamples.Add($"{light.type}:{DescribeBaseLightPath(light.transform)}[intensity={light.intensity:F1},range={light.range:F1}]");
            }
        }

        // SurfaceMapper has its own active registry and heightfield solver. The
        // Terrain/Collider enabled flags above do not remove it from that registry.
        // Scan after every root's terrain is registered (mapper/terrain may live
        // under different roots), and let OnDisable unregister through native code.
        foreach (var root in roots)
        {
            if (root == null) continue;
            foreach (var mapper in root.GetComponentsInChildren<SurfaceMapper>(true))
                ImportedTerrainIsolation.SuspendMapper(mapper);
        }

        if (baseLightCount > 0)
        {
            var typeSummary = string.Join(", ", disabledLightTypes.OrderBy(entry => entry.Key).Select(entry => $"{entry.Key}={entry.Value}"));
            Plugin.LogSource.LogInfo($"Isolated base-course lights for imported map '{courseName}': found {baseLightCount}, disabled {disabledBaseLightCount} unprotected enabled light(s) [{typeSummary}], retained requested base sun {retainedBaseSunCount}; examples={string.Join(" || ", disabledLightExamples)}.");
        }
    }

    private static string DescribeBaseLightPath(Transform transform)
    {
        var names = new Stack<string>();
        for (var current = transform; current != null; current = current.parent)
            names.Push(current.name);
        return string.Join("/", names);
    }

    private static void WakeMovedBasketChains(BasketBehaviour basket)
    {
        try
        {
            var rig = basket == null ? null : basket.chainRig;
            if (rig == null && basket != null) rig = basket.GetComponentInChildren<ChainRig>(true);
            if (rig == null)
            {
                Plugin.LogSource.LogInfo($"No chain rig found for moved basket '{(basket == null ? "<null>" : basket.name)}'.");
                return;
            }
            var wasAsleep = rig.asleep;
            rig.asleep = false;
            rig.quietTime = 0f;
            Plugin.LogSource.LogInfo($"Woke the chain rig for moved basket '{basket.name}' (wasAsleep={wasAsleep}, enabled={rig.enabled}).");
        }
        catch (Exception ex) { Plugin.LogSource.LogWarning($"Could not wake a moved basket's chains: {ex.Message}"); }
    }

    private static void AddProtectedSubtree(Transform root, HashSet<int> protectedIds)
    {
        if (root == null)
            return;
        protectedIds.Add(root.GetInstanceID());
        for (var i = 0; i < root.childCount; i++)
            AddProtectedSubtree(root.GetChild(i), protectedIds);
    }

    private static void ProtectComponentSubtrees<T>(Il2CppArrayBase<T> components, HashSet<int> protectedIds) where T : Component
    {
        if (components == null)
            return;
        for (var i = 0; i < components.Length; i++)
            AddProtectedSubtree(components[i] == null ? null : components[i].transform, protectedIds);
    }

    private static void RememberGameObject(GameObject gameObject)
    {
        var id = gameObject.GetInstanceID();
        if (!GameObjectSnapshots.ContainsKey(id))
            GameObjectSnapshots[id] = new GameObjectActiveSnapshot(gameObject, gameObject.activeSelf);
    }

    private static bool TryGetSceneBounds(Scene scene, out Bounds bounds)
    {
        var hasBounds = false;
        bounds = default;
        var roots = scene.GetRootGameObjects();
        for (var rootIndex = 0; rootIndex < roots.Length; rootIndex++)
        {
            var root = roots[rootIndex];
            if (root == null)
                continue;

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer != null && renderer.enabled && !IsImportedSkyboxRenderer(renderer))
                    Encapsulate(ref bounds, ref hasBounds, renderer.bounds);
            }

            var colliders = root.GetComponentsInChildren<Collider>(true);
            for (var i = 0; i < colliders.Length; i++)
            {
                var collider = colliders[i];
                if (collider != null && collider.enabled && !collider.isTrigger)
                    Encapsulate(ref bounds, ref hasBounds, collider.bounds);
            }

            var terrains = root.GetComponentsInChildren<Terrain>(true);
            for (var i = 0; i < terrains.Length; i++)
            {
                var terrain = terrains[i];
                if (terrain == null || terrain.terrainData == null)
                    continue;
                var size = terrain.terrainData.size;
                Encapsulate(ref bounds, ref hasBounds, new Bounds(terrain.transform.position + size * 0.5f, size));
            }
        }
        return hasBounds;
    }

    private static void Encapsulate(ref Bounds bounds, ref bool hasBounds, Bounds value)
    {
        // BONELAB scenes can contain giant sky domes, ocean sheets, and other
        // off-course meshes. Including them makes the placement grid land on
        // the map edge or thousands of metres below the playable island.
        if (value.size.sqrMagnitude < 0.01f || value.size.sqrMagnitude > 100000000f ||
            Mathf.Max(value.size.x, value.size.z) > 700f)
            return;
        if (!hasBounds)
        {
            bounds = value;
            hasBounds = true;
        }
        else
        {
            bounds.Encapsulate(value);
        }
    }

    private static List<ImportedCoursePlanner.Hole> PlanLongImportedHoles(Scene mapScene, Bounds bounds, MapWorldBoundsDefinition configuredBounds, int count, IReadOnlyList<int> targetLengths)
    {
        // Some imported levels include decorative meshes thousands of metres apart.
        // A fixed 13x13 grid can skip the playable island entirely; tighten spacing
        // for broad scenes while keeping the load-time query count bounded.
        var gridSize = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(bounds.size.x, bounds.size.z) / 80f) + 1, 13, 41);
        LayoutClearanceUnavailable = false;
        LayoutClearanceWarningLogged = false;
        LayoutRayDiagnosticsEnabled = true;
        LayoutRayCount = 0;
        LayoutRayHitCount = 0;
        LayoutRayMapHitCount = 0;
        LayoutRayOtherSceneHitCount = 0;
        LayoutRayWaterHitCount = 0;
        LayoutRayTriggerHitCount = 0;
        LayoutRaySlopeHitCount = 0;
        LayoutRayNamedHitCount = 0;
        LayoutRayWideHitCount = 0;
        LayoutRayHeightHitCount = 0;
        LayoutRayAcceptedCount = 0;
        LayoutRayExamples.Clear();
        var samples = new List<ImportedCoursePlanner.Point>();
        var clearSamples = new List<ImportedCoursePlanner.Point>();
        var groundSampleCounts = new Dictionary<int, (string Name, string Root, int Count, Vector3 Center, Vector3 Size)>();
        var obstructed = 0;
        var overheadObstructed = 0;
        var maxSampleDistance = 0f;
        var rayStartY = bounds.max.y + Mathf.Clamp(bounds.size.y * 0.2f, 30f, 500f);
        var rayLength = Mathf.Clamp(bounds.size.y + 250f, 250f, 2000f);
        void SampleGroundGrid(int sideLength)
        {
            for (var x = 0; x < sideLength; x++)
            for (var z = 0; z < sideLength; z++)
            {
                var u = Mathf.Lerp(0.04f, 0.96f, x / (float)(sideLength - 1));
                var v = Mathf.Lerp(0.04f, 0.96f, z / (float)(sideLength - 1));
                var origin = new Vector3(Mathf.Lerp(bounds.min.x, bounds.max.x, u), rayStartY, Mathf.Lerp(bounds.min.z, bounds.max.z, v));
                if (!TryFindLayoutGround(mapScene, origin, rayLength, configuredBounds, out var point, out var groundCollider)) continue;
                ImportedMapPhysicsSupport.UseDiscTerrainLayer(groundCollider);
                var candidate = new ImportedCoursePlanner.Point(point.x, point.y + 0.04f, point.z);
                samples.Add(candidate);
                var groundId = groundCollider.GetInstanceID();
                var oldGroundSample = groundSampleCounts.TryGetValue(groundId, out var oldGround) ? oldGround : default;
                groundSampleCounts[groundId] = (
                    groundCollider.name,
                    groundCollider.transform.root.name,
                    oldGround.Count + 1,
                    groundCollider.bounds.center,
                    groundCollider.bounds.size);
                if (HasLayoutClearance(mapScene, point, groundCollider, out var overheadBlocked)) clearSamples.Add(candidate);
                else
                {
                    obstructed++;
                    if (overheadBlocked) overheadObstructed++;
                }
            }
        }

        SampleGroundGrid(gridSize);
        var coarseSampleCount = samples.Count;
        if (coarseSampleCount < 6)
        {
            // Small islands and shoreline strips can fall between the original
            // 13x13 rays. Retry only sparse maps with a five-metre grid so their
            // entire playable land area gets a chance to contribute candidates.
            samples.Clear();
            clearSamples.Clear();
            groundSampleCounts.Clear();
            obstructed = 0;
            overheadObstructed = 0;
            gridSize = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(bounds.size.x, bounds.size.z) / 5f) + 1, 31, 61);
            SampleGroundGrid(gridSize);
            Plugin.LogSource.LogInfo($"Sparse imported ground retry: refined from {coarseSampleCount} samples on the coarse grid to {samples.Count} samples on a {gridSize}x{gridSize} grid.");
        }
        LayoutRayDiagnosticsEnabled = false;
        Plugin.LogSource.LogInfo($"Imported layout ray diagnostic for '{mapScene.name}': bounds X {bounds.min.x:F1}..{bounds.max.x:F1}, Y {bounds.min.y:F1}..{bounds.max.y:F1}, Z {bounds.min.z:F1}..{bounds.max.z:F1}; rays={LayoutRayCount}, hits={LayoutRayHitCount}, mapHits={LayoutRayMapHitCount}, otherSceneHits={LayoutRayOtherSceneHitCount}, waterHits={LayoutRayWaterHitCount}, triggerHits={LayoutRayTriggerHitCount}, slopeRejected={LayoutRaySlopeHitCount}, namedRejected={LayoutRayNamedHitCount}, oversizedRejected={LayoutRayWideHitCount}, groundHeightRejected={LayoutRayHeightHitCount}, accepted={LayoutRayAcceptedCount}; examples={string.Join(" || ", LayoutRayExamples)}.");
        for (var a = 0; a < samples.Count; a++)
        for (var b = a + 1; b < samples.Count; b++)
        {
            var dx = samples[a].X - samples[b].X;
            var dz = samples[a].Z - samples[b].Z;
            maxSampleDistance = Mathf.Max(maxSampleDistance, Mathf.Sqrt(dx * dx + dz * dz));
        }
        // A small set of unobstructed points is still preferable to generating
        // tees beneath roofs or inside buildings. The planner can reuse a pair
        // on sparse maps, while falling back only if fewer than two safe points
        // remain or the physics clearance query failed.
        var useClearSamples = !LayoutClearanceUnavailable && clearSamples.Count >= 2;
        var planningSamples = useClearSamples ? clearSamples : samples;
        if (!useClearSamples && obstructed > 0)
            Plugin.LogSource.LogWarning($"Long imported layout found only {clearSamples.Count} ground positions with local and overhead clearance; using all {samples.Count} grounded candidates to keep a complete fallback layout.");
        if (samples.Count is > 0 and < 6)
            Plugin.LogSource.LogWarning($"Long imported layout for a very small land area has only {samples.Count} grounded points; any planned holes may reuse the same short grounded pair.");
        var result = ImportedCoursePlanner.Plan(planningSamples, count, targetLengths, out var usedRelaxedPairs);
        if (usedRelaxedPairs)
            Plugin.LogSource.LogWarning($"Long imported layout for '{mapScene.name}' used relaxed grounded pairs to complete holes because the map's land clusters do not fit the preferred distance windows.");
        Plugin.LogSource.LogInfo($"Long imported layout: {gridSize}x{gridSize} ground grid, {samples.Count} grounded candidate positions, {clearSamples.Count} with local and overhead clearance ({overheadObstructed} rejected for overhead obstruction), target lengths=[{string.Join(", ", targetLengths)}]m, {result.Count} hole(s) planned; maximum candidate separation {maxSampleDistance:F1}m.");
        foreach (var sample in groundSampleCounts.Values.OrderByDescending(value => value.Count).Take(8))
            Plugin.LogSource.LogInfo($"Ground candidate collider '{sample.Name}' under root '{sample.Root}': {sample.Count} grid hit(s), center=({sample.Center.x:F1},{sample.Center.y:F1},{sample.Center.z:F1}), size=({sample.Size.x:F1},{sample.Size.y:F1},{sample.Size.z:F1}).");
        return result;
    }

    private static bool HasLayoutClearance(Scene scene, Vector3 point, Collider groundCollider, out bool overheadBlocked)
    {
        overheadBlocked = false;
        if (LayoutClearanceUnavailable) return true;
        try
        {
            // Reject sample points underneath low building roofs. The old
            // standing-height sphere could regard the center of a room as clear
            // even though a tee throw immediately hit the ceiling.
            var overheadHits = Physics.RaycastAll(point + Vector3.up * 1.4f, Vector3.up, 2.8f, ~0, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < overheadHits.Length; i++)
            {
                var collider = overheadHits[i].collider;
                if (collider == null || collider == groundCollider || collider.isTrigger || collider.gameObject.scene.handle != scene.handle)
                    continue;
                overheadBlocked = true;
                return false;
            }

            var nearby = Physics.OverlapSphere(point + Vector3.up * 0.9f, 0.85f, ~0, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < nearby.Length; i++)
            {
                var collider = nearby[i];
                if (collider == null || collider == groundCollider || collider.isTrigger || collider.gameObject.scene.handle != scene.handle)
                    continue;
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            LayoutClearanceUnavailable = true;
            if (!LayoutClearanceWarningLogged)
            {
                LayoutClearanceWarningLogged = true;
                Plugin.LogSource.LogWarning($"Long imported layout could not check nearby collider clearance; it will use the original grounded candidates. {ex.Message}");
            }
            return true;
        }
    }

    private static bool TryFindLayoutGround(Scene scene, Vector3 origin, float distance, MapWorldBoundsDefinition configuredBounds, out Vector3 point, out Collider groundCollider) =>
        TryFindLayoutGround(scene, origin, distance, configuredBounds, out point, out groundCollider, true);

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

    private static bool TryFindLayoutGround(Scene scene, Vector3 origin, float distance, MapWorldBoundsDefinition configuredBounds, out Vector3 point, out Collider groundCollider, bool allowMeshAlignment)
    {
        point = default;
        groundCollider = null;
        for (var preparationPass = 0; preparationPass < 9; preparationPass++)
        {
            var hits = Physics.RaycastAll(origin, Vector3.down, distance);
            if (LayoutRayDiagnosticsEnabled && preparationPass == 0)
            {
                LayoutRayCount++;
                LayoutRayHitCount += hits.Length;
            }
            var found = false;
            var bestScore = float.PositiveInfinity;
            var waterHeight = float.NegativeInfinity;
            point = default;
            groundCollider = null;
            for (var i = 0; i < hits.Length; i++)
            {
                var hit = hits[i];
                var collider = hit.collider;
                if (collider == null) continue;
                var sameScene = collider.gameObject.scene.handle == scene.handle;
                var upDot = Vector3.Dot(hit.normal, Vector3.up);
                if (LayoutRayDiagnosticsEnabled && preparationPass == 0)
                {
                    if (sameScene) LayoutRayMapHitCount++; else LayoutRayOtherSceneHitCount++;
                    if (LayoutRayExamples.Count < 12)
                        LayoutRayExamples.Add($"{collider.name}[layer={collider.gameObject.layer},scene={collider.gameObject.scene.name},same={sameScene},y={hit.point.y:F1},up={upDot:F2},trigger={collider.isTrigger},size={collider.bounds.size.x:F1}x{collider.bounds.size.y:F1}x{collider.bounds.size.z:F1}]");
                }
                if (!sameScene) continue;
                var name = collider.gameObject.name.ToLowerInvariant();
                if (ImportedMapPhysicsSupport.HasWaterNameToken(name))
                {
                    if (LayoutRayDiagnosticsEnabled && preparationPass == 0) LayoutRayWaterHitCount++;
                    waterHeight = Mathf.Max(waterHeight, hit.point.y);
                    continue;
                }
                if (collider.isTrigger)
                {
                    if (LayoutRayDiagnosticsEnabled && preparationPass == 0) LayoutRayTriggerHitCount++;
                    continue;
                }
                if (upDot < 0.94f)
                {
                    if (LayoutRayDiagnosticsEnabled && preparationPass == 0) LayoutRaySlopeHitCount++;
                    continue;
                }
                if (IsNamedBuildingSurface(collider))
                {
                    if (LayoutRayDiagnosticsEnabled && preparationPass == 0) LayoutRayNamedHitCount++;
                    continue;
                }
                if (Mathf.Max(collider.bounds.size.x, collider.bounds.size.z) > 700f)
                {
                    if (LayoutRayDiagnosticsEnabled && preparationPass == 0) LayoutRayWideHitCount++;
                    continue;
                }
                var score = configuredBounds == null ? origin.y - hit.point.y : Mathf.Abs(hit.point.y - configuredBounds.GroundY);
                if (configuredBounds != null && score > 20f)
                {
                    if (LayoutRayDiagnosticsEnabled && preparationPass == 0) LayoutRayHeightHitCount++;
                    continue;
                }
                if (score >= bestScore) continue;
                bestScore = score;
                point = hit.point;
                groundCollider = collider;
                found = true;
            }

            if (!found)
                return false;

            if (LayoutRayDiagnosticsEnabled && preparationPass == 0) LayoutRayAcceptedCount++;

            if (allowMeshAlignment && preparationPass < 8)
            {
                var visibleMeshChanged = ImportedMapPhysicsSupport.TryUseVisibleGroundMesh(groundCollider);
                var physicsChanged = ImportedMapPhysicsSupport.UseDiscTerrainLayer(groundCollider);
                if (visibleMeshChanged || physicsChanged)
                {
                    Physics.SyncTransforms();
                    continue;
                }
            }

            return point.y >= waterHeight - 0.05f && !ImportedMapPhysicsSupport.IsBelowWater(point);
        }
        return false;
    }

    private static bool TryPlaceGridHole(Scene mapScene, Bounds bounds, float fallbackGroundY, int holeIndex, out Vector3 teePosition, out Vector3 basketPosition)
    {
        var row = holeIndex / 3;
        var column = holeIndex % 3;
        var basketX = Mathf.Lerp(bounds.min.x, bounds.max.x, (column + 0.5f) / 3f);
        var basketZ = Mathf.Lerp(bounds.min.z, bounds.max.z, (row + 0.5f) / 3f);
        var teeX = Mathf.Clamp(basketX - Mathf.Max(5f, bounds.size.x * 0.12f), bounds.min.x + bounds.size.x * 0.03f, bounds.max.x - bounds.size.x * 0.03f);
        var teeZ = Mathf.Clamp(basketZ - Mathf.Max(4f, bounds.size.z * 0.08f), bounds.min.z + bounds.size.z * 0.03f, bounds.max.z - bounds.size.z * 0.03f);
        var rayStartY = bounds.max.y + Mathf.Max(30f, bounds.size.y * 0.2f);
        var rayLength = Mathf.Max(250f, bounds.size.y + 250f);
        var foundTee = TryFindGround(mapScene, new Vector3(teeX, rayStartY, teeZ), rayLength, fallbackGroundY, out teePosition);
        var foundBasket = TryFindGround(mapScene, new Vector3(basketX, rayStartY, basketZ), rayLength, fallbackGroundY, out basketPosition);
        if (foundTee)
            teePosition += Vector3.up * 0.04f;
        else
            teePosition = new Vector3(teeX, fallbackGroundY + 0.04f, teeZ);
        if (foundBasket)
            basketPosition += Vector3.up * 0.04f;
        else
            basketPosition = new Vector3(basketX, fallbackGroundY + 0.04f, basketZ);
        return foundTee && foundBasket;
    }

    private static bool TryFindGround(Scene mapScene, Vector3 origin, float distance, float preferredGroundY, out Vector3 position)
    {
        var hits = Physics.RaycastAll(origin, Vector3.down, distance);
        var found = false;
        var closestGroundDelta = float.PositiveInfinity;
        position = default;
        for (var i = 0; i < hits.Length; i++)
        {
            var hit = hits[i];
            var collider = hit.collider;
            if (collider == null || collider.isTrigger || collider.gameObject.scene.name != mapScene.name)
                continue;
            if (Vector3.Dot(hit.normal, Vector3.up) < 0.55f)
                continue;
            var groundDelta = Mathf.Abs(hit.point.y - preferredGroundY);
            if (groundDelta > 20f || groundDelta >= closestGroundDelta)
                continue;
            closestGroundDelta = groundDelta;
            position = hit.point;
            found = true;
        }
        return found;
    }

    private static void AddObZones(HoleSettings hole, TransformSnapshot teeStart, IReadOnlyList<ObZoneDefinition> zones)
    {
        if (zones == null || zones.Count == 0)
            return;

        hole.SetThisHoleOBManagerAsActive();
        var manager = OutOfBoundsManager.activeOBManager ?? UnityEngine.Object.FindObjectOfType<OutOfBoundsManager>();
        if (manager == null)
        {
            Plugin.LogSource.LogWarning($"Hole {hole.HoleNr}: the base scene has no OB manager; custom OB zones were skipped.");
            return;
        }

        RememberOutOfBoundsManager(manager);
        var baseAreas = manager.OBAreas ?? new Il2CppReferenceArray<OBArea>(0);
        foreach (var zone in zones)
        {
            var gameObject = new GameObject($"DGM_Custom_OB_H{hole.HoleNr}_{zone.Name}");
            TemporaryObjects.Add(gameObject);
            if (manager.OBAreasHolder != null)
                gameObject.transform.SetParent(manager.OBAreasHolder, false);

            var area = gameObject.AddComponent<OBArea>();
            area.insideIsOB = true;
            area.hideLineAndFlagpoles = true;
            area.bounds = new Il2CppStructArray<Vector2>(zone.Points.Count);

            var right = teeStart.Rotation * Vector3.right;
            var forward = teeStart.Rotation * Vector3.forward;
            for (var pointIndex = 0; pointIndex < zone.Points.Count; pointIndex++)
            {
                var point = zone.Points[pointIndex];
                var world = teeStart.Position + right * point.Right + forward * point.Forward;
                area.bounds[pointIndex] = new Vector2(world.x, world.z);
            }

            var areas = new Il2CppReferenceArray<OBArea>(baseAreas.Length + 1);
            for (var i = 0; i < baseAreas.Length; i++)
                areas[i] = baseAreas[i];
            areas[baseAreas.Length] = area;
            manager.OBAreas = areas;
            baseAreas = areas;
        }
    }

    private static bool AddImportedTeeSign(CourseDefinition course, string imageName, Transform tee, Vector3 right, Vector3 forward)
    {
        var imagePath = Path.Combine(course.SourceDirectory, imageName);
        if (!File.Exists(imagePath)) return false;
        try
        {
            var texture = LoadTeeCardTexture(course, imagePath);
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture");
            if (shader == null) return false;
            var rotation = Quaternion.LookRotation(-forward, Vector3.up);
            var center = tee.position + right * 1.3f + forward * 0.5f + Vector3.up * 1.25f;

            var frameMaterial = new Material(shader) { color = new Color(0.14f, 0.10f, 0.07f, 1f) };
            var imageMaterial = new Material(shader) { mainTexture = texture, color = Color.white };
            TemporaryMaterials.Add(frameMaterial);
            TemporaryMaterials.Add(imageMaterial);

            GameObject MakePart(PrimitiveType shape, string name, Vector3 position, Vector3 scale, Material material)
            {
                var part = GameObject.CreatePrimitive(shape);
                TemporaryObjects.Add(part);
                part.name = name;
                part.transform.SetPositionAndRotation(position, rotation);
                part.transform.localScale = scale;
                var collider = part.GetComponent<Collider>();
                if (collider != null) UnityEngine.Object.Destroy(collider);
                var renderer = part.GetComponent<Renderer>();
                if (renderer != null) renderer.sharedMaterial = material;
                return part;
            }

            MakePart(PrimitiveType.Cube, "DGM_TeeSignPost", center - Vector3.up * 0.70f, new Vector3(0.09f, 1.1f, 0.09f), frameMaterial);
            MakePart(PrimitiveType.Cube, "DGM_TeeSignBoard", center, new Vector3(0.85f, 0.68f, 0.06f), frameMaterial);
            MakePart(PrimitiveType.Quad, "DGM_TeeSignImage", center - forward * 0.035f, new Vector3(0.77f, 0.60f, 1f), imageMaterial);
            return true;
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Could not create imported tee sign '{imageName}': {ex.Message}");
            return false;
        }
    }

    private static Texture2D LoadTeeCardTexture(CourseDefinition course, string imagePath)
    {
        if (TeeCardTextures.TryGetValue(imagePath, out var texture)) return texture;
        // ImageConversion.LoadImage uses an unavailable IL2CPP ReadOnlySpan
        // method in this game. Share the PNG decoder used by the course list.
        texture = CourseCardFactory.LoadPngCourseCard(imagePath, course.Id, 256);
        TeeCardTextures[imagePath] = texture;
        TemporaryTextures.Add(texture);
        return texture;
    }

    private static void AddTeeCard(CourseDefinition course, string imageName, Transform tee, Vector3 right, Vector3 forward)
    {
        var imagePath = Path.Combine(course.SourceDirectory, imageName);
        if (!File.Exists(imagePath))
            return;

        try
        {
            var texture = LoadTeeCardTexture(course, imagePath);

            var card = GameObject.CreatePrimitive(PrimitiveType.Quad);
            TemporaryObjects.Add(card);
            var collider = card.GetComponent<Collider>();
            if (collider != null)
                UnityEngine.Object.Destroy(collider);
            card.name = $"DGM_TeeCard_{course.Id}";
            card.transform.SetPositionAndRotation(
                tee.position + right * 0.85f + Vector3.up * 1.35f - forward * 0.65f,
                Quaternion.LookRotation(-forward, Vector3.up));
            card.transform.localScale = new Vector3(0.55f, 0.72f, 1f);

            var renderer = card.GetComponent<MeshRenderer>();
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture") ?? Shader.Find("Sprites/Default");
            if (renderer != null && shader != null)
            {
                var material = new Material(shader) { mainTexture = texture };
                TemporaryMaterials.Add(material);
                renderer.sharedMaterial = material;
            }
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Could not show tee card '{imageName}': {ex.Message}");
        }
    }

    private static void SetVisible(GameObject root, bool visible)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        foreach (var renderer in renderers)
        {
            var key = renderer.GetInstanceID();
            if (!RendererSnapshots.ContainsKey(key))
                RendererSnapshots[key] = new RendererSnapshot(renderer, renderer.enabled);
            renderer.enabled = visible;
        }
    }

    private static HoleSettings GetCurrentHole(CourseManager courseManager, int startHoleIndex, int holeCount)
    {
        var roundManager = RoundManager.currentRound;
        if (roundManager != null)
        {
            var current = roundManager.GetCurrentHole();
            if (current != null)
                return current;
        }

        if (courseManager.holeList.Length == 0)
            return null;
        var index = Math.Clamp(startHoleIndex, 0, Math.Min(courseManager.holeList.Length, holeCount) - 1);
        return courseManager.holeList[index];
    }

    private static Vector3 ToWorldOffset(LocalOffset offset, Vector3 right, Vector3 forward) =>
        right * offset.Right + Vector3.up * offset.Up + forward * offset.Forward;

    private static void RememberTransform(Transform transform)
    {
        var key = transform.GetInstanceID();
        if (!TransformSnapshots.ContainsKey(key))
            TransformSnapshots.Add(key, new TransformSnapshot(transform, transform.position, transform.rotation));
    }

    private static void RememberBasketPar(BasketBehaviour basket)
    {
        var key = basket.GetInstanceID();
        if (!BasketParSnapshots.ContainsKey(key))
            BasketParSnapshots.Add(key, basket.par);
    }

    private static void RememberHole(HoleSettings hole)
    {
        var key = hole.GetInstanceID();
        if (!HoleSnapshots.ContainsKey(key))
            HoleSnapshots.Add(key, new HoleVariantSnapshot(hole, hole.activePinVariant, hole.activeTeeVariant));
    }

    private static void RememberOutOfBoundsManager(OutOfBoundsManager manager)
    {
        var key = manager.GetInstanceID();
        if (!OutOfBoundsSnapshots.ContainsKey(key))
            OutOfBoundsSnapshots[key] = new OutOfBoundsManagerSnapshot(manager, manager.OBAreas, manager.normalPlayArea, manager.overridePlayArea);
    }

    private static BasketBehaviour FindBasket(int instanceId)
    {
        foreach (var holeSnapshot in HoleSnapshots.Values)
        {
            var hole = holeSnapshot.Hole;
            if (hole == null || hole.pinVariants == null)
                continue;
            for (var i = 0; i < hole.pinVariants.Length; i++)
            {
                var basket = hole.pinVariants[i];
                if (basket != null && basket.GetInstanceID() == instanceId)
                    return basket;
            }
        }
        return null;
    }

    private sealed class TransformSnapshot
    {
        public Transform Transform { get; }
        public Vector3 Position { get; }
        public Quaternion Rotation { get; }

        public TransformSnapshot(Transform transform, Vector3 position, Quaternion rotation)
        {
            Transform = transform;
            Position = position;
            Rotation = rotation;
        }
    }

    private sealed class HoleVariantSnapshot
    {
        public HoleSettings Hole { get; }
        public BasketBehaviour.PinVariant PinVariant { get; }
        public BasketBehaviour.PinVariant TeeVariant { get; }

        public HoleVariantSnapshot(HoleSettings hole, BasketBehaviour.PinVariant pinVariant, BasketBehaviour.PinVariant teeVariant)
        {
            Hole = hole;
            PinVariant = pinVariant;
            TeeVariant = teeVariant;
        }
    }

    private sealed class OutOfBoundsManagerSnapshot
    {
        public OutOfBoundsManager Manager { get; }
        public Il2CppReferenceArray<OBArea> OriginalAreas { get; }
        public OBArea OriginalNormalPlayArea { get; }
        public OBArea OriginalOverridePlayArea { get; }

        public OutOfBoundsManagerSnapshot(OutOfBoundsManager manager, Il2CppReferenceArray<OBArea> originalAreas, OBArea originalNormalPlayArea, OBArea originalOverridePlayArea)
        {
            Manager = manager;
            OriginalAreas = originalAreas;
            OriginalNormalPlayArea = originalNormalPlayArea;
            OriginalOverridePlayArea = originalOverridePlayArea;
        }
    }

    private sealed class ShadowRendererSnapshot
    {
        public Renderer Renderer { get; }
        public ShadowCastingMode CastingMode { get; }
        public bool ReceiveShadows { get; }
        public ShadowRendererSnapshot(Renderer renderer)
        {
            Renderer = renderer;
            CastingMode = renderer.shadowCastingMode;
            ReceiveShadows = renderer.receiveShadows;
        }
    }

    private sealed class ShadowLightSnapshot
    {
        public Light Light { get; }
        public float Intensity { get; }
        public LightShadows Shadows { get; }
        public bool Enabled { get; }
        public float ShadowStrength { get; }
        public LightBakingOutput BakingOutput { get; }
        public bool HasBakingOutput { get; }
        public ShadowLightSnapshot(Light light)
        {
            Light = light;
            Intensity = light.intensity;
            Enabled = light.enabled;
            Shadows = light.shadows;
            ShadowStrength = light.shadowStrength;
            try { BakingOutput = light.bakingOutput; HasBakingOutput = true; }
            catch (Exception ex) { Plugin.LogSource.LogWarning($"Could not snapshot imported light bake mode: {ex.Message}"); }
        }
    }

    private sealed class BaseCourseLightSnapshot
    {
        public Light Light { get; }
        public bool Enabled { get; }
        public BaseCourseLightSnapshot(Light light, bool enabled)
        {
            Light = light;
            Enabled = enabled;
        }
    }

    private sealed class TerrainShadowSnapshot
    {
        public Terrain Terrain { get; }
        public ShadowCastingMode CastingMode { get; }
        public TerrainShadowSnapshot(Terrain terrain) { Terrain = terrain; CastingMode = terrain.shadowCastingMode; }
    }

    private sealed class TerrainLightmapSnapshot
    {
        public Terrain Terrain { get; }
        public int LightmapIndex { get; }
        public int RealtimeLightmapIndex { get; }
        public TerrainLightmapSnapshot(Terrain terrain)
        {
            Terrain = terrain;
            LightmapIndex = terrain.lightmapIndex;
            RealtimeLightmapIndex = terrain.realtimeLightmapIndex;
        }
    }

    private sealed class RendererSnapshot
    {
        public Renderer Renderer { get; }
        public bool Enabled { get; }

        public RendererSnapshot(Renderer renderer, bool enabled)
        {
            Renderer = renderer;
            Enabled = enabled;
        }
    }

    private sealed class RendererLocalBoundsSnapshot
    {
        public Renderer Renderer { get; }
        public Bounds LocalBounds { get; }

        public RendererLocalBoundsSnapshot(Renderer renderer)
        {
            Renderer = renderer;
            LocalBounds = renderer.localBounds;
        }
    }

    private sealed class RendererLightmapSnapshot
    {
        public Renderer Renderer { get; }
        public int LightmapIndex { get; }
        public int RealtimeLightmapIndex { get; }

        public RendererLightmapSnapshot(Renderer renderer)
        {
            Renderer = renderer;
            LightmapIndex = renderer.lightmapIndex;
            RealtimeLightmapIndex = renderer.realtimeLightmapIndex;
        }
    }

    private sealed class CameraOcclusionSnapshot
    {
        public Camera Camera { get; }
        public bool UseOcclusionCulling { get; }

        public CameraOcclusionSnapshot(Camera camera)
        {
            Camera = camera;
            UseOcclusionCulling = camera.useOcclusionCulling;
        }
    }

    private sealed class ColliderSnapshot
    {
        public Collider Collider { get; }
        public bool Enabled { get; }

        public ColliderSnapshot(Collider collider, bool enabled)
        {
            Collider = collider;
            Enabled = enabled;
        }
    }

    private sealed class TerrainSnapshot
    {
        public Terrain Terrain { get; }
        public bool Enabled { get; }

        public TerrainSnapshot(Terrain terrain, bool enabled)
        {
            Terrain = terrain;
            Enabled = enabled;
        }
    }

    private sealed class TerrainMaterialSnapshot
    {
        public Terrain Terrain { get; }
        public Material MaterialTemplate { get; }

        public TerrainMaterialSnapshot(Terrain terrain, Material materialTemplate)
        {
            Terrain = terrain;
            MaterialTemplate = materialTemplate;
        }
    }

    private sealed class TerrainDetailSnapshot
    {
        public TerrainData Data { get; }
        public Il2CppReferenceArray<DetailPrototype> Prototypes { get; }

        public TerrainDetailSnapshot(TerrainData data, Il2CppReferenceArray<DetailPrototype> prototypes)
        {
            Data = data;
            Prototypes = prototypes;
        }
    }

    private sealed class TerrainTreeSnapshot
    {
        public Terrain Terrain { get; }
        public float BillboardDistance { get; }
        public int MaximumFullLodCount { get; }
        public float CrossFadeLength { get; }

        public TerrainTreeSnapshot(Terrain terrain)
        {
            Terrain = terrain;
            BillboardDistance = terrain.treeBillboardDistance;
            MaximumFullLodCount = terrain.treeMaximumFullLODCount;
            CrossFadeLength = terrain.treeCrossFadeLength;
        }
    }

    private sealed class TreeLodSnapshot
    {
        public LODGroup Group { get; }
        public Il2CppReferenceArray<LOD> Lods { get; }
        public bool LastLodBillboard { get; }
        public LODFadeMode FadeMode { get; }
        public bool AnimateCrossFading { get; }

        public TreeLodSnapshot(LODGroup group, Il2CppReferenceArray<LOD> lods)
        {
            Group = group;
            Lods = lods;
            LastLodBillboard = group.lastLODBillboard;
            FadeMode = group.fadeMode;
            AnimateCrossFading = group.animateCrossFading;
        }
    }

    private sealed class GameObjectActiveSnapshot
    {
        public GameObject GameObject { get; }
        public bool ActiveSelf { get; }

        public GameObjectActiveSnapshot(GameObject gameObject, bool activeSelf)
        {
            GameObject = gameObject;
            ActiveSelf = activeSelf;
        }
    }
}
