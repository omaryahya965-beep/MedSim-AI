using UnityEditor; using UnityEngine; using System.Linq;
int n = 0; var report = new System.Text.StringBuilder();
foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/MedSimAI/Environment", "Assets/MedSimAI/Characters" })) {
  var p = AssetDatabase.GUIDToAssetPath(guid); if (!(AssetImporter.GetAtPath(p) is TextureImporter ti)) continue;
  bool character = p.Contains("/Characters/"); bool changed = false;
  int aniso = character ? 4 : 8;
  if (ti.anisoLevel != aniso) { ti.anisoLevel = aniso; changed = true; }
  if (!ti.mipmapEnabled) { ti.mipmapEnabled = true; changed = true; }
  if (ti.filterMode != FilterMode.Trilinear) { ti.filterMode = FilterMode.Trilinear; changed = true; }
  if (ti.textureCompression != TextureImporterCompression.CompressedHQ) { ti.textureCompression = TextureImporterCompression.CompressedHQ; changed = true; } // BC7 on PC: no blocky DXT artefacts on faces
  var pc = ti.GetPlatformTextureSettings("Standalone"); if (pc.overridden) { pc.overridden = false; ti.SetPlatformTextureSettings(pc); changed = true; }
  if (changed) { ti.SaveAndReimport(); n++; }
}
return $"textures updated: {n}";
