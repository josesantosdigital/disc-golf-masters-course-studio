using UnityEngine;

namespace DgmCustomCourses;

/// <summary>
/// Ports the source game's standard PBR shader to DGM's loaded URP Lit shader.
/// isSupported only establishes graphics-device support, not compatibility with
/// the host render pipeline's light/probe buffers and shader variants.
/// </summary>
internal static class ImportedLitMaterials
{
    internal const string SourceShaderName = "Universal Render Pipeline/Lit (PBR Workflow)";
    internal const string TargetShaderName = "Universal Render Pipeline/Lit";
    private static readonly Dictionary<int, (Material Target, Material Original)> Originals = new();
    private static readonly HashSet<string> CompatibleKeywords = new(StringComparer.Ordinal)
    {
        "_NORMALMAP", "_PARALLAXMAP", "_RECEIVE_SHADOWS_OFF", "_ALPHATEST_ON",
        "_SURFACE_TYPE_TRANSPARENT", "_ALPHAPREMULTIPLY_ON", "_ALPHAMODULATE_ON",
        "_EMISSION", "_METALLICSPECGLOSSMAP", "_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A",
        "_OCCLUSIONMAP", "_SPECULARHIGHLIGHTS_OFF", "_ENVIRONMENTREFLECTIONS_OFF"
    };

    internal static bool TryMigrate(Material material, Shader nativeLit, out string diagnostic)
    {
        diagnostic = null;
        if (material == null || material.shader == null ||
            !string.Equals(material.shader.name, SourceShaderName, StringComparison.Ordinal))
            return false;
        if (nativeLit == null || !nativeLit.isSupported ||
            !string.Equals(nativeLit.name, TargetShaderName, StringComparison.Ordinal))
        {
            diagnostic = $"Native URP Lit is unavailable; retained source material '{material.name}'.";
            return false;
        }

        var id = material.GetInstanceID();
        if (Originals.ContainsKey(id)) return false;
        Material original = null;
        try
        {
            // Clone only the material state, not its textures. Keep the source for
            // rollback and course cleanup, including shader keywords and UV transforms.
            original = new Material(material);
            var queue = material.renderQueue;
            var keywords = original.shaderKeywords;
            var transparent = original.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT") ||
                ReadFloat(original, "_Surface") > 0.5f;
            var cutout = original.IsKeywordEnabled("_ALPHATEST_ON") || ReadFloat(original, "_AlphaClip") > 0.5f;

            material.shader = nativeLit;
            material.CopyPropertiesFromMaterial(original);
            material.renderQueue = queue;
            // Remap by keyword name into the host shader's local keyword space.
            // The source's packed _DetailMap is NOT a URP _DetailAlbedoMap or
            // _DetailNormalMap. Do not enable their unrelated detail variants.
            material.shaderKeywords = Array.Empty<string>();
            for (var i = 0; keywords != null && i < keywords.Length; i++)
                if (CompatibleKeywords.Contains(keywords[i])) material.EnableKeyword(keywords[i]);
            SetFloat(material, "_WorkflowMode", 1f); // source is metallic PBR, not specular workflow
            SetFloat(material, "_Surface", transparent ? 1f : 0f);
            SetFloat(material, "_AlphaClip", cutout ? 1f : 0f);
            SetKeyword(material, "_SURFACE_TYPE_TRANSPARENT", transparent);
            SetKeyword(material, "_ALPHATEST_ON", cutout);
            material.SetOverrideTag("RenderType", transparent ? "Transparent" : cutout ? "TransparentCutout" : "Opaque");

            // Newer URP separates alpha blending from RGB. Retain the authored RGB
            // blend/depth/cull state; initialize only the alpha controls absent in
            // the source shader (standard URP blend modes: 0 alpha, 1 pre, 2 add, 3 multiply).
            var blend = (int)ReadFloat(original, "_Blend");
            if (!original.HasProperty("_SrcBlendAlpha"))
                SetFloat(material, "_SrcBlendAlpha", transparent && blend == 3 ? 0f : 1f);
            if (!original.HasProperty("_DstBlendAlpha"))
                SetFloat(material, "_DstBlendAlpha", !transparent ? 0f : blend >= 2 ? 1f : 10f);

            Originals[id] = (material, original);
            diagnostic = $"{material.name}[queue={queue},transparent={transparent},cutout={cutout}," +
                $"emission={material.IsKeywordEnabled("_EMISSION")},packedDetailOmitted={HasTexture(original, "_DetailMap")}]";
            return true;
        }
        catch (Exception ex)
        {
            var rollback = "";
            if (original != null)
            {
                try { Restore(material, original); }
                catch (Exception restoreError) { rollback = $" Rollback failed: {restoreError.Message}"; }
                UnityEngine.Object.Destroy(original);
            }
            diagnostic = $"Could not migrate imported material '{material.name}': {ex.Message}.{rollback}";
            return false;
        }
    }

    internal static void RestoreAll(Action<string> warning)
    {
        foreach (var snapshot in Originals.Values)
        {
            try
            {
                if (snapshot.Target != null && snapshot.Original != null)
                    Restore(snapshot.Target, snapshot.Original);
            }
            catch (Exception ex) { warning?.Invoke($"Could not restore an imported Lit material: {ex.Message}"); }
            finally
            {
                if (snapshot.Original != null) UnityEngine.Object.Destroy(snapshot.Original);
            }
        }
        Originals.Clear();
    }

    private static void Restore(Material target, Material original)
    {
        target.shader = original.shader; // CopyPropertiesFromMaterial does not copy the shader.
        target.CopyPropertiesFromMaterial(original);
        target.SetOverrideTag("RenderType", original.GetTag("RenderType", false, ""));
    }

    private static float ReadFloat(Material material, string property) =>
        material.HasProperty(property) ? material.GetFloat(property) : 0f;
    private static bool HasTexture(Material material, string property) =>
        material.HasProperty(property) && material.GetTexture(property) != null;
    private static void SetFloat(Material material, string property, float value)
    {
        if (material.HasProperty(property)) material.SetFloat(property, value);
    }
    private static void SetKeyword(Material material, string keyword, bool enabled)
    {
        if (enabled) material.EnableKeyword(keyword);
        else material.DisableKeyword(keyword);
    }
}
