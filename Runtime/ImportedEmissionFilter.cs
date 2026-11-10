using UnityEngine;

namespace DgmCustomCourses;

// Map authors can target a specific incompatible emission texture without
// changing other materials that happen to share the same renderer.
internal static class ImportedEmissionFilter
{
    private sealed class Snapshot
    {
        public readonly Material Material;
        public readonly bool HasFloat;
        public readonly float Float;
        public readonly bool HasColor;
        public readonly Color Color;
        public readonly bool Keyword;
        public readonly bool SlzKeyword;
        public Snapshot(Material material, bool hasFloat, float value, bool hasColor, Color color, bool keyword, bool slzKeyword)
        {
            Material = material; HasFloat = hasFloat; Float = value;
            HasColor = hasColor; Color = color; Keyword = keyword; SlzKeyword = slzKeyword;
        }
    }
    private static readonly Dictionary<int, Snapshot> Originals = new();

    // Only hide a renderer if every material belongs to a configured fixture.
    // A mixed renderer might also contain walls or other important scenery.
    public static bool IsDedicatedFixture(IEnumerable<Material> materials, IReadOnlyCollection<string> textureNames)
    {
        if (materials == null || textureNames == null || textureNames.Count == 0) return false;
        var found = false;
        foreach (var material in materials)
        {
            found = true;
            if (material == null || !material.HasProperty("_EmissionMap")) return false;
            var texture = material.GetTexture("_EmissionMap") as UnityEngine.Object;
            if (texture == null || !textureNames.Any(name => string.Equals(name, texture.name, StringComparison.OrdinalIgnoreCase))) return false;
        }
        return found;
    }

    public static bool TrySuppress(Material material, IReadOnlyCollection<string> textureNames, Action<string> warning)
    {
        if (material == null || textureNames == null || textureNames.Count == 0 || Originals.ContainsKey(material.GetInstanceID())) return false;
        Snapshot original = null;
        try
        {
            if (!material.HasProperty("_EmissionMap")) return false;
            var texture = material.GetTexture("_EmissionMap") as UnityEngine.Object;
            if (texture == null || !textureNames.Any(name => string.Equals(name, texture.name, StringComparison.OrdinalIgnoreCase))) return false;
            var hasFloat = material.HasProperty("_Emission");
            var hasColor = material.HasProperty("_EmissionColor");
            original = new Snapshot(material, hasFloat, hasFloat ? material.GetFloat("_Emission") : 0f,
                hasColor, hasColor ? material.GetColor("_EmissionColor") : default,
                material.IsKeywordEnabled("_EMISSION"), material.IsKeywordEnabled("_EMISSION_ON"));
            if (hasFloat) material.SetFloat("_Emission", 0f);
            if (hasColor) material.SetColor("_EmissionColor", Color.black);
            material.DisableKeyword("_EMISSION");
            material.DisableKeyword("_EMISSION_ON");
            Originals[material.GetInstanceID()] = original;
            return true;
        }
        catch (Exception ex)
        {
            if (original != null) { try { Restore(original); } catch { } }
            warning?.Invoke($"Could not suppress imported material emission: {ex.Message}");
            return false;
        }
    }

    public static void RestoreAll(Action<string> warning)
    {
        foreach (var original in Originals.Values)
        {
            try { Restore(original); }
            catch (Exception ex) { warning?.Invoke($"Could not restore imported material emission: {ex.Message}"); }
        }
        Originals.Clear();
    }

    private static void Restore(Snapshot original)
    {
        if (original.Material == null) return;
        if (original.HasFloat) original.Material.SetFloat("_Emission", original.Float);
        if (original.HasColor) original.Material.SetColor("_EmissionColor", original.Color);
        if (original.Keyword) original.Material.EnableKeyword("_EMISSION");
        else original.Material.DisableKeyword("_EMISSION");
        if (original.SlzKeyword) original.Material.EnableKeyword("_EMISSION_ON");
        else original.Material.DisableKeyword("_EMISSION_ON");
    }
}
