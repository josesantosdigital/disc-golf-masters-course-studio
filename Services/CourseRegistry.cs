using Dgm;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.UI;

namespace DgmCustomCourses;

internal sealed partial class CourseRegistry
{
    private readonly string _folder;
    private readonly BepInEx.Logging.ManualLogSource _log;
    private readonly CourseCardFactory _courseCardFactory;
    private readonly List<CustomCourseRecord> _records = new();
    private readonly HashSet<string> _addedCourseEntries = new(StringComparer.OrdinalIgnoreCase);
    private CourseSelectionViewController _activeCourseSelectionView;
    private CourseSelectionViewController _scrollResetView;
    private float _nextCourseSelectionLookupTime;
    private int _layoutRefreshFrames;
    private float _nextLayoutRefresh;
    private bool _scrollUserInteracted;
    private bool _loggedSoloCourseListHook;
    private bool _loggedScrollReset;
    private CourseSelectionViewController _pendingCourseSelectionView;
    private readonly CourseSelectionLoadGate _selectionLoadGate = new();
    private bool _selectorBuilding;

    public IReadOnlyList<CourseDefinition> Definitions => _records.Select(record => record.Definition).ToArray();

    public CourseRegistry(string folder, BepInEx.Logging.ManualLogSource log)
    {
        _folder = folder;
        _log = log;
        _courseCardFactory = new CourseCardFactory(log);
    }

