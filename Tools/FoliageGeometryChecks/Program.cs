using System.Numerics;
using DgmCustomCourses;

static void Check(bool value, string description)
{
    if (!value) throw new Exception(description);
}

var center = Vector3.Zero;
var radii = new Vector3(2, 1, 2);
Check(ImportedFoliageGeometry.TryEnterEllipsoid(new(-4, 0, 0), new(4, 0, 0), center, radii, out var entry), "A segment crossing a canopy should hit.");
Check(Math.Abs(entry - .25f) < .0001f, "The first entry point is incorrect.");
Check(!ImportedFoliageGeometry.TryEnterEllipsoid(new(-4, 1.1f, 0), new(4, 1.1f, 0), center, radii, out _), "A segment above the canopy should miss.");
Check(!ImportedFoliageGeometry.TryEnterEllipsoid(Vector3.Zero, Vector3.One, center, radii, out _), "A disc already inside should not be repeatedly slowed.");
Check(!ImportedFoliageGeometry.TryEnterEllipsoid(new(4, 0, 0), new(4, 0, 0), center, radii, out _), "A stationary disc should not hit.");
Check(!ImportedFoliageGeometry.TryEnterEllipsoid(new(4, 0, 0), new(5, 0, 0), center, radii, out _), "Movement away from a canopy should miss.");
Check(!ImportedFoliageGeometry.TryEnterEllipsoid(new(-4, 0, 0), new(4, 0, 0), center, new(0, 1, 1), out _), "Invalid radii should be rejected.");
Console.WriteLine("Foliage sweep geometry checks passed.");
