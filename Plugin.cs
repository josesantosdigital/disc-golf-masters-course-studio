using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace DgmCustomCourses;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInProcess("Disc Golf Masters.exe")]
public sealed class Plugin : BasePlugin
{
    public const string PluginGuid = "local.dgm.customcourse.loader";
    public const string PluginName = "Disc Golf Masters Custom Course Loader";
    public const string PluginVersion = "0.5.110";

    internal static ManualLogSource LogSource { get; private set; }
    internal static CourseRegistry Registry { get; private set; }
    internal static CustomPersonalBests PersonalBests { get; private set; }

    public override void Load()
    {
        LogSource = Log;
        PersonalBests = new CustomPersonalBests(Path.Combine(Paths.ConfigPath, "CustomCourseLoader.personal-bests.json"), message => LogSource.LogWarning(message));
        Log.LogInfo("Initializing the custom course loader.");
        var courseFolder = Path.Combine(Paths.PluginPath, "CustomCourseLoader", "Courses");
        Registry = new CourseRegistry(courseFolder, LogSource);
        Registry.LoadDefinitions();
        Log.LogInfo($"Parsed {Registry.Definitions.Count} custom course definition(s).");

        try
        {
            var loadingDriver = AddComponent<CourseLoadingDriver>();
            UnityEngine.Object.DontDestroyOnLoad(loadingDriver.gameObject);
            CourseLoadingDriver.Available = true;
        }
        catch (Exception ex)
        {
            Log.LogWarning($"Loading screen driver is unavailable; loading will continue synchronously: {ex.Message}");
        }

        new Harmony(PluginGuid).PatchAll(typeof(Plugin).Assembly);
        Log.LogInfo("Installed custom course patches.");

        Log.LogInfo($"Loaded {Registry.Definitions.Count} custom course definition(s). Open the Offline course selector to play them.");
    }
}
