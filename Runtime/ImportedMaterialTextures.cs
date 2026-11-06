using UnityEngine;

namespace DgmCustomCourses;

internal static class ImportedMaterialTextures
{
    public static bool IsPlaceholderTexture(Texture texture) =>
        texture != null && string.Equals(texture.name, "DummyMS_00", StringComparison.OrdinalIgnoreCase);

    // Shader graphs do not necessarily expose their albedo as _BaseMap or _MainTex.
    public static Texture FindAdditional(Material material, bool logCandidates)
    {
        try
        {
            var names = material.GetTexturePropertyNames();
            if (names == null)
                return null;
            Texture best = null;
            var bestScore = int.MinValue;
            for (var i = 0; i < names.Length; i++)
            {
                var property = names[i];
                if (string.IsNullOrWhiteSpace(property))
                    continue;
                var candidate = material.GetTexture(Shader.PropertyToID(property));
                if (candidate == null)
                    continue;
                if (IsPlaceholderTexture(candidate))
                {
                    if (logCandidates)
                        Plugin.LogSource.LogInfo($"Ignored placeholder texture on imported material '{material.name}': property='{property}', texture='{candidate.name}'.");
                    continue;
                }
                var label = (property + " " + candidate.name).ToLowerInvariant();
                if (logCandidates)
                    Plugin.LogSource.LogInfo($"Imported material texture candidate '{material.name}': property='{property}', texture='{candidate.name}'.");
                if (label.Contains("normal") || label.Contains("bump") || label.Contains("mask") ||
                    label.Contains("metal") || label.Contains("rough") || label.Contains("noise") ||
                    label.Contains("occlusion") || label.Contains("height") || label.Contains("specular"))
                    continue;
                var score = 1;
                if (label.Contains("albedo") || label.Contains("diffuse") || label.Contains("basecolor")) score += 20;
                if (label.Contains("leaf") || label.Contains("leaves") || label.Contains("needle") || label.Contains("foliage")) score += 10;
                if (label.Contains("base") || label.Contains("main") || label.Contains("color")) score += 5;
                if (score > bestScore) { best = candidate; bestScore = score; }
            }
            return best;
        }
        catch (Exception ex)
        {
            if (logCandidates)
                Plugin.LogSource.LogWarning($"Could not inspect texture properties for '{material.name}': {ex.Message}");
            return null;
        }
    }
}
