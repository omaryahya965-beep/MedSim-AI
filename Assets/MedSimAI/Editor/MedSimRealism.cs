using System.IO;
using UnityEditor;
using UnityEngine;

namespace MedSimAI.EditorTools
{
    /// <summary>
    /// v0.6 character surface realism (generated in-project, no third-party assets):
    ///  • hospital gown: tileable woven-cotton normal map (plain weave + slub variation) instead of a flat surface;
    ///  • skin (both characters' heads/bodies): fine pore/micro-relief detail normal (URP Lit detail map, tiled) so skin
    ///    breaks up light like skin instead of plastic; the patient's face is slightly moister (clammy, as authored).
    /// </summary>
    public static class MedSimRealism
    {
        const string Dir = "Assets/MedSimAI/Characters/v2/Textures/Generated/";
        const string MatDir = "Assets/MedSimAI/Characters/v2/Materials/";

        [MenuItem("MedSim AI/Polish/3. Fabric and skin micro-detail")]
        public static string Apply()
        {
            Directory.CreateDirectory(Dir);
            var weave = Save(Weave(512), "Fabric_Weave_N.png");
            var pores = Save(Pores(512), "Skin_Pores_N.png");
            var log = new System.Text.StringBuilder();
            foreach (var name in new[] { "Reyes_Gown", "Reyes_Gown_Trim" })
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(MatDir + name + ".mat"); if (!m) { log.AppendLine("missing " + name); continue; }
                m.SetTexture("_BumpMap", weave); m.SetFloat("_BumpScale", 0.38f); m.SetTextureScale("_BumpMap", new Vector2(13, 13));
                m.SetTextureScale("_BaseMap", m.GetTextureScale("_BaseMap")); m.EnableKeyword("_NORMALMAP"); m.SetFloat("_Smoothness", 0.14f);
                EditorUtility.SetDirty(m); log.AppendLine("gown weave → " + name);
            }
            foreach (var (name, scale, smooth) in new[] { ("Reyes_Head", 0.32f, 0.42f), ("Reyes_Body", 0.25f, 0.32f), ("DrHassan_Head", 0.28f, 0.34f), ("DrHassan_Body", 0.2f, 0.18f) })
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(MatDir + name + ".mat"); if (!m) { log.AppendLine("missing " + name); continue; }
                m.SetTexture("_DetailNormalMap", pores); m.SetFloat("_DetailNormalMapScale", scale); m.SetTextureScale("_DetailAlbedoMap", new Vector2(14, 14));
                m.SetFloat("_DetailAlbedoMapScale", 0f); m.EnableKeyword("_DETAIL_MULX2");
                if (name.EndsWith("Head")) m.SetFloat("_Smoothness", smooth);
                EditorUtility.SetDirty(m); log.AppendLine("skin pores → " + name);
            }
            AssetDatabase.SaveAssets();
            return log.ToString();
        }

        static Texture2D Save(Texture2D t, string file)
        {
            var path = Dir + file; File.WriteAllBytes(path, t.EncodeToPNG()); Object.DestroyImmediate(t); AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path); imp.textureType = TextureImporterType.NormalMap; imp.wrapMode = TextureWrapMode.Repeat; imp.mipmapEnabled = true; imp.anisoLevel = 4; imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // height fields → tangent-space normal maps (tileable)
        static Texture2D FromHeight(float[,] h, float strength)
        {
            int n = h.GetLength(0); var t = new Texture2D(n, n, TextureFormat.RGBA32, false, true);
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float dx = (h[(x + 1) % n, y] - h[(x - 1 + n) % n, y]) * strength, dy = (h[x, (y + 1) % n] - h[x, (y - 1 + n) % n]) * strength;
                var v = new Vector3(-dx, -dy, 1f).normalized; t.SetPixel(x, y, new Color(v.x * 0.5f + 0.5f, v.y * 0.5f + 0.5f, v.z * 0.5f + 0.5f, 1));
            }
            t.Apply(); return t;
        }
        static float Tile(float x, float y, int n, float freq, int seed) // tileable value noise
        {
            float fx = x / n * freq, fy = y / n * freq; int f = Mathf.RoundToInt(freq);
            int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy); float tx = fx - x0, ty = fy - y0;
            float H(int a, int b) { a = ((a % f) + f) % f; b = ((b % f) + f) % f; uint s = (uint)(a * 374761393 + b * 668265263 + seed * 982451653); s = (s ^ (s >> 13)) * 1274126177u; return (s & 0xffff) / 65535f; }
            tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty);
            return Mathf.Lerp(Mathf.Lerp(H(x0, y0), H(x0 + 1, y0), tx), Mathf.Lerp(H(x0, y0 + 1), H(x0 + 1, y0 + 1), tx), ty);
        }
        static Texture2D Weave(int n)
        {
            var h = new float[n, n]; const int threads = 64; float period = (float)n / threads;
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                int cx = (int)(x / period), cy = (int)(y / period); float u = (x % period) / period, v = (y % period) / period;
                bool warpOver = ((cx + cy) & 1) == 0; // plain weave
                float warp = Mathf.Sin(u * Mathf.PI), weft = Mathf.Sin(v * Mathf.PI);
                float slub = 0.85f + 0.3f * Tile(x, y, n, 16, 7);
                h[x, y] = (warpOver ? warp * (0.6f + 0.4f * weft) : weft * (0.6f + 0.4f * warp)) * slub + 0.15f * Tile(x, y, n, 32, 3);
            }
            return FromHeight(h, 2.2f);
        }
        static Texture2D Pores(int n)
        {
            var h = new float[n, n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float cells = Tile(x, y, n, 64, 11), fine = Tile(x, y, n, 128, 5), broad = Tile(x, y, n, 16, 9);
                float pore = Mathf.SmoothStep(0, 1, Mathf.Clamp01((cells - 0.72f) * 4f)); // sparse small dips
                h[x, y] = 0.55f * fine + 0.25f * broad - 0.6f * pore;
            }
            return FromHeight(h, 3.0f);
        }
    }
}
