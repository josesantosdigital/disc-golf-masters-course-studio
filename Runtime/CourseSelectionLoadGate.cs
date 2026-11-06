namespace DgmCustomCourses;

// Pure scheduling and layout-stability policy, separate from Unity UI calls.
internal sealed class CourseSelectionLoadGate
{
    private const int VisibleFramesBeforeWork = 2;
    private const float TimeoutSeconds = 8f;
    private const float HeightTolerance = 1f;
    private const int RequiredStablePasses = 3;
    private int _readyFrame;
    private float _deadline;
    private float _lastRequiredHeight;
    private int _stablePasses;

    internal bool Active { get; private set; }

    internal void Begin(int frame, float now)
    {
        Active = true;
        _readyFrame = frame + VisibleFramesBeforeWork;
        _deadline = now + TimeoutSeconds;
        ResetLayout();
    }

    internal bool ReadyToPrepare(int frame) => Active && frame >= _readyFrame;
    internal bool TimedOut(float now) => Active && now >= _deadline;

    internal void ResetLayout()
    {
        _lastRequiredHeight = -1f;
        _stablePasses = 0;
    }

    internal bool ObserveLayout(float requiredHeight, float previousContentHeight)
    {
        if (!float.IsFinite(requiredHeight) || requiredHeight < 0f || requiredHeight >= 30000f ||
            !float.IsFinite(previousContentHeight))
        {
            ResetLayout();
            return false;
        }

        if (MathF.Abs(requiredHeight - _lastRequiredHeight) <= HeightTolerance &&
            requiredHeight <= previousContentHeight + HeightTolerance)
            _stablePasses++;
        else
            _stablePasses = 0;
        _lastRequiredHeight = requiredHeight;
        return _stablePasses >= RequiredStablePasses;
    }

    internal void End()
    {
        Active = false;
        ResetLayout();
    }
}
