using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MedSimAI.EditorTools
{
    /// <summary>
    /// Imports the MPFB2-generated characters (Humanoid avatar, facial blend shapes), derives medical clothing textures,
    /// builds URP materials and saves prefabs: Assets/MedSimAI/Characters/{Name}/{Name}.prefab.
    /// </summary>
    public static class MedSimCharacterSetup
    {
        const string Root = "Assets/MedSimAI/Characters";
        static Shader Lit => Shader.Find("Universal Render Pipeline/Lit");

        [MenuItem("MedSim AI/Setup Realistic Characters")]
        public static string SetupAll()
        {
            var a = Setup("DrSaraHassan", new Color(0.93f, 0.84f, 0.77f));
            var b = Setup("AdamReyes", new Color(0.95f, 0.86f, 0.79f));
            AssetDatabase.SaveAssets();
            return a + "\n" + b;
        }

        static string Setup(string name, Color skinTint)
        {
            string dir = $"{Root}/{name}", fbx = $"{dir}/{name}.fbx", texDir = $"{dir}/Textures", matDir = $"{dir}/Materials";
            if (!AssetDatabase.IsValidFolder(matDir)) AssetDatabase.CreateFolder(dir, "Materials");

            // ---- Texture import settings ----
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { texDir }))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid); var ti = (TextureImporter)AssetImporter.GetAtPath(p);
                bool normal = p.Contains("_normal");
                ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                ti.alphaIsTransparency = !normal; ti.isReadable = true; ti.maxTextureSize = p.Contains("diffuse") || p.Contains("coat") || p.Contains("Dress") ? 2048 : 1024;
                if (normal) ti.maxTextureSize = 2048;
                var android = ti.GetPlatformTextureSettings("Android"); android.overridden = true; android.format = TextureImporterFormat.ASTC_6x6; android.maxTextureSize = 1024; ti.SetPlatformTextureSettings(android);
                ti.SaveAndReimport();
            }

            // ---- Derived clothing textures ----
            string coatWhite = DeriveTexture(texDir, "coat.png", "coat_labwhite.png", (l, x, y) => Color.Lerp(new Color(0.80f, 0.82f, 0.84f), new Color(0.98f, 0.985f, 0.99f), l));
            string gown = DeriveTexture(texDir, "ShiftDress.png", "hospital_gown.png", (l, x, y) =>
            {
                var baseCol = Color.Lerp(new Color(0.55f, 0.68f, 0.78f), new Color(0.78f, 0.87f, 0.93f), l);
                // small repeating diamond print typical of hospital gowns
                int px = x % 48, py = y % 48; int dx = Mathf.Abs(px - 24), dy = Mathf.Abs(py - 24);
                bool dot = dx + dy < 5; bool ring = Mathf.Abs(dx + dy - 14) < 1;
                if (dot) baseCol = Color.Lerp(baseCol, new Color(0.25f, 0.42f, 0.6f), 0.7f);
                else if (ring) baseCol = Color.Lerp(baseCol, Color.white, 0.4f);
                return baseCol;
            });

            string greyHair = DeriveTexture(texDir, "short02_diffuse.png", "short02_grey.png", (l, x, y) => { float n = Mathf.PerlinNoise(x * 0.05f, y * 0.05f); return Color.Lerp(new Color(0.22f, 0.21f, 0.2f), new Color(0.78f, 0.77f, 0.75f), Mathf.Clamp01(l * 0.7f + n * 0.45f)); });
            // ---- Model import ----
            var mi = (ModelImporter)AssetImporter.GetAtPath(fbx);
            mi.animationType = ModelImporterAnimationType.Human; mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.importBlendShapes = true; mi.importBlendShapeNormals = ModelImporterNormals.Calculate;
            mi.materialImportMode = ModelImporterMaterialImportMode.None; mi.importAnimation = false;
            mi.optimizeMeshPolygons = true; mi.optimizeMeshVertices = true; mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.SaveAndReimport();
            ApplyExplicitHumanMapping(mi, fbx);

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            var log = new System.Text.StringBuilder($"{name}: ");
            foreach (var r in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                string n = r.name.ToLowerInvariant();
                Material m;
                if (n == "human") { m = MakeMat(matDir, name + "_Skin", Tex(texDir, "lightskinned"), skinTint, 0.42f); ApplyDetail(m, SkinDetailNormal(), 14f, 0.35f); }
                else if (n.Contains("low-poly")) m = MakeMat(matDir, name + "_Eyes", Tex(texDir, "eye"), Color.white, 0.85f);
                else if (n.Contains("eyebrow")) m = MakeMat(matDir, name + "_Eyebrows", Tex(texDir, "eyebrow"), Color.white, 0.1f, cutout: 0.25f);
                else if (n.Contains("eyelash")) m = MakeMat(matDir, name + "_Eyelashes", Tex(texDir, "eyelash"), Color.white, 0.1f, cutout: 0.3f);
                else if (n.Contains("hair") || n.Contains("short02") || n.Contains("bun")) m = MakeMat(matDir, name + "_Hair", (name == "AdamReyes" && greyHair != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(greyHair) : Tex(texDir, "Hair_bun")), Color.white, 0.32f, cutout: 0.35f);
                else if (n.Contains("teeth")) m = MakeMat(matDir, name + "_Teeth", Tex(texDir, "teeth"), Color.white, 0.6f, cutout: 0.2f);
                else if (n.Contains("tongue")) m = MakeMat(matDir, name + "_Tongue", Tex(texDir, "tongue"), Color.white, 0.5f);
                else if (n.Contains("coat")) m = MakeMat(matDir, name + "_LabCoat", AssetDatabase.LoadAssetAtPath<Texture2D>(coatWhite), Color.white, 0.2f, normal: HeightToNormal(texDir, "coat.png", "coat_normal.png", 0.9f));
                else if (n.Contains("shift_dress")) m = MakeMat(matDir, name + "_HospitalGown", AssetDatabase.LoadAssetAtPath<Texture2D>(gown), Color.white, 0.12f, cutout: 0.3f, normal: HeightToNormal(texDir, "ShiftDress.png", "gown_normal.png", 1.2f));
                else if (n.Contains("casualsuit")) m = MakeMat(matDir, name + "_Scrubs", Tex(texDir, "casualsuit01_diffuse"), new Color(0.78f, 0.84f, 0.9f), 0.2f, normal: Tex(texDir, "casualsuit01_normal"));
                else if (n.Contains("shoes")) m = MakeMat(matDir, name + "_Shoes", Tex(texDir, "shoes"), Color.white, 0.45f);
                else m = MakeMat(matDir, name + "_" + r.name, null, Color.grey, 0.3f);
                r.sharedMaterial = m;
                r.updateWhenOffscreen = false; r.skinnedMotionVectors = false;
                r.quality = n == "human" ? SkinQuality.Bone4 : SkinQuality.Bone2;
                log.Append($"{r.name}({r.sharedMesh.vertexCount}v,{r.sharedMesh.blendShapeCount}bs) ");
            }
            var anim = inst.GetComponent<Animator>();
            log.Append(anim && anim.avatar ? $"avatar valid={anim.avatar.isValid} human={anim.avatar.isHuman}" : "NO AVATAR");
            var prefabPath = $"{dir}/{name}.prefab";
            PrefabUtility.SaveAsPrefabAsset(inst, prefabPath);
            Object.DestroyImmediate(inst);
            return log.ToString();
        }

        /// <summary>MPFB "game_engine" rig uses UE-mannequin bone names; map them explicitly (auto-mapping can confuse limbs).</summary>
        static void ApplyExplicitHumanMapping(ModelImporter mi, string fbx)
        {
            var map = new System.Collections.Generic.Dictionary<string, string>
            {
                {"pelvis","Hips"},{"spine_01","Spine"},{"spine_02","Chest"},{"spine_03","UpperChest"},{"neck_01","Neck"},{"head","Head"},
                {"clavicle_l","LeftShoulder"},{"upperarm_l","LeftUpperArm"},{"lowerarm_l","LeftLowerArm"},{"hand_l","LeftHand"},
                {"clavicle_r","RightShoulder"},{"upperarm_r","RightUpperArm"},{"lowerarm_r","RightLowerArm"},{"hand_r","RightHand"},
                {"thigh_l","LeftUpperLeg"},{"calf_l","LeftLowerLeg"},{"foot_l","LeftFoot"},{"ball_l","LeftToes"},
                {"thigh_r","RightUpperLeg"},{"calf_r","RightLowerLeg"},{"foot_r","RightFoot"},{"ball_r","RightToes"},
            };
            string[] fingers = { "thumb", "index", "middle", "ring", "pinky" }; string[] unityF = { "Thumb", "Index", "Middle", "Ring", "Little" }; string[] seg = { "Proximal", "Intermediate", "Distal" };
            for (int f = 0; f < 5; f++) for (int s = 0; s < 3; s++)
            {
                map[$"{fingers[f]}_0{s + 1}_l"] = $"Left {unityF[f]} {seg[s]}";
                map[$"{fingers[f]}_0{s + 1}_r"] = $"Right {unityF[f]} {seg[s]}";
            }
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            var all = go.GetComponentsInChildren<Transform>(true);
            var names = new System.Collections.Generic.HashSet<string>(all.Select(t => t.name));
            var human = map.Where(kv => names.Contains(kv.Key)).Select(kv => new HumanBone { boneName = kv.Key, humanName = kv.Value, limit = new HumanLimit { useDefaultValues = true } }).ToArray();
            var skeleton = all.Select(t => new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale }).ToArray();
            var hd = mi.humanDescription;
            hd.human = human; hd.skeleton = skeleton; hd.hasTranslationDoF = false;
            hd.upperArmTwist = 0.5f; hd.lowerArmTwist = 0.5f; hd.upperLegTwist = 0.5f; hd.lowerLegTwist = 0.5f; hd.armStretch = 0.05f; hd.legStretch = 0.05f; hd.feetSpacing = 0;
            mi.humanDescription = hd;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.SaveAndReimport();
        }

        static Texture2D Tex(string dir, string contains)
        {
            var g = AssetDatabase.FindAssets("t:Texture2D", new[] { dir }).Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => Path.GetFileName(p).ToLower().Contains(contains.ToLower()));
            return g != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(g) : null;
        }

        static string DeriveTexture(string dir, string src, string dst, System.Func<float, int, int, Color> f)
        {
            var sp = $"{dir}/{src}"; var s = AssetDatabase.LoadAssetAtPath<Texture2D>(sp);
            if (!s) return null;
            var px = s.GetPixels(); int w = s.width, h = s.height; var o = new Color[px.Length];
            // normalise luminance to keep stitching/fold detail
            float min = 1, max = 0; for (int i = 0; i < px.Length; i += 7) { float l = px[i].grayscale; if (px[i].a > 0.5f) { min = Mathf.Min(min, l); max = Mathf.Max(max, l); } }
            for (int i = 0; i < px.Length; i++)
            {
                float l = Mathf.InverseLerp(min, max, px[i].grayscale); l = Mathf.Pow(l, 0.8f);
                var c = f(l, i % w, i / w); c.a = px[i].a; o[i] = c;
            }
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false); t.SetPixels(o); t.Apply();
            var dp = $"{dir}/{dst}"; File.WriteAllBytes(dp, t.EncodeToPNG()); Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(dp);
            var ti = (TextureImporter)AssetImporter.GetAtPath(dp); ti.alphaIsTransparency = true; ti.maxTextureSize = 2048;
            var android = ti.GetPlatformTextureSettings("Android"); android.overridden = true; android.format = TextureImporterFormat.ASTC_6x6; android.maxTextureSize = 1024; ti.SetPlatformTextureSettings(android);
            ti.SaveAndReimport();
            return dp;
        }

        /// <summary>Tileable micro-relief (pores / fine skin texture) normal map, generated once (no third-party content).</summary>
        static Texture2D SkinDetailNormal()
        {
            const string path = "Assets/MedSimAI/Characters/SkinDetail_Normal.png";
            if (!AssetDatabase.LoadAssetAtPath<Texture2D>(path))
            {
                int s = 512; var h = new float[s * s]; var rng = new System.Random(42);
                for (int i = 0; i < 2600; i++) // pores: small soft pits
                {
                    int cx = rng.Next(s), cy = rng.Next(s); float r = 1.2f + (float)rng.NextDouble() * 1.6f;
                    for (int y = -4; y <= 4; y++) for (int x = -4; x <= 4; x++)
                    {
                        float d = Mathf.Sqrt(x * x + y * y) / r; if (d > 1.6f) continue;
                        int px = (cx + x + s) % s, py = (cy + y + s) % s; h[py * s + px] -= Mathf.Exp(-d * d * 2f) * 0.6f;
                    }
                }
                for (int y = 0; y < s; y++) for (int x = 0; x < s; x++) // fine tileable fbm
                {
                    float u = x / (float)s, v = y / (float)s, n = 0, a = 0.5f, f = 8;
                    for (int o = 0; o < 4; o++) { n += a * (Mathf.Sin((u * f + Mathf.Sin(v * f * 1.7f)) * Mathf.PI * 2) * Mathf.Sin((v * f + Mathf.Sin(u * f * 1.3f)) * Mathf.PI * 2)); a *= 0.5f; f *= 2; }
                    h[y * s + x] += n * 0.15f;
                }
                var tex = NormalFromHeight(h, s, s, 2.2f);
                System.IO.File.WriteAllBytes(path, tex.EncodeToPNG()); Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path);
                var ti = (TextureImporter)AssetImporter.GetAtPath(path); ti.textureType = TextureImporterType.NormalMap; ti.wrapMode = TextureWrapMode.Repeat; ti.anisoLevel = 4; ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static void ApplyDetail(Material m, Texture2D detailNormal, float tiling, float scale)
        {
            m.SetTexture("_DetailNormalMap", detailNormal); m.SetFloat("_DetailNormalMapScale", scale);
            m.SetTextureScale("_DetailAlbedoMap", new Vector2(tiling, tiling));
            m.SetFloat("_DetailAlbedoMapScale", 0f);
            m.EnableKeyword("_DETAIL_MULX2"); EditorUtility.SetDirty(m);
        }

        /// <summary>Derives a fabric normal map (folds / seams / weave relief) from a texture's luminance.</summary>
        static Texture2D HeightToNormal(string dir, string src, string dst, float strength)
        {
            var dp = $"{dir}/{dst}";
            var s = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/{src}"); if (!s) return null;
            var px = s.GetPixels(); int w = s.width, hgt = s.height; var h = new float[px.Length];
            for (int i = 0; i < px.Length; i++) h[i] = px[i].grayscale;
            var tex = NormalFromHeight(h, w, hgt, strength);
            System.IO.File.WriteAllBytes(dp, tex.EncodeToPNG()); Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(dp);
            var ti = (TextureImporter)AssetImporter.GetAtPath(dp); ti.textureType = TextureImporterType.NormalMap; ti.maxTextureSize = 2048; ti.anisoLevel = 4;
            var android = ti.GetPlatformTextureSettings("Android"); android.overridden = true; android.format = TextureImporterFormat.ASTC_6x6; android.maxTextureSize = 1024; ti.SetPlatformTextureSettings(android);
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(dp);
        }

        static Texture2D NormalFromHeight(float[] h, int w, int hgt, float strength)
        {
            var o = new Color[w * hgt];
            for (int y = 0; y < hgt; y++) for (int x = 0; x < w; x++)
            {
                float l = h[y * w + (x - 1 + w) % w], r = h[y * w + (x + 1) % w], d = h[((y - 1 + hgt) % hgt) * w + x], u = h[((y + 1) % hgt) * w + x];
                var n = new Vector3((l - r) * strength, (d - u) * strength, 1f).normalized;
                o[y * w + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1);
            }
            var t = new Texture2D(w, hgt, TextureFormat.RGBA32, false, true); t.SetPixels(o); t.Apply(); return t;
        }

        static Material MakeMat(string dir, string name, Texture2D tex, Color tint, float smooth, float cutout = -1, Texture2D normal = null)
        {
            var path = $"{dir}/{name}.mat"; var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Lit); AssetDatabase.CreateAsset(m, path); }
            m.shader = Lit; m.SetTexture("_BaseMap", tex); m.SetColor("_BaseColor", tint); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", 0);
            if (normal) { m.SetTexture("_BumpMap", normal); m.EnableKeyword("_NORMALMAP"); }
            if (cutout >= 0) { m.SetFloat("_AlphaClip", 1); m.SetFloat("_Cutoff", cutout); m.EnableKeyword("_ALPHATEST_ON"); m.SetFloat("_Cull", 0); m.doubleSidedGI = true; m.SetFloat("_AlphaToMask", 1); } // alpha-to-coverage: MSAA-smoothed hair/lash/brow edges
            else { m.SetFloat("_AlphaClip", 0); m.DisableKeyword("_ALPHATEST_ON"); m.SetFloat("_Cull", 2); }
            m.enableInstancing = true; EditorUtility.SetDirty(m);
            return m;
        }
    }
}