    public void LoadDefinitions()
    {
        Directory.CreateDirectory(_folder);
        foreach (var path in Directory.GetFiles(_folder, "*.json").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var definition = CourseDefinition.Read(path);
                if (_records.Any(record => string.Equals(record.Definition.Id, definition.Id, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException($"Duplicate course id '{definition.Id}'.");

                var numericId = MakeRuntimeId(definition.Id);
                while (_records.Any(record => record.RuntimeId == numericId))
                    numericId++;
                _records.Add(new CustomCourseRecord(definition, numericId));
            }
            catch (Exception ex)
            {
                _log.LogError($"Could not load course file '{Path.GetFileName(path)}': {ex.Message}");
            }
        }
    }

    public void RegisterLayouts(Il2CppSystem.Collections.Generic.List<CourseLayout> loadedLayouts)
    {
        if (loadedLayouts == null)
            return;

        foreach (var record in _records)
        {
            if (record.Layout != null)
                continue;

            if (!Enum.TryParse<CourseLayout.CourseID>(record.Definition.BaseCourse, true, out var baseCourseId))
            {
                if (!record.BaseCourseWarningLogged)
                {
                    _log.LogWarning($"Course '{record.Definition.Id}' names unknown baseCourse '{record.Definition.BaseCourse}'.");
                    record.BaseCourseWarningLogged = true;
                }
                continue;
            }

            CourseLayout source = null;
            for (var i = 0; i < loadedLayouts.Count; i++)
            {
                var candidate = loadedLayouts[i];
                if (candidate != null && candidate.courseID == baseCourseId &&
                    (string.IsNullOrWhiteSpace(record.Definition.BaseLayout) ||
                     string.Equals(candidate.layoutID, record.Definition.BaseLayout, StringComparison.OrdinalIgnoreCase)))
                {
                    source = candidate;
                    break;
                }
            }

            if (source == null)
            {
                if (!record.BaseCourseWarningLogged)
                {
                    _log.LogWarning($"Course '{record.Definition.Id}' is waiting for base course '{record.Definition.BaseCourse}'.");
                    record.BaseCourseWarningLogged = true;
                }
                continue;
            }

            try
            {
                record.Layout = UnityEngine.Object.Instantiate(source);
                record.Layout.name = $"DGM_Custom_{record.Definition.Id}";
                record.Layout.courseID = (CourseLayout.CourseID)record.RuntimeId;
                record.Layout.displayName = record.Definition.Name;
                record.Layout.layoutID = $"custom_{record.Definition.Id.ToLowerInvariant()}";
                record.Layout.sceneName = source.sceneName;
                record.Layout.coursePar = record.Definition.Holes.Sum(hole => hole.Par);
                record.Layout.courseLength = record.Definition.Holes.Sum(hole => hole.LengthMeters);
                record.Layout.holePinVariants = BuildHoleVariants(record.Definition.Holes);
                try { record.Layout.courseLogo = CustomCourseBadge.GetSprite(); }
                catch (Exception ex) { _log.LogWarning($"Could not create the custom course badge: {ex.Message}"); }
                var courseCardName = record.Definition.CourseCardImage;
                if (string.IsNullOrWhiteSpace(courseCardName))
                    courseCardName = record.Definition.Holes.FirstOrDefault(hole => !string.IsNullOrWhiteSpace(hole.CardImage))?.CardImage ?? "";
                var card = _courseCardFactory.LoadCourseCard(record.Definition, courseCardName);
                if (card != null)
                {
                    record.Layout.coursePreviewImageTexture = card;
                    record.Layout.layoutPreviewImageTexture = card;
                    record.CourseCardTexture = card;
                    record.CourseCardSprite = Sprite.Create(card, new Rect(0, 0, card.width, card.height), new Vector2(0.5f, 0.5f));
                }
                _log.LogInfo($"Registered custom course '{record.Definition.Name}' on scene '{record.Layout.sceneName}'.");
            }
            catch (Exception ex)
            {
                record.Layout = null;
                _log.LogError($"Could not create course '{record.Definition.Id}': {ex}");
            }
        }
    }

    public void SetRenderedCourseCard(CustomCourseRecord record, string path)
    {
        if (record?.Layout == null) return;
        try
        {
            var texture = CourseCardFactory.LoadPngCourseCard(path, record.Definition.Id);
            var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            _courseCardFactory.RememberRenderedCard(record.Definition, texture);
            record.CourseCardTexture = texture;
            record.CourseCardSprite = sprite;
            record.Layout.coursePreviewImageTexture = texture;
            record.Layout.layoutPreviewImageTexture = texture;
        }
        catch (Exception ex) { _log.LogWarning($"Could not load the new rendered card for '{record.Definition.Name}': {ex.Message}"); }
    }

    internal void RefreshAutoLayoutMetadata(CustomCourseRecord record)
    {
        if (record?.Layout == null) return;
        record.Layout.holePinVariants = BuildHoleVariants(record.Definition.Holes);
        record.Layout.coursePar = record.Definition.Holes.Sum(hole => hole.Par);
        record.Layout.courseLength = record.Definition.Holes.Sum(hole => hole.LengthMeters);
    }

    public void AppendLayouts(Il2CppSystem.Collections.Generic.List<CourseLayout> loadedLayouts)
    {
        if (_pendingCourseSelectionView != null && !_selectorBuilding)
            return;

        CourseCatalogExporter.ExportBuiltInCourses(loadedLayouts);
        RegisterLayouts(loadedLayouts);
        foreach (var record in _records)
        {
            if (record.Layout != null && !ContainsReference(loadedLayouts, record.Layout))
                loadedLayouts.Add(record.Layout);
        }

        if (_activeCourseSelectionView != null)
        {
            if (_selectorBuilding)
                AppendRegisteredCourseEntries(_activeCourseSelectionView);
            else
                SetUpCourseSelection(_activeCourseSelectionView);
        }

    }

    public void AppendCourseIds(Il2CppSystem.Collections.Generic.List<CourseLayout.CourseID> courseIds)
    {
        if (_pendingCourseSelectionView != null && !_selectorBuilding)
            return;
        foreach (var record in _records)
        {
            if (record.Layout == null)
                _ = CourseLayout.GetAllLayouts();
            if (record.Layout == null)
                continue;

            var id = (CourseLayout.CourseID)record.RuntimeId;
            var present = false;
            for (var i = 0; i < courseIds.Count; i++)
            {
                if (courseIds[i] == id)
                {
                    present = true;
                    break;
                }
            }
            if (!present)
                courseIds.Add(id);
        }
    }

    public void EnsureLayoutsRegistered()
    {
        if (!_records.Any(record => record.Layout == null))
            return;

        var layouts = CourseLayout.GetAllLayouts();
        if (layouts != null)
            RegisterLayouts(layouts);
    }

    public CustomCourseRecord FindByRuntimeId(int runtimeId) => _records.FirstOrDefault(record => record.RuntimeId == runtimeId && record.Layout != null);

    public CustomCourseRecord FindByLayoutId(string layoutId) => _records.FirstOrDefault(record =>
        record.Layout != null && string.Equals(record.Layout.layoutID, layoutId, StringComparison.OrdinalIgnoreCase));

    public CustomCourseRecord FindByCourseId(CourseLayout.CourseID courseId) => FindByRuntimeId((int)courseId);

    public Il2CppSystem.Collections.Generic.List<CourseLayout> CreateLayoutList(CustomCourseRecord record)
    {
        var result = new Il2CppSystem.Collections.Generic.List<CourseLayout>();
        if (record.Layout != null)
            result.Add(record.Layout);
        return result;
    }

    private static Il2CppReferenceArray<CourseManager.PinVariantListEntry> BuildHoleVariants(IReadOnlyList<HoleDefinition> holes)
    {
        var entries = new Il2CppReferenceArray<CourseManager.PinVariantListEntry>(holes.Count);
        for (var i = 0; i < holes.Count; i++)
        {
            var hole = holes[i];
            var entry = new CourseManager.PinVariantListEntry
            {
                name = $"Hole {i + 1}",
                par = hole.Par,
                holeLength = hole.LengthMeters,
                pinVariant = ParseVariant(hole.PinVariant),
                teeVariant = ParseVariant(hole.TeeVariant)
            };
            entries[i] = entry;
        }
        return entries;
    }

    internal static BasketBehaviour.PinVariant ParseVariant(string name) =>
        Enum.TryParse(name, true, out BasketBehaviour.PinVariant variant) ? variant : BasketBehaviour.PinVariant.Default;

    private static bool ContainsReference(Il2CppSystem.Collections.Generic.List<CourseLayout> list, CourseLayout value)
    {
        for (var i = 0; i < list.Count; i++)
            if (ReferenceEquals(list[i], value))
                return true;
        return false;
    }

    private static int MakeRuntimeId(string id)
    {
        uint hash = 2166136261;
        foreach (var b in System.Text.Encoding.UTF8.GetBytes(id.ToLowerInvariant()))
        {
            hash ^= b;
            hash *= 16777619;
        }
        return 10000 + (int)(hash % 1000000);
    }
}

internal sealed class CustomCourseRecord
{
    public CourseDefinition Definition { get; }
    public IReadOnlyList<int> LayoutTargetLengths { get; }
    public int RuntimeId { get; }
    public CourseLayout Layout { get; set; }
    public Texture2D CourseCardTexture { get; set; }
    public Sprite CourseCardSprite { get; set; }
    public bool BaseCourseWarningLogged { get; set; }

    public CustomCourseRecord(CourseDefinition definition, int runtimeId)
    {
        Definition = definition;
        LayoutTargetLengths = Array.AsReadOnly(definition.Holes.Select(hole => hole.LengthMeters).ToArray());
        RuntimeId = runtimeId;
    }
}
