using System.Text.Json;

namespace DgmCustomCourses;

// Local custom-course results never alter the game's official progression.
internal sealed class CustomPersonalBests
{
    private readonly string _path;
    private readonly Action<string> _warning;
    private Dictionary<string, int> _scores = new(StringComparer.OrdinalIgnoreCase);
    private bool _readFailed;

    internal CustomPersonalBests(string path, Action<string> warning)
    {
        _path = path;
        _warning = warning;
        try
        {
            if (File.Exists(path))
            {
                var loaded = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(path));
                if (loaded == null) throw new InvalidDataException("Personal-best file has no score data.");
                _scores = new Dictionary<string, int>(loaded, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex)
        {
            _readFailed = true;
            _warning($"Could not read custom personal bests; the existing file will be preserved: {ex.Message}");
        }
    }

    internal bool TryGet(string layoutId, out int score) => _scores.TryGetValue(layoutId, out score);

    internal static bool IsCompleteRound(int expectedHoles, int roundHoles, IReadOnlyList<int> strokes) =>
        expectedHoles > 0 && roundHoles == expectedHoles && strokes != null &&
        strokes.Count == expectedHoles && strokes.All(score => score > 0);

    internal bool Record(string layoutId, int score)
    {
        if (_readFailed || string.IsNullOrWhiteSpace(layoutId)) return false;
        if (_scores.TryGetValue(layoutId, out var previous) && score >= previous) return false;
        var next = new Dictionary<string, int>(_scores, StringComparer.OrdinalIgnoreCase) { [layoutId] = score };
        var directory = Path.GetDirectoryName(_path);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(temporary, JsonSerializer.Serialize(next, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, _path, true);
            _scores = next;
            return true;
        }
        catch (Exception ex)
        {
            _warning($"Could not save custom personal best for '{layoutId}': {ex.Message}");
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
            return false;
        }
    }
}
