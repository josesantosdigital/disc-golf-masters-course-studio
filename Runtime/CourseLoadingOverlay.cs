using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DgmCustomCourses;

internal static class CourseLoadingOverlay
{
    private static GameObject _root;
    private static Text _message;
    private static EventSystem _eventSystem;
    private static bool _previousNavigationEnabled;
    private static bool _selectorLoading;
    private static float _nextMessageUpdate;
    private static bool _creationFailed;

    internal static bool SelectorLoading => _selectorLoading;
    internal static bool SelectorInteractionBlocked => _selectorLoading && _root != null;

    internal static void BeginSelector()
    {
        _selectorLoading = true;
        Refresh();
    }

    internal static void EndSelector()
    {
        _selectorLoading = false;
        Refresh();
    }

    internal static void Cancel()
    {
        _selectorLoading = false;
        try { if (_root != null) _root.SetActive(false); }
        finally { RestoreNavigation(); }
    }

    internal static void Tick()
    {
        if (!_selectorLoading) return;
        if (_selectorLoading && _root != null) DisableNavigation();
        else RestoreNavigation();
        if (_message == null || Time.unscaledTime < _nextMessageUpdate) return;
        _nextMessageUpdate = Time.unscaledTime + 0.4f;
        var dots = new string('.', 1 + (int)(Time.unscaledTime / 0.4f) % 3);
        _message.text = "Loading custom courses" + dots;
    }

    private static void Refresh()
    {
        var visible = _selectorLoading;
        if (visible)
        {
            EnsureCreated();
            if (_root != null) _root.SetActive(true);
            if (_selectorLoading && _root != null) DisableNavigation();
            else RestoreNavigation();
            _nextMessageUpdate = 0f;
            Tick();
        }
        else
        {
            if (_root != null) _root.SetActive(false);
            RestoreNavigation();
        }
    }

    private static void EnsureCreated()
    {
        if (_root != null || _creationFailed) return;
        try
        {
            _root = new GameObject("DGM_CustomCourseLoading", new Il2CppSystem.Type[]
            {
                Il2CppType.Of<RectTransform>(), Il2CppType.Of<Canvas>(),
                Il2CppType.Of<CanvasScaler>(), Il2CppType.Of<GraphicRaycaster>()
            });
            UnityEngine.Object.DontDestroyOnLoad(_root);
            var canvas = _root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = short.MaxValue;
            var scaler = _root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            var background = new GameObject("Background", new Il2CppSystem.Type[]
            {
                Il2CppType.Of<RectTransform>(), Il2CppType.Of<CanvasRenderer>(), Il2CppType.Of<Image>()
            });
            var backgroundRect = background.GetComponent<RectTransform>();
            backgroundRect.SetParent(_root.transform, false);
            backgroundRect.anchorMin = Vector2.zero;
            backgroundRect.anchorMax = Vector2.one;
            backgroundRect.offsetMin = Vector2.zero;
            backgroundRect.offsetMax = Vector2.zero;
            var image = background.GetComponent<Image>();
            image.color = new Color(0.025f, 0.04f, 0.035f, 0.96f);
            image.raycastTarget = true;

            var textObject = new GameObject("Message", new Il2CppSystem.Type[]
            {
                Il2CppType.Of<RectTransform>(), Il2CppType.Of<CanvasRenderer>(), Il2CppType.Of<Text>()
            });
            var textRect = textObject.GetComponent<RectTransform>();
            textRect.SetParent(backgroundRect, false);
            textRect.anchorMin = new Vector2(0.1f, 0.35f);
            textRect.anchorMax = new Vector2(0.9f, 0.65f);
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            _message = textObject.GetComponent<Text>();
            try { _message.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
            catch { _message.font = Font.CreateDynamicFontFromOSFont("Arial", 42); }
            if (_message.font == null)
                _message.font = Font.CreateDynamicFontFromOSFont("Arial", 42);
            if (_message.font == null)
                throw new InvalidOperationException("No font is available for the loading message.");
            _message.fontSize = 42;
            _message.alignment = TextAnchor.MiddleCenter;
            _message.color = Color.white;
            _message.raycastTarget = false;
        }
        catch (Exception ex)
        {
            _creationFailed = true;
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
            _message = null;
            Plugin.LogSource.LogWarning($"Could not create the custom-course loading screen: {ex.Message}");
        }
    }

    private static void DisableNavigation()
    {
        var current = EventSystem.current;
        if (current == null) return;
        if (_eventSystem != current)
        {
            RestoreNavigation();
            _eventSystem = current;
            _previousNavigationEnabled = current.sendNavigationEvents;
        }
        current.sendNavigationEvents = false;
    }

    private static void RestoreNavigation()
    {
        if (_eventSystem != null)
            _eventSystem.sendNavigationEvents = _previousNavigationEnabled;
        _eventSystem = null;
    }
}
