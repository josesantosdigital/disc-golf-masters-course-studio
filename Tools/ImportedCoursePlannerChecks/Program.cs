using DgmCustomCourses;

static void Check(bool value, string message)
{
    if (!value) throw new InvalidOperationException(message);
    Console.WriteLine("PASS " + message);
}

var samples = new List<ImportedCoursePlanner.Point>();
for (var x = 0; x < 13; x++)
for (var z = 0; z < 13; z++) samples.Add(new ImportedCoursePlanner.Point(x * 35f, 2f, z * 35f));
var first = ImportedCoursePlanner.Plan(samples, 9);
var second = ImportedCoursePlanner.Plan(samples, 9);
Check(first.Count == 9 && first.SequenceEqual(second), "plans nine deterministic holes");
Check(first.Any(hole => hole.Distance >= 280f && hole.Distance <= 335f), "supports approximately 300 m holes on a large map");
Check(first.Max(hole => hole.Distance) - first.Min(hole => hole.Distance) > 100f, "varies hole lengths");
Check(first.SelectMany(hole => new[] { hole.Tee, hole.Basket }).Distinct().Count() >= 16, "varies tee and basket locations");
var shortProfile = ImportedCoursePlanner.Plan(samples, 9, Enumerable.Repeat(80, 9).ToArray(), out _);
var longProfile = ImportedCoursePlanner.Plan(samples, 9, Enumerable.Repeat(320, 9).ToArray(), out _);
Check(shortProfile[0].Distance < 120f && longProfile[0].Distance > 250f, "uses each course's configured target lengths");
Check(ImportedCoursePlanner.ParFor(250f, 0) == 5 && ImportedCoursePlanner.ParFor(340f, 0) == 6, "assigns higher pars for long holes");
var small = samples.Select(point => new ImportedCoursePlanner.Point(point.X / 3f, point.Y, point.Z / 3f)).ToList();
var smallHoles = ImportedCoursePlanner.Plan(small, 9);
Check(smallHoles.Count == 9 && smallHoles.All(hole => hole.Distance < 220f), "fits holes to a smaller map without inventing 300 m distances");
var sparse = new[]
{
    new ImportedCoursePlanner.Point(0f, 0f, 0f),
    new ImportedCoursePlanner.Point(23f, 0f, 0f)
};
var sparseHoles = ImportedCoursePlanner.Plan(sparse, 9);
Check(sparseHoles.Count == 9 && sparseHoles.All(hole => MathF.Abs(hole.Distance - 23f) < 0.01f), "reuses a short grounded pair when only two land points are available");
var separatedLand = new[]
{
    new ImportedCoursePlanner.Point(0f, 0f, 0f),
    new ImportedCoursePlanner.Point(5f, 0f, 0f),
    new ImportedCoursePlanner.Point(10f, 0f, 0f),
    new ImportedCoursePlanner.Point(300f, 0f, 0f),
    new ImportedCoursePlanner.Point(305f, 0f, 0f),
    new ImportedCoursePlanner.Point(310f, 0f, 0f)
};
var relaxed = ImportedCoursePlanner.Plan(separatedLand, 9, out var usedRelaxedPairs);
Check(relaxed.Count == 9 && usedRelaxedPairs && relaxed.All(hole => hole.Distance >= 8f), "completes a course when separated land clusters leave gaps in target distances");
Check(ImportedCoursePlanner.Plan(new[] { sparse[0] }, 9).Count == 0, "does not invent a basket when only one grounded point is available");
Check(ImportedCoursePlanner.Plan(Array.Empty<ImportedCoursePlanner.Point>(), 9).Count == 0, "declines to invent ground positions");
