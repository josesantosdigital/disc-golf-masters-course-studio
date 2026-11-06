using BepInEx;
using Dgm;
using Il2CppInterop.Runtime;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace DgmCustomCourses;

internal static class MapSceneRuntime
{
    [StructLayout(LayoutKind.Sequential)]
    private struct ManagedSpanWrapper
    {
        public IntPtr Begin;
        public int Length;
    }

    private delegate IntPtr LoadFromFileUnity6Delegate(ref ManagedSpanWrapper path, uint crc, ulong offset);
    private delegate IntPtr LoadFromFileLegacyDelegate(IntPtr path, uint crc, ulong offset);

    private static LoadFromFileUnity6Delegate _loadFromFileUnity6;
    private static LoadFromFileLegacyDelegate _loadFromFileLegacy;
    private static readonly List<AssetBundle> LoadedBundles = new();
    private static bool _initialized;
    private static UnityAction<Scene, LoadSceneMode> _sceneLoadedCallback;
    private static UnityAction<Scene> _sceneUnloadedCallback;
    private static CustomCourseRecord _activeRecord;
    private static CourseManager _courseManager;
    private static PlayerBehaviour _player;
    private static int _startHoleIndex;
    private static string _scenePath = "";
    private static string _sceneName = "";
    private static string _baseSceneName = "";
    private static Scene _loadedScene;
    private static bool _hasLoadedScene;
    private static bool _applied;
    private static bool _applyAttempted;
    private static AsyncOperation _loadOperation;
    private static bool _preloadPending;
    private static int _preloadFrame;

    public static void Initialize()
    {
        if (_initialized)
            return;

        _sceneLoadedCallback = DelegateSupport.ConvertDelegate<UnityAction<Scene, LoadSceneMode>>(new Action<Scene, LoadSceneMode>(OnSceneLoaded));
        _sceneUnloadedCallback = DelegateSupport.ConvertDelegate<UnityAction<Scene>>(new Action<Scene>(OnSceneUnloaded));
        SceneManager.sceneLoaded += _sceneLoadedCallback;
        SceneManager.sceneUnloaded += _sceneUnloadedCallback;
        _initialized = true;
    }

    public static void BeginPreload(CustomCourseRecord custom)
    {
        if (custom?.Definition.MapScene == null)
            return;

        try
        {
            Initialize();

            if (string.Equals(_activeRecord?.Definition.Id, custom.Definition.Id, StringComparison.OrdinalIgnoreCase) &&
                (_preloadPending || LoadedBundles.Count > 0 || _hasLoadedScene))
            {
                TryApplyToRound();
                return;
            }

            CourseScenePatcher.RestoreSnapshots();
            Cleanup();
            _activeRecord = custom;
            _baseSceneName = custom.Layout?.sceneName ?? "";

            if (CourseLoadingDriver.Available)
            {
                _preloadPending = true;
                _preloadFrame = Time.frameCount + 2;
                return;
            }

            LoadPendingBundles(custom);
        }
        catch (Exception ex)
        {
            HandlePreloadFailure(custom, ex);
        }
    }

    public static void TickLoading()
    {
        if (_applied && _activeRecord != null)
            CourseScenePatcher.MaintainImportedCameraOcclusionSetting(_activeRecord.Definition.MapLighting, _activeRecord.Definition.Name);

        if (_preloadPending && Time.frameCount >= _preloadFrame)
        {
            _preloadPending = false;
            var custom = _activeRecord;
            if (custom != null)
            {
                try { LoadPendingBundles(custom); }
                catch (Exception ex) { HandlePreloadFailure(custom, ex); }
            }
        }

        if (_hasLoadedScene && _courseManager != null && !_applied && !_applyAttempted)
            TryApplyToRound();

    }

    public static void FailLoading(Exception ex) => HandlePreloadFailure(_activeRecord, ex);

