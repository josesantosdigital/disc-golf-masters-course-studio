using Dgm;
using HarmonyLib;
using Unity.Cinemachine;
using UnityEngine;

namespace DgmCustomCourses;

// Some imported tees leave the aiming rig several metres to one side of the
// character. Move only the tee camera while loading or running; do not
// rotate it or move the player, aim holder, or run-up sequence.
[HarmonyPatch(typeof(TeeCamera), "Update")]
internal static class ImportedRunupCameraPatch
{
    private const float StartLateralMismatch = 0.15f;
    private const float FullLateralMismatch = 1.25f;
    private const float MaxLateralMismatch = 6f;
    private const float DesiredDistanceBehindPlayer = 4.5f;
    private const float MaxBackwardAdjustment = 5f;
    private static Transform _adjustedCamera;
    private static Vector3 _localAdjustment;
    private static bool _warningLogged;
    private static PlayerBehaviour _runupPlayer;
    private static bool _discReleased;
    private static float _releasedAt;

    private static void Prefix() => Restore();

    private static void Postfix(TeeCamera __instance)
    {
        if (!CourseScenePatcher.IsImportedMapActive || __instance == null) return;
        try
        {
            var player = __instance.player;
            var sequence = player == null ? null : player.GetRunupSequence();
            var loading = player != null && player.playerInput != null && player.playerInput.isLoadingRunup;
            if (sequence == null || player.aimHolder == null) return;
            var running = sequence.IsRunupSequenceCurrentlyOngoing();
            if (running && !_discReleased) _runupPlayer = player;
            if (_discReleased && _runupPlayer == player)
            {
                // Cinemachine keeps the tee camera live during the blend to
                // the flight camera; restoring it mid-blend causes a jump.
                var cameraStillLive = __instance.cineCam != null && CinemachineCore.IsLive(__instance.cineCam.Cast<ICinemachineCamera>());
                if (!cameraStillLive || Time.realtimeSinceStartup - _releasedAt > 3f || player.playerState != PlayerState.InFlight)
                {
                    _runupPlayer = null;
                    _discReleased = false;
                    return;
                }
            }
            var pendingRelease = _runupPlayer == player &&
                (player.playerState == PlayerState.RunupOngoing || player.playerState == PlayerState.InFlight);
            if (!loading && !running && !pendingRelease)
            {
                if (_runupPlayer == player) { _runupPlayer = null; _discReleased = false; }
                return;
            }

            var camera = __instance.transform;
            if (camera == null || camera.parent == null) return;
            var forward = camera.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f) return;
            forward.Normalize();
            var right = Vector3.Cross(Vector3.up, forward);
            var lateral = Vector3.Dot(player.transform.position - player.aimHolder.position, right);
            if (!float.IsFinite(lateral) || Mathf.Abs(lateral) <= StartLateralMismatch || Mathf.Abs(lateral) > MaxLateralMismatch) return;
            var weight = Mathf.InverseLerp(StartLateralMismatch, FullLateralMismatch, Mathf.Abs(lateral));
            var behind = Vector3.Dot(player.transform.position - camera.position, forward);
            if (!float.IsFinite(behind)) return;
            var backward = Mathf.Clamp(DesiredDistanceBehindPlayer - behind, 0f, MaxBackwardAdjustment);
            var worldAdjustment = (right * lateral - forward * backward) * weight;
            _localAdjustment = camera.parent.InverseTransformVector(worldAdjustment);
            _adjustedCamera = camera;
            camera.localPosition += _localAdjustment;
        }
        catch (Exception ex)
        {
            Restore();
            if (!_warningLogged)
            {
                _warningLogged = true;
                Plugin.LogSource.LogWarning($"Could not frame the imported-course run-up: {ex.Message}");
            }
        }
    }

    internal static void Restore()
    {
        if (_adjustedCamera != null) _adjustedCamera.localPosition -= _localAdjustment;
        _adjustedCamera = null;
        _localAdjustment = Vector3.zero;
    }

    internal static void EndRunup(PlayerBehaviour player)
    {
        if (_runupPlayer != player) return;
        _discReleased = true;
        _releasedAt = Time.realtimeSinceStartup;
    }

    internal static void Reset()
    {
        Restore();
        _runupPlayer = null;
        _discReleased = false;
    }
}

// The run-up movement ends before the throw animation releases the disc.
// After the release callback, retain the correction while the tee camera is
// still live in Cinemachine's transition to the flight camera.
[HarmonyPatch(typeof(PlayerBehaviour), "OnThrowingSequenceFinished")]
internal static class ImportedRunupCameraThrowReleasedPatch
{
    private static void Postfix(PlayerBehaviour __instance) => ImportedRunupCameraPatch.EndRunup(__instance);
}
