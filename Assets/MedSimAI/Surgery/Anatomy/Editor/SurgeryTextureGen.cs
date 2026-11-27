using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MedSimAI.Surgery.AssetsEditor
{
    /// <summary>
    /// Procedural, tileable detail textures generated in-project (no third-party content): bone micro-porosity,
    /// cartilage, fibre striation (ligament/tendon/muscle/meniscus), fat lobules, cancellous cut-bone, skin pores.
    /// </summary>
    public static class SurgeryTextureGen
    {
        public const string Dir = "Assets/MedSimAI/Surgery/Anatomy/Textures/Generated/";
        const int N = 512;

        static float Hash(int x, int y, int seed) { unchecked { uint h = (uint)(x * 374761393 + y * 668265263 + seed * 982451653); h = (h ^ (h >> 13)) * 1274126177u; return (h & 0xffffff) / 16777215f; } }
        static float Value(float x, float y, int period, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y); float tx = x - x0, ty = y - y0; tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty);
            float H(int a, int b) => Hash(((a % period) + period) % period, ((b % period) + period) % period, seed);
            return Mathf.Lerp(Mathf.Lerp(H(x0, y0), H(x0 + 1, y0), tx), Mathf.Lerp(H(x0, y0 + 1), H(x0 + 1, y0 + 1), tx), ty);
        }
        static float Fbm(float u, float v, int baseFreq, int oct, int seed)
        {
            float s = 0, a = 0.5f, n = 0; int f = baseFreq;
            for (int o = 0; o < oct; o++) { s += a * Value(u * f, v * f, f, seed + o * 31); n += a; a *= 0.5f; f *= 2; }
            return s / n;
        }
        static float Worley(float u, float v, int cells, int seed, out float second)
        {
            float x = u * cells, y = v * cells; int cx = Mathf.FloorToInt(x), cy = Mathf.FloorToInt(y); float d1 = 9, d2 = 9;
            for (int i = -1; i <= 1; i++) for (int j = -1; j <= 1; j++)
            {
                int gx = cx + i, gy = cy + j; int wx = ((gx % cells) + cells) % cells, wy = ((gy % cells) + cells) % cells;
                float px = gx + Hash(wx, wy, seed), py = gy + Hash(wx, wy, seed + 7); float d = (px - x) * (px - x) + (py - y) * (py - y);
                if (d < d1) { d2 = d1; d1 = d; } else if (d < d2) d2 = d;
            }
            second = Mathf.Sqrt(d2); return Mathf.Sqrt(d1);
        }

        static Texture2D NormalFromHeight(Func<float, float, float> h, float strength)
        {
            var hm = new float[N, N];
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++) hm[x, y] = h((float)x / N, (float)y / N);
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false, true);
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
            {
                float dx = (hm[(x + 1) % N, y] - hm[(x - 1 + N) % N, y]) * strength, dy = (hm[x, (y + 1) % N] - hm[x, (y - 1 + N) % N]) * strength;
                var n = new Vector3(-dx, -dy, 1).normalized; t.SetPixel(x, y, new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1));
            }
            t.Apply(); return t;
        }
        static Texture2D Save(Texture2D t, string name, bool normal)
        {
            Directory.CreateDirectory(Dir); var path = Dir + name + ".png";
            File.WriteAllBytes(path, t.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(t); AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default; imp.sRGBTexture = !normal;
            imp.wrapMode = TextureWrapMode.Repeat; imp.mipmapEnabled = true; imp.anisoLevel = 4; imp.maxTextureSize = 512; imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        public static Texture2D Bone() => Save(NormalFromHeight((u, v) => { float f2; float w = Worley(u, v, 40, 3, out f2); return 0.7f * Fbm(u, v, 8, 5, 11) - 0.5f * Mathf.SmoothStep(0.25f, 0f, w); }, 2.4f), "Bone_N", true);
        public static Texture2D Cartilage() => Save(NormalFromHeight((u, v) => Fbm(u, v, 4, 4, 21), 0.8f), "Cartilage_N", true);
        public static Texture2D Fibre() => Save(NormalFromHeight((u, v) => 0.6f * Mathf.Sin((v * 48 + 2.2f * Fbm(u, v, 4, 3, 5)) * Mathf.PI * 2) * 0.5f + 0.4f * Fbm(u, v * 0.25f, 16, 3, 9), 1.6f), "Fibre_N", true);
        public static Texture2D Fat() => Save(NormalFromHeight((u, v) => { float f2; float w = Worley(u, v, 14, 41, out f2); return Mathf.Clamp01(f2 - w) * 1.2f + 0.2f * Fbm(u, v, 16, 3, 2); }, 2.0f), "Fat_N", true);
        public static Texture2D SkinPores() => Save(NormalFromHeight((u, v) => { float f2; float w = Worley(u, v, 70, 13, out f2); return 0.5f * Fbm(u, v, 32, 3, 17) - 0.6f * Mathf.SmoothStep(0.18f, 0f, w); }, 2.0f), "SkinPores_N", true);
        public static Texture2D CutBoneNormal() => Save(NormalFromHeight(Trabecular, 3.0f), "CutBone_N", true);
        public static Texture2D CutBoneAlbedo()
        {
            var t = new Texture2D(N, N, TextureFormat.RGBA32, true, false);
            var bone = new Color(0.86f, 0.74f, 0.58f); var pore = new Color(0.46f, 0.20f, 0.14f);
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
            {
                float h = Trabecular((float)x / N, (float)y / N); var c = Color.Lerp(pore, bone, Mathf.SmoothStep(0.15f, 0.55f, h));
                c *= 0.9f + 0.2f * Fbm((float)x / N, (float)y / N, 8, 3, 77); c.a = 1; t.SetPixel(x, y, c);
            }
            t.Apply(); return Save(t, "CutBone_Albedo", false);
        }
        static float Trabecular(float u, float v) { float f2; float w = Worley(u, v, 22, 5, out f2); return Mathf.Clamp01((f2 - w) * 3.2f) * 0.8f + 0.2f * Fbm(u, v, 16, 3, 61); }
    }
}
