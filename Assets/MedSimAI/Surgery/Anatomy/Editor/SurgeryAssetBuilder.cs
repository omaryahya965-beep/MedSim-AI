using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MedSimAI.Surgery.Anatomy;
using MedSimAI.Surgery.AssetsInspector;
using MedSimAI.Surgery.Instruments;
using MedSimAI.Surgery.Shared;
using MedSimAI.Surgery.Shared.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace MedSimAI.Surgery.AssetsEditor
{
    /// <summary>
    /// Builds every Person 2 asset from the Blender exports (Models/*.fbx + reports): URP materials, KneeAnatomy_R,
    /// SurgicalDrape, SurgicalPatient, implant prefabs + KneeImplantSet, instrument prefabs, instrument tray, catalogs
    /// and the Anatomy_Test scene. Re-runnable: everything is regenerated from the source files.
    /// Menu: MedSim AI ▸ Surgery ▸ Build anatomy, implant and instrument assets
    /// </summary>
    public static class SurgeryAssetBuilder
    {
        public const string R = "Assets/MedSimAI/Surgery/", A = R + "Anatomy/", I = R + "Instruments/";
        public const string KneePrefab = A + "Prefabs/KneeAnatomy_R.prefab", PatientPrefab = A + "Prefabs/SurgicalPatient.prefab", DrapePrefab = A + "Prefabs/SurgicalDrape.prefab";
        public const string ImplantSetPrefab = A + "Prefabs/KneeImplantSet.prefab", TrayPrefab = I + "Prefabs/InstrumentTray.prefab", TrayPopulatedPrefab = I + "Prefabs/InstrumentTray_Populated.prefab";
        public const string ScenePath = R + "Scenes/Anatomy_Test.unity", ScenarioPath = R + "Scenarios/Resources/SurgeryScenarios/OR_TKA_TRAINING_001.json";
        public static string InstrumentPrefab(string id) => I + "Prefabs/" + id + ".prefab";
        public static string ImplantPrefab(string id) => I + "Prefabs/Implants/" + id + ".prefab";
        public static readonly string[] ExtraInstruments = { "inst.forceps", "inst.suction" };
        public static IEnumerable<string> AllInstrumentIds => InstrumentIds.All.Where(i => i != InstrumentIds.RobotArm).Concat(ExtraInstruments);

        const string SRC_ZA = "Z-Anatomy (CC BY-SA 4.0), derived from BodyParts3D © DBCLS; adapted (posed, refined, split) for MedSim AI — derived mesh CC BY-SA 4.0";
        const string SRC_MPFB = "MakeHuman / MPFB2 system assets (CC0 1.0); generated, posed and modified for MedSim AI";
        const string SRC_ORIG = "Original MedSim AI geometry (procedural Blender scripts)";
        static StringBuilder log;
        static Dictionary<string, Material> M;

        [MenuItem("MedSim AI/Surgery/Build anatomy, implant and instrument assets")]
        public static string BuildAll()
        {
            log = new StringBuilder();
            foreach (var d in new[] { A + "Prefabs", A + "Materials", I + "Prefabs/Implants", I + "Materials", I + "Catalog", R + "Scenes" }) Directory.CreateDirectory(d);
            ConfigureImporters();
            BuildMaterials();
            var implants = BuildImplants();
            BuildInstruments();
            BuildTray();
            BuildKnee();
            BuildDrape();
            BuildPatient();
            BuildImplantSet();
            WriteCatalogs();
            AssetDatabase.SaveAssets();
            BuildTestScene();
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            log.AppendLine("validation: " + SurgeryAssetValidator.Summary());
            return log.ToString();
        }

        // ------------------------------------------------------------------------------------------------ importers
        static void ConfigureImporters()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { A + "Models", I + "Models" }))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid); var mi = (ModelImporter)AssetImporter.GetAtPath(p);
                bool ch = false;
                void Set<T>(Func<T> get, Action<T> set, T v) { if (!EqualityComparer<T>.Default.Equals(get(), v)) { set(v); ch = true; } }
                Set(() => mi.globalScale, v => mi.globalScale = v, 1f); Set(() => mi.useFileScale, v => mi.useFileScale = v, true);
                Set(() => mi.importNormals, v => mi.importNormals = v, ModelImporterNormals.Import); Set(() => mi.importTangents, v => mi.importTangents = v, ModelImporterTangents.CalculateMikk);
                Set(() => mi.isReadable, v => mi.isReadable = v, true); Set(() => mi.animationType, v => mi.animationType = v, ModelImporterAnimationType.None);
                Set(() => mi.importAnimation, v => mi.importAnimation = v, false); Set(() => mi.importCameras, v => mi.importCameras = v, false); Set(() => mi.importLights, v => mi.importLights = v, false);
                Set(() => mi.importBlendShapes, v => mi.importBlendShapes = v, false); Set(() => mi.meshCompression, v => mi.meshCompression = v, ModelImporterMeshCompression.Off);
                Set(() => mi.bakeAxisConversion, v => mi.bakeAxisConversion = v, false);
                if (ch) mi.SaveAndReimport();
            }
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { A + "Textures/Patient", A + "Textures/Drapes", I + "Textures" }))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid); var ti = (TextureImporter)AssetImporter.GetAtPath(p); bool normal = p.Contains("Normal");
                var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default; bool srgb = !normal && !p.Contains("Roughness") && !p.Contains("AmbientOcclusion");
                if (ti.textureType != type || ti.sRGBTexture != srgb || ti.maxTextureSize != 2048 || ti.wrapMode != TextureWrapMode.Repeat)
                { ti.textureType = type; ti.sRGBTexture = srgb; ti.maxTextureSize = 2048; ti.wrapMode = TextureWrapMode.Repeat; ti.alphaIsTransparency = p.Contains("eyebrow") || p.Contains("eyelash") || p.Contains("short04"); ti.SaveAndReimport(); }
            }
        }

        // ------------------------------------------------------------------------------------------------ materials
        static Texture2D T(string path) => AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        static Material Mat(string folder, string name, Color c, float smooth, float metal = 0, Texture2D normal = null, float nScale = 1, float tiling = 1,
                            Texture2D albedo = null, bool cutout = false, bool twoSided = false, Texture2D detailN = null, float detailScale = 0.3f, float detailTiling = 8, Texture2D occ = null)
        {
            var path = folder + name + ".mat"; var sh = Shader.Find("Universal Render Pipeline/Lit");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path); if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            m.shader = sh; m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metal); m.SetTexture("_BaseMap", albedo);
            m.SetTextureScale("_BaseMap", Vector2.one * tiling);
            if (normal) { m.SetTexture("_BumpMap", normal); m.SetFloat("_BumpScale", nScale); m.EnableKeyword("_NORMALMAP"); } else { m.SetTexture("_BumpMap", null); m.DisableKeyword("_NORMALMAP"); }
            if (detailN) { m.SetTexture("_DetailNormalMap", detailN); m.SetFloat("_DetailNormalMapScale", detailScale); m.SetTextureScale("_DetailAlbedoMap", Vector2.one * detailTiling); m.SetFloat("_DetailAlbedoMapScale", 0); m.EnableKeyword("_DETAIL_MULX2"); }
            else { m.SetTexture("_DetailNormalMap", null); m.DisableKeyword("_DETAIL_MULX2"); }
            if (occ) { m.SetTexture("_OcclusionMap", occ); m.SetFloat("_OcclusionStrength", 0.7f); m.EnableKeyword("_OCCLUSIONMAP"); } else { m.SetTexture("_OcclusionMap", null); m.DisableKeyword("_OCCLUSIONMAP"); }
            m.SetFloat("_AlphaClip", cutout ? 1 : 0); m.SetFloat("_Cutoff", 0.45f);
            if (cutout) { m.EnableKeyword("_ALPHATEST_ON"); m.renderQueue = (int)RenderQueue.AlphaTest; m.SetOverrideTag("RenderType", "TransparentCutout"); }
            else { m.DisableKeyword("_ALPHATEST_ON"); m.renderQueue = -1; m.SetOverrideTag("RenderType", "Opaque"); }
            m.SetFloat("_Cull", twoSided ? 0 : 2); m.doubleSidedGI = twoSided;
            m.enableInstancing = true; EditorUtility.SetDirty(m); M[name] = m; return m;
        }
        static Color C(float r, float g, float b) => new Color(r, g, b, 1);
        static void BuildMaterials()
        {
            M = new Dictionary<string, Material>(); string am = A + "Materials/", im = I + "Materials/";
            var boneN = SurgeryTextureGen.Bone(); var cartN = SurgeryTextureGen.Cartilage(); var fibreN = SurgeryTextureGen.Fibre(); var fatN = SurgeryTextureGen.Fat();
            var poresN = SurgeryTextureGen.SkinPores(); var cutN = SurgeryTextureGen.CutBoneNormal(); var cutA = SurgeryTextureGen.CutBoneAlbedo();
            string pt = A + "Textures/Patient/", dt = A + "Textures/Drapes/", it = I + "Textures/";
            var skin = T(pt + "old_lightskinned_male_diffuse.png"); var fabricN = T(dt + "Fabric030_1K_NormalGL.jpg"); var fabricAO = T(dt + "Fabric030_1K_AmbientOcclusion.jpg");
            var brushN = T(it + "Metal009_1K_NormalGL.jpg"); var plasticN = T(it + "Plastic013A_1K_NormalGL.jpg"); var rubberN = T(it + "Rubber004_1K_NormalGL.jpg");
            // anatomy: realistic but clean, each structure family distinguishable
            Mat(am, "M_Bone", C(0.86f, 0.79f, 0.63f), 0.28f, 0, boneN, 0.6f, 2.5f);
            Mat(am, "M_Cartilage", C(0.73f, 0.79f, 0.85f), 0.72f, 0, cartN, 0.25f, 2f);
            Mat(am, "M_Meniscus", C(0.86f, 0.86f, 0.79f), 0.6f, 0, fibreN, 0.35f, 2f);
            Mat(am, "M_Ligament", C(0.91f, 0.88f, 0.80f), 0.58f, 0, fibreN, 0.85f, 3f);
            Mat(am, "M_Tendon", C(0.90f, 0.89f, 0.84f), 0.62f, 0, fibreN, 0.9f, 3f);
            Mat(am, "M_Capsule", C(0.86f, 0.76f, 0.74f), 0.5f, 0, fibreN, 0.3f, 1.5f);
            Mat(am, "M_Fat", C(0.95f, 0.83f, 0.46f), 0.62f, 0, fatN, 0.6f, 2f);
            Mat(am, "M_Muscle", C(0.47f, 0.12f, 0.11f), 0.42f, 0, fibreN, 0.9f, 1.5f);
            Mat(am, "M_CutBone", Color.white, 0.22f, 0, cutN, 0.9f, 3f, cutA);
            Mat(am, "M_WoundFat", C(0.93f, 0.79f, 0.45f), 0.65f, 0, fatN, 0.5f, 3f, null, false, true);
            Mat(am, "M_WoundTissue", C(0.58f, 0.20f, 0.18f), 0.6f, 0, fibreN, 0.5f, 3f, null, false, true);
            Mat(am, "M_Skin", C(1f, 0.97f, 0.95f), 0.33f, 0, null, 1, 1, skin, false, false, poresN, 0.3f, 18);
            Mat(am, "M_SkinPrepped", C(0.93f, 0.80f, 0.67f), 0.42f, 0, null, 1, 1, skin, false, false, poresN, 0.3f, 18);   // povidone-iodine tint, slightly wet
            Mat(am, "M_SkinMarker", C(0.34f, 0.13f, 0.48f), 0.35f);
            Mat(am, "M_Incision", C(0.40f, 0.04f, 0.05f), 0.7f);
            Mat(am, "M_SutureLine", C(0.36f, 0.12f, 0.10f), 0.5f);
            Mat(am, "M_Staple", C(0.80f, 0.81f, 0.83f), 0.85f, 1f);
            Mat(am, "M_GuidePin", C(0.78f, 0.79f, 0.81f), 0.8f, 1f);
            Mat(am, "M_Eyes", Color.white, 0.85f, 0, null, 1, 1, T(pt + "brown_eye.png"));
            Mat(am, "M_Hair", C(0.85f, 0.85f, 0.85f), 0.25f, 0, null, 1, 1, T(pt + "short04_diffuse.png"), true, true);
            Mat(am, "M_Eyebrows", Color.white, 0.2f, 0, null, 1, 1, T(pt + "eyebrow001.png"), true, true);
            Mat(am, "M_Eyelashes", Color.white, 0.2f, 0, null, 1, 1, T(pt + "eyelashes01.png"), true, true);
            Mat(am, "M_Teeth", Color.white, 0.6f, 0, null, 1, 1, T(pt + "teeth.png"));
            Mat(am, "M_Tongue", Color.white, 0.6f, 0, null, 1, 1, T(pt + "tongue01_diffuse.png"));
            Mat(am, "M_Gown", C(0.58f, 0.70f, 0.78f), 0.15f, 0, fabricN, 0.5f, 6f, null, false, true, null, 0, 1, fabricAO);
            Mat(am, "M_Brief", C(0.62f, 0.74f, 0.86f), 0.15f, 0, fabricN, 0.5f, 8f, null, false, true);
            Mat(am, "M_DrapeBlue", C(0.10f, 0.29f, 0.44f), 0.18f, 0, fabricN, 0.55f, 4f, null, false, true, null, 0, 1, fabricAO);
            Mat(am, "M_Stockinette", C(0.90f, 0.90f, 0.88f), 0.12f, 0, fabricN, 0.8f, 18f, null, false, true);
            Mat(am, "M_Cuff", C(0.17f, 0.25f, 0.52f), 0.25f, 0, fabricN, 0.4f, 8f, null, false, true);
            // implants (generic): polished CoCr articular surfaces, blasted backs, Ti tray, UHMWPE insert, trials
            Mat(im, "M_Implant_CoCr", C(0.82f, 0.83f, 0.85f), 0.93f, 1f);
            Mat(im, "M_Implant_Back", C(0.66f, 0.67f, 0.69f), 0.38f, 1f, brushN, 0.6f, 6f);
            Mat(im, "M_Implant_Ti", C(0.60f, 0.61f, 0.64f), 0.55f, 1f, brushN, 0.4f, 6f);
            Mat(im, "M_Poly", C(0.93f, 0.93f, 0.89f), 0.72f, 0f);
            Mat(im, "M_Trial", C(0.56f, 0.62f, 0.70f), 0.6f, 0.85f, brushN, 0.3f, 6f);
            // instruments
            Mat(im, "M_SteelPolished", C(0.80f, 0.81f, 0.83f), 0.9f, 1f, brushN, 0.15f, 8f);
            Mat(im, "M_SteelSatin", C(0.70f, 0.71f, 0.73f), 0.62f, 1f, brushN, 0.5f, 8f);
            Mat(im, "M_SteelDark", C(0.42f, 0.43f, 0.45f), 0.5f, 1f, brushN, 0.5f, 8f);
            Mat(im, "M_Blade", C(0.86f, 0.87f, 0.89f), 0.95f, 1f);
            Mat(im, "M_Tungsten", C(0.30f, 0.30f, 0.32f), 0.45f, 1f, brushN, 0.8f, 16f);
            Mat(im, "M_PolymerGrey", C(0.34f, 0.36f, 0.39f), 0.42f, 0, plasticN, 0.4f, 6f);
            Mat(im, "M_PolymerDark", C(0.11f, 0.12f, 0.13f), 0.38f, 0, plasticN, 0.5f, 6f);
            Mat(im, "M_PolymerWhite", C(0.90f, 0.90f, 0.88f), 0.45f, 0, plasticN, 0.3f, 6f);
            Mat(im, "M_PolymerBlue", C(0.18f, 0.38f, 0.66f), 0.45f, 0, plasticN, 0.3f, 6f);
            Mat(im, "M_PolymerPurple", C(0.40f, 0.20f, 0.56f), 0.45f, 0, plasticN, 0.3f, 6f);
            Mat(im, "M_PolymerAmber", C(0.82f, 0.52f, 0.14f), 0.45f, 0, plasticN, 0.3f, 6f);
            Mat(im, "M_PolymerClear", C(0.78f, 0.84f, 0.90f), 0.8f, 0, null);
            Mat(im, "M_Rubber", C(0.07f, 0.07f, 0.08f), 0.2f, 0, rubberN, 0.7f, 10f);
            Mat(im, "M_Towel", C(0.16f, 0.40f, 0.50f), 0.12f, 0, fabricN, 0.6f, 6f, null, false, false, null, 0, 1, fabricAO);
            log.AppendLine($"materials: {M.Count}");
        }
        static readonly Dictionary<string, string> MatMap = new Dictionary<string, string>
        {
            { "MA_Bone", "M_Bone" }, { "MA_Cartilage", "M_Cartilage" }, { "MA_Meniscus", "M_Meniscus" }, { "MA_Ligament", "M_Ligament" }, { "MA_Capsule", "M_Capsule" },
            { "MA_Fat", "M_Fat" }, { "MA_Muscle", "M_Muscle" }, { "MA_Tendon", "M_Tendon" }, { "MA_CutBone", "M_CutBone" }, { "MA_Implant", "M_Implant_CoCr" },
            { "MA_ImplantBack", "M_Implant_Back" }, { "MA_ImplantTi", "M_Implant_Ti" }, { "MA_Poly", "M_Poly" }, { "MA_Trial", "M_Trial" }, { "MA_Steel", "M_Staple" },
            { "MA_SkinMarker", "M_SkinMarker" }, { "MA_Incision", "M_Incision" }, { "MA_SutureLine", "M_SutureLine" }, { "MA_WoundFat", "M_WoundFat" }, { "MA_WoundTissue", "M_WoundTissue" },
            { "Human.eyebrow001", "M_Eyebrows" }, { "Human.eyelashes01", "M_Eyelashes" }, { "Human.low-poly", "M_Eyes" }, { "Human.short04", "M_Hair" }, { "Human.teeth_base", "M_Teeth" },
            { "Human.tongue01", "M_Tongue" }, { "M_Brief", "M_Brief" }, { "M_Gown", "M_Gown" }, { "MA_DrapeBlue", "M_DrapeBlue" }, { "MA_Stockinette", "M_Stockinette" },
            { "MA_CuffBlue", "M_Cuff" }, { "MA_SteelSatin", "M_SteelSatin" }, { "MA_SteelPolished", "M_SteelPolished" }, { "MA_SteelDark", "M_SteelDark" }, { "MA_Blade", "M_Blade" },
            { "MA_Tungsten", "M_Tungsten" }, { "MA_PolymerGrey", "M_PolymerGrey" }, { "MA_PolymerDark", "M_PolymerDark" }, { "MA_PolymerWhite", "M_PolymerWhite" },
            { "MA_PolymerBlue", "M_PolymerBlue" }, { "MA_PolymerPurple", "M_PolymerPurple" }, { "MA_PolymerAmber", "M_PolymerAmber" }, { "MA_PolymerClear", "M_PolymerClear" },
            { "MA_Rubber", "M_Rubber" }, { "MA_Towel", "M_Towel" }, { "MA_GoldInsert", "M_SteelPolished" },
        };
        static Material MapMat(string fbxMat, string objectName)
        {
            var n = Regex.Replace(fbxMat ?? "", @"\.\d{3}$", "");
            if (n == "Human.body") return M[objectName == "Body" ? "M_Skin" : "M_SkinPrepped"];
            if (objectName == "KneeSkin_Open" && n.StartsWith("Human")) return M["M_SkinPrepped"];
            if (objectName == "ScreenFrame") return M["M_SteelSatin"];
            if (MatMap.TryGetValue(n, out var k)) return M[k];
            if (objectName.StartsWith("GuidePins")) return M["M_GuidePin"];
            log.AppendLine($"  ! unmapped material '{fbxMat}' on {objectName} -> M_Bone"); return M["M_Bone"];
        }

        // ------------------------------------------------------------------------------------------------ helpers
        static GameObject LoadFbx(string path) { var g = AssetDatabase.LoadAssetAtPath<GameObject>(path); if (!g) throw new Exception("missing model " + path); return g; }
        static Dictionary<string, Transform> Nodes(GameObject fbx)
        {
            var d = new Dictionary<string, Transform>();
            foreach (var t in fbx.GetComponentsInChildren<Transform>(true)) if (t != fbx.transform && !d.ContainsKey(t.name)) d[t.name] = t;
            return d;
        }
        static Vector3 PosIn(GameObject fbx, Transform t) => fbx.transform.InverseTransformPoint(t.position);
        static GameObject Group(Transform parent, string name) { var g = new GameObject(name); g.transform.SetParent(parent, false); return g; }
        /// <summary>New object with the FBX node's mesh; placed at the node's position (root-relative), under parent (whose world pose is known).</summary>
        static GameObject MeshObj(GameObject fbx, Transform node, Transform parent, string name = null)
        {
            var go = new GameObject(name ?? node.name); go.transform.SetParent(parent, false);
            go.transform.position = parent.root.TransformPoint(PosIn(fbx, node)); go.transform.rotation = parent.root.rotation * (Quaternion.Inverse(fbx.transform.rotation) * node.rotation);
            var mf = node.GetComponent<MeshFilter>(); var mr = node.GetComponent<MeshRenderer>();
            if (mf && mf.sharedMesh)
            {
                go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterials = (mr ? mr.sharedMaterials : new Material[0]).Select(m => MapMat(m ? m.name : "", node.name)).ToArray();
            }
            return go;
        }
        static T Add<T>(GameObject g) where T : Component { var c = g.GetComponent<T>(); return c ? c : g.AddComponent<T>(); }   // Unity fake-null safe
        static AnatomyStructure Tag(GameObject g, string id, AnatomyCategory cat, string en, string ar, string src, bool placeholder = false, bool optional = false, string notes = "")
        {
            var s = Add<AnatomyStructure>(g); s.structureId = id; s.category = cat; s.displayNameEn = en; s.displayNameAr = ar; s.source = src;
            s.educationalPlaceholder = placeholder; s.optionalStructure = optional; s.notes = notes; return s;
        }
        static Transform Anchor(Transform parent, string name, Vector3 pos, Quaternion rot, bool surgeryAnchor = false)
        {
            var g = new GameObject(name); g.transform.SetParent(parent, false); g.transform.position = parent.root.TransformPoint(pos); g.transform.rotation = parent.root.rotation * rot;
            if (surgeryAnchor) Add<SurgeryAnchor>(g).anchorId = name;
            return g.transform;
        }
        static GameObject SavePrefab(GameObject go, string path) { var p = PrefabUtility.SaveAsPrefabAsset(go, path); UnityEngine.Object.DestroyImmediate(go); log.AppendLine("prefab: " + path); return p; }
        static Dictionary<string, object> Json(string path) => SimpleJson.Obj(SimpleJson.Parse(File.ReadAllText(path)));
        static Quaternion Frame(Dictionary<string, object> f, string fwd = "anterior", string up = "proximal") => Quaternion.LookRotation(SimpleJson.V3(f[fwd]), SimpleJson.V3(f[up]));
        static Dictionary<string, (string en, string ar)> ScenarioNames(string listKey)
        {
            var d = new Dictionary<string, (string, string)>();
            foreach (var o in SimpleJson.Arr(Json(ScenarioPath)[listKey]))
            {
                var e = SimpleJson.Obj(o); var n = SimpleJson.Obj(e["name"]); d[(string)e["id"]] = ((string)n["en"], (string)n["ar"]);
            }
            return d;
        }
        static Dictionary<string, string[]> ScenarioTargetStates()
        {
            var d = new Dictionary<string, string[]>();
            foreach (var o in SimpleJson.Arr(Json(ScenarioPath)["targets"])) { var e = SimpleJson.Obj(o); d[(string)e["id"]] = SimpleJson.Arr(e["states"]).Cast<string>().ToArray(); }
            return d;
        }

        // ------------------------------------------------------------------------------------------------ implants
        static readonly Dictionary<string, (string node, string target, bool trial, float mass)> ImplantDefs = new Dictionary<string, (string, string, bool, float)>
        {
            { ImplantIds.FemoralTrial, ("FemoralTrial", AnatomyTargetIds.DistalFemur, true, 0.30f) },
            { ImplantIds.TibialTrial, ("TibialTrial", AnatomyTargetIds.ProximalTibia, true, 0.12f) },
            { ImplantIds.FemoralComponent, ("FemoralComponent", AnatomyTargetIds.DistalFemur, false, 0.42f) },
            { ImplantIds.TibialTray, ("TibialTray", AnatomyTargetIds.ProximalTibia, false, 0.22f) },
            { ImplantIds.PolyInsert, ("PolyInsert", AnatomyTargetIds.ProximalTibia, false, 0.05f) },
        };
        static List<string> BuildImplants()
        {
            var names = ScenarioNames("implants"); var made = new List<string>();
            foreach (var kv in ImplantDefs)
            {
                var fbx = LoadFbx(I + "Models/Implants/" + kv.Key + ".fbx"); var nodes = Nodes(fbx);
                var root = new GameObject(kv.Key);
                var vis = MeshObj(fbx, nodes.TryGetValue(kv.Value.node, out var nd) ? nd : fbx.transform, root.transform, "Visual");   // single-mesh FBX: the mesh is on the model root
                var b = vis.GetComponent<MeshFilter>().sharedMesh.bounds;
                var mc = vis.AddComponent<MeshCollider>(); mc.convex = true;
                var grip = Anchor(root.transform, "GripPoint", b.center, Quaternion.identity);
                var place = Anchor(root.transform, "PlacementAnchor", Vector3.zero, Quaternion.identity);
                var reset = Anchor(root.transform, "ResetAnchor", Vector3.zero, Quaternion.identity);
                Add<ImplantTagBridge>(root).implantId = kv.Key;   // adds SurgeryImplantTag at runtime
                var info = Add<ImplantAssetInfo>(root); info.isTrial = kv.Value.trial; info.targetId = kv.Value.target; info.gripPoint = grip; info.placementAnchor = place; info.resetAnchor = reset;
                info.sizeMm = b.size * 1000f;
                var rb = Add<Rigidbody>(root); rb.mass = kv.Value.mass; rb.interpolation = RigidbodyInterpolation.Interpolate; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                var g = Add<XRGrabInteractable>(root); g.attachTransform = grip; g.throwOnDetach = false; g.useDynamicAttach = false;
                SavePrefab(root, ImplantPrefab(kv.Key)); made.Add(kv.Key);
            }
            return made;
        }

        // ------------------------------------------------------------------------------------------------ instruments
        static readonly Dictionary<string, (InstrumentCategory cat, float mass)> InstrumentDefs = new Dictionary<string, (InstrumentCategory, float)>
        {
            { InstrumentIds.SkinMarker, (InstrumentCategory.Marking, 0.015f) }, { InstrumentIds.Scalpel, (InstrumentCategory.Cutting, 0.02f) },
            { InstrumentIds.Retractor, (InstrumentCategory.Retraction, 0.2f) }, { InstrumentIds.FemoralCuttingGuide, (InstrumentCategory.Guide, 0.35f) },
            { InstrumentIds.TibialCuttingGuide, (InstrumentCategory.Guide, 0.33f) }, { InstrumentIds.OscillatingSaw, (InstrumentCategory.PoweredCutting, 1.3f) },
            { InstrumentIds.Drill, (InstrumentCategory.PoweredDrilling, 1.2f) }, { InstrumentIds.Mallet, (InstrumentCategory.Impaction, 0.6f) },
            { InstrumentIds.Impactor, (InstrumentCategory.Impaction, 0.45f) }, { InstrumentIds.NeedleDriver, (InstrumentCategory.Suturing, 0.06f) },
            { InstrumentIds.SkinStapler, (InstrumentCategory.Closure, 0.12f) }, { "inst.forceps", (InstrumentCategory.Grasping, 0.03f) }, { "inst.suction", (InstrumentCategory.Suction, 0.03f) },
        };
        static readonly Dictionary<string, (InstrumentMovablePart.Motion motion, Vector3 axis, float min, float max, string desc)> Movable = new Dictionary<string, (InstrumentMovablePart.Motion, Vector3, float, float, string)>
        {
            { "inst.oscillating_saw/Pivot_Blade", (InstrumentMovablePart.Motion.Rotate, Vector3.up, -4f, 4f, "Blade oscillation (±4°) about the head axis") },
            { "inst.oscillating_saw/Trigger", (InstrumentMovablePart.Motion.Translate, Vector3.forward, 0f, 0.008f, "Trigger travel") },
            { "inst.drill/Pivot_Chuck", (InstrumentMovablePart.Motion.Spin, Vector3.forward, 0f, 1800f, "Chuck + bit spin about the bit axis (deg/s placeholder)") },
            { "inst.drill/Trigger", (InstrumentMovablePart.Motion.Translate, Vector3.forward, 0f, 0.008f, "Trigger travel") },
            { "inst.needle_driver/Pivot_JawUpper", (InstrumentMovablePart.Motion.Rotate, Vector3.up, 0f, 12f, "Upper jaw/handle half opens about the box-lock") },
            { "inst.needle_driver/Pivot_JawLower", (InstrumentMovablePart.Motion.Rotate, Vector3.up, 0f, -12f, "Lower jaw/handle half opens about the box-lock") },
            { "inst.skin_stapler/Pivot_Trigger", (InstrumentMovablePart.Motion.Rotate, Vector3.right, 0f, 14f, "Trigger squeeze") },
            { "inst.forceps/Pivot_ArmL", (InstrumentMovablePart.Motion.Rotate, Vector3.up, 0f, 2.5f, "Arm closes toward the centre") },
            { "inst.forceps/Pivot_ArmR", (InstrumentMovablePart.Motion.Rotate, Vector3.up, 0f, -2.5f, "Arm closes toward the centre") },
            { "inst.skin_marker/Cap", (InstrumentMovablePart.Motion.Translate, Vector3.forward, 0f, 0.05f, "Cap removal") },
        };
        static readonly HashSet<string> AnchorNames = new HashSet<string> { "GripPoint", "Tip", "InteractionPoint", "AttachmentPoint", "ResetAnchor", "SeatPoint", "SlotPlane", "StrikePoint", "Pivot_BoxLock" };
        static void BuildInstruments()
        {
            var report = Json(I + "Models/instruments_report.json"); var names = ScenarioNames("instruments");
            names["inst.forceps"] = ("Adson forceps (toothed)", "ملقط أدسون مسنّن"); names["inst.suction"] = ("Yankauer suction", "شفّاط يانكاور");
            foreach (var id in AllInstrumentIds)
            {
                var info = SimpleJson.Obj(report[id]); var parents = SimpleJson.Obj(info["parents"]);
                var fbx = LoadFbx(I + "Models/" + id + ".fbx"); var nodes = Nodes(fbx);
                var root = new GameObject(id); var made = new Dictionary<string, GameObject>();
                // create everything at its root-relative pose, then re-parent following the Blender hierarchy (keeping world pose)
                foreach (var kv in parents) { if (!nodes.TryGetValue(kv.Key, out var n)) { log.AppendLine($"  ! {id}: missing node {kv.Key}"); continue; } made[kv.Key] = MeshObj(fbx, n, root.transform); }
                foreach (var kv in parents) if (made.ContainsKey(kv.Key) && !string.IsNullOrEmpty((string)kv.Value) && made.ContainsKey((string)kv.Value)) made[kv.Key].transform.SetParent(made[(string)kv.Value].transform, true);
                foreach (var go in made.Values)
                    if (go.GetComponent<MeshFilter>()) { var bc = go.AddComponent<BoxCollider>(); var b = go.GetComponent<MeshFilter>().sharedMesh.bounds; bc.center = b.center; bc.size = Vector3.Max(b.size, Vector3.one * 0.002f); }
                Transform F(string n) => made.TryGetValue(n, out var g) ? g.transform : null;
                var tag = Add<InstrumentTagBridge>(root); tag.instrumentId = id; tag.tip = F("Tip");   // adds SurgeryInstrumentTag at runtime
                if (names.TryGetValue(id, out var nm)) { tag.displayNameEn = nm.en; tag.displayNameAr = nm.ar; }
                var meta = Add<InstrumentAssetInfo>(root); var def = InstrumentDefs[id];
                meta.category = def.cat; meta.catalogName = (string)info["name"]; var sz = SimpleJson.V3(info["size_mm"]); meta.sizeMm = sz;
                meta.gripPoint = F("GripPoint"); meta.tip = F("Tip"); meta.interactionPoint = F("InteractionPoint"); meta.attachmentPoint = F("AttachmentPoint"); meta.resetAnchor = F("ResetAnchor");
                meta.proposedIdNotInContract = ExtraInstruments.Contains(id);
                var parts = new List<InstrumentMovablePart>();
                foreach (var mv in Movable.Where(m => m.Key.StartsWith(id + "/")))
                {
                    var t = F(mv.Key.Substring(id.Length + 1)); if (!t) continue;
                    var p = Add<InstrumentMovablePart>(t.gameObject); p.partId = t.name; p.motion = mv.Value.motion; p.localAxis = mv.Value.axis; p.minValue = mv.Value.min; p.maxValue = mv.Value.max; p.description = mv.Value.desc; parts.Add(p);
                }
                meta.movableParts = parts.ToArray();
                var rb = Add<Rigidbody>(root); rb.mass = def.mass; rb.interpolation = RigidbodyInterpolation.Interpolate; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                var grab = Add<XRGrabInteractable>(root); grab.attachTransform = meta.gripPoint; grab.throwOnDetach = false; grab.useDynamicAttach = false;
                SavePrefab(root, InstrumentPrefab(id));
            }
        }

        // ------------------------------------------------------------------------------------------------ tray
        static void BuildTray()
        {
            var fbx = LoadFbx(I + "Models/InstrumentTray.fbx"); var root = new GameObject("InstrumentTray");
            var vis = Group(root.transform, "Visual");
            foreach (var t in fbx.GetComponentsInChildren<MeshFilter>(true)) { var g = MeshObj(fbx, t.transform, vis.transform); var bc = g.AddComponent<BoxCollider>(); var b = t.sharedMesh.bounds; bc.center = b.center; bc.size = b.size; }
            float top = 0.0145f;
            // two columns of instruments lying along the tray's long axis (+X); long instruments on the left, short ones on the right
            var colA = new[] { InstrumentIds.OscillatingSaw, InstrumentIds.Drill, InstrumentIds.Mallet, InstrumentIds.Impactor, "inst.suction", InstrumentIds.Retractor };
            var colB = new[] { InstrumentIds.SkinMarker, InstrumentIds.Scalpel, InstrumentIds.FemoralCuttingGuide, InstrumentIds.TibialCuttingGuide, InstrumentIds.NeedleDriver, InstrumentIds.SkinStapler, "inst.forceps" };
            var slots = new List<Transform>(); var slotsRoot = Group(root.transform, "Slots");
            foreach (var (col, cx) in new[] { (colA, -0.148f), (colB, 0.150f) })
            {
                var bounds = col.Select(id => PrefabBounds(InstrumentPrefab(id))).ToArray();
                float total = bounds.Sum(b => b.size.x) + 0.012f * (col.Length - 1); float z = -total / 2;
                for (int i = 0; i < col.Length; i++)
                {
                    var b = bounds[i]; var rot = Quaternion.Euler(0, 90, 0);   // instrument +Z -> tray +X, instrument +X -> tray -Z
                    float zc = z + b.size.x / 2; z += b.size.x + 0.012f;
                    var pos = new Vector3(cx - b.center.z, top - b.min.y + 0.0005f, zc + b.center.x);
                    var s = Anchor(slotsRoot.transform, AnchorIds.InstrumentSlot(col[i]), pos, rot, true); slots.Add(s);
                }
            }
            Add<InstrumentTrayLayout>(root).slots = slots.ToArray();
            var trayAsset = SavePrefab(root, TrayPrefab);
            // populated variant: nested instrument prefabs at their slots
            var pop = (GameObject)PrefabUtility.InstantiatePrefab(trayAsset); pop.name = "InstrumentTray_Populated";
            var layout = pop.GetComponent<InstrumentTrayLayout>(); var holder = Group(pop.transform, "Instruments");
            foreach (var id in AllInstrumentIds)
            {
                var slot = layout.FindSlot(id); var inst = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(InstrumentPrefab(id)));
                inst.transform.SetParent(holder.transform, false); inst.transform.SetPositionAndRotation(slot.position, slot.rotation);
                var rb = inst.GetComponent<Rigidbody>(); if (rb) rb.isKinematic = true;   // resting on the tray in the display variant
            }
            SavePrefab(pop, TrayPopulatedPrefab);
        }
        static Bounds PrefabBounds(string path)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path); bool first = true; var b = new Bounds();
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                var m = go.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix; var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++) { var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) * 2 - 1, (i & 2) - 1, (i & 4) / 2 - 1)); var p = m.MultiplyPoint3x4(c); if (first) { b = new Bounds(p, Vector3.zero); first = false; } else b.Encapsulate(p); }
            }
            return b;
        }

        // ------------------------------------------------------------------------------------------------ knee
        struct S { public string node, group, id, en, ar; public AnatomyCategory cat; public bool placeholder, optional, inactive; public string src; }
        static S Sx(string node, string group, string id, AnatomyCategory cat, string en, string ar, string src = SRC_ZA, bool placeholder = false, bool optional = false, bool inactive = false)
            => new S { node = node, group = group, id = id, cat = cat, en = en, ar = ar, src = src, placeholder = placeholder, optional = optional, inactive = inactive };
        static readonly S[] KneeStructures =
        {
            Sx("FemurShaft", "Bones", "knee_r.bone.femur_shaft", AnatomyCategory.Bone, "Femur (shaft)", "جسم عظم الفخذ"),
            Sx("DistalFemur", "Bones", "knee_r.bone.distal_femur", AnatomyCategory.Bone, "Distal femur", "النهاية السفلية لعظم الفخذ"),
            Sx("DistalFemur_Resected", "Bones", "knee_r.bone.distal_femur_resected", AnatomyCategory.Bone, "Distal femur — illustrative resection", "الفخذ بعد القطع (توضيحي)", SRC_ZA, true, false, true),
            Sx("TibiaShaft", "Bones", "knee_r.bone.tibia_shaft", AnatomyCategory.Bone, "Tibia (shaft)", "جسم عظم الساق"),
            Sx("ProximalTibia", "Bones", "knee_r.bone.proximal_tibia", AnatomyCategory.Bone, "Proximal tibia", "النهاية العلوية لعظم الساق"),
            Sx("ProximalTibia_Resected", "Bones", "knee_r.bone.proximal_tibia_resected", AnatomyCategory.Bone, "Proximal tibia — illustrative resection", "الساق بعد القطع (توضيحي)", SRC_ZA, true, false, true),
            Sx("Fibula", "Bones", "knee_r.bone.fibula", AnatomyCategory.Bone, "Fibula", "عظم الشظية"),
            Sx("Patella", "Bones/PatellaGroup", "knee_r.bone.patella", AnatomyCategory.Bone, "Patella", "الرضفة"),
            Sx("FemoralCartilage", "Cartilage", "knee_r.cartilage.femoral", AnatomyCategory.Cartilage, "Femoral articular cartilage", "الغضروف المفصلي لعظم الفخذ"),
            Sx("TibialCartilage", "Cartilage", "knee_r.cartilage.tibial", AnatomyCategory.Cartilage, "Tibial articular cartilage", "الغضروف المفصلي لعظم الساق"),
            Sx("PatellarCartilage", "Bones/PatellaGroup", "knee_r.cartilage.patellar", AnatomyCategory.Cartilage, "Patellar articular cartilage", "غضروف الرضفة"),
            Sx("MedialMeniscus", "Menisci", "knee_r.meniscus.medial", AnatomyCategory.Meniscus, "Medial meniscus", "الغضروف الهلالي الإنسي"),
            Sx("LateralMeniscus", "Menisci", "knee_r.meniscus.lateral", AnatomyCategory.Meniscus, "Lateral meniscus", "الغضروف الهلالي الوحشي"),
            Sx("TransverseLigament", "Menisci", "knee_r.ligament.transverse", AnatomyCategory.Ligament, "Transverse ligament of the knee", "الرباط المستعرض للركبة"),
            Sx("MeniscotibialLigaments", "Menisci", "knee_r.ligament.meniscotibial", AnatomyCategory.Ligament, "Meniscotibial (coronary) ligaments", "الأربطة الهلالية الظنبوبية"),
            Sx("ACL", "Ligaments", "knee_r.ligament.acl", AnatomyCategory.Ligament, "Anterior cruciate ligament", "الرباط الصليبي الأمامي"),
            Sx("PCL", "Ligaments", "knee_r.ligament.pcl", AnatomyCategory.Ligament, "Posterior cruciate ligament", "الرباط الصليبي الخلفي"),
            Sx("MCL", "Ligaments", "knee_r.ligament.mcl", AnatomyCategory.Ligament, "Medial (tibial) collateral ligament", "الرباط الجانبي الإنسي"),
            Sx("LCL", "Ligaments", "knee_r.ligament.lcl", AnatomyCategory.Ligament, "Lateral (fibular) collateral ligament", "الرباط الجانبي الوحشي"),
            Sx("PoplitealLigaments", "Ligaments", "knee_r.ligament.popliteal", AnatomyCategory.Ligament, "Oblique & arcuate popliteal, popliteofibular ligaments", "الأربطة المأبضية"),
            Sx("PatellarRetinacula", "Ligaments", "knee_r.ligament.patellar_retinacula", AnatomyCategory.Ligament, "Patellar retinacula & meniscopatellar ligaments", "قيد الرضفة والأربطة الهلالية الرضفية"),
            Sx("PatellarTendon", "Bones/PatellaGroup", "knee_r.tendon.patellar", AnatomyCategory.Tendon, "Patellar tendon (ligament) — procedural approximation", "وتر الرضفة (تقريب إجرائي)", SRC_ORIG),
            Sx("Quadriceps", "Tendons", "knee_r.tendon.quadriceps", AnatomyCategory.Tendon, "Quadriceps tendon and distal quadriceps", "وتر العضلة رباعية الرؤوس"),
            Sx("JointCapsule", "SoftTissue", "knee_r.soft.joint_capsule", AnatomyCategory.SoftTissue, "Articular capsule of the knee", "محفظة مفصل الركبة"),
            Sx("InfrapatellarFatPad", "SoftTissue", "knee_r.soft.infrapatellar_fat_pad", AnatomyCategory.SoftTissue, "Infrapatellar fat pad", "الوسادة الدهنية تحت الرضفة"),
            Sx("SuprapatellarBursa", "SoftTissue", "knee_r.soft.suprapatellar_bursa", AnatomyCategory.SoftTissue, "Suprapatellar bursa", "الجراب فوق الرضفة"),
            Sx("Gastrocnemius", "SoftTissue/OptionalMuscles", "knee_r.muscle.gastrocnemius", AnatomyCategory.Muscle, "Gastrocnemius heads (sectioned)", "رأسا العضلة التوأمية", SRC_ZA, false, true, true),
            Sx("Popliteus", "SoftTissue/OptionalMuscles", "knee_r.muscle.popliteus", AnatomyCategory.Muscle, "Popliteus", "العضلة المأبضية", SRC_ZA, false, true, true),
            Sx("PesAnserinus", "SoftTissue/OptionalMuscles", "knee_r.muscle.pes_anserinus", AnatomyCategory.Muscle, "Sartorius, gracilis, semitendinosus (sectioned)", "عضلات القدم الإوزية", SRC_ZA, false, true, true),
            Sx("Hamstrings", "SoftTissue/OptionalMuscles", "knee_r.muscle.hamstrings", AnatomyCategory.Muscle, "Biceps femoris, semimembranosus (sectioned)", "عضلات أوتار المأبض", SRC_ZA, false, true, true),
            Sx("IliotibialTract", "SoftTissue/OptionalMuscles", "knee_r.soft.iliotibial_tract", AnatomyCategory.Muscle, "Iliotibial tract (sectioned)", "السبيل الحرقفي الظنبوبي", SRC_ZA, false, true, true),
            Sx("KneeSkin", "Skin", "knee_r.skin.intact", AnatomyCategory.Skin, "Knee skin (prepped)", "جلد الركبة (معقّم)", SRC_MPFB),
            Sx("KneeSkin_Open", "Skin", "knee_r.skin.open", AnatomyCategory.Skin, "Knee skin with retracted wound (placeholder)", "جلد الركبة مع الجرح مفتوحاً (توضيحي)", SRC_MPFB, true, false, true),
            Sx("SiteMarking", "Skin", "knee_r.overlay.site_marking", AnatomyCategory.SkinOverlay, "Site marking line", "خط تعليم الموقع", SRC_ORIG, true, false, true),
            Sx("IncisionLine", "Skin", "knee_r.overlay.incision", AnatomyCategory.SkinOverlay, "Incision line", "خط الشق", SRC_ORIG, true, false, true),
            Sx("ClosureLine", "Skin", "knee_r.overlay.closure", AnatomyCategory.SkinOverlay, "Closed wound line", "خط الجرح المغلق", SRC_ORIG, true, false, true),
            Sx("SkinStaples", "Skin", "knee_r.overlay.staples", AnatomyCategory.SkinOverlay, "Skin staples", "دبابيس الجلد", SRC_ORIG, true, false, true),
            Sx("GuidePins_Femur", "StateVisuals", "knee_r.state.guide_pins_femur", AnatomyCategory.StateVisual, "Femoral guide pins (placeholder)", "مسامير دليل الفخذ (توضيحي)", SRC_ORIG, true, false, true),
            Sx("GuidePins_Tibia", "StateVisuals", "knee_r.state.guide_pins_tibia", AnatomyCategory.StateVisual, "Tibial guide pins (placeholder)", "مسامير دليل الساق (توضيحي)", SRC_ORIG, true, false, true),
            Sx("FemoralTrial", "StateVisuals/ComponentVisuals", "knee_r.visual.femoral_trial", AnatomyCategory.ComponentVisual, "Femoral trial (visual)", "قطعة الفخذ التجريبية", SRC_ORIG, true, false, true),
            Sx("FemoralComponent", "StateVisuals/ComponentVisuals", "knee_r.visual.femoral_component", AnatomyCategory.ComponentVisual, "Femoral component (visual)", "مكوّن الفخذ", SRC_ORIG, true, false, true),
            Sx("TibialTrial", "StateVisuals/ComponentVisuals", "knee_r.visual.tibial_trial", AnatomyCategory.ComponentVisual, "Tibial trial (visual)", "قطعة الساق التجريبية", SRC_ORIG, true, false, true),
            Sx("TibialTray", "StateVisuals/ComponentVisuals", "knee_r.visual.tibial_tray", AnatomyCategory.ComponentVisual, "Tibial tray (visual)", "صينية الساق", SRC_ORIG, true, false, true),
            Sx("PolyInsert", "StateVisuals/ComponentVisuals", "knee_r.visual.poly_insert", AnatomyCategory.ComponentVisual, "Polyethylene insert (visual)", "الفاصل البلاستيكي", SRC_ORIG, true, false, true),
        };
        static void BuildKnee()
        {
            var fbx = LoadFbx(A + "Models/Knee/KneeAnatomy_R.fbx"); var nodes = Nodes(fbx);
            var L = SimpleJson.Obj(Json(A + "Models/Knee/KneeAnatomy_R.landmarks.json")["anchors"]);
            var root = new GameObject("KneeAnatomy_R"); var ctl = Add<KneeAnatomyStateController>(root);
            var groups = new Dictionary<string, Transform>();
            Transform G(string path)
            {
                if (groups.TryGetValue(path, out var t)) return t;
                var parts = path.Split('/'); var parent = parts.Length > 1 ? G(string.Join("/", parts.Take(parts.Length - 1))) : root.transform;
                t = Group(parent, parts.Last()).transform; groups[path] = t; return t;
            }
            foreach (var g in new[] { "Bones", "Cartilage", "Menisci", "Ligaments", "Tendons", "SoftTissue", "Skin", "StateVisuals", "InteractionTargets", "ImplantAnchors", "InteractionAnchors" }) G(g);
            // patella group pivots on the tibial tuberosity; retracted pose from the Blender search (see PatellaEversion)
            var ev = SimpleJson.Obj(L["PatellaEversion"]); var pivot = SimpleJson.V3(ev["pivot"]);
            var pg = G("Bones/PatellaGroup"); pg.localPosition = pivot;
            var axis = SimpleJson.V3(ev["axis"]); float ang = SimpleJson.F(ev["angleDeg"]); var off = SimpleJson.V3(ev["offset"]);
            // Blender rotation (angle, axis) -> Unity: the axis conversion is a reflection, so the angle changes sign
            var retracted = Anchor(G("Bones"), "PatellaRetractedPose", pivot + off, Quaternion.AngleAxis(-ang, axis));
            var objs = new Dictionary<string, GameObject>();
            foreach (var s in KneeStructures)
            {
                if (!nodes.TryGetValue(s.node, out var n)) { log.AppendLine("  ! knee: missing " + s.node); continue; }
                var go = MeshObj(fbx, n, G(s.group)); Tag(go, s.id, s.cat, s.en, s.ar, s.src, s.placeholder, s.optional);
                if (s.inactive) go.SetActive(false);
                if (s.cat == AnatomyCategory.Bone || s.cat == AnatomyCategory.Cartilage || s.cat == AnatomyCategory.Meniscus) go.AddComponent<MeshCollider>();
                objs[s.node] = go;
            }
            G("SoftTissue/OptionalMuscles").gameObject.SetActive(true);
            GameObject O(string n) => objs.TryGetValue(n, out var g) ? g : null;
            ctl.distalFemur = O("DistalFemur"); ctl.distalFemurResected = O("DistalFemur_Resected"); ctl.femoralCartilage = O("FemoralCartilage");
            ctl.proximalTibia = O("ProximalTibia"); ctl.proximalTibiaResected = O("ProximalTibia_Resected"); ctl.tibialCartilage = O("TibialCartilage");
            ctl.removedWithTibialResection = new[] { O("MedialMeniscus"), O("LateralMeniscus"), O("TransverseLigament"), O("MeniscotibialLigaments"), O("ACL") };
            ctl.skinIntact = O("KneeSkin"); ctl.skinOpen = O("KneeSkin_Open"); ctl.siteMarking = O("SiteMarking"); ctl.incisionLine = O("IncisionLine"); ctl.closureLine = O("ClosureLine"); ctl.skinStaples = O("SkinStaples");
            ctl.hiddenWhenExposed = new[] { O("JointCapsule"), O("InfrapatellarFatPad"), O("SuprapatellarBursa"), O("PatellarRetinacula"), O("Quadriceps"), O("PatellarTendon") };
            ctl.patellaGroup = pg; ctl.patellaRetractedPose = retracted;
            ctl.guidePinsFemur = O("GuidePins_Femur"); ctl.guidePinsTibia = O("GuidePins_Tibia");
            ctl.femoralTrialVisual = O("FemoralTrial"); ctl.femoralComponentVisual = O("FemoralComponent"); ctl.tibialTrialVisual = O("TibialTrial"); ctl.tibialTrayVisual = O("TibialTray"); ctl.polyInsertVisual = O("PolyInsert");
            ctl.showComponentVisuals = false;
            ctl.enclosedGroups = new[] { G("Bones"), G("Cartilage"), G("Menisci"), G("Ligaments"), G("Tendons"), G("SoftTissue") };
            // anatomical frames and anchors
            var fem = SimpleJson.Obj(L["femur"]); var tib = SimpleJson.Obj(L["tibia"]); var qF = Frame(fem); var qT = Frame(tib);
            Anchor(G("InteractionAnchors"), "KneeRoot", Vector3.zero, Quaternion.identity);
            Anchor(G("InteractionAnchors"), "FemurRoot", SimpleJson.V3(fem["pivot"]), qF);
            Anchor(G("InteractionAnchors"), "TibiaRoot", SimpleJson.V3(tib["pivot"]), qT);
            Anchor(G("InteractionAnchors"), "FibulaRoot", O("Fibula").transform.localPosition, qT);
            Anchor(pg, "PatellaRoot", SimpleJson.V3(L["patellaCenter"]), qF);
            foreach (var kv in L.Where(k => k.Key.StartsWith("PlacementTarget_") || k.Key.StartsWith("GuideTarget_")))
            {
                var a = SimpleJson.Obj(kv.Value); Anchor(G("ImplantAnchors"), kv.Key, SimpleJson.V3(a["pos"]), Quaternion.LookRotation(SimpleJson.V3(a["forward"]), SimpleJson.V3(a["up"])));
            }
            // interaction targets (trigger colliders + SurgeryAnatomyTag + IAnatomyStateView)
            var states = ScenarioTargetStates(); states[AnatomyTargetIds.Patella] = new[] { AnatomyStates.Intact, AnatomyStates.Exposed };
            var path = SimpleJson.Arr(L["incisionPath"]).Select(SimpleJson.V3).ToList();
            var p0 = path.First(); var p1 = path.Last(); var mid = path.Aggregate(Vector3.zero, (s, v) => s + v) / path.Count;
            var outward = (SimpleJson.V3(fem["anterior"]) + SimpleJson.V3(tib["anterior"])).normalized; var along = (p1 - p0).normalized; float len = (p1 - p0).magnitude + 0.02f;
            var qPath = Quaternion.LookRotation(along, Vector3.ProjectOnPlane(outward, along).normalized);
            float sag = path.Max(p => Vector3.Dot(p - mid, Vector3.ProjectOnPlane(outward, along).normalized));
            var pathCenter = mid + Vector3.ProjectOnPlane(outward, along).normalized * (sag * 0.5f);
            void Target(string id, Transform parent, Vector3 c, Quaternion q, Vector3 size)
            {
                var t = Anchor(parent, id, c, q); var bc = t.gameObject.AddComponent<BoxCollider>(); bc.isTrigger = true; bc.size = size;
                Add<AnatomyTargetStateView>(t.gameObject).EditorConfigure(id, states.TryGetValue(id, out var st) ? st : new[] { AnatomyStates.Intact }, ctl);   // adds SurgeryAnatomyTag at runtime
            }
            Target(AnatomyTargetIds.SiteMarking, G("InteractionTargets"), pathCenter, qPath, new Vector3(0.03f, sag + 0.03f, len));
            Target(AnatomyTargetIds.IncisionLine, G("InteractionTargets"), pathCenter, qPath, new Vector3(0.02f, sag + 0.03f, len));
            Target(AnatomyTargetIds.Wound, G("InteractionTargets"), pathCenter, qPath, new Vector3(0.075f, sag + 0.04f, len));
            var pc = SimpleJson.V3(L["patellaCenter"]); var dc = SimpleJson.V3(L["distalCutCenter"]);
            Target(AnatomyTargetIds.JointCapsule, G("InteractionTargets"), (pc + dc) * 0.5f, qF, new Vector3(0.07f, 0.06f, 0.05f));
            Target(AnatomyTargetIds.Patella, pg, pc, qF, new Vector3(0.05f, 0.05f, 0.03f));
            Target(AnatomyTargetIds.DistalFemur, G("InteractionTargets"), SimpleJson.V3(fem["pivot"]) - SimpleJson.V3(fem["proximal"]) * 0.012f, qF, new Vector3(0.075f, 0.05f, 0.065f));
            Target(AnatomyTargetIds.ProximalTibia, G("InteractionTargets"), SimpleJson.V3(tib["pivot"]) + SimpleJson.V3(tib["proximal"]) * 0.004f, qT, new Vector3(0.075f, 0.03f, 0.055f));
            ctl.Refresh(true);
            SavePrefab(root, KneePrefab);
        }

        // ------------------------------------------------------------------------------------------------ drape / patient
        static void BuildDrape()
        {
            var fbx = LoadFbx(A + "Models/Drapes/SurgicalDrape.fbx"); var nodes = Nodes(fbx); var root = new GameObject("SurgicalDrape");
            var defs = new (string n, string id, string en, string ar)[] {
                ("Drape_Upper", "drape.upper", "Upper drape (chest, arms, pelvis)", "الغطاء العلوي"), ("Drape_Lower", "drape.lower", "Lower drape (left leg, table end)", "الغطاء السفلي"),
                ("Drape_Screen", "drape.anaesthesia_screen", "Anaesthesia screen drape", "غطاء حاجز التخدير"), ("ScreenFrame", "drape.screen_frame", "Screen frame (optional; Person 1 may supply)", "إطار حاجز التخدير"),
                ("ThighCuff", "drape.thigh_cuff", "Thigh cuff at the drape edge", "رباط الفخذ"), ("Stockinette", "drape.stockinette", "Stockinette over foot and shin", "غطاء القدم والساق") };
            foreach (var d in defs)
            {
                if (!nodes.TryGetValue(d.n, out var n)) { log.AppendLine("  ! drape: missing " + d.n); continue; }
                var go = MeshObj(fbx, n, root.transform); Tag(go, d.id, AnatomyCategory.Drape, d.en, d.ar, SRC_ORIG + " (baked cloth / height-field drape)");
                if (d.n == "Drape_Upper" || d.n == "Drape_Lower") go.AddComponent<MeshCollider>();
            }
            SavePrefab(root, DrapePrefab);
        }
        static void BuildPatient()
        {
            var fbx = LoadFbx(A + "Models/Patient/SurgicalPatient_Body.fbx"); var nodes = Nodes(fbx);
            var L = SimpleJson.Obj(Json(A + "Models/Knee/KneeAnatomy_R.landmarks.json")["anchors"]); var pr = Json("Assets/MedSimAI/Surgery/Anatomy/Models/Patient/SurgicalPatient_report.json");
            var root = new GameObject("SurgicalPatient"); var info = Add<SurgicalPatientInfo>(root);
            var body = Group(root.transform, "Body");
            foreach (var n in new[] { "Body", "RightLeg_Prepped", "Brief", "Gown", "Hair", "Eyes", "Eyebrows", "Eyelashes", "Teeth", "Tongue" })
            {
                if (!nodes.TryGetValue(n, out var t)) { log.AppendLine("  ! patient: missing " + n); continue; }
                var go = MeshObj(fbx, t, body.transform);
                var cat = n == "Body" || n == "RightLeg_Prepped" ? AnatomyCategory.Skin : AnatomyCategory.Patient;
                Tag(go, "patient." + n.ToLowerInvariant(), cat, n == "RightLeg_Prepped" ? "Right leg skin (prepped)" : n, "", SRC_MPFB);
            }
            var joints = Group(root.transform, "JointAnchors");
            foreach (var t in fbx.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Joint_"))) Anchor(joints.transform, t.name, PosIn(fbx, t), Quaternion.identity);
            var site = Anchor(root.transform, AnchorIds.SurgicalSiteKneeR, SimpleJson.V3(L["kneeCenter_patient"]), Quaternion.identity, true);
            var knee = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(KneePrefab)); knee.transform.SetParent(site, false);
            var drape = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(DrapePrefab)); drape.transform.SetParent(root.transform, false);
            info.surgicalSite = site; info.kneeAnatomy = knee.transform; info.drape = drape.transform; info.bodyRoot = body.transform; info.jointAnchors = joints.transform;
            info.operativeSide = "R";
            var rep = pr; info.hipFlexionDeg = SimpleJson.F(rep["hipFlexDeg"]); info.kneeFlexionDeg = SimpleJson.F(SimpleJson.Obj(Json(A + "Models/Knee/KneeAnatomy_R.landmarks.json")["report"]).TryGetValue("pose", out var pose) ? SimpleJson.Obj(pose)["kneeFlexDeg_patient"] : 97.5);
            var bmin = SimpleJson.V3(SimpleJson.Obj(rep["bounds"])["min"]); var bmax = SimpleJson.V3(SimpleJson.Obj(rep["bounds"])["max"]); info.bodyWidthMeters = bmax.x - bmin.x;
            SavePrefab(root, PatientPrefab);
        }
        static void BuildImplantSet()
        {
            var root = new GameObject("KneeImplantSet"); float x = -0.24f;
            foreach (var id in new[] { ImplantIds.FemoralTrial, ImplantIds.FemoralComponent, ImplantIds.TibialTrial, ImplantIds.TibialTray, ImplantIds.PolyInsert })
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(ImplantPrefab(id)); var b = PrefabBounds(ImplantPrefab(id));
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(asset); inst.transform.SetParent(root.transform, false);
                bool fem = id == ImplantIds.FemoralTrial || id == ImplantIds.FemoralComponent; inst.transform.localRotation = fem ? Quaternion.Euler(0, 180, 0) : Quaternion.identity;   // articular (anterior) side toward the viewer
                inst.transform.localPosition = new Vector3(x + (fem ? b.center.x : -b.center.x), -b.min.y + 0.001f, fem ? b.center.z : -b.center.z); x += Mathf.Max(0.09f, b.size.x + 0.03f);
                var rb = inst.GetComponent<Rigidbody>(); if (rb) rb.isKinematic = true;
            }
            SavePrefab(root, ImplantSetPrefab);
        }

        // ------------------------------------------------------------------------------------------------ catalogs (machine-readable)
        static string V(Vector3 v) => $"[{v.x:0.#####},{v.y:0.#####},{v.z:0.#####}]";
        static string Q(Quaternion q) => $"[{q.x:0.#####},{q.y:0.#####},{q.z:0.#####},{q.w:0.#####}]";
        static string Esc(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
        static string RelPath(Transform t, Transform root) { var parts = new List<string>(); for (var x = t; x && x != root; x = x.parent) parts.Insert(0, x.name); return string.Join("/", parts); }
        static void WriteCatalogs()
        {
            var sb = new StringBuilder(); sb.Append("{\n  \"contractVersion\": \"" + SurgeryContract.CONTRACT_VERSION + "\",\n  \"conventions\": \"pivot = GripPoint at the root origin; +Z toward the working tip; +Y up; metres; scale (1,1,1)\",\n  \"instruments\": [\n");
            var ids = AllInstrumentIds.ToList();
            for (int k = 0; k < ids.Count; k++)
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(InstrumentPrefab(ids[k])); var tag = go.GetComponent<InstrumentTagBridge>(); var info = go.GetComponent<InstrumentAssetInfo>();
                var cols = go.GetComponentsInChildren<Collider>(true);
                sb.Append($"    {{\"id\":\"{ids[k]}\",\"inContract\":{(!info.proposedIdNotInContract).ToString().ToLower()},\"nameEn\":\"{Esc(tag.displayNameEn)}\",\"nameAr\":\"{Esc(tag.displayNameAr)}\",\"category\":\"{info.category}\",\"prefab\":\"{InstrumentPrefab(ids[k])}\",");
                sb.Append($"\"sizeMm\":{V(info.sizeMm)},\"massKg\":{go.GetComponent<Rigidbody>().mass:0.###},\"colliders\":{cols.Length},\"colliderType\":\"BoxCollider per part\",");
                sb.Append($"\"gripPoint\":\"{RelPath(info.gripPoint, go.transform)}\",\"tip\":\"{RelPath(tag.tip, go.transform)}\",\"tipLocalPos\":{V(go.transform.InverseTransformPoint(tag.tip.position))},");
                sb.Append($"\"interactionPoint\":\"{RelPath(info.interactionPoint, go.transform)}\",\"attachmentPoint\":\"{RelPath(info.attachmentPoint, go.transform)}\",\"resetAnchor\":\"{RelPath(info.resetAnchor, go.transform)}\",");
                sb.Append("\"movableParts\":[" + string.Join(",", info.movableParts.Select(p => $"{{\"path\":\"{RelPath(p.transform, go.transform)}\",\"motion\":\"{p.motion}\",\"axis\":{V(p.localAxis)},\"min\":{p.minValue},\"max\":{p.maxValue}}}")) + "]");
                var extra = go.GetComponentsInChildren<Transform>(true).Where(t => t.name == "SeatPoint" || t.name == "SlotPlane" || t.name == "StrikePoint").Select(t => $"\"{t.name}\":{V(go.transform.InverseTransformPoint(t.position))}");
                sb.Append(extra.Any() ? "," + string.Join(",", extra) : "");
                sb.Append("}" + (k < ids.Count - 1 ? ",\n" : "\n"));
            }
            sb.Append("  ],\n  \"implants\": [\n"); var imps = ImplantDefs.Keys.ToList();
            var knee = AssetDatabase.LoadAssetAtPath<GameObject>(KneePrefab);
            for (int k = 0; k < imps.Count; k++)
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(ImplantPrefab(imps[k])); var info = go.GetComponent<ImplantAssetInfo>();
                var pt = knee.GetComponentsInChildren<Transform>(true).First(t => t.name == "PlacementTarget_" + imps[k]);
                sb.Append($"    {{\"id\":\"{imps[k]}\",\"trial\":{info.isTrial.ToString().ToLower()},\"target\":\"{info.targetId}\",\"prefab\":\"{ImplantPrefab(imps[k])}\",\"sizeMm\":{V(info.sizeMm)},");
                sb.Append($"\"placementTargetPath\":\"{RelPath(pt, knee.transform)}\",\"placementTargetLocalPos\":{V(knee.transform.InverseTransformPoint(pt.position))},\"placementTargetLocalRot\":{Q(Quaternion.Inverse(knee.transform.rotation) * pt.rotation)}}}" + (k < imps.Count - 1 ? ",\n" : "\n"));
            }
            sb.Append("  ]\n}\n"); File.WriteAllText(I + "Catalog/surgery_asset_catalog.json", sb.ToString());
            // anatomy catalog
            var a = new StringBuilder(); a.Append("{\n  \"prefab\": \"" + KneePrefab + "\",\n  \"frame\": \"KneeAnatomy_R root = ANCHOR_SurgicalSite_Knee_R (knee joint centre); axes parallel to the patient frame (+Z head, +Y up, patient right = -X)\",\n  \"structures\": [\n");
            var ss = knee.GetComponentsInChildren<AnatomyStructure>(true);
            for (int k = 0; k < ss.Length; k++)
                a.Append($"    {{\"id\":\"{ss[k].structureId}\",\"path\":\"{RelPath(ss[k].transform, knee.transform)}\",\"category\":\"{ss[k].category}\",\"nameEn\":\"{Esc(ss[k].displayNameEn)}\",\"nameAr\":\"{Esc(ss[k].displayNameAr)}\",\"activeByDefault\":{ss[k].gameObject.activeSelf.ToString().ToLower()},\"placeholder\":{ss[k].educationalPlaceholder.ToString().ToLower()},\"source\":\"{Esc(ss[k].source)}\"}}" + (k < ss.Length - 1 ? ",\n" : "\n"));
            a.Append("  ],\n  \"targets\": [\n"); var tv = knee.GetComponentsInChildren<AnatomyTargetStateView>(true);
            for (int k = 0; k < tv.Length; k++) a.Append($"    {{\"id\":\"{tv[k].TargetId}\",\"path\":\"{RelPath(tv[k].transform, knee.transform)}\",\"states\":[{string.Join(",", tv[k].SupportedStates.Select(s => "\"" + s + "\""))}],\"collider\":\"BoxCollider (trigger) {V(tv[k].GetComponent<BoxCollider>().size)}\"}}" + (k < tv.Length - 1 ? ",\n" : "\n"));
            a.Append("  ],\n  \"anchors\": [\n"); var an = knee.GetComponentsInChildren<Transform>(true).Where(t => t.name.EndsWith("Root") || t.name.StartsWith("PlacementTarget_") || t.name.StartsWith("GuideTarget_") || t.name == "PatellaRetractedPose").ToArray();
            for (int k = 0; k < an.Length; k++) a.Append($"    {{\"name\":\"{an[k].name}\",\"path\":\"{RelPath(an[k], knee.transform)}\",\"localPos\":{V(knee.transform.InverseTransformPoint(an[k].position))},\"localRot\":{Q(Quaternion.Inverse(knee.transform.rotation) * an[k].rotation)}}}" + (k < an.Length - 1 ? ",\n" : "\n"));
            a.Append("  ]\n}\n"); File.WriteAllText(A + "Prefabs/anatomy_catalog.json", a.ToString());
            AssetDatabase.ImportAsset(I + "Catalog/surgery_asset_catalog.json"); AssetDatabase.ImportAsset(A + "Prefabs/anatomy_catalog.json");
            log.AppendLine("catalogs written");
        }

        // ------------------------------------------------------------------------------------------------ test scene
        static Material Flat(string name, Color c, float smooth = 0.3f, float metal = 0) => Mat(R + "Anatomy/Materials/", name, c, smooth, metal);
        static GameObject Box(string name, Transform parent, Vector3 pos, Vector3 size, Material m)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.name = name; g.transform.SetParent(parent, false); g.transform.localPosition = pos; g.transform.localScale = size;
            g.GetComponent<Renderer>().sharedMaterial = m; return g;
        }
        public static void BuildTestScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Trilight; RenderSettings.ambientSkyColor = new Color(0.62f, 0.66f, 0.70f); RenderSettings.ambientEquatorColor = new Color(0.45f, 0.47f, 0.48f); RenderSettings.ambientGroundColor = new Color(0.25f, 0.25f, 0.25f);
            var env = new GameObject("TestEnvironment (P2 fixture — not the OR)").transform;
            var sun = new GameObject("KeyLight").AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 1.1f; sun.shadows = LightShadows.Soft; sun.transform.rotation = Quaternion.Euler(52, -35, 0); sun.transform.SetParent(env);
            foreach (var (n, p) in new[] { ("SurgicalLight_A", new Vector3(-0.35f, 2.2f, -0.25f)), ("SurgicalLight_B", new Vector3(0.25f, 2.25f, -0.55f)) })
            {
                var l = new GameObject(n).AddComponent<Light>(); l.type = LightType.Spot; l.range = 4; l.spotAngle = 48; l.intensity = 6f; l.shadows = LightShadows.Soft; l.color = new Color(1f, 0.98f, 0.95f);
                l.intensity = 4.2f; l.transform.position = p; l.transform.LookAt(new Vector3(-0.15f, 1.3f, -0.25f)); l.transform.SetParent(env);
            }
            var probe = new GameObject("ReflectionProbe (room)").AddComponent<ReflectionProbe>(); probe.transform.SetParent(env); probe.transform.position = new Vector3(0, 1.4f, 0); probe.size = new Vector3(7.2f, 3.2f, 7.2f); probe.mode = ReflectionProbeMode.Custom; probe.resolution = 256; probe.boxProjection = true;
            var floorM = Flat("M_TestFloor", new Color(0.55f, 0.58f, 0.60f), 0.35f); var tableM = Flat("M_TestTable", new Color(0.22f, 0.24f, 0.27f), 0.4f, 0.3f); var standM = Flat("M_TestStand", new Color(0.75f, 0.76f, 0.78f), 0.5f, 0.6f);
            Box("Floor", env, new Vector3(0, -0.01f, 0), new Vector3(8, 0.02f, 8), floorM);
            var wallM = Flat("M_TestWall", new Color(0.70f, 0.76f, 0.74f), 0.2f);
            foreach (var (n, c, sz) in new[] { ("Wall_N", new Vector3(0, 1.6f, 3.6f), new Vector3(7.2f, 3.2f, 0.1f)), ("Wall_S", new Vector3(0, 1.6f, -3.6f), new Vector3(7.2f, 3.2f, 0.1f)), ("Wall_E", new Vector3(3.6f, 1.6f, 0), new Vector3(0.1f, 3.2f, 7.2f)), ("Wall_W", new Vector3(-3.6f, 1.6f, 0), new Vector3(0.1f, 3.2f, 7.2f)) }) Box(n, env, c, sz, wallM);
            // temporary operating table (Person 1's table: 0.51 x 2.09 m deck, mattress top 0.92 m)
            var table = new GameObject("TempOperatingTable (P2 fixture)").transform; table.SetParent(env);
            Box("Deck", table, new Vector3(0, 0.89f, 0.005f), new Vector3(0.51f, 0.06f, 2.09f), tableM); Box("Column", table, new Vector3(0, 0.43f, 0.1f), new Vector3(0.3f, 0.86f, 0.4f), tableM); Box("Base", table, new Vector3(0, 0.04f, 0.1f), new Vector3(0.6f, 0.08f, 1.2f), tableM);
            var patientRoot = new GameObject(AnchorIds.PatientRoot); patientRoot.transform.position = new Vector3(0, 0.92f, 0); Add<SurgeryAnchor>(patientRoot).anchorId = AnchorIds.PatientRoot; patientRoot.transform.SetParent(table);
            var patient = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PatientPrefab)); patient.transform.SetParent(patientRoot.transform, false);
            // back table with the populated instrument tray
            var back = new GameObject("BackTable (P2 fixture)").transform; back.SetParent(env); back.position = new Vector3(-1.05f, 0, -0.45f);
            Box("Top", back, new Vector3(0, 0.88f, 0), new Vector3(0.75f, 0.04f, 0.6f), standM); Box("Leg", back, new Vector3(0, 0.44f, 0), new Vector3(0.06f, 0.86f, 0.06f), standM);
            var instTable = new GameObject(AnchorIds.InstrumentTable); instTable.transform.SetParent(back, false); instTable.transform.localPosition = new Vector3(0, 0.90f, 0); Add<SurgeryAnchor>(instTable).anchorId = AnchorIds.InstrumentTable;
            var tray = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(TrayPopulatedPrefab)); tray.transform.SetParent(instTable.transform, false);
            // implant table
            var impT = new GameObject("ImplantTable (P2 fixture)").transform; impT.SetParent(env); impT.position = new Vector3(1.0f, 0, -0.75f);
            Box("Top", impT, new Vector3(0, 0.88f, 0), new Vector3(0.6f, 0.04f, 0.35f), standM); Box("Leg", impT, new Vector3(0, 0.44f, 0), new Vector3(0.06f, 0.86f, 0.06f), standM);
            var impA = new GameObject(AnchorIds.ImplantTable); impA.transform.SetParent(impT, false); impA.transform.localPosition = new Vector3(0, 0.90f, 0); Add<SurgeryAnchor>(impA).anchorId = AnchorIds.ImplantTable;
            var set = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ImplantSetPrefab)); set.transform.SetParent(impA.transform, false);
            // exploded / inspection knee on a stand (anatomy only)
            var stand = new GameObject("KneeDisplayStand").transform; stand.SetParent(env); stand.position = new Vector3(1.75f, 0, 1.55f);
            Box("Plinth", stand, new Vector3(0, 0.5f, 0), new Vector3(0.35f, 1.0f, 0.35f), standM);
            var disp = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(KneePrefab)); disp.name = "KneeAnatomy_R (display)"; disp.transform.SetParent(stand, false); disp.transform.localPosition = new Vector3(0, 1.32f, 0);
            foreach (var s in disp.GetComponentsInChildren<AnatomyStructure>(true)) if (s.category == AnatomyCategory.Skin || s.category == AnatomyCategory.SkinOverlay) s.gameObject.SetActive(false);
            disp.GetComponent<KneeAnatomyStateController>().skinIntact = null; disp.GetComponent<KneeAnatomyStateController>().skinOpen = null;
            // camera + inspector + poses
            var camGo = new GameObject("InspectionCamera"); var cam = camGo.AddComponent<Camera>(); cam.nearClipPlane = 0.02f; cam.fieldOfView = 45; camGo.tag = "MainCamera";
            camGo.AddComponent<AudioListener>();
            var data = camGo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>(); data.renderPostProcessing = true; data.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            var poses = new GameObject("CameraPoses").transform;
            var site = patient.GetComponent<SurgicalPatientInfo>().surgicalSite.position;
            Transform Pose(string n, Vector3 pos, Vector3 look) { var t = new GameObject(n).transform; t.SetParent(poses); t.position = pos; t.LookAt(look); return t; }
            var list = new[] {
                Pose("Overview", new Vector3(2.1f, 2.0f, -2.1f), new Vector3(0, 1.0f, -0.1f)),
                Pose("Patient side (right)", new Vector3(-2.0f, 1.55f, 0.05f), new Vector3(0, 1.05f, 0.05f)),
                Pose("Surgeon view (knee)", site + new Vector3(-0.55f, 0.42f, -0.35f), site),
                Pose("Knee front", site + new Vector3(-0.05f, 0.22f, -0.42f), site),
                Pose("Knee lateral", site + new Vector3(-0.45f, 0.05f, 0.02f), site),
                Pose("Knee above", site + new Vector3(-0.08f, 0.45f, -0.05f), site),
                Pose("Instrument tray", tray.transform.position + new Vector3(0.0f, 0.62f, -0.42f), tray.transform.position),
                Pose("Implants", set.transform.position + new Vector3(0.05f, 0.32f, -0.38f), set.transform.position + Vector3.up * 0.03f),
                Pose("Knee display", disp.transform.position + new Vector3(-0.45f, 0.12f, -0.35f), disp.transform.position) };
            var insp = camGo.AddComponent<AnatomyAssetInspector>(); insp.cam = cam; insp.knee = patient.GetComponent<SurgicalPatientInfo>().kneeAnatomy.GetComponent<KneeAnatomyStateController>();
            insp.patient = patient.GetComponent<SurgicalPatientInfo>(); insp.explodedKnee = disp.transform; insp.cameraPoses = list;
            camGo.transform.SetPositionAndRotation(list[2].position, list[2].rotation);
            // bake the room reflections once into a cubemap (works whatever the realtime-probe quality setting is)
            var probePath = R + "Scenes/Anatomy_Test_ReflectionProbe.exr";
            if (UnityEditor.Lightmapping.BakeReflectionProbe(probe, probePath)) { AssetDatabase.ImportAsset(probePath); probe.customBakedTexture = AssetDatabase.LoadAssetAtPath<Texture>(probePath); }
            else log.AppendLine("  ! reflection probe bake failed");
            EditorSceneManager.SaveScene(scene, ScenePath); log.AppendLine("scene: " + ScenePath);
        }
    }
}