    private static void LoadPendingBundles(CustomCourseRecord custom)
    {
        if (custom?.Definition.MapScene == null)
            return;

        foreach (var relativePath in custom.Definition.MapScene.DependencyBundlePaths)
        {
            var dependencyPath = ResolvePackagePath(relativePath);
            if (!File.Exists(dependencyPath))
                throw new FileNotFoundException($"Required imported map dependency '{relativePath}' was not found.", dependencyPath);

            var dependency = LoadBundleFromFile(dependencyPath);
            if (dependency == null)
                throw new InvalidDataException($"Unity rejected required imported map dependency '{relativePath}'.");

            LoadedBundles.Add(dependency);
            Plugin.LogSource.LogInfo($"Loaded required imported map dependency '{relativePath}'.");
        }

        var bundlePath = ResolvePackagePath(custom.Definition.MapScene.BundlePath);
        if (!File.Exists(bundlePath))
            throw new FileNotFoundException("The imported map scene bundle was not found.", bundlePath);

        var sceneBundle = LoadBundleFromFile(bundlePath);
        if (sceneBundle == null)
            throw new InvalidDataException("Unity rejected the imported map scene bundle.");
        LoadedBundles.Add(sceneBundle);
        Plugin.LogSource.LogInfo($"Loaded imported map scene bundle '{custom.Definition.MapScene.BundlePath}'.");

        var requestedPath = NormalizeScenePath(custom.Definition.MapScene.ScenePath);
        _scenePath = requestedPath;

        _sceneName = Path.GetFileNameWithoutExtension(_scenePath);
        _loadOperation = SceneManager.LoadSceneAsync(_scenePath, LoadSceneMode.Additive);
        if (_loadOperation == null)
            throw new InvalidOperationException($"Unity couldn't start loading scene '{_scenePath}'.");

        Plugin.LogSource.LogInfo($"Loading imported map scene '{_sceneName}'.");
    }

    private static void HandlePreloadFailure(CustomCourseRecord custom, Exception ex)
    {
        Plugin.LogSource.LogError($"Imported map '{custom?.Definition.Name ?? "<unknown>"}' could not be loaded: {ex}");
        ReleaseBundles();
        _activeRecord = null;
        _scenePath = "";
        _sceneName = "";
        _baseSceneName = "";
        _loadOperation = null;
        _preloadPending = false;
    }

    public static void SetRoundContext(CourseManager courseManager, PlayerBehaviour player, int startHoleIndex, CustomCourseRecord custom)
    {
        if (custom?.Definition.MapScene == null)
            return;

        if (!string.Equals(_activeRecord?.Definition.Id, custom.Definition.Id, StringComparison.OrdinalIgnoreCase) ||
            (!_preloadPending && LoadedBundles.Count == 0 && !_hasLoadedScene))
            BeginPreload(custom);

        _courseManager = courseManager;
        _player = player;
        _startHoleIndex = startHoleIndex;
        _activeRecord = custom;
        TryApplyToRound();
    }

    public static void Cleanup()
    {
        if (_hasLoadedScene && _loadedScene.isLoaded)
        {
            var baseScene = FindBaseCourseScene();
            var activeScene = SceneManager.GetActiveScene();
            if (baseScene.isLoaded && activeScene.handle == _loadedScene.handle)
                SceneManager.SetActiveScene(baseScene);
            SceneManager.UnloadSceneAsync(_loadedScene);
        }

        ReleaseBundles();
        _activeRecord = null;
        _courseManager = null;
        _player = null;
        _startHoleIndex = 0;
        _scenePath = "";
        _sceneName = "";
        _baseSceneName = "";
        _loadedScene = default;
        _hasLoadedScene = false;
        _applied = false;
        _applyAttempted = false;
        _loadOperation = null;
        _preloadPending = false;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (string.IsNullOrWhiteSpace(_sceneName) || !string.Equals(scene.name, _sceneName, StringComparison.OrdinalIgnoreCase))
            return;

        _loadedScene = scene;
        _hasLoadedScene = true;
        _loadOperation = null;
        TryApplyToRound();
    }

    private static void OnSceneUnloaded(Scene scene)
    {
        if (!string.IsNullOrWhiteSpace(_baseSceneName) && string.Equals(scene.name, _baseSceneName, StringComparison.OrdinalIgnoreCase))
        {
            Cleanup();
            return;
        }

        if (!string.IsNullOrWhiteSpace(_sceneName) && string.Equals(scene.name, _sceneName, StringComparison.OrdinalIgnoreCase))
        {
            ReleaseBundles();
            _sceneName = "";
            _scenePath = "";
            _loadedScene = default;
            _hasLoadedScene = false;
            _applied = false;
            _applyAttempted = false;
        }
    }

    private static void TryApplyToRound()
    {
        if (_activeRecord == null || _courseManager == null || string.IsNullOrWhiteSpace(_sceneName))
            return;

        if (!_hasLoadedScene || !_loadedScene.isLoaded)
            return;

        if (_applied || _applyAttempted || _courseManager.holeList == null || _courseManager.holeList.Length == 0)
            return;

        var courseName = _activeRecord.Definition.Name;
        _applyAttempted = true;
        try
        {
            _applied = CourseScenePatcher.ApplyImportedMap(_courseManager, _player, _startHoleIndex, _activeRecord, _loadedScene);
            if (!_applied)
            {
                Plugin.LogSource.LogWarning($"Imported map '{_activeRecord.Definition.Name}' could not complete course setup.");
                RollBackFailedCourseSetup(courseName);
            }
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogError($"Imported map scene loaded, but DGM course setup failed: {ex}");
            RollBackFailedCourseSetup(courseName);
        }
    }

