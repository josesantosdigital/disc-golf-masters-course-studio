using DgmCustomCourses;
using UnityEngine;

// Executes production migration code with synthetic material state. These checks
// establish transfer/rollback policy, not Unity shader variant or visual correctness.
var assertions = 0;
void Check(bool condition, string message)
{
    assertions++;
    if (!condition) throw new Exception(message);
}
var source = new Shader(ImportedLitMaterials.SourceShaderName);
var native = new Shader(ImportedLitMaterials.TargetShaderName, native: true);
var leafTexture = new object();
var normalTexture = new object();
var leaf = new Material(source) { name = "synthetic leaf", renderQueue = 2467 };
leaf.SetFloat("_AlphaClip", 1);
leaf.SetFloat("_Cutoff", 0.35f);
leaf.SetFloat("_Cull", 0);
leaf.SetFloat("_Smoothness", 0.28f);
leaf.SetTexture("_BaseMap", leafTexture);
leaf.SetTexture("_BumpMap", normalTexture);
leaf.Properties["_BaseMap_ST"] = (2f, 3f, 0.1f, 0.2f);
leaf.EnableKeyword("_NORMALMAP");
leaf.EnableKeyword("_SOURCE_ONLY_VARIANT");
leaf.EnableKeyword("_DETAIL_MULX2");
leaf.SetTexture("_DetailMap", new object());
leaf.SetOverrideTag("RenderType", "SourceCutoutTag");
Check(ImportedLitMaterials.TryMigrate(leaf, native, out _), "Known Lit family must migrate");
Check(leaf.shader == native && leaf.renderQueue == 2467, "Native shader with authored queue");
Check(ReferenceEquals(leaf.GetTexture("_BaseMap"), leafTexture) && ReferenceEquals(leaf.GetTexture("_BumpMap"), normalTexture), "Albedo and normals must remain shared, not substituted");
Check(Equals(leaf.Properties["_BaseMap_ST"], (2f, 3f, 0.1f, 0.2f)), "UV scale and offset must survive");
Check(leaf.IsKeywordEnabled("_ALPHATEST_ON") && leaf.GetFloat("_Cutoff") == 0.35f && leaf.GetFloat("_Cull") == 0, "Cutout threshold and double-sided leaves");
Check(leaf.GetFloat("_Smoothness") == 0.28f && leaf.IsKeywordEnabled("_NORMALMAP"), "Preserve PBR detail");
Check(!leaf.IsKeywordEnabled("_SOURCE_ONLY_VARIANT") && !leaf.IsKeywordEnabled("_DETAIL_MULX2"), "Do not select incompatible packed-detail/source variants");
Check(!ImportedLitMaterials.TryMigrate(leaf, native, out _), "Repeated repair must be idempotent");

foreach (var mode in new[] { 0, 1, 2, 3 })
{
    var glass = new Material(source) { name = "synthetic glass", renderQueue = 3020 + mode };
    glass.SetFloat("_Surface", 1);
    glass.SetFloat("_Blend", mode);
    glass.SetFloat("_ZWrite", 0);
    glass.SetFloat("_SrcBlend", mode == 3 ? 2 : mode == 1 ? 1 : 5);
    glass.SetFloat("_DstBlend", mode == 3 ? 0 : mode == 2 ? 1 : 10);
    var src = glass.GetFloat("_SrcBlend");
    var dst = glass.GetFloat("_DstBlend");
    glass.Properties["_EmissionColor"] = (0.1f, 0.4f, 1f, 1f);
    glass.EnableKeyword("_EMISSION");
    Check(ImportedLitMaterials.TryMigrate(glass, native, out _) && glass.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT") && glass.GetFloat("_ZWrite") == 0 && glass.renderQueue == 3020 + mode, $"Transparent mode {mode} retains depth/queue");
    Check(glass.GetFloat("_SrcBlend") == src && glass.GetFloat("_DstBlend") == dst, $"Transparent mode {mode} retains authored RGB blending");
    Check(glass.GetFloat("_SrcBlendAlpha") == (mode == 3 ? 0 : 1) && glass.GetFloat("_DstBlendAlpha") == (mode >= 2 ? 1 : 10), $"Transparent mode {mode} initializes separate alpha blending");
    Check(glass.IsKeywordEnabled("_EMISSION") && Equals(glass.Properties["_EmissionColor"], (0.1f, 0.4f, 1f, 1f)), "Do not extinguish authored emissive signs");
}

foreach (var shaderName in new[] { "Custom/Water", "Custom/Foliage", "Universal Render Pipeline/Particles/Lit", ImportedLitMaterials.TargetShaderName })
{
    var shader = new Shader(shaderName);
    var untouched = new Material(shader);
    Check(!ImportedLitMaterials.TryMigrate(untouched, native, out _) && untouched.shader == shader, $"Do not migrate {shaderName}");
}
var unavailable = new Material(source);
Check(!ImportedLitMaterials.TryMigrate(unavailable, null, out var missing) && missing != null && unavailable.shader == source, "Missing target must not mutate the material");
Check(!ImportedLitMaterials.TryMigrate(unavailable, new Shader(ImportedLitMaterials.TargetShaderName) { isSupported = false }, out _) && unavailable.shader == source, "Unsupported target must not mutate the material");

var broken = new Material(source) { renderQueue = 2470, FailNextNativeCopy = true };
broken.SetOverrideTag("RenderType", "OriginalTag");
Check(!ImportedLitMaterials.TryMigrate(broken, native, out var failure) && failure.Contains("synthetic copy failure"), "Copy failure must be reported");
Check(broken.shader == source && broken.renderQueue == 2470 && broken.GetTag("RenderType", false, "") == "OriginalTag", "Failure must restore original state");
var warnings = new List<string>();
ImportedLitMaterials.RestoreAll(warnings.Add);
Check(warnings.Count == 0 && leaf.shader == source && leaf.renderQueue == 2467, "Cleanup must restore source shader and queue");
Check(leaf.IsKeywordEnabled("_SOURCE_ONLY_VARIANT") && leaf.IsKeywordEnabled("_DETAIL_MULX2") && !leaf.IsKeywordEnabled("_ALPHATEST_ON"), "Cleanup must restore source keyword set exactly");
Check(leaf.GetTag("RenderType", false, "") == "SourceCutoutTag", "Cleanup must restore original RenderType");
Check(Material.LiveClones == 0, "All snapshot resources must be released");
ImportedLitMaterials.RestoreAll(warnings.Add);
Check(warnings.Count == 0, "Cleanup is idempotent");
Console.WriteLine($"PASS: {assertions} material transfer/scope/rollback assertions. Unity rendering still requires manual verification.");
