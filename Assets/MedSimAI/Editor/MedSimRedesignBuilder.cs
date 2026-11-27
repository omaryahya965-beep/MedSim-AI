using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MedSimAI.Characters;
using MedSimAI.Core;
using MedSimAI.Instruments;
using MedSimAI.UI;
using MedSimAI.XR;
using TMPro;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace MedSimAI.EditorTools
{
    /// <summary>
    /// Environment redesign v0.2. Modifies the existing MedSim_ER scene idempotently: builds the corridor waiting bay, the hinged
    /// exam-room door, staging marks, NavMesh, and upgrades the examination room (real bed, cloth blanket, clinical furniture,
    /// instrument visuals, materials, lighting rig). Everything it creates lives under roots named "Redesign_*" or under
    /// children named "v2_*" of legacy objects; legacy visuals are deactivated, never deleted. Menu: MedSim AI ▸ Build Redesign (v0.2).
    /// Third-party meshes are converted with Blender (scratch_env/convert.py) into Assets/MedSimAI/ThirdParty.
    /// </summary>
    public static class MedSimRedesignBuilder
    {
        const string Root = "Assets/MedSimAI", TP = Root + "/ThirdParty", GenMesh = Root + "/Generated/RedesignMeshes", GenMat = Root + "/Generated/RedesignMaterials";
        const float CX0 = -1.60f, CX1 = 3.40f, CZ0 = -5.02f, CZ1 = -2.62f, CH = 2.60f, WH = 2.89f; // corridor interior
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
        static readonly Dictionary<string, AssetInfo> infos = new Dictionary<string, AssetInfo>();
        static int meshN; static TMP_FontAsset latin, arabic; static StringBuilder log;

        [System.Serializable] class MatInfo { public string name; public float[] color; public float metallic, roughness; public string @base, normal, mask, emission; public bool metallicLinked, roughnessLinked, alphaLinked, flat, doubleSided; }
        [System.Serializable] class AssetInfo { public string asset; public MatInfo[] materials; public bool calcNormals; }

        public static readonly Vector3 StudentSpawn = new Vector3(0.45f, 0, -4.0f), DoctorCorridor = new Vector3(1.30f, 0, -3.30f),
            DoctorDoorway = new Vector3(1.50f, 0, -1.45f), DoctorObserve = new Vector3(-0.80f, 0, -0.45f);

        [MenuItem("MedSim AI/Build Redesign (v0.2)")]
        public static void BuildMenu() => Debug.Log(Build());

        public static string Build()
        {
            log = new StringBuilder(); mats.Clear(); infos.Clear(); meshN = 0;
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != MedSimSceneBuilder.ScenePath) scene = EditorSceneManager.OpenScene(MedSimSceneBuilder.ScenePath);
            // 1. remove everything this builder made earlier
            foreach (var go in scene.GetRootGameObjects().Where(g => g.name.StartsWith("Redesign_")).ToList()) Object.DestroyImmediate(go);
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(t => t && t.name.StartsWith("v2_")).ToList()) if (t) Object.DestroyImmediate(t.gameObject);
            Folder(GenMesh); Folder(GenMat);
            foreach (var f in Directory.GetFiles(GenMesh, "*.asset")) AssetDatabase.DeleteAsset(f.Replace('\\', '/'));
            ConfigureImports();
            latin = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
            arabic = ArabicFont();

            RestyleRoomShell();
            BuildCorridor();
            var door = BuildDoor();
            UpgradeRoom();
            UpgradeEquipment();
            UpgradeInstruments();
            Physics.SyncTransforms();
            BuildLighting();
            Stage(door);
            BuildNavMesh();
            EditorSceneManager.MarkSceneDirty(scene); AssetDatabase.SaveAssets(); EditorSceneManager.SaveScene(scene);
            int tris = 0; foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None)) if (mf.sharedMesh && mf.GetComponent<MeshRenderer>() && mf.GetComponent<MeshRenderer>().enabled) tris += mf.sharedMesh.triangles.Length / 3;
            log.AppendLine("static+prop tris (active mesh renderers) = " + tris);
            return log.ToString();
        }

        // ======================================================================================= helpers
        static void Folder(string path) { if (AssetDatabase.IsValidFolder(path)) return; var p = Path.GetDirectoryName(path).Replace('\\', '/'); Folder(p); AssetDatabase.CreateFolder(p, Path.GetFileName(path)); }
        static string San(string s) => new string(s.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
        static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }
        static Shader Lit => Shader.Find("Universal Render Pipeline/Lit");
        const StaticEditorFlags GIFlags = StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.BatchingStatic | StaticEditorFlags.ReflectionProbeStatic;

        static Transform New(string name, Transform parent, Vector3 worldPos, Quaternion? rot = null)
        { var go = new GameObject(name); if (parent) go.transform.SetParent(parent, false); go.transform.SetPositionAndRotation(worldPos, rot ?? Quaternion.identity); return go.transform; }

        static Transform Find(string path)
        {
            var parts = path.Split('/'); var root = EditorSceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g => g.name == parts[0]); if (!root) return null;
            var t = root.transform; for (int i = 1; i < parts.Length && t; i++) t = t.Find(parts[i]); return t;
        }
        static void Off(string path) { var t = Find(path); if (t) t.gameObject.SetActive(false); else log.AppendLine("  (not found: " + path + ")"); }

        /// <summary>ambientCG-backed URP/Lit material. tile = repeats per metre (procedural boxes carry metre UVs).</summary>
        static Material M(string name, Color c, float smooth = 0.4f, float metal = 0f, string acg = null, float tile = 1f, bool colorTex = false, float normal = 0.5f, Color? emit = null, bool transparent = false, bool twoSided = false)
        {
            if (mats.TryGetValue(name, out var cached)) return cached;
            string path = $"{GenMat}/{name}.mat"; var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Lit); AssetDatabase.CreateAsset(m, path); }
            m.shader = Lit; m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metal);
            m.SetTexture("_BaseMap", null); m.SetTexture("_BumpMap", null); m.SetTexture("_MetallicGlossMap", null); m.DisableKeyword("_NORMALMAP"); m.DisableKeyword("_METALLICSPECGLOSSMAP");
            if (acg != null)
            {
                var n = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TP}/ambientCG/{acg}/{acg}_2K-JPG_NormalGL.jpg");
                if (n) { m.SetTexture("_BumpMap", n); m.EnableKeyword("_NORMALMAP"); m.SetFloat("_BumpScale", normal); }
                if (colorTex) m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{TP}/ambientCG/{acg}/{acg}_2K-JPG_Color.jpg"));
                m.SetTextureScale("_BaseMap", Vector2.one * tile); m.SetTextureScale("_BumpMap", Vector2.one * tile);
            }
            if (emit.HasValue) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emit.Value); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive; }
            else { m.DisableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.black); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack; }
            if (transparent) { m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0); m.SetOverrideTag("RenderType", "Transparent"); m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha); m.SetFloat("_ZWrite", 0); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = 3000; }
            else { m.SetFloat("_Surface", 0); m.SetOverrideTag("RenderType", "Opaque"); m.SetFloat("_ZWrite", 1); m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = -1; m.SetFloat("_SrcBlend", 1); m.SetFloat("_DstBlend", 0); }
            m.SetFloat("_Cull", twoSided ? 0f : 2f); m.doubleSidedGI = twoSided; m.enableInstancing = true;
            EditorUtility.SetDirty(m); mats[name] = m; return m;
        }

        // Shared palette (RESEARCH_AND_VISUAL_DIRECTION §5.1)
        static Material Paint => M("Wall_Paint", Hex("#D9D6CE"), 0.15f, 0, "PaintedPlaster017", 0.7f, false, 0.3f);
        static Material Sage => M("Wall_Sage", Hex("#7FA3A0"), 0.15f, 0, "PaintedPlaster017", 0.7f, false, 0.3f);
        static Material Vinyl { get { var m = M("Floor_SheetVinyl", Color.white, 0.60f); var t = FleckTexture(); if (m.GetTexture("_BaseMap") != t) { m.SetTexture("_BaseMap", t); m.SetTextureScale("_BaseMap", Vector2.one * 1.0f); EditorUtility.SetDirty(m); } return m; } }
        /// <summary>Sheet vinyl has no CC0 scan: solid cool grey with a fine two-tone fleck, generated once (deterministic).</summary>
        static Texture2D FleckTexture()
        {
            string path = GenMat + "/Vinyl_Fleck.png"; var t = AssetDatabase.LoadAssetAtPath<Texture2D>(path); if (t) return t;
            const int N = 1024; var px = new Color32[N * N]; var rnd = new System.Random(7); var f = new float[N * N];
            for (int i = 0; i < 26000; i++)
            {
                int cx = rnd.Next(N), cy = rnd.Next(N); float r = 0.8f + (float)rnd.NextDouble() * 1.9f; float v = ((float)rnd.NextDouble() < 0.55f ? 1 : -1) * (0.035f + (float)rnd.NextDouble() * 0.075f);
                for (int y = -3; y <= 3; y++) for (int x = -3; x <= 3; x++) { float d = Mathf.Sqrt(x * x + y * y); if (d > r) continue; int ix = (cx + x + N) % N, iy = (cy + y + N) % N; f[iy * N + ix] = v * Mathf.Clamp01(r - d + 0.4f); }
            }
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
            {
                float lo = (Mathf.PerlinNoise(x * 0.012f, y * 0.012f) - 0.5f) * 0.035f; float v = f[y * N + x] + lo;
                px[y * N + x] = new Color(Mathf.Clamp01(0.655f + v), Mathf.Clamp01(0.685f + v), Mathf.Clamp01(0.678f + v * 0.9f), 1);
            }
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false); tex.SetPixels32(px); File.WriteAllBytes(path, tex.EncodeToPNG()); Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path); var ti = (TextureImporter)AssetImporter.GetAtPath(path); ti.wrapMode = TextureWrapMode.Repeat; ti.maxTextureSize = 1024; ti.anisoLevel = 8; ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        static Material Skirt => M("Skirting_Coved", Hex("#8E9694"), 0.5f);
        static Material Dado => M("Dado_WallProtection", Hex("#C3C7C3"), 0.4f, 0, "Plastic013A", 2f, false, 0.25f);
        static Material CeilTile => M("Ceiling_Tile", Hex("#E0E0DB"), 0.05f, 0, "OfficeCeiling001", 0.833f, true, 0.6f);
        static Material Steel => M("Stainless_Brushed", Hex("#C8CACB"), 0.62f, 1f, "Metal009", 3f, false, 0.3f);
        static Material Powder => M("PowderCoat_Grey", Hex("#CFD0CD"), 0.5f, 0, "Plastic013A", 3f, false, 0.2f);
        static Material Abs => M("ABS_OffWhite", Hex("#DDDCD6"), 0.55f, 0, "Plastic013A", 3f, false, 0.2f);
        static Material AbsDark => M("ABS_Charcoal", Hex("#2B2D2F"), 0.5f, 0, "Plastic013A", 3f, false, 0.2f);
        static Material Rubber => M("Rubber_Dark", Hex("#1C1E20"), 0.4f);
        static Material Alu => M("Aluminium_Frame", Hex("#B9BCBE"), 0.55f, 0.8f, "Metal009", 3f, false, 0.2f);
        static Material Linen => M("Linen_White", Hex("#DEDEDA"), 0.08f, 0, "Fabric030", 5f, false, 0.6f);
        static Material Glass => M("Glass_Clear", new Color(0.8f, 0.88f, 0.9f, 0.12f), 0.95f, 0, transparent: true);
        static Material SignWhite => M("Sign_Acrylic", Hex("#ECEBE6"), 0.2f);
        static Material Upholstery(string name, string hex) => M(name, Hex(hex), 0.42f, 0, "Leather026", 4f, false, 0.6f);

        static Mesh Save(Mesh m, string name, bool lightmapUV)
        { m.name = name; if (lightmapUV) Unwrapping.GenerateSecondaryUVSet(m); AssetDatabase.CreateAsset(m, $"{GenMesh}/{meshN++:0000}_{San(name)}.asset"); return m; }

        static GameObject Part(string name, Transform parent, Mesh mesh, Material mat, Vector3 localPos, Vector3? euler = null, bool isStatic = true)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = localPos; go.transform.localRotation = Quaternion.Euler(euler ?? Vector3.zero);
            go.AddComponent<MeshFilter>().sharedMesh = Save(mesh, name, isStatic); go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            if (isStatic) GameObjectUtility.SetStaticEditorFlags(go, GIFlags);
            return go;
        }
        static GameObject Box(string name, Transform parent, Vector3 size, float radius, Material mat, Vector3 localPos, Vector3? euler = null, bool collider = false, bool isStatic = true, float uv = 1f)
        { var go = Part(name, parent, MeshKit.RoundedBox(size, radius, 3, uv), mat, localPos, euler, isStatic); if (collider) go.AddComponent<BoxCollider>().size = size; return go; }
        static GameObject Tube(string name, Transform parent, Vector3[] path, float r, Material mat, bool isStatic = true, int sides = 10, int subdiv = 6)
            => Part(name, parent, MeshKit.Tube(path, r, sides, subdiv), mat, Vector3.zero, null, isStatic);
        static GameObject Lathe(string name, Transform parent, Vector2[] profile, Material mat, Vector3 localPos, Vector3? euler = null, int sides = 28, bool isStatic = true)
            => Part(name, parent, MeshKit.Lathe(profile, sides), mat, localPos, euler, isStatic);
        static GameObject Quad(string name, Transform parent, float w, float h, Material mat, Vector3 localPos, Vector3? euler = null, bool isStatic = false)
            => Part(name, parent, MeshKit.Quad(w, h), mat, localPos, euler, isStatic);
        static Vector2[] Torus(float R, float r, float y = 0) => Enumerable.Range(0, 9).Select(i => new Vector2(R + r * Mathf.Cos(i * Mathf.PI / 4), y + r * Mathf.Sin(i * Mathf.PI / 4))).ToArray();
        static void AddBoxCollider(Transform parent, string name, Vector3 localCenter, Vector3 size)
        { var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = localCenter; go.AddComponent<BoxCollider>().size = size; }

        static TextMeshPro Text(Transform parent, string text, Vector3 localPos, Vector3 localEuler, float size, Color color, Vector2 box, TextAlignmentOptions align = TextAlignmentOptions.Center, bool bold = false, bool wrap = true)
        {
            var go = new GameObject("Text"); go.transform.SetParent(parent, false); go.transform.localPosition = localPos; go.transform.localRotation = Quaternion.Euler(localEuler);
            var t = go.AddComponent<TextMeshPro>(); bool ar = ArabicShaper.ContainsArabic(text);
            t.font = ar && arabic ? arabic : latin; t.fontSize = size; t.color = color; t.alignment = align; t.rectTransform.sizeDelta = box; t.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Overflow;
            if (bold && !ar) t.fontStyle = FontStyles.Bold;
            ArabicShaper.Apply(t, text);
            if (ar && arabic) arabic.TryAddCharacters(t.text, out _);
            return t;
        }

        static TMP_FontAsset ArabicFont()
        {
            const string path = Root + "/Art/Fonts/NotoKufiArabic SDF.asset";
            AssetDatabase.DeleteAsset(path); TMP_FontAsset fa = null; // rebuilt every run: a reloaded dynamic atlas cannot be extended reliably from script
            {
                var font = AssetDatabase.LoadAssetAtPath<Font>(Root + "/Art/Fonts/NotoKufiArabic-Medium.ttf");
                if (!font) { log.AppendLine("!! Noto Kufi TTF missing"); return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Root + "/Art/Fonts/NotoSansArabic SDF.asset"); }
                fa = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
                AssetDatabase.CreateAsset(fa, path);
                fa.material.name = "NotoKufiArabic SDF Material"; AssetDatabase.AddObjectToAsset(fa.material, fa);
                fa.atlasTexture.name = "NotoKufiArabic SDF Atlas"; AssetDatabase.AddObjectToAsset(fa.atlasTexture, fa);
                if (latin) fa.fallbackFontAssetTable = new List<TMP_FontAsset> { latin };
            }
            string sample = ArabicShaper.Shape("جامعة بيرزيت أهلاً وسهلاً غرفة الفحص ٠١٢٣٤٥٦٧٨٩ مخرج الطوارئ الأشعة عقّم يديك نفايات عامة طبية لوحة الإعلانات قسم مهارات سريرية مؤقتة طفاية حريق") + " —·-،";
            fa.TryAddCharacters(sample, out string missing);
            if (!string.IsNullOrEmpty(missing)) log.AppendLine("Arabic font missing glyphs: " + string.Join(" ", missing.Select(ch => ((int)ch).ToString("X4"))));
            EditorUtility.SetDirty(fa); return fa;
        }

        // ======================================================================================= third-party import
        static void ConfigureImports()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture", new[] { TP }))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid); if (!(AssetImporter.GetAtPath(p) is TextureImporter ti)) continue; bool ch = false;
                bool normal = p.Contains("_Normal"); bool linear = normal || p.Contains("_Mask") || p.Contains("_Roughness"); bool hdr = p.EndsWith(".hdr");
                if (normal && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; ch = true; }
                if (!normal && !hdr && ti.sRGBTexture == linear) { ti.sRGBTexture = !linear; ch = true; }
                int max = hdr ? 4096 : p.Contains("Terrazzo") || p.Contains("HospitalDoor") ? 2048 : 1024;
                if (ti.maxTextureSize != max) { ti.maxTextureSize = max; ch = true; }
                if (hdr && ti.textureShape != TextureImporterShape.Texture2D) { ti.textureShape = TextureImporterShape.Texture2D; ch = true; }
                if (p.Contains("_Mask") && ti.alphaSource != TextureImporterAlphaSource.FromInput) { ti.alphaSource = TextureImporterAlphaSource.FromInput; ch = true; }
                if (ch) ti.SaveAndReimport();
            }
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { TP }))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid); var mi = (ModelImporter)AssetImporter.GetAtPath(p); bool ch = false;
                var info = Info(Path.GetDirectoryName(p).Replace('\\', '/'));
                var wantN = info != null && info.calcNormals ? ModelImporterNormals.Calculate : ModelImporterNormals.Import;
                if (mi.importNormals != wantN) { mi.importNormals = wantN; mi.normalSmoothingAngle = 50; ch = true; }
                if (mi.importCameras || mi.importLights || mi.importAnimation) { mi.importCameras = false; mi.importLights = false; mi.importAnimation = false; ch = true; }
                if (mi.animationType != ModelImporterAnimationType.None) { mi.animationType = ModelImporterAnimationType.None; ch = true; }
                if (!mi.bakeAxisConversion) { mi.bakeAxisConversion = true; ch = true; }
                if (mi.generateSecondaryUV) { mi.generateSecondaryUV = false; ch = true; }
                if (ch) mi.SaveAndReimport();
            }
            var areas = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/NavMeshAreas.asset").FirstOrDefault();
            if (areas)
            {   // default Humanoid agent: slim clinician (radius 0.25 m, height 1.7 m) so the doorway and bedside are walkable
                var so = new SerializedObject(areas); var s = so.FindProperty("m_Settings");
                if (s != null && s.arraySize > 0) { var e = s.GetArrayElementAtIndex(0); e.FindPropertyRelative("agentRadius").floatValue = 0.25f; e.FindPropertyRelative("agentHeight").floatValue = 1.7f; so.ApplyModifiedPropertiesWithoutUndo(); }
            }
        }

        static AssetInfo Info(string dir)
        {
            if (infos.TryGetValue(dir, out var i)) return i;
            var f = Directory.GetFiles(dir, "*.json").FirstOrDefault();
            i = f != null ? JsonUtility.FromJson<AssetInfo>(File.ReadAllText(f)) : null; infos[dir] = i; return i;
        }

        static Material Library(string name)
        {
            switch (name)
            {
                case "Bed_Plastic": return Abs;
                case "Bed_Accent": return M("Bed_AccentPanel", Hex("#6F8597"), 0.5f, 0, "Plastic013A", 3f, false, 0.2f);
                case "Bed_Steel": return M("Bed_BaseSteel", Hex("#55585B"), 0.45f, 0.3f);
                case "Bed_Grey": return M("Bed_GreyPlastic", Hex("#9A9D9F"), 0.5f);
                case "Bed_Rubber": return Rubber;
                case "Bed_Trim": return M("Bed_Trim", Hex("#B4B7B8"), 0.5f);
                case "Linen_White": return Linen;
                case "Seat_Pad": return Upholstery("Seat_Vinyl_Slate", "#4E6E7A");
                case "Seat_Frame": return M("Seat_Frame_Black", Hex("#2A2C2E"), 0.5f, 0.4f);
                case "Blanket": return M("Blanket_Cellular", Hex("#9FB4C7"), 0.03f, 0, "Fabric030", 7f, false, 0.9f, twoSided: true);
            }
            return null;
        }

        static Material PropMat(string dir, string fbxMatName)
        {
            string key = dir + "|" + fbxMatName; if (mats.TryGetValue(key, out var c)) return c;
            var info = Info(dir); MatInfo mi = null;
            if (info != null && info.materials != null) mi = info.materials.FirstOrDefault(x => x.name == fbxMatName) ?? info.materials.FirstOrDefault(x => San(x.name) == San(fbxMatName)) ?? info.materials.FirstOrDefault(x => fbxMatName.StartsWith(x.name) || x.name.StartsWith(fbxMatName)) ?? (info.materials.Length == 1 ? info.materials[0] : null);
            Material m = mi != null && mi.flat ? Library(mi.name) : null;
            if (!m)
            {
                Folder(dir + "/Materials"); string path = $"{dir}/Materials/{San(fbxMatName)}.mat";
                m = AssetDatabase.LoadAssetAtPath<Material>(path); if (!m) { m = new Material(Lit); AssetDatabase.CreateAsset(m, path); }
                m.shader = Lit;
                if (mi != null)
                {
                    var col = mi.color != null && mi.color.Length >= 3 ? new Color(mi.color[0], mi.color[1], mi.color[2], 1).gamma : Color.white;
                    Texture2D T(string rel) => string.IsNullOrEmpty(rel) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "/" + rel);
                    var b = T(mi.@base); var n = T(mi.normal); var k = T(mi.mask); var e = T(mi.emission);
                    m.SetColor("_BaseColor", col); m.SetTexture("_BaseMap", b);
                    if (n) { m.SetTexture("_BumpMap", n); m.EnableKeyword("_NORMALMAP"); m.SetFloat("_BumpScale", 1f); }
                    if (k && mi.metallicLinked && mi.roughnessLinked) { m.SetTexture("_MetallicGlossMap", k); m.EnableKeyword("_METALLICSPECGLOSSMAP"); m.SetFloat("_Smoothness", 0.9f); m.SetFloat("_SmoothnessTextureChannel", 0); }
                    else { m.SetFloat("_Metallic", mi.metallic); m.SetFloat("_Smoothness", Mathf.Clamp01(1f - mi.roughness) * 0.9f); }
                    if (e) { m.SetTexture("_EmissionMap", e); m.SetColor("_EmissionColor", Color.white * 0.8f); m.EnableKeyword("_EMISSION"); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack; }
                    if (mi.alphaLinked) { m.SetFloat("_AlphaClip", 1); m.SetFloat("_Cutoff", 0.4f); m.EnableKeyword("_ALPHATEST_ON"); m.renderQueue = 2450; }
                    m.SetFloat("_Cull", mi.doubleSided || mi.alphaLinked ? 0 : 2);
                }
                m.enableInstancing = true; EditorUtility.SetDirty(m);
            }
            mats[key] = m; return m;
        }

        /// <summary>Instantiates a converted third-party model (unpacked), with URP materials. Props light from probes and still cast baked shadows.</summary>
        static GameObject Prop(string source, string asset, Transform parent, Vector3 worldPos, float yaw, string name = null, bool isStatic = true, Vector3? scale = null)
        {
            string dir = $"{TP}/{source}/{asset}"; var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{dir}/{asset}.fbx");
            if (!prefab) { log.AppendLine("!! missing model " + asset); return new GameObject(name ?? asset); }
            var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab); PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            // wrapper so that local axes are (x right, z = Blender -y); the FBX arrives yawed 180 degrees
            var go = new GameObject(name ?? asset); if (parent) go.transform.SetParent(parent, true);
            model.name = "Model"; model.transform.SetParent(go.transform, false); model.transform.localPosition = Vector3.zero; model.transform.localRotation = Quaternion.Euler(0, 180, 0);
            go.transform.SetPositionAndRotation(worldPos, Quaternion.Euler(0, yaw, 0)); if (scale.HasValue) go.transform.localScale = scale.Value;
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
            {
                var sm = r.sharedMaterials; for (int i = 0; i < sm.Length; i++) sm[i] = PropMat(dir, sm[i] ? sm[i].name : asset); r.sharedMaterials = sm;
                r.receiveGI = ReceiveGI.LightProbes; r.lightProbeUsage = LightProbeUsage.BlendProbes; r.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
                if (isStatic) GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.ContributeGI | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
            }
            var b = Bounds(go.transform); log.AppendLine($"  prop {go.name}: centre {b.center:F2} size {b.size:F2}");
            return go;
        }
        static Transform Deep(Transform t, string name) => t.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == name);
        static Bounds Bounds(Transform t)
        { var rs = t.GetComponentsInChildren<Renderer>().Where(r => !(r.GetComponent<TMP_Text>())).ToArray(); if (rs.Length == 0) return new Bounds(t.position, Vector3.zero); var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b; }
        static void FitCollider(GameObject go, float shrink = 0f)
        { var b = Bounds(go.transform); var c = new GameObject("Collider"); c.transform.SetParent(go.transform, false); c.transform.position = b.center; c.transform.rotation = Quaternion.identity; var s = b.size - Vector3.one * shrink; var ls = go.transform.lossyScale; c.AddComponent<BoxCollider>().size = new Vector3(Mathf.Abs(s.x / ls.x), Mathf.Abs(s.y / ls.y), Mathf.Abs(s.z / ls.z)); }

        // ======================================================================================= room shell restyle
        static void RestyleRoomShell()
        {
            var room = Find("Environment/ER Exam Room"); if (!room) { log.AppendLine("!! room not found"); return; }
            foreach (var r in room.GetComponentsInChildren<MeshRenderer>(true))
            {
                string n = r.name; if (r.transform.parent != room) continue;
                if (n == "Floor") r.sharedMaterial = Vinyl;
                else if (n == "Ceiling") r.sharedMaterial = CeilTile;
                else if (n == "Wall_Back_TealAccent") r.sharedMaterial = Sage;
                else if (n.StartsWith("Wall_")) r.sharedMaterial = Paint;
                else if (n.StartsWith("Skirt_")) r.sharedMaterial = Skirt;
                else if (n == "CrashRail") r.sharedMaterial = Dado;
            }
            Off("Environment/ER Exam Room/Door"); Off("Environment/ER Exam Room/Window/Exterior_Backdrop");
            var glass = Find("Environment/ER Exam Room/Window/Glass"); if (glass) glass.GetComponent<MeshRenderer>().sharedMaterial = Glass;
            var cur = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Generated/Materials/Curtain_Fabric.mat");
            if (cur) { cur.SetColor("_BaseColor", Hex("#7FA3A0")); cur.SetFloat("_Smoothness", 0.05f); var n = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TP}/ambientCG/Fabric030/Fabric030_2K-JPG_NormalGL.jpg"); if (n) { cur.SetTexture("_BumpMap", n); cur.EnableKeyword("_NORMALMAP"); cur.SetFloat("_BumpScale", 0.6f); cur.SetTextureScale("_BumpMap", Vector2.one * 4); cur.SetTextureScale("_BaseMap", Vector2.one * 4); } EditorUtility.SetDirty(cur); }
            // exterior: limestone ledge outside the room window (parallax cue) – the HDRI sky supplies the view
            var ext = New("Redesign_Exterior", null, Vector3.zero);
            var stone = M("Exterior_Limestone", Hex("#C9C2B2"), 0.15f, 0, "PaintedPlaster017", 1.5f, false, 0.8f);
            Box("Ledge_RoomWindow", ext, new Vector3(0.34f, 0.08f, 2.3f), 0.01f, stone, new Vector3(-3.27f, 0.84f, -0.7f));
            Box("Ledge_CorridorWindow", ext, new Vector3(2.5f, 0.08f, 0.34f), 0.01f, stone, new Vector3(1.9f, 0.84f, CZ0 - 0.27f));
            // light-tightness: roof slab with overhang and corner fillers stop sun/sky leaking through box-wall junctions in the bake
            Box("Roof_Slab", ext, new Vector3(8.4f, 0.06f, 9.2f), 0.005f, stone, new Vector3(0.2f, 2.93f, -1.32f));
            foreach (var x in new[] { -3.06f, 3.06f }) foreach (var z in new[] { -2.56f, 2.56f }) Box("Corner_Filler", ext, new Vector3(0.12f, 2.89f, 0.12f), 0.004f, stone, new Vector3(x, 1.445f, z));
            Box("Reveal_RoomWindow_T", ext, new Vector3(0.3f, 0.3f, 2.3f), 0.01f, stone, new Vector3(-3.27f, 2.42f, -0.7f));
            Box("Reveal_CorridorWindow_T", ext, new Vector3(2.5f, 0.3f, 0.3f), 0.01f, stone, new Vector3(1.9f, 2.32f, CZ0 - 0.27f));
        }

        // ======================================================================================= corridor
        static void BuildCorridor()
        {
            var c = New("Redesign_Corridor", null, Vector3.zero); float cx = (CX0 + CX1) / 2, cz = (CZ0 + CZ1) / 2, len = CX1 - CX0, dep = CZ1 - CZ0;
            var shell = New("Shell", c, Vector3.zero);
            Box("Floor", shell, new Vector3(len, 0.04f, dep), 0.004f, Vinyl, new Vector3(cx, -0.02f, cz), collider: true);
            Box("Floor_Threshold", shell, new Vector3(1.1f, 0.04f, 0.13f), 0.003f, Vinyl, new Vector3(1.85f, -0.02f, -2.56f), collider: true);
            Box("Threshold_Strip", shell, new Vector3(1.04f, 0.006f, 0.05f), 0.002f, Steel, new Vector3(1.85f, 0.002f, -2.53f));
            Box("Ceiling", shell, new Vector3(len, 0.04f, dep), 0.004f, CeilTile, new Vector3(cx, CH + 0.02f, cz));
            // south wall with window x 0.9..2.9, y 0.9..2.1
            Box("Wall_S_A", shell, new Vector3(0.9f - CX0 + 0.12f, WH, 0.12f), 0.004f, Paint, new Vector3((CX0 - 0.12f + 0.9f) / 2, WH / 2, CZ0 - 0.06f), collider: true);
            Box("Wall_S_B", shell, new Vector3(CX1 + 0.12f - 2.9f, WH, 0.12f), 0.004f, Paint, new Vector3((2.9f + CX1 + 0.12f) / 2, WH / 2, CZ0 - 0.06f), collider: true);
            Box("Wall_S_Sill", shell, new Vector3(2.0f, 0.9f, 0.12f), 0.004f, Paint, new Vector3(1.9f, 0.45f, CZ0 - 0.06f), collider: true);
            Box("Wall_S_Head", shell, new Vector3(2.0f, WH - 2.1f, 0.12f), 0.004f, Paint, new Vector3(1.9f, (WH + 2.1f) / 2, CZ0 - 0.06f));
            var winCol = new GameObject("Window_Collider"); winCol.transform.SetParent(shell, false); winCol.transform.position = new Vector3(1.9f, 1.5f, CZ0 - 0.06f); winCol.AddComponent<BoxCollider>().size = new Vector3(2f, 1.2f, 0.06f);
            // west wall with single door opening z -4.37..-3.27 ; east wall with double-door opening z -4.88..-2.76
            Box("Wall_W_A", shell, new Vector3(0.12f, WH, -4.37f - (CZ0 - 0.12f)), 0.004f, Paint, new Vector3(CX0 - 0.06f, WH / 2, (-4.37f + CZ0 - 0.12f) / 2), collider: true);
            Box("Wall_W_B", shell, new Vector3(0.12f, WH, -2.5f + 3.27f), 0.004f, Paint, new Vector3(CX0 - 0.06f, WH / 2, (-3.27f - 2.5f) / 2), collider: true);
            Box("Wall_W_Head", shell, new Vector3(0.12f, WH - 2.15f, 1.1f), 0.004f, Paint, new Vector3(CX0 - 0.06f, (WH + 2.15f) / 2, -3.82f));
            Box("Wall_E_A", shell, new Vector3(0.12f, WH, -4.90f - (CZ0 - 0.12f)), 0.004f, Paint, new Vector3(CX1 + 0.06f, WH / 2, (-4.90f + CZ0 - 0.12f) / 2), collider: true);
            Box("Wall_E_B", shell, new Vector3(0.12f, WH, -2.5f + 2.74f), 0.004f, Paint, new Vector3(CX1 + 0.06f, WH / 2, (-2.74f - 2.5f) / 2), collider: true);
            Box("Wall_E_Head", shell, new Vector3(0.12f, WH - 2.15f, 2.16f), 0.004f, Paint, new Vector3(CX1 + 0.06f, (WH + 2.15f) / 2, -3.82f));
            Box("Wall_N_Filler", shell, new Vector3(0.52f, WH, 0.12f), 0.004f, Paint, new Vector3(3.26f, WH / 2, -2.56f), collider: true);
            AddBoxCollider(shell, "Door_W_Blocker", new Vector3(CX0 - 0.06f, 1.05f, -3.82f), new Vector3(0.12f, 2.1f, 1.1f));
            AddBoxCollider(shell, "Door_E_Blocker", new Vector3(CX1 + 0.06f, 1.05f, -3.82f), new Vector3(0.12f, 2.1f, 2.16f));
            // skirting (coved), dado + cap, on N and S walls; skirting on W/E returns
            void Run(string n, float xa, float xb, float z, float face)
            {
                float L = xb - xa, xm = (xa + xb) / 2;
                Box("Skirting_" + n, shell, new Vector3(L, 0.10f, 0.02f), 0.008f, Skirt, new Vector3(xm, 0.05f, z + face * 0.01f));
                Box("Dado_" + n, shell, new Vector3(L, 0.80f, 0.008f), 0.003f, Dado, new Vector3(xm, 0.50f, z + face * 0.004f));
                Box("DadoCap_" + n, shell, new Vector3(L, 0.03f, 0.016f), 0.006f, Skirt, new Vector3(xm, 0.915f, z + face * 0.008f));
            }
            Run("N_West", CX0, 1.255f, CZ1, -1); Run("N_East", 2.445f, CX1, CZ1, -1); Run("S_West", CX0, 0.9f, CZ0, 1); Run("S_Mid", 0.9f, 2.9f, CZ0, 1); Run("S_East", 2.9f, CX1, CZ0, 1);
            Box("Skirting_W_A", shell, new Vector3(0.02f, 0.10f, 0.63f), 0.008f, Skirt, new Vector3(CX0 + 0.01f, 0.05f, -4.70f));
            Box("Skirting_W_B", shell, new Vector3(0.02f, 0.10f, 0.63f), 0.008f, Skirt, new Vector3(CX0 + 0.01f, 0.05f, -2.94f));
            // handrail (north wall, west of the sign-clear zone)
            var rail = New("Handrail", shell, Vector3.zero); float rz = CZ1 - 0.07f;
            Tube("Rail", rail, new[] { new Vector3(-1.45f, 0.88f, CZ1), new Vector3(-1.45f, 0.88f, rz), new Vector3(-1.38f, 0.88f, rz), new Vector3(1.03f, 0.88f, rz), new Vector3(1.10f, 0.88f, rz), new Vector3(1.10f, 0.88f, CZ1) }, 0.02f, M("Handrail_Oak", Hex("#B79A72"), 0.45f, 0, "Wood049", 2f, true, 0.3f), true, 12, 4);
            foreach (var x in new[] { -0.9f, -0.15f, 0.6f }) Tube("Bracket", rail, new[] { new Vector3(x, 0.80f, CZ1), new Vector3(x, 0.80f, rz), new Vector3(x, 0.862f, rz) }, 0.007f, Steel, true, 8, 3);

            // ceiling services
            var ledMat = M("LED_Panel_Corridor", Color.white, 0.2f, 0, emit: new Color(1f, 0.95f, 0.87f) * 1.3f);
            foreach (var x in new[] { 0.0f, 2.4f })
            {
                var fx = New("Ceiling LED Panel", c, new Vector3(x, CH, cz));
                Box("Trim", fx, new Vector3(0.6f, 0.012f, 0.6f), 0.003f, Alu, new Vector3(0, -0.004f, 0));
                Box("Diffuser", fx, new Vector3(0.55f, 0.008f, 0.55f), 0.002f, ledMat, new Vector3(0, -0.009f, 0));
                var lg = new GameObject("Light"); lg.transform.SetParent(fx, false); lg.transform.localPosition = new Vector3(0, -0.10f, 0); lg.transform.localRotation = Quaternion.Euler(90, 0, 0);
                var li = lg.AddComponent<Light>(); li.type = LightType.Spot; li.spotAngle = 150; li.innerSpotAngle = 80; li.range = 5.5f; li.intensity = 3.2f; li.color = new Color(1f, 0.97f, 0.92f); li.shadows = LightShadows.Soft; li.lightmapBakeType = LightmapBakeType.Baked;
            }
            var diff = New("Air_Diffuser", c, new Vector3(1.2f, CH, cz));
            Box("Frame", diff, new Vector3(0.58f, 0.01f, 0.58f), 0.003f, Powder, new Vector3(0, -0.004f, 0));
            for (int i = 0; i < 4; i++) Box("Louvre", diff, new Vector3(0.50f - i * 0.11f, 0.012f, 0.50f - i * 0.11f), 0.004f, i % 2 == 0 ? M("Diffuser_Shadow", Hex("#8E9291"), 0.3f) : Powder, new Vector3(0, -0.008f - i * 0.004f, 0));
            Lathe("Smoke_Detector", c, new[] { new Vector2(0, 0), new Vector2(0.055f, 0), new Vector2(0.055f, -0.02f), new Vector2(0.04f, -0.04f), new Vector2(0, -0.045f) }, Abs, new Vector3(-1.0f, CH, cz));

            // window (south)
            var w = New("Window", c, new Vector3(1.9f, 1.5f, CZ0 - 0.06f));
            Box("Frame_Top", w, new Vector3(2.06f, 0.06f, 0.14f), 0.01f, Alu, new Vector3(0, 0.60f, 0));
            Box("Frame_Bottom", w, new Vector3(2.06f, 0.05f, 0.14f), 0.01f, Alu, new Vector3(0, -0.60f, 0));
            Box("Frame_L", w, new Vector3(0.06f, 1.2f, 0.14f), 0.01f, Alu, new Vector3(-1.0f, 0, 0));
            Box("Frame_R", w, new Vector3(0.06f, 1.2f, 0.14f), 0.01f, Alu, new Vector3(1.0f, 0, 0));
            Box("Mullion", w, new Vector3(0.05f, 1.16f, 0.10f), 0.01f, Alu, new Vector3(0, 0, 0));
            Box("Glass", w, new Vector3(1.96f, 1.16f, 0.008f), 0.002f, Glass, new Vector3(0, 0, -0.02f), isStatic: false);
            Box("Sill_Board", w, new Vector3(2.1f, 0.03f, 0.22f), 0.008f, SignWhite, new Vector3(0, -0.615f, 0.10f));
            var blind = M("RollerBlind_Fabric", Hex("#D8D6CC"), 0.05f, 0, "Fabric030", 4f, false, 0.4f, twoSided: true);
            Box("Blind_Cassette", w, new Vector3(2.0f, 0.07f, 0.07f), 0.015f, Alu, new Vector3(0, 0.565f, 0.09f));
            Box("Blind_Fabric", w, new Vector3(1.94f, 0.24f, 0.004f), 0.001f, blind, new Vector3(0, 0.42f, 0.09f));
            Box("Blind_Bar", w, new Vector3(1.94f, 0.022f, 0.014f), 0.005f, Alu, new Vector3(0, 0.295f, 0.09f));
            var rad = New("Radiator", c, new Vector3(1.9f, 0.42f, CZ0 + 0.06f));
            Box("Panel", rad, new Vector3(1.6f, 0.5f, 0.07f), 0.012f, Powder, Vector3.zero);
            for (int i = 0; i < 15; i++) Box("Fin", rad, new Vector3(0.012f, 0.44f, 0.012f), 0.004f, Powder, new Vector3(-0.7f + i * 0.1f, 0, 0.037f));
            Tube("Pipe", rad, new[] { new Vector3(-0.72f, -0.2f, 0), new Vector3(-0.72f, -0.42f, 0) }, 0.011f, Steel, true, 8, 2);
            Tube("Pipe", rad, new[] { new Vector3(0.72f, -0.2f, 0), new Vector3(0.72f, -0.42f, 0) }, 0.011f, Steel, true, 8, 2);

            // end doors (closed, static): west single "Exam Room 2", east double fire doors
            var wd = Prop("Sketchfab", "HospitalDoor", c, new Vector3(CX0 - 0.06f, 0, -3.82f), -90, "Door_West_ExamRoom2");
            FixedDoorDetails(wd.transform);
            var ed = New("Door_East_Double", c, new Vector3(CX1 + 0.06f, 0, -3.82f));
            var e1 = Prop("Sketchfab", "HospitalDoor", ed, new Vector3(CX1 + 0.06f, 0, -3.30f), 90, "Leaf_North", true, new Vector3(-1, 1, 1));
            var e2 = Prop("Sketchfab", "HospitalDoor", ed, new Vector3(CX1 + 0.06f, 0, -4.34f), 90, "Leaf_South");
            foreach (var e in new[] { e1, e2 }) { var f = Deep(e.transform, "DoorFrame"); if (f) Object.DestroyImmediate(f.gameObject); }
            var frameMat = M("DoorFrame_PowderCoat", Hex("#D5D6D2"), 0.45f);
            Box("Jamb_N", ed, new Vector3(0.17f, 2.19f, 0.06f), 0.008f, frameMat, new Vector3(0, 1.095f, 1.09f));
            Box("Jamb_S", ed, new Vector3(0.17f, 2.19f, 0.06f), 0.008f, frameMat, new Vector3(0, 1.095f, -1.09f));
            Box("Head", ed, new Vector3(0.17f, 0.06f, 2.24f), 0.008f, frameMat, new Vector3(0, 2.16f, 0));
            Box("Meeting_Stile_Seal", ed, new Vector3(0.02f, 2.1f, 0.02f), 0.004f, Rubber, new Vector3(0.07f, 1.05f, 0));
            // EXIT sign
            var exit = New("Exit_Sign", c, new Vector3(CX1 - 0.03f, 2.36f, -3.82f), Quaternion.Euler(0, 90, 0));
            Box("Body", exit, new Vector3(0.42f, 0.16f, 0.04f), 0.008f, M("Exit_Green", Hex("#1E7A46"), 0.3f, 0, emit: Hex("#1E7A46") * 1.2f), Vector3.zero);
            Text(exit, "EXIT", new Vector3(-0.09f, 0, -0.022f), Vector3.zero, 0.75f, Color.white, new Vector2(0.2f, 0.12f), TextAlignmentOptions.Center, true);
            Text(exit, "مخرج", new Vector3(0.10f, 0.005f, -0.022f), Vector3.zero, 0.6f, Color.white, new Vector2(0.2f, 0.14f));
            // room plates
            Plate(c, new Vector3(2.60f, 1.50f, CZ1 - 0.006f), Quaternion.identity, "غرفة الفحص ٣", "Exam Room 3");
            Plate(c, new Vector3(CX0 + 0.006f, 1.50f, -3.02f), Quaternion.Euler(0, -90, 0), "غرفة الفحص ٢", "Exam Room 2");

            // welcome sign (provisional, no logo)
            var sign = New("Welcome_Sign", c, new Vector3(0.50f, 1.60f, CZ1 - 0.022f));
            Box("Panel", sign, new Vector3(1.20f, 0.45f, 0.008f), 0.003f, SignWhite, Vector3.zero);
            Box("Accent_Bar", sign, new Vector3(1.20f, 0.012f, 0.009f), 0.002f, M("Sign_Green", Hex("#3B7A66"), 0.35f), new Vector3(0, -0.150f, 0));
            foreach (var p in new[] { new Vector2(-0.56f, 0.185f), new Vector2(0.56f, 0.185f), new Vector2(-0.56f, -0.185f), new Vector2(0.56f, -0.185f) })
                Lathe("Standoff", sign, new[] { new Vector2(0, 0), new Vector2(0.009f, 0), new Vector2(0.009f, 0.03f), new Vector2(0, 0.03f) }, Steel, new Vector3(p.x, p.y, 0.022f), new Vector3(-90, 0, 0), 14);
            var ink = Hex("#27423B");
            Text(sign, "جامعة بيرزيت — أهلاً وسهلاً", new Vector3(0, 0.105f, -0.0052f), Vector3.zero, 0.80f, ink, new Vector2(1.14f, 0.2f), TextAlignmentOptions.Center, false, false);
            Text(sign, "Birzeit University — Welcome", new Vector3(0, -0.062f, -0.0052f), Vector3.zero, 0.56f, ink, new Vector2(1.14f, 0.12f), TextAlignmentOptions.Center, true, false);
            Text(sign, "Clinical Skills Simulation Suite · provisional signage", new Vector3(0, -0.190f, -0.0052f), Vector3.zero, 0.19f, Hex("#3F4A47"), new Vector2(1.14f, 0.04f));

            // notice board
            var nb = New("Notice_Board", c, new Vector3(-0.95f, 1.50f, CZ1 - 0.012f));
            Box("Felt", nb, new Vector3(0.90f, 0.60f, 0.012f), 0.002f, M("NoticeBoard_Felt", Hex("#58707A"), 0.03f, 0, "Fabric030", 6f, false, 0.7f), Vector3.zero);
            foreach (var (p, s) in new[] { (new Vector3(0, 0.305f, 0), new Vector3(0.93f, 0.02f, 0.02f)), (new Vector3(0, -0.305f, 0), new Vector3(0.93f, 0.02f, 0.02f)), (new Vector3(-0.455f, 0, 0), new Vector3(0.02f, 0.6f, 0.02f)), (new Vector3(0.455f, 0, 0), new Vector3(0.02f, 0.6f, 0.02f)) })
                Box("Frame", nb, s, 0.004f, Alu, p);
            var paper = M("Paper_White", Hex("#E9E8E2"), 0.1f);
            Sheet(nb, paper, new Vector3(-0.30f, 0.07f, -0.008f), 1.5f, "Clinical Skills Rota", "Week 48\nMon  Group A  08:30\nTue  Group B  08:30\nWed  Group C  10:30\nThu  Group D  08:30", Hex("#2F5D50"));
            Sheet(nb, paper, new Vector3(-0.07f, 0.06f, -0.008f), -2f, "Hand Hygiene Audit", "November\nCompliance 92 %\nTarget 95 %\n\nClean hands before\nevery patient contact.", Hex("#2F6690"));
            Sheet(nb, paper, new Vector3(0.16f, 0.075f, -0.008f), 1f, "Simulation Etiquette", "1  Treat the patient as real\n2  Introduce yourself\n3  Ask for consent\n4  Think aloud\n5  What happens here stays here", Hex("#7A5C2E"));
            Sheet(nb, paper, new Vector3(0.345f, -0.06f, -0.008f), -1.5f, "DRAFT", "Scenario content is not\nclinically validated.\nAll patients are fictional.", Hex("#9B2F2F"), 0.15f, 0.2f);
            Text(nb, "لوحة الإعلانات", new Vector3(0.25f, 0.345f, -0.004f), Vector3.zero, 0.26f, Hex("#3A4A46"), new Vector2(0.4f, 0.05f), TextAlignmentOptions.Right);
            Text(nb, "Notices", new Vector3(-0.25f, 0.342f, -0.004f), Vector3.zero, 0.2f, Hex("#3A4A46"), new Vector2(0.4f, 0.05f), TextAlignmentOptions.Left, true);

            // hanging way-finding sign
            var hs = New("Hanging_Sign", c, new Vector3(2.55f, 2.26f, cz), Quaternion.Euler(0, 90, 0));
            Box("Panel", hs, new Vector3(0.95f, 0.22f, 0.02f), 0.006f, M("Sign_Wayfinding", Hex("#27423B"), 0.3f), Vector3.zero);
            foreach (var x in new[] { -0.38f, 0.38f }) Tube("Rod", hs, new[] { new Vector3(x, 0.11f, 0), new Vector3(x, CH - 2.26f, 0) }, 0.004f, Steel, true, 6, 2);
            Text(hs, "الطوارئ", new Vector3(0.20f, 0.045f, -0.011f), Vector3.zero, 0.5f, Color.white, new Vector2(0.5f, 0.1f), TextAlignmentOptions.Right);
            Text(hs, "Emergency", new Vector3(0.20f, -0.05f, -0.011f), Vector3.zero, 0.34f, Color.white, new Vector2(0.5f, 0.06f), TextAlignmentOptions.Right);
            Text(hs, "الأشعة", new Vector3(-0.20f, 0.045f, -0.011f), Vector3.zero, 0.5f, Color.white, new Vector2(0.5f, 0.1f), TextAlignmentOptions.Left);
            Text(hs, "Radiology", new Vector3(-0.20f, -0.05f, -0.011f), Vector3.zero, 0.34f, Color.white, new Vector2(0.5f, 0.06f), TextAlignmentOptions.Left);
            Box("Divider", hs, new Vector3(0.004f, 0.16f, 0.022f), 0.001f, SignWhite, Vector3.zero);

            // furniture and safety fixtures
            var bench = Prop("Sketchfab", "WaitingBeamSeating", c, new Vector3(-0.42f, 0, CZ0 + 0.34f), 270, "Beam_Seating"); FitCollider(bench, 0.04f);
            var san = Prop("Sketchfab", "SanitizerStation", c, new Vector3(2.78f, 0, CZ1 - 0.24f), 180, "Sanitiser_Station"); FitCollider(san);
            var fe = New("Fire_Extinguisher", c, new Vector3(3.20f, 0, CZ1 - 0.14f), Quaternion.Euler(0, 180, 0));
            Prop("PolyHaven", "FireExtinguisher", fe, fe.position + new Vector3(0, 0.52f, 0), 20, "Extinguisher");
            Box("Bracket", fe, new Vector3(0.10f, 0.30f, 0.02f), 0.004f, M("Bracket_Red", Hex("#8E1E1E"), 0.4f), new Vector3(0, 0.80f, -0.125f));
            Box("Shelf", fe, new Vector3(0.20f, 0.012f, 0.16f), 0.004f, M("Bracket_Red", Hex("#8E1E1E"), 0.4f), new Vector3(0, 0.515f, -0.06f));
            var fs = New("ID_Sign", fe, fe.position + new Vector3(0, 1.72f, 0.135f), Quaternion.identity);
            Box("Plate", fs, new Vector3(0.20f, 0.26f, 0.004f), 0.002f, M("Sign_FireRed", Hex("#B3261E"), 0.35f), Vector3.zero);
            Text(fs, "طفاية حريق", new Vector3(0, 0.04f, -0.003f), Vector3.zero, 0.26f, Color.white, new Vector2(0.19f, 0.08f));
            Text(fs, "FIRE\nEXTINGUISHER", new Vector3(0, -0.06f, -0.003f), Vector3.zero, 0.16f, Color.white, new Vector2(0.19f, 0.1f), TextAlignmentOptions.Center, true);

            // ambience: low HVAC room tone + muffled plant behind the fire doors
            Ambience(c, "Ambience_Corridor_HVAC", new Vector3(1.2f, CH - 0.1f, cz), Root + "/Audio/Ambience/BSB_1471_air-conditioner.wav", 0.10f, 1.5f, 9f, 0);
            Ambience(c, "Ambience_BeyondFireDoors", new Vector3(CX1 + 0.6f, 1.4f, cz), Root + "/Audio/Ambience/BSB_0174_ventilation-2.wav", 0.06f, 1.0f, 7f, 900);

            // teleport surface
            var ex = Object.FindObjectsByType<TeleportationArea>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(t => t.name == "Teleport Floor");
            foreach (var (n, pos, size) in new[] { ("Teleport Corridor", new Vector3(cx, 0.001f, cz), new Vector3(len - 0.5f, 0.002f, dep - 0.9f)), ("Teleport Doorway", new Vector3(1.85f, 0.001f, -2.60f), new Vector3(0.8f, 0.002f, 0.7f)) })
            {
                var tp = new GameObject(n); tp.transform.SetParent(c, false); tp.transform.position = pos; if (ex) tp.layer = ex.gameObject.layer;
                var bc = tp.AddComponent<BoxCollider>(); bc.size = size; var ta = tp.AddComponent<TeleportationArea>();
                if (ex) { ta.interactionLayers = ex.interactionLayers; ta.interactionManager = ex.interactionManager; ta.teleportationProvider = ex.teleportationProvider; ta.matchOrientation = ex.matchOrientation; ta.teleportTrigger = ex.teleportTrigger; }
                ta.colliders.Clear(); ta.colliders.Add(bc);
            }
        }

        static void FixedDoorDetails(Transform door) { foreach (var r in door.GetComponentsInChildren<MeshRenderer>()) GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.ContributeGI | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic); }

        static void Plate(Transform parent, Vector3 pos, Quaternion rot, string ar, string en)
        {
            var p = New("Room_Plate", parent, pos, rot);
            Box("Plate", p, new Vector3(0.30f, 0.12f, 0.006f), 0.002f, SignWhite, Vector3.zero);
            Box("Bar", p, new Vector3(0.30f, 0.008f, 0.007f), 0.001f, M("Sign_Green", Hex("#2F5D50"), 0.35f), new Vector3(0, -0.052f, 0));
            Text(p, ar, new Vector3(0, 0.022f, -0.0045f), Vector3.zero, 0.30f, Hex("#27423B"), new Vector2(0.28f, 0.06f));
            Text(p, en, new Vector3(0, -0.025f, -0.0045f), Vector3.zero, 0.20f, Hex("#27423B"), new Vector2(0.28f, 0.04f), TextAlignmentOptions.Center, true);
        }

        static void Sheet(Transform parent, Material paper, Vector3 pos, float tilt, string title, string body, Color accent, float w = 0.21f, float h = 0.297f)
        {
            var s = New("Sheet " + title, parent, parent.TransformPoint(pos), parent.rotation * Quaternion.Euler(0, 0, tilt));
            Box("Paper", s, new Vector3(w, h, 0.0015f), 0.0005f, paper, Vector3.zero, isStatic: false);
            Lathe("Pin", s, new[] { new Vector2(0, 0), new Vector2(0.006f, 0), new Vector2(0.004f, 0.006f), new Vector2(0, 0.007f) }, M("Pin_Red", Hex("#B3261E"), 0.6f), new Vector3(0, h / 2 - 0.015f, -0.001f), new Vector3(-90, 0, 0), 10, false);
            Text(s, title, new Vector3(0, h / 2 - 0.045f, -0.0012f), Vector3.zero, 0.15f, accent, new Vector2(w - 0.02f, 0.03f), TextAlignmentOptions.Center, true);
            Text(s, body, new Vector3(0, -0.02f, -0.0012f), Vector3.zero, 0.095f, Hex("#3B3F42"), new Vector2(w - 0.03f, h - 0.1f), TextAlignmentOptions.TopLeft);
        }

        static void Ambience(Transform parent, string name, Vector3 pos, string clipPath, float vol, float min, float max, int lowpass)
        {
            var go = New(name, parent, pos).gameObject; var a = go.AddComponent<AudioSource>(); a.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);
            if (!a.clip) log.AppendLine("!! missing clip " + clipPath);
            a.loop = true; a.playOnAwake = true; a.volume = vol; a.spatialBlend = 1; a.rolloffMode = AudioRolloffMode.Linear; a.minDistance = min; a.maxDistance = max; a.dopplerLevel = 0;
            if (lowpass > 0) go.AddComponent<AudioLowPassFilter>().cutoffFrequency = lowpass;
        }

        // ======================================================================================= door
        static HospitalDoor BuildDoor()
        {
            var root = New("Redesign_Door", null, new Vector3(1.85f, 0, -2.56f));
            var set = Prop("Sketchfab", "HospitalDoor", root, root.position, 0, "Exam Room 3 Door", false).transform;
            var frame = Deep(set, "DoorFrame"); var leaf = Deep(set, "DoorLeaf"); var handleMesh = Deep(set, "DoorHandle");
            if (!frame || !leaf || !handleMesh) { log.AppendLine("!! door parts missing: " + string.Join(",", set.Cast<Transform>().Select(t => t.name))); return null; }
            GameObjectUtility.SetStaticEditorFlags(frame.gameObject, StaticEditorFlags.ContributeGI | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
            var hinge = New("Hinge", set, leaf.position, Quaternion.Euler(0, 180, 0));
            leaf.name = "Door_Leaf"; leaf.SetParent(hinge, true);
            var lb = leaf.GetComponent<MeshRenderer>().bounds;
            var bc = leaf.gameObject.AddComponent<BoxCollider>(); bc.center = leaf.InverseTransformPoint(lb.center); bc.size = new Vector3(lb.size.x - 0.03f, lb.size.y - 0.02f, 0.05f);
            // lever: pivot on the spindle, local X along the spindle (door normal)
            var hb = handleMesh.GetComponent<MeshRenderer>().bounds;
            var pivot = New("Door_Handle", leaf, handleMesh.position, Quaternion.Euler(0, -90, 0));
            Vector3 tip = new Vector3(hb.max.x, handleMesh.position.y, handleMesh.position.z) - pivot.position; // lever points at the hinge side (+x)
            Vector3 after = pivot.rotation * Quaternion.Euler(-32, 0, 0) * Quaternion.Inverse(pivot.rotation) * tip;
            if (after.y > 0) pivot.rotation = Quaternion.Euler(0, 90, 0);
            handleMesh.name = "Lever_Mesh"; handleMesh.SetParent(pivot, true);
            var hc = pivot.gameObject.AddComponent<BoxCollider>(); hc.center = pivot.InverseTransformPoint(hb.center); var ls = pivot.InverseTransformVector(hb.size); hc.size = new Vector3(Mathf.Abs(ls.x) + 0.06f, Mathf.Abs(ls.y) + 0.08f, Mathf.Abs(ls.z) + 0.06f);
            var si = pivot.gameObject.AddComponent<XRSimpleInteractable>(); si.colliders.Clear(); si.colliders.Add(hc);
            // overhead closer on the room face
            var closer = New("Closer", leaf, new Vector3(lb.max.x - 0.22f, lb.max.y - 0.07f, lb.max.z + 0.025f));
            Box("Body", closer, new Vector3(0.26f, 0.055f, 0.045f), 0.008f, M("Closer_Silver", Hex("#A9ACAE"), 0.5f, 0.7f), Vector3.zero, isStatic: false);
            Tube("Arm", closer, new[] { new Vector3(0.08f, 0.035f, 0), new Vector3(0.20f, 0.06f, 0.02f), new Vector3(0.30f, 0.11f, 0.0f) }, 0.006f, Steel, false, 6, 3);
            // audio + behaviour
            var src = leaf.gameObject.AddComponent<AudioSource>(); src.spatialBlend = 1; src.playOnAwake = false; src.minDistance = 0.8f; src.maxDistance = 10; src.dopplerLevel = 0;
            var d = root.gameObject.AddComponent<HospitalDoor>(); d.hinge = hinge; d.leaf = leaf; d.handle = pivot; d.handleInteractable = si; d.openSign = 1f; d.maxAngle = 95f; d.source = src;
            d.latchClip = AssetDatabase.LoadAssetAtPath<AudioClip>(Root + "/Audio/Door/metalLatch.ogg"); d.swingClip = AssetDatabase.LoadAssetAtPath<AudioClip>(Root + "/Audio/Door/creak2.ogg"); d.closeClip = AssetDatabase.LoadAssetAtPath<AudioClip>(Root + "/Audio/Door/doorClose_4.ogg");
            // verify the swing direction numerically (+angle must move the latch edge into the room, +z)
            Vector3 tipDir = hinge.rotation * Quaternion.Euler(0, 60, 0) * Vector3.right; if (tipDir.z < 0) d.openSign = -1f;
            log.AppendLine($"  door: hinge {hinge.position:F3} leaf bounds {lb.min:F2}..{lb.max:F2} handle {pivot.position:F3} leverDownY {after.y:F3} openSign {d.openSign}");
            // room-side "in use" lamp + keep the existing EXAM ROOM 3 sign inside
            Lathe("InUse_Lamp", root, new[] { new Vector2(0, 0), new Vector2(0.03f, 0), new Vector2(0.028f, 0.012f), new Vector2(0.018f, 0.024f), new Vector2(0, 0.028f) }, M("InUse_Amber", Hex("#C98A2B"), 0.6f, 0, emit: Hex("#C98A2B") * 0.6f), new Vector3(0, 2.32f, -0.062f), new Vector3(-90, 0, 0), 16);
            return d;
        }

        // ======================================================================================= room furniture
        static void UpgradeRoom()
        {
            foreach (var p in new[] { "Props/drawer_cabinet", "Props/modern_arm_chair_01", "Props/metal_stool_02", "Props/metal_trash_can", "Props/fire_alarm", "Props/Poster CHEST PAIN", "Props/Poster MEDSIM AI", "Medical Equipment/IV Stand", "Medical Equipment/Instrument Trolley/medical_tape", "Medical Equipment/Instrument Trolley/TopShelf/Glove_Box_M_Trolley" }) Off(p);
            var r = New("Redesign_Room", null, Vector3.zero);

            // ---- bed
            var bed = Find("Environment/Hospital Bed");
            if (bed)
            {
                var keep = new HashSet<string> { "Mattress_Foot", "Head_Section", "Patient Chart" };
                foreach (Transform ch in bed) if (!keep.Contains(ch.name)) ch.gameObject.SetActive(false);
                var head = bed.Find("Head_Section"); var oldPillow = head ? head.Find("Pillow") : null; if (oldPillow) oldPillow.gameObject.SetActive(false);
                foreach (var n in new[] { "Mattress_Foot", "Head_Section/Mattress_Head", "Head_Section/Sheet_Head" }) { var t = bed.Find(n); if (t) t.GetComponent<MeshRenderer>().sharedMaterial = n.Contains("Sheet") || n.Contains("Foot") ? Linen : M("Mattress_Cover", Hex("#3F566B"), 0.4f, 0, "Leather026", 4f, false, 0.4f); }
                var v2 = Prop("Sketchfab", "HospitalBed", bed, bed.position, 0, "v2_Bed");
                AddBoxCollider(v2.transform, "Collider_Frame", new Vector3(0, 0.27f, 0.0f), new Vector3(0.98f, 0.52f, 2.16f));
                AddBoxCollider(v2.transform, "Collider_Headboard", new Vector3(0, 0.62f, 1.09f), new Vector3(0.98f, 0.76f, 0.07f));
                AddBoxCollider(v2.transform, "Collider_Footboard", new Vector3(0, 0.55f, -1.09f), new Vector3(0.98f, 0.70f, 0.07f));
                AddBoxCollider(v2.transform, "Collider_FarRails", new Vector3(-0.48f, 0.70f, 0.0f), new Vector3(0.05f, 0.34f, 1.9f));
                if (head)
                {
                    var mh = head.Find("Mattress_Head"); var deck = New("v2_Backrest", head, head.position, head.rotation);
                    Vector3 mp = mh ? mh.localPosition : new Vector3(0, 0, 0.42f);
                    Box("Deck_Panel", deck, new Vector3(0.84f, 0.035f, 0.80f), 0.01f, Library("Bed_Grey"), mp + new Vector3(0, -0.095f, -0.01f), isStatic: true);
                    var act = New("v2_Actuator", bed, bed.position);
                    Tube("Actuator", act, new[] { new Vector3(0, 0.43f, 0.30f), new Vector3(0, 0.66f, 0.62f) }, 0.022f, Library("Bed_Steel"), true, 10, 2);
                    Tube("Actuator_Rod", act, new[] { new Vector3(0, 0.66f, 0.62f), new Vector3(0, 0.78f, 0.79f) }, 0.012f, Steel, true, 8, 2);
                    var pil = Prop("Sketchfab", "BedPillow", head, new Vector3(bed.position.x, 1.15f, bed.position.z + 0.60f), 0, "v2_Pillow"); pil.transform.rotation = Quaternion.Euler(-56, 0, 0);
                }
                Prop("MedSim", "Blanket", bed, bed.position, 0, "v2_Blanket", false);
                var bb = Bounds(v2.transform); var chart = bed.Find("Patient Chart");
                if (chart) { var cp = chart.position; cp.z = bb.min.z - 0.025f; cp.y = 0.78f; chart.position = cp; chart.rotation = Quaternion.Euler(4, 0, 0); }
                log.AppendLine($"  bed bounds {bb.min:F2}..{bb.max:F2}");
            }

            // ---- seating
            var chair = Prop("PolyHaven", "SchoolChair_01", r, new Vector3(-2.35f, 0, -1.70f), 55, "Visitor_Chair"); FitCollider(chair, 0.03f);
            Stool(r, new Vector3(2.25f, 0, 0.72f));
            // ---- waste
            PedalBin(r, new Vector3(2.80f, 0, 0.36f), "General_Waste", "#2B2D2F", "نفايات عامة", "General waste");
            PedalBin(r, new Vector3(2.80f, 0, -0.02f), "Clinical_Waste", "#D9772B", "نفايات طبية", "Clinical waste");
            // ---- resus trolley where the domestic drawer chest stood
            var cart = Prop("Sketchfab", "CodeCart", r, new Vector3(-2.42f, 0, 2.08f), 180, "Resus_Trolley"); FitCollider(cart, 0.03f);
            // ---- over-bed table pushed aside on the far side, IV pole
            var obt = Prop("Sketchfab", "OverbedTable", r, new Vector3(-0.66f, 0, 0.05f), 90, "Overbed_Table"); FitCollider(obt, 0.03f);
            Lathe("Vomit_Bowl", obt.transform, new[] { new Vector2(0, 0.0f), new Vector2(0.06f, 0.0f), new Vector2(0.085f, 0.05f), new Vector2(0.08f, 0.052f), new Vector2(0.056f, 0.006f), new Vector2(0, 0.006f) }, M("Pulp_Grey", Hex("#B8B4A8"), 0.05f), obt.transform.InverseTransformPoint(new Vector3(Bounds(obt.transform).center.x, Bounds(obt.transform).max.y, Bounds(obt.transform).center.z - 0.1f)), null, 20, false);
            var iv = Prop("Sketchfab", "IVPole", r, new Vector3(-0.62f, 0, 1.62f), 20, "IV_Pole"); AddBoxCollider(iv.transform, "Collider", new Vector3(0, 1.0f, 0), new Vector3(0.12f, 2.0f, 0.12f));
            // ---- tablet stand out of the working zone
            var tab = Find("Environment/Clinical Tablet Stand"); if (tab) { tab.position = new Vector3(2.62f, 0, -0.62f); tab.rotation = Quaternion.Euler(0, 110, 0); }
            // ---- neutral observations chart replaces the give-away poster
            var chartWall = New("Poster_Observations", r, new Vector3(-0.20f, 1.60f, -2.488f), Quaternion.Euler(0, 180, 0));
            Box("Frame", chartWall, new Vector3(0.46f, 0.62f, 0.012f), 0.004f, Alu, Vector3.zero);
            Box("Paper", chartWall, new Vector3(0.42f, 0.58f, 0.002f), 0.001f, M("Paper_White", Hex("#E9E8E2"), 0.1f), new Vector3(0, 0, -0.006f));
            Box("Header", chartWall, new Vector3(0.42f, 0.08f, 0.002f), 0.001f, M("Sign_Green", Hex("#2F5D50"), 0.35f), new Vector3(0, 0.25f, -0.0065f));
            Text(chartWall, "OBSERVATIONS", new Vector3(0, 0.25f, -0.008f), Vector3.zero, 0.3f, Color.white, new Vector2(0.4f, 0.06f), TextAlignmentOptions.Center, true);
            Text(chartWall, "Record with every set:\n\n•  Respiratory rate\n•  Oxygen saturation\n•  Blood pressure\n•  Pulse\n•  Level of consciousness\n•  Temperature\n\n<size=75%>Escalate if you are worried.</size>", new Vector3(0, -0.04f, -0.008f), Vector3.zero, 0.2f, Hex("#2E3336"), new Vector2(0.36f, 0.46f), TextAlignmentOptions.TopLeft);
            // ---- sink dressing: towel dispenser
            var td = New("Towel_Dispenser", r, new Vector3(2.94f, 1.52f, 1.02f));
            Box("Body", td, new Vector3(0.10f, 0.34f, 0.27f), 0.02f, Abs, Vector3.zero); Box("Slot", td, new Vector3(0.06f, 0.012f, 0.2f), 0.004f, AbsDark, new Vector3(-0.015f, -0.172f, 0)); Box("Towel", td, new Vector3(0.003f, 0.05f, 0.18f), 0.001f, M("Paper_White", Hex("#E9E8E2"), 0.1f), new Vector3(-0.02f, -0.19f, 0));
        }

        static void Stool(Transform parent, Vector3 pos)
        {
            var s = New("Doctor_Stool", parent, pos); var chrome = M("Chrome", Hex("#D0D2D4"), 0.9f, 1f);
            for (int i = 0; i < 5; i++)
            {
                var q = Quaternion.Euler(0, i * 72, 0);
                Box("Leg", s, new Vector3(0.045f, 0.022f, 0.27f), 0.008f, AbsDark, q * new Vector3(0, 0.085f, 0.135f) , new Vector3(0, i * 72, 0));
                Lathe("Castor", s, new[] { new Vector2(0, -0.014f), new Vector2(0.022f, -0.014f), new Vector2(0.025f, 0), new Vector2(0.022f, 0.014f), new Vector2(0, 0.014f) }, Rubber, q * new Vector3(0, 0.025f, 0.255f), new Vector3(0, i * 72, 90), 14);
                Tube("Stem", s, new[] { q * new Vector3(0, 0.05f, 0.255f), q * new Vector3(0, 0.08f, 0.255f) }, 0.007f, chrome, true, 6, 2);
            }
            Lathe("Hub", s, new[] { new Vector2(0, 0.07f), new Vector2(0.04f, 0.07f), new Vector2(0.034f, 0.11f), new Vector2(0.026f, 0.115f), new Vector2(0.026f, 0.27f), new Vector2(0.018f, 0.275f), new Vector2(0.018f, 0.44f), new Vector2(0, 0.44f) }, chrome, Vector3.zero);
            Lathe("Height_Ring", s, Torus(0.19f, 0.008f, 0.22f), chrome, Vector3.zero);
            foreach (var a in new[] { 0f, 120f, 240f }) Tube("Ring_Spoke", s, new[] { new Vector3(0, 0.22f, 0), Quaternion.Euler(0, a, 0) * new Vector3(0, 0.22f, 0.19f) }, 0.005f, chrome, true, 6, 2);
            Lathe("Seat_Pan", s, new[] { new Vector2(0, 0.44f), new Vector2(0.15f, 0.44f), new Vector2(0.17f, 0.455f), new Vector2(0, 0.455f) }, AbsDark, Vector3.zero);
            Lathe("Seat", s, new[] { new Vector2(0, 0.455f), new Vector2(0.175f, 0.455f), new Vector2(0.183f, 0.47f), new Vector2(0.183f, 0.505f), new Vector2(0.172f, 0.522f), new Vector2(0.12f, 0.528f), new Vector2(0, 0.524f) }, Upholstery("Stool_Vinyl", "#2F3A40"), Vector3.zero, null, 36);
            Lathe("Welt", s, Torus(0.183f, 0.004f, 0.49f), Upholstery("Stool_Vinyl", "#2F3A40"), Vector3.zero, null, 36);
            var c = s.gameObject.AddComponent<CapsuleCollider>(); c.center = new Vector3(0, 0.27f, 0); c.radius = 0.22f; c.height = 0.54f;
        }

        static void PedalBin(Transform parent, Vector3 pos, string name, string lidHex, string ar, string en)
        {
            var b = New("Pedal_Bin_" + name, parent, pos, Quaternion.Euler(0, -90, 0)); // front faces the room (-x)
            Box("Body", b, new Vector3(0.30f, 0.50f, 0.34f), 0.03f, Abs, new Vector3(0, 0.275f, 0));
            Box("Bag_Rim", b, new Vector3(0.305f, 0.012f, 0.345f), 0.004f, lidHex == "#2B2D2F" ? M("Bag_Black", Hex("#18191A"), 0.5f) : M("Bag_Orange", Hex("#E08A3A"), 0.5f), new Vector3(0, 0.528f, 0));
            Box("Lid", b, new Vector3(0.315f, 0.045f, 0.355f), 0.018f, M("BinLid_" + name, Hex(lidHex), 0.5f, 0, "Plastic013A", 3f, false, 0.2f), new Vector3(0, 0.556f, 0));
            Box("Lid_Hinge", b, new Vector3(0.20f, 0.03f, 0.03f), 0.01f, AbsDark, new Vector3(0, 0.54f, 0.17f));
            Box("Pedal", b, new Vector3(0.12f, 0.016f, 0.07f), 0.006f, AbsDark, new Vector3(0, 0.03f, -0.20f), new Vector3(-12, 0, 0));
            Box("Foot_Plinth", b, new Vector3(0.29f, 0.03f, 0.33f), 0.01f, AbsDark, new Vector3(0, 0.015f, 0));
            var lab = New("Label", b, b.TransformPoint(new Vector3(0, 0.36f, -0.1712f)), b.rotation);
            Box("Sticker", lab, new Vector3(0.16f, 0.09f, 0.001f), 0.0004f, M("Sticker_" + name, Hex(lidHex == "#2B2D2F" ? "#3A3D40" : "#E9A23B"), 0.3f), Vector3.zero, isStatic: false);
            Text(lab, ar, new Vector3(0, 0.018f, -0.001f), Vector3.zero, 0.2f, Color.white, new Vector2(0.15f, 0.04f));
            Text(lab, en, new Vector3(0, -0.022f, -0.001f), Vector3.zero, 0.13f, Color.white, new Vector2(0.15f, 0.03f), TextAlignmentOptions.Center, true);
            b.gameObject.AddComponent<BoxCollider>().center = new Vector3(0, 0.29f, 0); b.GetComponent<BoxCollider>().size = new Vector3(0.31f, 0.58f, 0.36f);
        }

        // ======================================================================================= monitor + ECG housings
        static void UpgradeEquipment()
        {
            var mon = Find("Medical Equipment/Bedside Patient Monitor");
            if (mon)
            {
                var body = mon.Find("Body"); if (body) body.GetComponent<MeshRenderer>().sharedMaterial = Abs;
                var v = New("v2_Housing", mon, mon.position, mon.rotation);
                Tube("Carry_Handle", v, new[] { new Vector3(-0.11f, 0.155f, 0.03f), new Vector3(-0.11f, 0.195f, 0.03f), new Vector3(0.11f, 0.195f, 0.03f), new Vector3(0.11f, 0.155f, 0.03f) }, 0.009f, AbsDark, false, 10, 6);
                Box("Alarm_Bar", v, new Vector3(0.11f, 0.012f, 0.03f), 0.005f, M("Monitor_AlarmBar", new Color(0.85f, 0.9f, 0.95f, 1f), 0.8f), new Vector3(0, 0.162f, -0.002f), isStatic: false);
                for (int i = 0; i < 5; i++) Box("Key", v, new Vector3(0.032f, 0.013f, 0.004f), 0.003f, M("Monitor_Key", Hex("#8D9498"), 0.4f), new Vector3(-0.17f + i * 0.043f, -0.141f, -0.016f), isStatic: false);
                Lathe("Rotary_Knob", v, new[] { new Vector2(0, 0), new Vector2(0.016f, 0), new Vector2(0.015f, 0.012f), new Vector2(0.012f, 0.014f), new Vector2(0, 0.014f) }, AbsDark, new Vector3(0.165f, -0.141f, -0.015f), new Vector3(-90, 0, 0), 20, false);
                Box("Power_LED", v, new Vector3(0.006f, 0.006f, 0.002f), 0.001f, M("LED_Green", Hex("#2FBF6B"), 0.6f, 0, emit: Hex("#2FBF6B") * 1.5f), new Vector3(0.115f, -0.141f, -0.0155f), isStatic: false);
                Box("Connector_Panel", v, new Vector3(0.006f, 0.20f, 0.06f), 0.002f, AbsDark, new Vector3(-0.212f, -0.02f, 0.03f), isStatic: false);
                string[] sock = { "#3FA85A", "#3B7DC4", "#D8B23A", "#C8C8C8" };
                for (int i = 0; i < 4; i++) Lathe("Socket", v, new[] { new Vector2(0, 0), new Vector2(0.011f, 0), new Vector2(0.011f, 0.006f), new Vector2(0.007f, 0.006f), new Vector2(0.007f, 0.002f), new Vector2(0, 0.002f) }, M("Socket_" + i, Hex(sock[i]), 0.5f), new Vector3(-0.215f, 0.05f - i * 0.045f, 0.03f), new Vector3(0, 0, 90), 14, false);
                Box("Vesa_Plate", v, new Vector3(0.12f, 0.12f, 0.02f), 0.004f, AbsDark, new Vector3(0, 0, 0.082f), isStatic: false);
                Tube("Cable_ECG", v, new[] { new Vector3(-0.218f, 0.05f, 0.03f), new Vector3(-0.27f, 0.02f, 0.04f), new Vector3(-0.29f, -0.2f, 0.10f), new Vector3(-0.25f, -0.42f, 0.22f) }, 0.004f, M("Cable_Green", Hex("#567A60"), 0.4f), false, 6, 6);
                Tube("Cable_SpO2", v, new[] { new Vector3(-0.218f, 0.005f, 0.03f), new Vector3(-0.26f, -0.03f, 0.05f), new Vector3(-0.27f, -0.25f, 0.12f), new Vector3(-0.22f, -0.45f, 0.24f) }, 0.0035f, M("Cable_Grey2", Hex("#9AA0A4"), 0.4f), false, 6, 6);
            }
            var ecg = Find("Medical Equipment/ECG Machine Cart");
            if (ecg)
            {
                foreach (var n in new[] { "Base", "Column" }) { var t = ecg.Find(n); if (t) t.gameObject.SetActive(false); }
                var eb = ecg.Find("ECG_Body"); if (eb) eb.GetComponent<MeshRenderer>().sharedMaterial = Abs;
                var bez = ecg.Find("Screen_Pivot/Screen_Bezel"); if (bez) bez.GetComponent<MeshRenderer>().sharedMaterial = AbsDark;
                var v = New("v2_Cart", ecg, ecg.position, ecg.rotation);
                Box("Base_Frame", v, new Vector3(0.50f, 0.035f, 0.46f), 0.015f, M("Cart_Base", Hex("#6C7175"), 0.45f), new Vector3(0, 0.105f, 0));
                foreach (var x in new[] { -0.215f, 0.215f }) foreach (var z in new[] { -0.195f, 0.195f })
                { Tube("Upright", v, new[] { new Vector3(x, 0.12f, z), new Vector3(x, 0.83f, z) }, 0.011f, Steel, true, 10, 2); Box("Castor_Fork", v, new Vector3(0.03f, 0.05f, 0.04f), 0.006f, AbsDark, new Vector3(x * 0.93f, 0.075f, z * 0.92f)); }
                Box("Top_Tray", v, new Vector3(0.48f, 0.018f, 0.44f), 0.008f, Abs, new Vector3(0, 0.826f, 0));
                Box("Drawer", v, new Vector3(0.44f, 0.11f, 0.40f), 0.008f, Abs, new Vector3(0, 0.755f, 0));
                Box("Drawer_Front", v, new Vector3(0.40f, 0.085f, 0.006f), 0.003f, M("Cart_DrawerFront", Hex("#B9C4C8"), 0.45f), new Vector3(0, 0.755f, -0.203f));
                Tube("Drawer_Handle", v, new[] { new Vector3(-0.08f, 0.76f, -0.206f), new Vector3(-0.08f, 0.76f, -0.228f), new Vector3(0.08f, 0.76f, -0.228f), new Vector3(0.08f, 0.76f, -0.206f) }, 0.005f, Steel, true, 8, 4);
                Box("Shelf", v, new Vector3(0.45f, 0.012f, 0.41f), 0.004f, Abs, new Vector3(0, 0.42f, 0));
                // wire basket on the shelf
                var wire = M("Basket_Wire", Hex("#C8CACB"), 0.7f, 1f);
                foreach (var y in new[] { 0.44f, 0.50f, 0.56f }) Tube("Basket_Hoop", v, new[] { new Vector3(-0.19f, y, -0.17f), new Vector3(0.19f, y, -0.17f), new Vector3(0.19f, y, 0.17f), new Vector3(-0.19f, y, 0.17f), new Vector3(-0.19f, y, -0.17f) }, 0.003f, wire, true, 6, 1);
                for (int i = 0; i <= 6; i++) { float x = -0.19f + i * 0.0633f; Tube("Basket_Rib", v, new[] { new Vector3(x, 0.56f, -0.17f), new Vector3(x, 0.43f, -0.17f), new Vector3(x, 0.43f, 0.17f), new Vector3(x, 0.56f, 0.17f) }, 0.0022f, wire, true, 5, 1); }
                Box("Electrode_Pack", v, new Vector3(0.16f, 0.03f, 0.11f), 0.006f, M("Pack_Blue", Hex("#5E88A8"), 0.3f), new Vector3(-0.05f, 0.447f, 0.02f), new Vector3(0, 14, 0));
                Tube("Push_Handle", v, new[] { new Vector3(-0.2f, 0.83f, 0.2f), new Vector3(-0.2f, 0.93f, 0.27f), new Vector3(0.2f, 0.93f, 0.27f), new Vector3(0.2f, 0.83f, 0.2f) }, 0.011f, Steel, true, 10, 5);
                // cable arm with trunk cable and colour-coded lead wires
                Tube("Cable_Arm", v, new[] { new Vector3(-0.225f, 0.83f, 0.12f), new Vector3(-0.225f, 1.22f, 0.12f), new Vector3(-0.225f, 1.27f, 0.08f), new Vector3(-0.225f, 1.27f, -0.02f) }, 0.008f, Steel, true, 8, 5);
                Tube("Trunk_Cable", v, new[] { new Vector3(-0.2f, 0.9f, 0.05f), new Vector3(-0.26f, 0.98f, 0.06f), new Vector3(-0.25f, 1.2f, 0.02f), new Vector3(-0.225f, 1.275f, -0.02f), new Vector3(-0.225f, 1.2f, -0.05f) }, 0.005f, M("Cable_Grey2", Hex("#9AA0A4"), 0.4f), true, 6, 6);
                Box("Lead_Yoke", v, new Vector3(0.03f, 0.07f, 0.02f), 0.006f, Abs, new Vector3(-0.225f, 1.17f, -0.05f));
                string[] lc = { "#B3261E", "#D8B23A", "#3FA85A", "#2B2D2F", "#E9E8E2", "#7A4EA3" };
                for (int i = 0; i < 6; i++)
                {
                    float o = (i - 2.5f) * 0.012f; var m = M("Lead_" + i, Hex(lc[i]), 0.4f);
                    Tube("Lead_Wire", v, new[] { new Vector3(-0.225f + o * 0.4f, 1.14f, -0.05f), new Vector3(-0.23f + o, 1.02f, -0.07f - Mathf.Abs(o)), new Vector3(-0.235f + o * 1.6f, 0.93f, -0.06f) }, 0.0022f, M("Cable_Grey2", Hex("#9AA0A4"), 0.4f), true, 5, 5);
                    Box("Lead_Clip", v, new Vector3(0.008f, 0.022f, 0.008f), 0.002f, m, new Vector3(-0.235f + o * 1.6f, 0.92f, -0.06f));
                }
                Box("Paper_Slot", v, new Vector3(0.22f, 0.003f, 0.012f), 0.001f, AbsDark, new Vector3(-0.06f, 0.9262f, 0.06f), isStatic: false);
                Box("Keypad", v, new Vector3(0.11f, 0.002f, 0.06f), 0.001f, M("Monitor_Key", Hex("#8D9498"), 0.4f), new Vector3(0.11f, 0.9258f, -0.01f), isStatic: false);
                AddBoxCollider(v, "Collider", new Vector3(0, 0.45f, 0), new Vector3(0.5f, 0.8f, 0.46f));
            }
            var san = Find("Medical Equipment/Hand Sanitiser Dispenser/Body"); if (san) san.GetComponent<MeshRenderer>().sharedMaterial = Abs;
        }

        // ======================================================================================= instruments (visual swap only)
        static void HideLegacyRenderers(Transform root, params string[] keep)
        { foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true)) { if (mr.GetComponent<TMP_Text>() || keep.Contains(mr.name) || mr.GetComponentsInParent<Transform>(true).Any(p => p.name.StartsWith("v2_"))) continue; mr.enabled = false; } }

        static void UpgradeInstruments()
        {
            float shelfY = 0.894f;
            var st = Find("Instruments/Stethoscope");
            if (st)
            {
                HideLegacyRenderers(st);
                var v = Prop("Sketchfab", "Stethoscope", st, st.position, 0, "v2_Visual", false);
                v.transform.localRotation = Quaternion.identity; v.transform.localPosition = -new Vector3(-0.03354f, 0.0f, -0.07617f) + new Vector3(0, -0.0065f, 0); // chest piece (marker from convert.py) sits on the probe
                var b = Bounds(v.transform); var cols = st.GetComponents<BoxCollider>();
                if (cols.Length > 1) { cols[1].center = st.InverseTransformPoint(b.center); var s = st.InverseTransformVector(b.size); cols[1].size = new Vector3(Mathf.Abs(s.x), Mathf.Max(0.03f, Mathf.Abs(s.y)), Mathf.Abs(s.z)); }
            }
            var cuff = Find("Instruments/Blood Pressure Cuff");
            if (cuff)
            {
                var wrapped = cuff.Find("Cuff_Visual"); var hose = cuff.Find("Hose");
                var band = wrapped ? wrapped.Find("Cuff_Band") : null; if (band) band.GetComponent<MeshRenderer>().sharedMaterial = M("Cuff_Nylon_Black", Hex("#26282B"), 0.12f, 0, "Fabric030", 14f, false, 0.8f);
                var v = Prop("Sketchfab", "Sphygmomanometer", cuff, cuff.position, 0, "v2_Loose", false);
                v.transform.rotation = Quaternion.Euler(0, 200, 0);
                Vector3 cuffMarker = new Vector3(0.00974f, 0.0f, -0.11461f); // rolled cuff position in the model (convert.py marker)
                v.transform.position = new Vector3(cuff.position.x, shelfY, cuff.position.z) - v.transform.rotation * cuffMarker;
                var vs = cuff.GetComponent<InstrumentVisualState>(); if (!vs) vs = cuff.gameObject.AddComponent<InstrumentVisualState>();
                vs.looseVisuals = new[] { v }; vs.appliedVisuals = new[] { wrapped ? wrapped.gameObject : null, hose ? hose.gameObject : null }.Where(g => g).ToArray();
                if (wrapped) wrapped.gameObject.SetActive(false); if (hose) hose.gameObject.SetActive(false);
            }
            var ox = Find("Instruments/Pulse Oximeter");
            if (ox)
            {
                HideLegacyRenderers(ox, "Screen");
                var v = Prop("Sketchfab", "PulseOximeter", ox, ox.position, 0, "v2_Visual", false); v.transform.localRotation = Quaternion.identity; v.transform.localPosition = new Vector3(0, -0.0195f, 0);
                var scr = ox.Find("Screen"); if (scr) { scr.localPosition = new Vector3(0, 0.0213f, 0.002f); scr.localScale = new Vector3(1.0f, 1f, 1.7f); }
                var txt = ox.GetComponentInChildren<TMP_Text>(true); if (txt) txt.transform.localPosition = new Vector3(0, 0.0223f, 0.002f);
            }
        }

        // ======================================================================================= lighting
        static void BuildLighting()
        {
            var l = New("Redesign_Lighting", null, Vector3.zero);
            // sky: overcast courtyard HDRI is both the window view and the ambient source
            var hdr = AssetDatabase.LoadAssetAtPath<Texture>($"{TP}/PolyHaven/HDRI/urban_courtyard_02_4k.hdr");
            string skyPath = GenMat + "/Sky_UrbanCourtyard.mat"; var sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
            if (!sky) { sky = new Material(Shader.Find("Skybox/Panoramic")); AssetDatabase.CreateAsset(sky, skyPath); }
            sky.shader = Shader.Find("Skybox/Panoramic"); sky.SetTexture("_MainTex", hdr); sky.SetFloat("_Exposure", 1.15f); sky.SetFloat("_Rotation", 200f); sky.SetFloat("_Mapping", 1); sky.SetFloat("_ImageType", 0); EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky; RenderSettings.ambientMode = AmbientMode.Skybox; RenderSettings.ambientIntensity = 1.0f; RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox; RenderSettings.fog = false;
            // existing fixtures: warmer-neutral 4000 K and about half the previous output; the sun does the modelling
            foreach (var li in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (li.transform.parent && li.transform.parent.name.StartsWith("Ceiling LED Panel") && li.transform.root.name == "Environment") { li.intensity = 4.4f; li.color = new Color(1f, 0.97f, 0.92f); li.shadows = LightShadows.Soft; }
                if (li.name == "Daylight") { li.transform.rotation = Quaternion.Euler(38, 40, 0); li.intensity = 2.6f; li.color = new Color(1f, 0.96f, 0.89f); li.shadows = LightShadows.Soft; li.shadowStrength = 1f; li.lightmapBakeType = LightmapBakeType.Baked; li.shadowAngle = 2.5f; }
                if (li.name == "Exam Spot") { li.intensity = 1.6f; li.spotAngle = 70; li.innerSpotAngle = 40; li.range = 3f; li.color = new Color(1f, 0.96f, 0.9f); }
            }
            var led = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Generated/Materials/LED_Panel.mat"); if (led) { led.SetColor("_EmissionColor", new Color(1f, 0.95f, 0.87f) * 1.5f); EditorUtility.SetDirty(led); }
            // bedhead uplight (warm wash behind the patient)
            var up = New("Bedhead_Uplight", l, new Vector3(0.3f, 1.66f, 2.20f), Quaternion.Euler(-80, 180, 0)); var ul = up.gameObject.AddComponent<Light>();
            ul.type = LightType.Spot; ul.spotAngle = 170; ul.innerSpotAngle = 120; ul.range = 2.4f; ul.intensity = 0.45f; ul.color = new Color(1f, 0.84f, 0.66f); ul.shadows = LightShadows.None; ul.lightmapBakeType = LightmapBakeType.Baked;
            // probes
            var old = Find("Environment/Lighting/Light Probes"); var lpg = old ? old.GetComponent<LightProbeGroup>() : null;
            if (lpg)
            {
                var pts = new List<Vector3>(); float[] ys = { 0.2f, 1.1f, 1.9f, 2.6f };
                for (float x = -2.75f; x <= 2.76f; x += 0.6875f) for (float z = -2.25f; z <= 2.26f; z += 0.75f) foreach (var y in ys) pts.Add(new Vector3(x, y, z));
                for (float x = CX0 + 0.25f; x <= CX1 - 0.2f; x += 0.75f) for (float z = CZ0 + 0.3f; z <= CZ1 - 0.25f; z += 0.6f) foreach (var y in ys) if (y < 2.5f) pts.Add(new Vector3(x, y, z));
                foreach (var z in new[] { -2.78f, -2.56f, -2.32f }) foreach (var x in new[] { 1.5f, 1.85f, 2.2f }) foreach (var y in new[] { 0.2f, 1.1f, 1.9f }) pts.Add(new Vector3(x, y, z));
                foreach (var p in new[] { new Vector3(-0.35f, 0.9f, 0.2f), new Vector3(0.95f, 0.9f, 0.2f), new Vector3(-0.35f, 0.9f, 1.4f), new Vector3(0.95f, 0.9f, 1.4f), new Vector3(0.3f, 1.3f, 0.9f), new Vector3(0.3f, 1.3f, 1.5f), new Vector3(0.3f, 1.0f, -0.4f) }) pts.Add(p);
                lpg.transform.position = Vector3.zero; lpg.probePositions = pts.ToArray(); log.AppendLine("  light probes: " + pts.Count);
            }
            var rp = Find("Environment/Lighting/Reflection Probe"); if (rp) { var p = rp.GetComponent<ReflectionProbe>(); p.transform.position = new Vector3(0, 1.4f, 0); p.size = new Vector3(6.0f, 2.85f, 5.0f); p.center = Vector3.zero; p.boxProjection = true; p.blendDistance = 0.3f; p.importance = 1; }
            var cp = New("Reflection Probe Corridor", l, new Vector3(0.9f, 1.3f, -3.82f)).gameObject.AddComponent<ReflectionProbe>();
            cp.size = new Vector3(5.0f, 2.6f, 2.4f); cp.boxProjection = true; cp.mode = ReflectionProbeMode.Baked; cp.resolution = 256; cp.blendDistance = 0.3f; cp.importance = 1;
            // post: neutral, no vignette/bloom haze
            var prof = AssetDatabase.LoadAssetAtPath<VolumeProfile>(Root + "/Generated/MedSim_PostProfile.asset");
            if (prof)
            {
                if (prof.TryGet<UnityEngine.Rendering.Universal.ColorAdjustments>(out var ca)) { ca.postExposure.Override(0.35f); ca.contrast.Override(10f); ca.saturation.Override(-4f); }
                if (prof.TryGet<UnityEngine.Rendering.Universal.Vignette>(out var vg)) vg.intensity.Override(0f);
                if (prof.TryGet<UnityEngine.Rendering.Universal.Bloom>(out var bl)) { bl.intensity.Override(0.08f); bl.threshold.Override(1.2f); }
                if (prof.TryGet<UnityEngine.Rendering.Universal.WhiteBalance>(out var wb)) wb.temperature.Override(0f);
                EditorUtility.SetDirty(prof);
            }
        }

        // ======================================================================================= staging, physics, navigation
        static void Stage(HospitalDoor door)
        {
            var s = New("Redesign_Staging", null, Vector3.zero);
            Quaternion Look(Vector3 from, Vector3 to) { var d = to - from; d.y = 0; return Quaternion.LookRotation(d.normalized); }
            var mCorr = New("Doctor Corridor Mark", s, DoctorCorridor, Look(DoctorCorridor, StudentSpawn));
            var mDoor = New("Doctor Doorway Mark", s, DoctorDoorway, Look(DoctorDoorway, new Vector3(0.3f, 0, 0.9f)));
            var mObs = New("Doctor Observe Mark", s, DoctorObserve, Look(DoctorObserve, new Vector3(0.45f, 0, 0.75f)));
            var spawn = Find("Environment/Player Spawn"); if (!spawn) spawn = New("Player Spawn", s, StudentSpawn);
            spawn.SetPositionAndRotation(StudentSpawn, Quaternion.Euler(0, 20, 0));
            var player = Object.FindAnyObjectByType<XRPlayerController>();
            if (player) { player.spawnPoint = spawn; player.transform.SetPositionAndRotation(StudentSpawn, spawn.rotation); EditorUtility.SetDirty(player); }
            var cam = Camera.main ? Camera.main.transform : (player ? player.GetComponentInChildren<Camera>(true).transform : null);

            var ent = New("Room Entry Trigger", s, new Vector3(1.85f, 1.0f, -1.95f)); var eb = ent.gameObject.AddComponent<BoxCollider>(); eb.isTrigger = true; eb.size = new Vector3(1.5f, 2.0f, 0.8f);
            var ret = ent.gameObject.AddComponent<RoomEntryTrigger>(); ret.playerHead = cam; ent.gameObject.layer = 2; // Ignore Raycast

            // characters: solid capsules so the rig cannot walk through them
            var ins = Object.FindAnyObjectByType<MedicalInstructorController>(FindObjectsInactive.Include);
            DoctorMovement dm = null;
            if (ins)
            {
                var t = ins.transform; t.SetPositionAndRotation(mCorr.position, mCorr.rotation);
                var cap = t.GetComponent<CapsuleCollider>(); if (!cap) cap = t.gameObject.AddComponent<CapsuleCollider>(); cap.center = new Vector3(0, 0.86f, 0); cap.radius = 0.22f; cap.height = 1.72f;
                var ag = t.GetComponent<NavMeshAgent>(); if (!ag) ag = t.gameObject.AddComponent<NavMeshAgent>();
                ag.radius = 0.25f; ag.height = 1.7f; ag.baseOffset = 0; ag.speed = 1.05f; ag.angularSpeed = 0; ag.acceleration = 6f; ag.stoppingDistance = 0.08f; ag.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance; ag.autoBraking = true;
                dm = t.GetComponent<DoctorMovement>(); if (!dm) dm = t.gameObject.AddComponent<DoctorMovement>(); dm.animator = t.GetComponent<Animator>(); dm.agent = ag; EditorUtility.SetDirty(dm);
            }
            var pat = Object.FindAnyObjectByType<Characters.PatientInteractionController>(FindObjectsInactive.Include);
            if (pat)
            {
                var a = pat.GetComponent<Animator>(); Vector3 hips = a && a.isHuman ? a.GetBoneTransform(HumanBodyBones.Hips).position : pat.transform.position + Vector3.up * 0.86f;
                Vector3 head = a && a.isHuman ? a.GetBoneTransform(HumanBodyBones.Head).position : hips + new Vector3(0, 0.47f, 0.34f); Vector3 foot = a && a.isHuman ? (a.GetBoneTransform(HumanBodyBones.LeftFoot).position + a.GetBoneTransform(HumanBodyBones.RightFoot).position) / 2 : hips + new Vector3(0, -0.08f, -0.78f);
                void Cap(string n, Vector3 p0, Vector3 p1, float rad) { var g = New(n, pat.transform, (p0 + p1) / 2, Quaternion.FromToRotation(Vector3.up, (p1 - p0).normalized)); var c = g.gameObject.AddComponent<CapsuleCollider>(); c.radius = rad; c.height = (p1 - p0).magnitude + rad * 2; }
                Cap("v2_Body_Torso", hips + Vector3.down * 0.03f, head + Vector3.up * 0.05f, 0.12f); Cap("v2_Body_Legs", hips + Vector3.down * 0.03f, foot + Vector3.down * 0.02f, 0.13f);
            }
            var sm = Object.FindAnyObjectByType<SessionManager>(FindObjectsInactive.Include);
            if (sm) { sm.examDoor = door; sm.doctorMovement = dm; sm.doctorCorridorMark = mCorr; sm.doctorDoorwayMark = mDoor; sm.doctorObserveMark = mObs; sm.studentCorridorSpawn = spawn; EditorUtility.SetDirty(sm); }
            else log.AppendLine("!! SessionManager not found");
            log.AppendLine($"  staging: spawn {spawn.position:F2} yaw {spawn.eulerAngles.y:F0}; doctor {mCorr.position:F2}; door mark {mDoor.position:F2}; observe {mObs.position:F2}; head={(cam ? cam.name : "none")}");
        }

        static void BuildNavMesh()
        {
            var s = Find("Redesign_Staging"); var go = New("NavMesh Surface", s, Vector3.zero).gameObject;
            var surf = go.AddComponent<NavMeshSurface>(); surf.collectObjects = CollectObjects.All; surf.useGeometry = NavMeshCollectGeometry.PhysicsColliders; surf.layerMask = ~(1 << 2);
            surf.overrideVoxelSize = true; surf.voxelSize = 0.05f; surf.overrideTileSize = true; surf.tileSize = 128;
            // dynamic things must not carve the mesh: door leaf, characters, instruments, XR rig, loose props
            var hide = new List<GameObject>();
            void Hide(GameObject g) { if (g && g.activeSelf) { g.SetActive(false); hide.Add(g); } }
            var hinge = Find("Redesign_Door/Exam Room 3 Door/Hinge"); if (hinge) Hide(hinge.gameObject);
            foreach (var n in new[] { "Characters", "Instruments", "XR Origin (XR Rig)" }) { var t = Find(n); if (t) Hide(t.gameObject); }
            var tab = Find("Environment/Clinical Tablet Stand"); if (tab) Hide(tab.gameObject);
            Physics.SyncTransforms();
            surf.BuildNavMesh();
            foreach (var g in hide) g.SetActive(true);
            const string path = Root + "/Generated/MedSim_NavMesh.asset"; AssetDatabase.DeleteAsset(path);
            if (surf.navMeshData) AssetDatabase.CreateAsset(surf.navMeshData, path);
            var tri = NavMesh.CalculateTriangulation(); bool c = NavMesh.SamplePosition(DoctorCorridor, out var h1, 0.4f, NavMesh.AllAreas), o = NavMesh.SamplePosition(DoctorObserve, out var h2, 0.4f, NavMesh.AllAreas);
            var pth = new NavMeshPath(); bool ok = c && o && NavMesh.CalculatePath(h1.position, h2.position, NavMesh.AllAreas, pth);
            log.AppendLine($"  navmesh: verts {tri.vertices.Length}; corridor mark on mesh {c}; observe mark on mesh {o}; path {(ok ? pth.status.ToString() : "none")} corners {pth.corners.Length}");
        }
    }
}
