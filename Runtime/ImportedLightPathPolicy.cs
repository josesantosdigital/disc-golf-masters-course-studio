namespace DgmCustomCourses;

internal static class ImportedLightPathPolicy
{
    public static bool IsSuppressed(string lightPath, IReadOnlyCollection<string> roots)
    {
        if (string.IsNullOrEmpty(lightPath) || roots == null) return false;
        foreach (var item in roots)
        {
            if (string.IsNullOrWhiteSpace(item)) continue;
            var root = item.Trim().TrimEnd('/');
            if (root.Length == 0) continue;
            if (lightPath.Equals(root, StringComparison.OrdinalIgnoreCase) ||
                lightPath.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
