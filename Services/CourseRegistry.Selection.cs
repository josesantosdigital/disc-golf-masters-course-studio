using Dgm;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.UI;

namespace DgmCustomCourses;

internal sealed partial class CourseRegistry
{
    public void SetActiveCourseSelectionView(CourseSelectionViewController view)
    {
        if (view != null)
            _activeCourseSelectionView = view;
    }

    public void SetUpCourseSelection(CourseSelectionViewController view)
    {
        if (view == null)
            return;

        _activeCourseSelectionView = view;
        if (_selectorBuilding || _pendingCourseSelectionView == view || !NeedsCourseEntries(view))
            return;

        _pendingCourseSelectionView = view;
        _scrollResetView = view;
        _selectionLoadGate.Begin(Time.frameCount, Time.unscaledTime);
        CourseLoadingOverlay.BeginSelector();
        if (!CourseLoadingDriver.Available)
            PreparePendingCourseSelection();
    }

    private bool NeedsCourseEntries(CourseSelectionViewController view)
    {
        var viewPointer = view.Pointer;
        return _records.Any(record =>
            (record.Layout == null && !record.BaseCourseWarningLogged) ||
            (record.Layout != null && !_addedCourseEntries.Contains($"{viewPointer}:custom_{record.Definition.Id.ToLowerInvariant()}")));
    }

    public void TickCourseSelectionLoading()
    {
        if (_pendingCourseSelectionView != null && _selectionLoadGate.ReadyToPrepare(Time.frameCount))
            PreparePendingCourseSelection();

        if (CourseLoadingOverlay.SelectorLoading && _selectionLoadGate.TimedOut(Time.unscaledTime))
        {
            _log.LogWarning("Course list loading timed out while waiting for its layout; releasing the selector.");
            FinishCourseSelectionLoading();
        }
    }

    private void PreparePendingCourseSelection()
    {
        var view = _pendingCourseSelectionView;
        _pendingCourseSelectionView = null;
        if (view == null)
        {
            FinishCourseSelectionLoading();
            return;
        }

        _selectorBuilding = true;
        try
        {
            EnsureLayoutsRegistered();
            AppendRegisteredCourseEntries(view);
            if (_layoutRefreshFrames <= 0 || !CourseLoadingDriver.Available)
                FinishCourseSelectionLoading();
        }
        catch (Exception ex)
        {
            _log.LogWarning($"Could not prepare custom course entries: {ex}");
            FinishCourseSelectionLoading();
        }
        finally { _selectorBuilding = false; }
    }

    private void FinishCourseSelectionLoading()
    {
        _pendingCourseSelectionView = null;
        _layoutRefreshFrames = 0;
        _selectionLoadGate.End();
        CourseLoadingOverlay.EndSelector();
    }

    public void CancelCourseSelectionLoading() => FinishCourseSelectionLoading();

    public void AppendCourseEntries(CourseSelectionViewController view)
    {
        if (view == null)
            return;

        SetUpCourseSelection(view);
    }

    private void AppendRegisteredCourseEntries(CourseSelectionViewController view)
    {
        if (view == null)
            return;

        var viewPointer = view.Pointer;
        var addedAny = false;
        foreach (var record in _records)
        {
            if (record.Layout == null)
                continue;

            var courseId = $"custom_{record.Definition.Id.ToLowerInvariant()}";
            var entryKey = $"{viewPointer}:{courseId}";
            if (!_addedCourseEntries.Add(entryKey))
                continue;

            try
            {
                view.SetupCourseEntry(CreateSoloCourse(record, courseId));
                addedAny = true;
                _log.LogInfo($"Added '{record.Definition.Name}' to the offline course selector.");
            }
            catch (Exception ex)
            {
                _addedCourseEntries.Remove(entryKey);
                _log.LogError($"Could not add '{record.Definition.Name}' to the offline course selector: {ex}");
            }
        }
        if (addedAny)
            RequestCourseSelectionScrollToTop(view);
    }

    public void RequestCourseSelectionScrollToTop(CourseSelectionViewController view)
    {
        if (view == null)
            return;

        _scrollResetView = view;
        _layoutRefreshFrames = 24;
        _nextLayoutRefresh = 0f;
        _scrollUserInteracted = false;
        _selectionLoadGate.ResetLayout();
    }

    public void ResetCourseEntryScrollImmediately(CourseSelectionViewController view)
    {
        if (view == null || view.courseEntryRoot == null)
            return;

        RequestCourseSelectionScrollToTop(view);
    }

