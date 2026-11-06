using DgmCustomCourses;

static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
var tempRoot = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
var dir = Path.GetFullPath(Path.Combine(tempRoot, "dgm-personal-best-" + Guid.NewGuid().ToString("N")));
Check(dir.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase), "Test directory escaped the temporary root.");
Directory.CreateDirectory(dir);
try
{
    var path = Path.Combine(dir, "scores.json");
    var warnings = new List<string>();
    var scores = new CustomPersonalBests(path, warnings.Add);
    Check(!scores.TryGet("custom_wood", out _), "A new course should not have a best.");
    Check(scores.Record("custom_wood", 3), "The first round should be stored: " + string.Join("; ", warnings));
    Check(scores.Record("CUSTOM_WOOD", 0), "An even round should improve +3.");
    Check(scores.Record("custom_wood", -4), "A negative score should improve even par.");
    Check(!scores.Record("custom_wood", -3), "Worse scores must not replace the best.");
    Check(!scores.Record("custom_wood", -4), "Equal scores must not replace the best.");
    Check(scores.Record("custom_other", 5), "Courses need separate records.");
    var reloaded = new CustomPersonalBests(path, warnings.Add);
    Check(reloaded.TryGet("CUSTOM_WOOD", out var best) && best == -4, "Best must persist between launches.");
    Check(reloaded.TryGet("custom_other", out var other) && other == 5, "Other courses must remain distinct.");
    Check(CustomPersonalBests.IsCompleteRound(3, 3, new[] { 3, 1, 5 }), "A fully scored round must qualify.");
    Check(!CustomPersonalBests.IsCompleteRound(3, 2, new[] { 3, 1, 5 }), "Short rounds must not qualify.");
    Check(!CustomPersonalBests.IsCompleteRound(3, 3, new[] { 3, 0, 5 }), "An unscored hole must not qualify.");
    File.WriteAllText(path, "{broken user data");
    var broken = new CustomPersonalBests(path, warnings.Add);
    Check(!broken.Record("custom_wood", -8), "Unreadable existing data must not be overwritten.");
    Check(File.ReadAllText(path) == "{broken user data" && warnings.Count == 1, "Invalid file must be preserved and reported.");
    Console.WriteLine("Custom personal-best checks passed.");
}
finally { Directory.Delete(dir, recursive: true); }
