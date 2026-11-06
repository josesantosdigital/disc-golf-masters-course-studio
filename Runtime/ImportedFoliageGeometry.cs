using System.Numerics;

namespace DgmCustomCourses;

// Pure geometry: no Unity or IL2CPP calls, so the sweep can be checked in
// isolation and never invokes the game's native foliage classifier.
internal static class ImportedFoliageGeometry
{
    internal static bool TryEnterEllipsoid(Vector3 from, Vector3 to, Vector3 center, Vector3 radii, out float entry)
    {
        entry = 0f;
        if (radii.X <= 0 || radii.Y <= 0 || radii.Z <= 0) return false;
        var start = (from - center) / radii;
        var delta = (to - from) / radii;
        var c = Vector3.Dot(start, start) - 1f;
        if (c <= 0f) return false; // Do not repeatedly slow a disc already inside.
        var a = Vector3.Dot(delta, delta);
        if (a < 1e-10f) return false;
        var b = Vector3.Dot(start, delta);
        var discriminant = b * b - a * c;
        if (discriminant < 0f) return false;
        var t = (-b - MathF.Sqrt(discriminant)) / a;
        if (t < 0f || t > 1f) return false;
        entry = t;
        return true;
    }
}
