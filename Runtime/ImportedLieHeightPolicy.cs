namespace DgmCustomCourses;

internal static class ImportedLieHeightPolicy
{
    public static bool TryChooseGround(float dx, float dz, float lieY, float discY, float groundDistance, float chosenY, out float groundY)
    {
        groundY = discY - groundDistance;
        return float.IsFinite(dx) && float.IsFinite(dz) && float.IsFinite(lieY) &&
            float.IsFinite(discY) && float.IsFinite(groundDistance) && float.IsFinite(chosenY) &&
            dx * dx + dz * dz <= 2.25f && MathF.Abs(lieY - discY) <= 0.5f &&
            groundDistance >= -0.1f && groundDistance <= 0.4f && chosenY > groundY + 0.75f;
    }
}
