using Dgm;
using HarmonyLib;
using UnityEngine;

namespace DgmCustomCourses;

// Temporary, bounded observations for diagnosing run-up camera orientation on imported maps.
// This only reads transforms; it never moves a player, changes input, or controls a camera.
internal static class RunupCameraDiagnostics
{
    private static PlayerBehaviour _player;
    private static RunupSequence _sequence;
    private static float _startedAt;
    private static float _nextSample;
    private static int _sample;
    private static int _runs;

    internal static void Start(RunupSequence sequence)
    {
        if (!CourseScenePatcher.IsImportedMapActive || sequence == null || sequence.player == null || _runs >= 3) return;
        _sequence = sequence;
        _player = sequence.player;
        _startedAt = Time.realtimeSinceStartup;
        _nextSample = _startedAt;
        _sample = 0;
        _runs++;
        Plugin.LogSource.LogInfo($"Run-up camera diagnostic started (run {_runs}/3); reporting camera, player, aim, and tee orientation.");
    }

    internal static void Tick()
    {
        if (_sequence == null || _player == null || !CourseScenePatcher.IsImportedMapActive) { _player = null; _sequence = null; return; }
        var now = Time.realtimeSinceStartup;
        if (now < _nextSample) return;
        if (_sample >= 35 || now - _startedAt > 7f) { _player = null; _sequence = null; return; }
        _nextSample = now + 0.2f;
        _sample++;
        try
        {
            var camera = _player.GetGameCamera();
            var tee = _player.GetTeepad();
            var teeCamera = _player.GetTeeCamera();
            Plugin.LogSource.LogInfo($"Run-up camera diagnostic t={now - _startedAt:F2}s sample={_sample}: camera={Describe(camera == null ? null : camera.transform)}; teeCamera={Describe(teeCamera == null ? null : teeCamera.transform)}; player={Describe(_player.transform)}; aim={Describe(_player.aimHolder)}; tee={Describe(tee == null ? null : tee.transform)}; runup={Describe(_sequence.transform)}; running={_sequence.IsRunupSequenceCurrentlyOngoing()}.");
        }
        catch (Exception ex) { Plugin.LogSource.LogWarning($"Run-up camera diagnostic stopped: {ex.Message}"); _player = null; _sequence = null; }
    }

    private static string Describe(Transform transform)
    {
        if (transform == null) return "<missing>";
        var position = transform.position;
        var rotation = transform.eulerAngles;
        return $"{transform.name}[pos=({position.x:F1},{position.y:F1},{position.z:F1}),rot=({rotation.x:F1},{rotation.y:F1},{rotation.z:F1})]";
    }

    internal static void Reset() { _player = null; _sequence = null; _runs = 0; }
}

[HarmonyPatch(typeof(RunupSequence), nameof(RunupSequence.StartRunupSequence))]
internal static class RunupCameraDiagnosticsPatch
{
    private static void Postfix(RunupSequence __instance)
    {
        try { RunupCameraDiagnostics.Start(__instance); }
        catch (Exception ex) { Plugin.LogSource.LogWarning($"Could not start run-up camera diagnostic: {ex.Message}"); }
    }
}