    private static void RollBackFailedCourseSetup(string courseName)
    {
        Plugin.LogSource.LogWarning($"Restoring the base course and unloading imported map '{courseName}' after setup failed.");
        try
        {
            CourseScenePatcher.RestoreAll();
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogError($"Imported map '{courseName}' rollback did not complete cleanly: {ex}");
            try { CourseScenePatcher.RestoreSnapshots(); }
            catch (Exception restoreException)
            {
                Plugin.LogSource.LogError($"Imported map '{courseName}' snapshot retry also failed: {restoreException}");
            }
            try { Cleanup(); }
            catch (Exception cleanupException)
            {
                Plugin.LogSource.LogError($"Imported map '{courseName}' fallback cleanup also failed: {cleanupException}");
            }
        }
    }

    private static string ResolvePackagePath(string relativePath)
    {
        var root = Path.GetFullPath(Path.Combine(Paths.PluginPath, "CustomCourseLoader"));
        var candidate = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar)));
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? root
            : root + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Map package path escapes the CustomCourseLoader folder.");
        return candidate;
    }

    private static string NormalizeScenePath(string path) => (path ?? "").Replace('\\', '/').Trim();

    private static AssetBundle LoadBundleFromFile(string path)
    {
        if (_loadFromFileUnity6 == null)
        {
            try
            {
                _loadFromFileUnity6 = IL2CPP.ResolveICall<LoadFromFileUnity6Delegate>(
                    "UnityEngine.AssetBundle::LoadFromFile_Internal_Injected(System.String,System.UInt32,System.UInt64)");
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogDebug($"Unity 6 AssetBundle entry point is unavailable: {ex.Message}");
            }
        }

        if (_loadFromFileUnity6 != null)
        {
            var characters = path.ToCharArray();
            var pinnedCharacters = GCHandle.Alloc(characters, GCHandleType.Pinned);
            try
            {
                var managedPath = new ManagedSpanWrapper
                {
                    Begin = pinnedCharacters.AddrOfPinnedObject(),
                    Length = characters.Length
                };
                Plugin.LogSource.LogDebug($"Calling Unity 6 native AssetBundle loader for '{path}'.");
                var bundleHandle = _loadFromFileUnity6(ref managedPath, 0, 0);
                if (bundleHandle == IntPtr.Zero)
                    return null;

                var bundlePointer = IL2CPP.il2cpp_gchandle_get_target(bundleHandle);
                return bundlePointer == IntPtr.Zero ? null : new AssetBundle(bundlePointer);
            }
            finally
            {
                pinnedCharacters.Free();
            }
        }

        if (_loadFromFileLegacy == null)
        {
            try
            {
                _loadFromFileLegacy = IL2CPP.ResolveICall<LoadFromFileLegacyDelegate>(
                    "UnityEngine.AssetBundle::LoadFromFile_Internal(System.String,System.UInt32,System.UInt64)");
            }
            catch (Exception ex)
            {
                throw new MissingMethodException("Unity's native AssetBundle.LoadFromFile entry points could not be resolved.", ex);
            }
        }

        var nativePath = IL2CPP.ManagedStringToIl2Cpp(path);
        if (nativePath == IntPtr.Zero)
            throw new InvalidOperationException("Could not marshal the AssetBundle path to IL2CPP.");
        var legacyPointer = _loadFromFileLegacy(nativePath, 0, 0);
        return legacyPointer == IntPtr.Zero ? null : new AssetBundle(legacyPointer);
    }

    private static Scene FindBaseCourseScene()
    {
        if (_courseManager?.holeList == null)
            return default;
        for (var i = 0; i < _courseManager.holeList.Length; i++)
        {
            var hole = _courseManager.holeList[i];
            if (hole != null && hole.gameObject.scene.isLoaded)
                return hole.gameObject.scene;
        }
        return default;
    }

    private static void ReleaseBundles()
    {
        foreach (var bundle in LoadedBundles)
        {
            if (bundle != null)
                bundle.Unload(false);
        }
        LoadedBundles.Clear();
    }
}
