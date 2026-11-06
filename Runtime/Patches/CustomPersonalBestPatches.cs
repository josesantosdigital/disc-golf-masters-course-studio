using Dgm;
using Dgm.Modules.Services.Economy.Core;
using Dgm.UI.Components;
using HarmonyLib;
using UnityEngine;

namespace DgmCustomCourses;

[HarmonyPatch(typeof(SoloRoundManager), nameof(SoloRoundManager.HandleRoundCompleted))]
internal static class CustomPersonalBestRecordPatch
{
    private static void Prefix(SoloRoundManager __instance)
    {
        try
        {
            var layout = __instance == null ? null : __instance.GetCurrentCourseLayout();
            var custom = layout == null ? null : Plugin.Registry.FindByCourseId(layout.courseID);
            if (custom == null || !string.Equals(layout.layoutID, custom.Layout.layoutID, StringComparison.OrdinalIgnoreCase)) return;

            var expected = custom.Definition.Holes.Count;
            var round = __instance.GetCurrentRoundScore();
            var id = __instance.GetLocalClientID();
            var scores = round == null ? null : round.GetAllHoleScores(id);
            var strokes = new List<int>();
            if (scores != null)
                foreach (var holeScore in scores) strokes.Add(holeScore.score);
            if (!CustomPersonalBests.IsCompleteRound(expected, __instance.GetNumberOfHolesInRound(), strokes))
            {
                Plugin.LogSource.LogWarning($"Did not save a personal best for '{custom.Definition.Id}': the round does not contain scores for all {expected} holes.");
                return;
            }
            var score = __instance.GetCurrentTotalStrokesInRelationToCoursePar(id);
            if (Plugin.PersonalBests.Record(layout.layoutID, score))
                Plugin.LogSource.LogInfo($"Saved a local custom personal best for '{custom.Definition.Id}': {score:+#;-#;0} relative to par.");
        }
        catch (Exception ex) { Plugin.LogSource.LogWarning($"Could not record a custom personal best: {ex.Message}"); }
    }
}

[HarmonyPatch(typeof(SoloConfiguration), nameof(SoloConfiguration.GetPersonalBest))]
internal static class CustomPersonalBestReadPatch
{
    private static bool Prefix(string __0, ref Il2CppSystem.Threading.Tasks.Task<int> __result)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(__0) || Plugin.Registry.FindByLayoutId(__0) == null || !Plugin.PersonalBests.TryGet(__0, out var best))
                return true;
            __result = Il2CppSystem.Threading.Tasks.Task.FromResult(best);
            return false;
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Could not supply a local custom personal best: {ex.Message}");
            return true;
        }
    }
}

// A custom course has no reward item. Let the game's progress coroutine
// continue instead of dereferencing the missing item in its reward UI.
[HarmonyPatch(typeof(ItemDefinitionDisplayUI), nameof(ItemDefinitionDisplayUI.Setup))]
internal static class CustomCourseMissingRewardPatch
{
    private static readonly HashSet<int> HiddenByLoader = new();

    private static void RestoreIfHidden(ItemDefinitionDisplayUI instance)
    {
        if (instance != null && HiddenByLoader.Remove(instance.GetInstanceID()))
            instance.gameObject.SetActive(true);
    }

    private static bool Prefix(ItemDefinitionDisplayUI __instance, ItemDefinition __0)
    {
        if (__instance == null) return true;
        try
        {
            if (__0 != null)
            {
                RestoreIfHidden(__instance);
                return true;
            }
            var view = __instance.GetComponentInParent<SoloModeViewController>(true);
            if (view == null) view = UnityEngine.Object.FindObjectOfType<SoloModeViewController>(true);
            var layout = view == null || view.selectedLayout == null ? null : view.selectedLayout.layout;
            var course = view == null ? null : view.selectedCourse;
            var custom = layout == null ? null : Plugin.Registry.FindByCourseId(layout.courseID);
            if (custom == null && course != null) custom = Plugin.Registry.FindByLayoutId(course.courseId);
            if (custom == null || view == null || (view.layoutRewardDisplay != __instance && view.courseRewardDisplay != __instance))
            {
                RestoreIfHidden(__instance);
                return true;
            }
            HiddenByLoader.Add(__instance.GetInstanceID());
            __instance.gameObject.SetActive(false);
            return false;
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Could not hide a missing custom-course reward: {ex.Message}");
            try { RestoreIfHidden(__instance); } catch { }
            return true;
        }
    }
}