    public void ResetCourseSelectionScrollIfPending(ScrollRect scroll)
    {
        if (scroll == null || _scrollResetView == null || _layoutRefreshFrames <= 0 ||
            !scroll.vertical || scroll.content == null)
            return;

        if (!IsCourseEntryScroll(scroll, out var scrollContent))
            return;

        if (Time.unscaledTime < _nextLayoutRefresh)
            return;
        _nextLayoutRefresh = Time.unscaledTime + 0.25f;
        _layoutRefreshFrames--;
        var operation = "get course-root RectTransform";
        try
        {
            var root = _scrollResetView.courseEntryRoot.GetComponent<RectTransform>();
            if (root == null)
                return;
            operation = "force canvas update";
            Canvas.ForceUpdateCanvases();
            operation = "rebuild course-root layout";
            LayoutRebuilder.ForceRebuildLayoutImmediate(root);
            if (root != scroll.content)
            {
                operation = "rebuild ScrollRect content layout";
                LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
            }
            operation = "read preferred layout height";
            var preferred = LayoutUtility.GetPreferredHeight(root);
            var viewportHeight = scroll.viewport == null ? 0f : scroll.viewport.rect.height;
            // CalculateRelativeRectTransformBounds is not supported by the
            // installed IL2CPP unstripping layer. The rebuilt layout's
            // preferred height and root rect cover the selector content.
            var rootHeight = root.rect.height;
            operation = "calculate required content height";
            var required = Mathf.Max(viewportHeight, Mathf.Max(preferred, rootHeight));
            var previousHeight = scroll.content.rect.height;
            if (float.IsFinite(required) && required > previousHeight + 1f && required < 30000f)
            {
                operation = "resize ScrollRect content";
                scroll.content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, required);
            }
            if (!_scrollUserInteracted)
            {
                operation = "stop ScrollRect movement";
                scroll.StopMovement();
                operation = "reset ScrollRect position";
                scroll.verticalNormalizedPosition = 1f;
            }
            var layoutStable = _selectionLoadGate.ObserveLayout(required, previousHeight);
            if (!_loggedScrollReset)
            {
                _loggedScrollReset = true;
                _log.LogInfo($"Course list layout: viewport={viewportHeight:F1}, content={previousHeight:F1}, preferred={preferred:F1}, root={rootHeight:F1}, required={required:F1}.");
            }
            if (layoutStable || _layoutRefreshFrames <= 0)
                FinishCourseSelectionLoading();
        }
        catch (Exception ex)
        {
            _layoutRefreshFrames = 0;
            _log.LogWarning($"Could not refresh course list layout during '{operation}': {ex}");
            FinishCourseSelectionLoading();
        }
    }

    public void ReleaseCourseSelectionScrollPin(ScrollRect scroll)
    {
        if (scroll == null || !IsCourseEntryScroll(scroll, out _))
            return;

        _scrollUserInteracted = true;
        _layoutRefreshFrames = 0;
    }

    public bool IsCourseSelectionScrollBlocked(ScrollRect scroll) =>
        CourseLoadingOverlay.SelectorInteractionBlocked && IsCourseEntryScroll(scroll, out _);

    private bool IsCourseEntryScroll(ScrollRect scroll, out Transform content)
    {
        content = null;
        if (_scrollResetView == null || _scrollResetView.courseEntryRoot == null || scroll == null || scroll.content == null)
            return false;

        content = scroll.content;
        var courseContent = _scrollResetView.courseEntryRoot;
        return content == courseContent || content.IsChildOf(courseContent) || courseContent.IsChildOf(content);
    }

    private static SoloConfiguration.SoloCourse CreateSoloCourse(CustomCourseRecord record, string courseId)
    {
        var soloCourse = new SoloConfiguration.SoloCourse
        {
            courseId = courseId,
            name = record.Definition.Name,
            description = record.Definition.Description,
            previewImage = record.CourseCardSprite ?? record.Layout.GetCoursePreviewImage(),
            courseLogoImage = record.Layout.GetCourseLogo()
        };

        var soloLayout = new SoloConfiguration.SoloLayout
        {
            layout = record.Layout,
            challengeScore = 0,
            reward = 0
        };
        var soloLayouts = new Il2CppReferenceArray<SoloConfiguration.SoloLayout>(1);
        soloLayouts[0] = soloLayout;
        soloCourse.layouts = soloLayouts;
        return soloCourse;
    }

}
