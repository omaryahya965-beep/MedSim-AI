using System.Collections.Generic;
using System.IO;
using System.Linq;
using MedSimAI.AI;
using MedSimAI.Audio;
using MedSimAI.Characters;
using MedSimAI.Core;
using MedSimAI.Instruments;
using MedSimAI.UI;
using MedSimAI.Voice;
using MedSimAI.XR;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace MedSimAI.EditorTools
{
    /// <summary>
    /// Builds the MedSim_ER scene from code: room, PBR materials, equipment, characters, instruments, XR rig, UI, lighting.
    /// Re-runnable (overwrites generated meshes/materials under Assets/MedSimAI/Generated). Menu: MedSim AI ▸ Build ER Scene.
    /// </summary>
    public static class MedSimSceneBuilder
    {
        const string Root = "Assets/MedSimAI";
        const string Gen = Root + "/Generated";
        const string PH = Root + "/Environment/PolyHaven";
        public const string ScenePath = Root + "/Scenes/MedSim_ER.unity";
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
        static int meshCounter;
        static TMP_FontAsset font;

        // Room extents (metres)
        const float RX = 3.0f, RZ = 2.5f, RH = 2.85f;

        [MenuItem("MedSim AI/Build ER Scene")]
        public static string Build()
        {
            mats.Clear(); meshCounter = 0;
            EnsureFolder(Gen); EnsureFolder(Gen + "/Meshes"); EnsureFolder(Gen + "/Materials"); EnsureFolder(Root + "/Scenes");
            foreach (var f in Directory.GetFiles(Gen + "/Meshes", "*.asset")) AssetDatabase.DeleteAsset(f.Replace('\\', '/'));
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
            ConfigurePolyHavenImports();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var env = new GameObject("Environment").transform;
            BuildRoom(env);
            BuildLighting(env);
            var bed = BuildBed(env);
            var props = new GameObject("Props").transform;
            BuildFurniture(props);

            var equipment = new GameObject("Medical Equipment").transform;
            var monitor = BuildPatientMonitor(equipment);
            var trolley = BuildTrolley(equipment);
            var ecg = BuildECGCart(equipment);
            BuildWallEquipment(equipment);
            var sanitizer = BuildSanitizer(equipment);

            var chars = new GameObject("Characters").transform;
            bool real = MedSimRealCharacters.Available;
            var patient = real ? MedSimRealCharacters.BuildPatient(chars, Mat("Zone_Highlight", new Color(0.1f, 0.85f, 0.75f, 0.35f), 0.2f, 0, transparent: true)) : BuildPatient(chars);
            var doctor = real ? MedSimRealCharacters.BuildInstructor(chars, font) : BuildInstructor(chars);

            var inst = new GameObject("Instruments").transform;
            var bpDevice = BuildBPDevice(trolley);
            var steth = BuildStethoscope(inst, trolley.Find("TopShelf").position + new Vector3(-0.12f, 0.02f, 0.05f));
            var cuff = BuildCuff(inst, trolley.Find("TopShelf").position + new Vector3(0.12f, 0.03f, -0.1f), bpDevice, monitor);
            var oxi = BuildOximeter(inst, trolley.Find("TopShelf").position + new Vector3(0.14f, 0.02f, 0.12f), monitor);
            BuildGlovesBox(trolley.Find("TopShelf"));

            // ---------- Systems ----------
            var systems = new GameObject("MedSim Systems");
            var mgr = systems.AddComponent<ClinicalScenarioManager>();
            systems.AddComponent<AIProviderManager>();
            var perf = systems.AddComponent<PerformanceManager>();
            perf.pcvrAsset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/MedSimAI/Settings/MedSim_PCVR_RP.asset");
            perf.standaloneAsset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/MedSimAI/Settings/MedSim_Standalone_RP.asset");
            var voice = systems.AddComponent<VoiceInteractionManager>();
            var session = systems.AddComponent<SessionManager>();

            // ---------- XR ----------
            var (head, xrPlayer) = BuildXR(env);
            voice.patientSpeech = patient.speech; voice.instructorSpeech = doctor.speech; voice.playerHead = head;
            patient.life.lookTarget = head; doctor.life.lookTarget = head;
            steth.earpiece = CreateEarpiece(head);

            session.instructor = doctor; session.patient = patient; session.ecg = ecg; session.monitor = monitor;
            session.instruments = new MedicalInstrumentBase[] { steth, cuff, oxi };
            doctor.trolleyPoint = trolley;

            // ---------- UI ----------
            var uiGo = new GameObject("MedSim UI");
            var ui = uiGo.AddComponent<MedSimUI>();
            var menuAnchor = new GameObject("Menu Anchor").transform; menuAnchor.position = new Vector3(-0.35f, 1.72f, 0.62f); menuAnchor.rotation = Quaternion.LookRotation(new Vector3(-1, -0.08f, 0.0f));
            var tabletAnchor = BuildTabletStand(env);
            ui.menuAnchor = menuAnchor; ui.tabletAnchor = tabletAnchor; ui.playerHead = head;
            ui.logo = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Branding/MedSimAI_Icon_1024.png");

            var amb = new GameObject("Ambience HVAC"); amb.transform.position = new Vector3(0, RH - 0.1f, -1.5f);
            var amba = amb.AddComponent<AudioSource>(); amba.loop = true; amba.spatialBlend = 0.6f; amba.volume = 0.12f; amb.AddComponent<AmbienceLoop>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            var list = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
            AssetDatabase.SaveAssets();
            return $"Built {ScenePath}: {Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Length} renderers, {meshCounter} generated meshes";
        }

        // =====================================================================================
        // Materials
        // =====================================================================================
        static Shader Lit => Shader.Find("Universal Render Pipeline/Lit");

        static Material Mat(string name, Color color, float smooth = 0.4f, float metal = 0f, string phTex = null, Vector2? tiling = null, bool emissive = false, Color? emit = null, bool transparent = false, bool diffuse = true, bool twoSided = false)
        {
            if (mats.TryGetValue(name, out var cached)) return cached;
            string path = $"{Gen}/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Lit); AssetDatabase.CreateAsset(m, path); }
            m.shader = Lit;
            m.SetColor("_BaseColor", color); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metal);
            m.SetTexture("_BaseMap", null); m.SetTexture("_BumpMap", null); m.SetTexture("_OcclusionMap", null);
            m.DisableKeyword("_NORMALMAP"); m.DisableKeyword("_OCCLUSIONMAP");
            if (phTex != null)
            {
                var d = AssetDatabase.LoadAssetAtPath<Texture2D>($"{PH}/{phTex}/{phTex}_diff.jpg");
                var n = AssetDatabase.LoadAssetAtPath<Texture2D>($"{PH}/{phTex}/{phTex}_nor_gl.jpg");
                var a = AssetDatabase.LoadAssetAtPath<Texture2D>($"{PH}/{phTex}/{phTex}_arm.jpg");
                if (d && diffuse) m.SetTexture("_BaseMap", d);
                if (n) { m.SetTexture("_BumpMap", n); m.EnableKeyword("_NORMALMAP"); m.SetFloat("_BumpScale", 0.8f); }
                if (a) { m.SetTexture("_OcclusionMap", a); m.EnableKeyword("_OCCLUSIONMAP"); m.SetFloat("_OcclusionStrength", 0.7f); }
                var t = tiling ?? Vector2.one; m.SetTextureScale("_BaseMap", t); m.mainTextureScale = t;
            }
            if (emissive) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emit ?? color); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive; }
            else { m.DisableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.black); }
            if (transparent)
            {
                m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0); m.SetOverrideTag("RenderType", "Transparent");
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha); m.SetFloat("_ZWrite", 0);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = 3000;
            }
            else { m.SetFloat("_Surface", 0); m.SetOverrideTag("RenderType", "Opaque"); m.SetFloat("_ZWrite", 1); m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = -1; m.SetFloat("_SrcBlend", 1); m.SetFloat("_DstBlend", 0); }
            m.SetFloat("_Cull", twoSided ? 0f : 2f); m.doubleSidedGI = twoSided;
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            mats[name] = m; return m;
        }

        static void ConfigurePolyHavenImports()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { PH }))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(p) is TextureImporter ti)) continue;
                bool changed = false;
                bool normal = p.Contains("_nor_gl") || p.ToLower().Contains("_nor_");
                if (normal && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; changed = true; }
                bool linear = normal || p.Contains("_arm") || p.Contains("_rough");
                if (!normal && ti.sRGBTexture == linear) { ti.sRGBTexture = !linear; changed = true; }
                if (ti.maxTextureSize != 1024) { ti.maxTextureSize = 1024; changed = true; }
                var android = ti.GetPlatformTextureSettings("Android");
                if (!android.overridden || android.format != TextureImporterFormat.ASTC_6x6) { android.overridden = true; android.format = TextureImporterFormat.ASTC_6x6; android.maxTextureSize = 1024; ti.SetPlatformTextureSettings(android); changed = true; }
                if (changed) ti.SaveAndReimport();
            }
        }

        // =====================================================================================
        // Helpers
        // =====================================================================================
        static GameObject Part(string name, Transform parent, Mesh mesh, Material mat, Vector3 pos, Vector3? euler = null, bool local = true, bool isStatic = true, bool collider = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            if (local) { go.transform.localPosition = pos; go.transform.localRotation = Quaternion.Euler(euler ?? Vector3.zero); }
            else { go.transform.position = pos; go.transform.rotation = Quaternion.Euler(euler ?? Vector3.zero); }
            go.AddComponent<MeshFilter>().sharedMesh = Save(mesh, name);
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat;
            if (collider) go.AddComponent<MeshCollider>().sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
            if (isStatic) GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.BatchingStatic | StaticEditorFlags.ReflectionProbeStatic);
            return go;
        }

        static Mesh Save(Mesh m, string name)
        {
            if (AssetDatabase.Contains(m)) return m;
            m.name = name;
            Unwrapping.GenerateSecondaryUVSet(m);
            AssetDatabase.CreateAsset(m, $"{Gen}/Meshes/{meshCounter++:0000}_{Sanitize(name)}.asset");
            return m;
        }
        static string Sanitize(string s) => new string(s.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

        static GameObject Box(string name, Transform parent, Vector3 size, float radius, Material mat, Vector3 pos, Vector3? euler = null, bool collider = false, bool isStatic = true, float uv = 1f)
        {
            var go = Part(name, parent, MeshKit.RoundedBox(size, radius, 3, uv), mat, pos, euler, true, isStatic);
            if (collider) { var bc = go.AddComponent<BoxCollider>(); bc.size = size; }
            return go;
        }

        static GameObject Tube(string name, Transform parent, Vector3[] path, float r, Material mat, bool isStatic = true, int sides = 12)
            => Part(name, parent, MeshKit.Tube(path, r, sides, 6), mat, Vector3.zero, null, true, isStatic);

        static GameObject Lathe(string name, Transform parent, Vector2[] profile, Material mat, Vector3 pos, Vector3? euler = null, int sides = 28, bool isStatic = true)
            => Part(name, parent, MeshKit.Lathe(profile, sides), mat, pos, euler, true, isStatic);

        static GameObject Model(string id, Transform parent, Vector3 pos, float yaw, float scale = 1f)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PH}/{id}/{id}.fbx");
            if (!prefab) { Debug.LogWarning("[MedSim] missing model " + id); return null; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.position = pos; go.transform.rotation = Quaternion.Euler(0, yaw, 0); go.transform.localScale = Vector3.one * scale;
            foreach (var t in go.GetComponentsInChildren<Transform>()) GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var shared = r.sharedMaterials;
                for (int i = 0; i < shared.Length; i++) shared[i] = ModelMaterial(id, shared[i] ? shared[i].name : id);
                r.sharedMaterials = shared;
            }
            return go;
        }

        /// <summary>Builds a URP Lit material for a Poly Haven model part from its bundled textures (diff / nor_gl / arm / rough / metal).</summary>
        static Material ModelMaterial(string id, string matName)
        {
            string key = "PH_" + id + "_" + Sanitize(matName);
            if (mats.TryGetValue(key, out var cached)) return cached;
            var texDir = $"{PH}/{id}/textures";
            var files = Directory.Exists(texDir) ? Directory.GetFiles(texDir).Where(f => !f.EndsWith(".meta")).Select(f => f.Replace('\\', '/')).ToList() : new List<string>();
            // Group by prefix before the map suffix; pick the group that best matches the material name.
            string Prefix(string f) { var n = Path.GetFileNameWithoutExtension(f); foreach (var s in new[] { "_diff", "_nor_gl", "_arm", "_rough", "_metal", "_ao", "_alpha" }) { int ix = n.IndexOf(s); if (ix > 0) return n.Substring(0, ix); } return n; }
            var groups = files.GroupBy(Prefix).ToList();
            var g = groups.OrderByDescending(x => matName.ToLower().Contains(x.Key.ToLower()) ? 2 : x.Key.ToLower().Contains(matName.ToLower().Split(' ')[0]) ? 1 : 0).FirstOrDefault();
            string path = $"{Gen}/Materials/{key}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Lit); AssetDatabase.CreateAsset(m, path); }
            m.shader = Lit; m.SetColor("_BaseColor", Color.white); m.SetFloat("_Smoothness", 0.45f);
            if (g != null)
            {
                Texture2D T(string suffix) { var f = g.FirstOrDefault(x => Path.GetFileNameWithoutExtension(x).Contains(suffix)); return f != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(f) : null; }
                var d = T("_diff"); var n = T("_nor_gl"); var arm = T("_arm"); var alpha = T("_alpha");
                if (d) m.SetTexture("_BaseMap", d);
                if (n) { var ti = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(n)); if (ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; ti.SaveAndReimport(); } m.SetTexture("_BumpMap", n); m.EnableKeyword("_NORMALMAP"); }
                if (arm) { m.SetTexture("_OcclusionMap", arm); m.EnableKeyword("_OCCLUSIONMAP"); }
                if (alpha || id.Contains("calathea")) { m.SetFloat("_AlphaClip", 1); m.SetFloat("_Cutoff", 0.4f); m.EnableKeyword("_ALPHATEST_ON"); m.SetFloat("_Cull", 0); }
            }
            m.enableInstancing = true; EditorUtility.SetDirty(m);
            mats[key] = m; return m;
        }

        static TextMeshPro Label(Transform parent, string text, Vector3 localPos, Vector3 localEuler, float size, Color color, Vector2 box, TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            var go = new GameObject("Text"); go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos; go.transform.localRotation = Quaternion.Euler(localEuler);
            var t = go.AddComponent<TextMeshPro>(); if (font) t.font = font;
            t.text = text; t.fontSize = size; t.color = color; t.alignment = align; t.rectTransform.sizeDelta = box; t.textWrappingMode = TextWrappingModes.Normal;
            return t;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/'); EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        // =====================================================================================
        // Room
        // =====================================================================================
        static void BuildRoom(Transform env)
        {
            var room = new GameObject("ER Exam Room").transform; room.SetParent(env);
            var floor = Mat("Floor_Vinyl", new Color(0.72f, 0.77f, 0.8f), 0.6f, 0, "terrazzo_tiles", new Vector2(3, 2.5f), diffuse: false);
            var wall = Mat("Wall_Plaster", new Color(0.93f, 0.94f, 0.95f), 0.2f, 0, "white_plaster_02", new Vector2(3, 1.5f), diffuse: false);
            var wallTeal = Mat("Wall_TealAccent", new Color(0.55f, 0.78f, 0.78f), 0.2f, 0, "white_plaster_02", new Vector2(2, 1), diffuse: false);
            var tile = Mat("Wall_Tiles", new Color(0.96f, 0.97f, 0.98f), 0.75f, 0, "long_white_tiles", new Vector2(2, 1));
            var ceil = Mat("Ceiling", new Color(0.95f, 0.95f, 0.95f), 0.1f, 0, "ceiling_interior", new Vector2(3, 3), diffuse: false);
            var rail = Mat("CrashRail_Vinyl", new Color(0.55f, 0.6f, 0.64f), 0.45f);
            var skirting = Mat("Skirting", new Color(0.36f, 0.42f, 0.48f), 0.35f);

            var f = Box("Floor", room, new Vector3(RX * 2, 0.04f, RZ * 2), 0.005f, floor, new Vector3(0, -0.02f, 0), collider: true, uv: 1f);
            Box("Ceiling", room, new Vector3(RX * 2, 0.04f, RZ * 2), 0.005f, ceil, new Vector3(0, RH + 0.02f, 0));
            // Walls with window cut (x = -RX) and door cut (z = -RZ)
            Box("Wall_Back", room, new Vector3(RX * 2, RH, 0.12f), 0.004f, wall, new Vector3(0, RH / 2, RZ + 0.06f), collider: true);
            Box("Wall_Back_TealAccent", room, new Vector3(2.4f, RH - 0.02f, 0.02f), 0.004f, wallTeal, new Vector3(0.3f, RH / 2, RZ - 0.005f));
            Box("Wall_Right", room, new Vector3(0.12f, RH, RZ * 2), 0.004f, wall, new Vector3(RX + 0.06f, RH / 2, 0), collider: true);
            // Left wall with window (opening x=-RX, z from -1.6 to 0.2, y 0.9..2.2)
            Box("Wall_Left_A", room, new Vector3(0.12f, RH, RZ - 1.6f + 0.0f), 0.004f, wall, new Vector3(-RX - 0.06f, RH / 2, -RZ + (RZ - 1.6f) / 2), collider: true);
            Box("Wall_Left_B", room, new Vector3(0.12f, RH, RZ - 0.2f), 0.004f, wall, new Vector3(-RX - 0.06f, RH / 2, 0.2f + (RZ - 0.2f) / 2), collider: true);
            Box("Wall_Left_Sill", room, new Vector3(0.12f, 0.9f, 1.8f), 0.004f, wall, new Vector3(-RX - 0.06f, 0.45f, -0.7f), collider: true);
            Box("Wall_Left_Head", room, new Vector3(0.12f, RH - 2.2f, 1.8f), 0.004f, wall, new Vector3(-RX - 0.06f, 2.2f + (RH - 2.2f) / 2, -0.7f));
            // Front wall with door opening (x 1.3..2.4, height 2.15)
            Box("Wall_Front_A", room, new Vector3(RX + 1.3f, RH, 0.12f), 0.004f, wall, new Vector3((-RX + 1.3f) / 2, RH / 2, -RZ - 0.06f), collider: true);
            Box("Wall_Front_B", room, new Vector3(RX - 2.4f, RH, 0.12f), 0.004f, wall, new Vector3((2.4f + RX) / 2, RH / 2, -RZ - 0.06f), collider: true);
            Box("Wall_Front_Head", room, new Vector3(1.1f, RH - 2.15f, 0.12f), 0.004f, wall, new Vector3(1.85f, 2.15f + (RH - 2.15f) / 2, -RZ - 0.06f));

            // Clinical splash-back tiles near sink wall + crash rails + skirting
            Box("Tiles_Splashback", room, new Vector3(0.015f, 0.6f, 1.5f), 0.003f, tile, new Vector3(RX - 0.008f, 1.2f, 1.6f));
            foreach (var (pos, size) in new[] { (new Vector3(0, 0.95f, RZ - 0.02f), new Vector3(RX * 2 - 0.02f, 0.12f, 0.035f)), (new Vector3(RX - 0.02f, 0.95f, -1.2f), new Vector3(0.035f, 0.12f, 2.2f)) })
                Box("CrashRail", room, size, 0.012f, rail, pos);
            Box("Skirt_Back", room, new Vector3(RX * 2, 0.1f, 0.015f), 0.003f, skirting, new Vector3(0, 0.05f, RZ - 0.007f));
            Box("Skirt_Right", room, new Vector3(0.015f, 0.1f, RZ * 2), 0.003f, skirting, new Vector3(RX - 0.007f, 0.05f, 0));
            Box("Skirt_Front", room, new Vector3(RX + 1.3f, 0.1f, 0.015f), 0.003f, skirting, new Vector3((-RX + 1.3f) / 2, 0.05f, -RZ + 0.007f));
            Box("Skirt_LeftA", room, new Vector3(0.015f, 0.1f, RZ * 2), 0.003f, skirting, new Vector3(-RX + 0.007f, 0.05f, 0));

            // Teleport area on the floor
            var tp = new GameObject("Teleport Floor"); tp.transform.SetParent(room);
            tp.transform.position = new Vector3(0, 0.001f, 0);
            var tpc = tp.AddComponent<BoxCollider>(); tpc.size = new Vector3(RX * 2 - 0.3f, 0.002f, RZ * 2 - 0.3f);
            tp.AddComponent<TeleportationArea>();

            BuildWindow(room); BuildDoor(room); BuildCurtains(room);
        }

        static void BuildWindow(Transform room)
        {
            var frame = Mat("Window_Frame_AluPowder", new Color(0.88f, 0.9f, 0.92f), 0.5f, 0.2f);
            var glass = Mat("Glass", new Color(0.75f, 0.85f, 0.9f, 0.18f), 0.95f, 0f, transparent: true);
            var outside = Mat("Exterior_Sky", new Color(0.7f, 0.82f, 0.95f), 0f, 0, emissive: true, emit: new Color(0.9f, 1.0f, 1.15f) * 1.6f);
            var blind = Mat("Blind_Slats", new Color(0.94f, 0.94f, 0.92f), 0.35f);
            var w = new GameObject("Window").transform; w.SetParent(room); w.position = new Vector3(-RX, 1.55f, -0.7f);
            Box("Frame_Top", w, new Vector3(0.14f, 0.06f, 1.86f), 0.01f, frame, new Vector3(0, 0.67f, 0));
            Box("Frame_Bottom", w, new Vector3(0.2f, 0.05f, 1.86f), 0.01f, frame, new Vector3(0.03f, -0.66f, 0));
            Box("Frame_L", w, new Vector3(0.14f, 1.36f, 0.06f), 0.01f, frame, new Vector3(0, 0, -0.9f));
            Box("Frame_R", w, new Vector3(0.14f, 1.36f, 0.06f), 0.01f, frame, new Vector3(0, 0, 0.9f));
            Box("Mullion", w, new Vector3(0.1f, 1.3f, 0.05f), 0.01f, frame, new Vector3(0, 0, 0));
            Box("Glass", w, new Vector3(0.01f, 1.3f, 1.76f), 0.002f, glass, new Vector3(-0.02f, 0, 0));
            Box("Exterior_Backdrop", w, new Vector3(0.02f, 3.0f, 4.5f), 0.002f, outside, new Vector3(-1.6f, 0, 0));
            // Half-lowered venetian blind for realism
            for (int i = 0; i < 18; i++) Box("Slat", w, new Vector3(0.025f, 0.003f, 1.74f), 0.001f, blind, new Vector3(0.05f, 0.62f - i * 0.026f, 0), new Vector3(0, 0, 35));
            Box("Blind_Head", w, new Vector3(0.05f, 0.04f, 1.78f), 0.006f, frame, new Vector3(0.05f, 0.64f, 0));
        }

        static void BuildDoor(Transform room)
        {
            var wood = Mat("Door_Laminate", new Color(0.78f, 0.7f, 0.6f), 0.45f, 0, "laminate_floor_02", new Vector2(0.6f, 1.5f));
            var frame = Mat("Door_Frame", new Color(0.82f, 0.85f, 0.88f), 0.5f, 0.1f);
            var steel = Mat("Steel_Brushed", new Color(0.78f, 0.79f, 0.8f), 0.7f, 1f, "metal_plate_02", new Vector2(0.5f, 0.5f));
            var glass = Mat("Glass", Color.clear);
            var d = new GameObject("Door").transform; d.SetParent(room); d.position = new Vector3(1.85f, 0, -RZ);
            Box("Frame_L", d, new Vector3(0.06f, 2.18f, 0.16f), 0.008f, frame, new Vector3(-0.56f, 1.09f, 0));
            Box("Frame_R", d, new Vector3(0.06f, 2.18f, 0.16f), 0.008f, frame, new Vector3(0.56f, 1.09f, 0));
            Box("Frame_T", d, new Vector3(1.18f, 0.06f, 0.16f), 0.008f, frame, new Vector3(0, 2.18f, 0));
            var leaf = Box("Door_Leaf", d, new Vector3(1.04f, 2.12f, 0.045f), 0.006f, wood, new Vector3(0, 1.07f, 0.02f), collider: true);
            Box("Vision_Panel", leaf.transform, new Vector3(0.22f, 0.55f, 0.05f), 0.006f, glass, new Vector3(0.22f, 0.35f, 0));
            Box("Kick_Plate", leaf.transform, new Vector3(1.0f, 0.25f, 0.05f), 0.003f, steel, new Vector3(0, -0.92f, 0.001f));
            Tube("Handle", leaf.transform, new[] { new Vector3(-0.38f, 0.0f, 0.03f), new Vector3(-0.38f, 0.0f, 0.075f), new Vector3(-0.24f, 0.0f, 0.075f) }, 0.011f, steel);
            var sign = Box("Door_Sign", room, new Vector3(0.5f, 0.12f, 0.01f), 0.01f, Mat("Sign_Navy", new Color(0.04f, 0.1f, 0.18f), 0.6f), new Vector3(1.85f, 2.42f, -RZ + 0.006f), new Vector3(0, 180, 0));
            Label(sign.transform, "<b>EXAM ROOM 3</b>  ·  RESUS", new Vector3(0, 0, -0.007f), Vector3.zero, 0.42f, Color.white, new Vector2(0.48f, 0.1f));
        }

        static void BuildCurtains(Transform room)
        {
            var curtain = Mat("Curtain_Fabric", new Color(0.55f, 0.78f, 0.8f), 0.08f, 0, "terry_cloth", new Vector2(14, 14), diffuse: false, twoSided: true);
            var track = Mat("Curtain_Track", new Color(0.9f, 0.91f, 0.92f), 0.6f, 0.3f);
            var c = new GameObject("Privacy Curtain").transform; c.SetParent(room);
            // Ceiling track: L-shape around bed foot/left side
            Tube("Track", c, new[] { new Vector3(-1.05f, RH - 0.05f, RZ - 0.05f), new Vector3(-1.05f, RH - 0.05f, -0.7f), new Vector3(-0.9f, RH - 0.05f, -0.85f), new Vector3(1.6f, RH - 0.05f, -0.85f) }, 0.014f, track);
            // Bunched curtain on the left side (drawn back toward the head wall)
            var mesh = MeshKit.Cloth(1.2f, 2.2f, 60, 30, (u, v) => new Vector3(0, 0, Mathf.Sin(u * Mathf.PI * 14) * 0.05f + Mathf.Sin(u * 31f) * 0.008f + (1 - v) * 0.02f * Mathf.Sin(u * 7)));
            Part("Curtain_Left", c, mesh, curtain, new Vector3(-1.05f, RH - 0.08f - 1.1f, RZ - 0.75f), new Vector3(0, 90, 0));
            var mesh2 = MeshKit.Cloth(0.6f, 2.2f, 40, 30, (u, v) => new Vector3(0, 0, Mathf.Sin(u * Mathf.PI * 12) * 0.055f + (1 - v) * 0.02f * Mathf.Sin(u * 9)));
            Part("Curtain_Foot", c, mesh2, curtain, new Vector3(-0.62f, RH - 0.08f - 1.1f, -0.85f), Vector3.zero);
            for (int i = 0; i < 24; i++) Lathe("Glider", c, new[] { new Vector2(0, 0), new Vector2(0.012f, 0), new Vector2(0.012f, 0.02f), new Vector2(0, 0.02f) }, track, new Vector3(-1.05f, RH - 0.09f, RZ - 0.15f - i * 0.05f), null, 10);
        }

        // =====================================================================================
        // Lighting, probes, post
        // =====================================================================================
        static void BuildLighting(Transform env)
        {
            var l = new GameObject("Lighting").transform; l.SetParent(env);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.78f, 0.8f, 0.82f); RenderSettings.ambientEquatorColor = new Color(0.62f, 0.64f, 0.66f); RenderSettings.ambientGroundColor = new Color(0.42f, 0.42f, 0.43f);
            RenderSettings.skybox = null; RenderSettings.fog = false;

            var fixtures = new[] { new Vector3(-0.2f, RH, -1.2f), new Vector3(-0.2f, RH, 1.3f), new Vector3(2.0f, RH, 0f) };
            var panel = Mat("LED_Panel", Color.white, 0.2f, 0, emissive: true, emit: new Color(1f, 0.98f, 0.95f) * 2.2f);
            var housing = Mat("LED_Housing", new Color(0.9f, 0.9f, 0.9f), 0.4f, 0.2f);
            int i = 0;
            foreach (var p in fixtures)
            {
                var fx = new GameObject("Ceiling LED Panel " + i++).transform; fx.SetParent(l); fx.position = p;
                Box("Housing", fx, new Vector3(1.2f, 0.04f, 0.6f), 0.006f, housing, new Vector3(0, -0.02f, 0));
                Box("Diffuser", fx, new Vector3(1.16f, 0.01f, 0.56f), 0.004f, panel, new Vector3(0, -0.04f, 0));
                var lg = new GameObject("Light"); lg.transform.SetParent(fx, false); lg.transform.localPosition = new Vector3(0, -0.12f, 0); lg.transform.localRotation = Quaternion.Euler(90, 0, 0);
                var li = lg.AddComponent<Light>(); li.type = LightType.Spot; li.spotAngle = 150; li.innerSpotAngle = 90; li.range = 6.5f; li.intensity = 5.5f; li.color = new Color(1f, 0.97f, 0.93f);
                li.shadows = i == 2 ? LightShadows.Soft : LightShadows.None; li.lightmapBakeType = LightmapBakeType.Mixed;
            }
            // Examination light on ceiling arm above the bed
            var arm = new GameObject("Examination Light").transform; arm.SetParent(l); arm.position = new Vector3(0.3f, RH, 0.9f);
            var white = Mat("Medical_White_Plastic", new Color(0.93f, 0.94f, 0.95f), 0.55f);
            Lathe("Mount", arm, new[] { new Vector2(0, 0), new Vector2(0.09f, 0), new Vector2(0.08f, -0.05f), new Vector2(0.03f, -0.07f), new Vector2(0, -0.07f) }, white, Vector3.zero);
            Tube("Arm", arm, new[] { new Vector3(0, -0.06f, 0), new Vector3(0, -0.45f, 0), new Vector3(-0.25f, -0.7f, 0.15f), new Vector3(-0.35f, -0.78f, 0.25f) }, 0.022f, white);
            var head = Lathe("Lamp_Head", arm, new[] { new Vector2(0, 0.06f), new Vector2(0.1f, 0.05f), new Vector2(0.18f, 0f), new Vector2(0.19f, -0.03f), new Vector2(0, -0.03f) }, white, new Vector3(-0.38f, -0.84f, 0.28f), new Vector3(10, 0, -18));
            Lathe("Lamp_Lens", head.transform, new[] { new Vector2(0, -0.031f), new Vector2(0.17f, -0.031f), new Vector2(0, -0.032f) }, Mat("Lamp_Lens", Color.white, 0.9f, 0, emissive: true, emit: Color.white * 1.2f), Vector3.zero);
            var spot = new GameObject("Exam Spot"); spot.transform.SetParent(head.transform, false); spot.transform.localPosition = new Vector3(0, -0.05f, 0); spot.transform.localRotation = Quaternion.Euler(90, 0, 0);
            var sl = spot.AddComponent<Light>(); sl.type = LightType.Spot; sl.spotAngle = 55; sl.innerSpotAngle = 30; sl.range = 2.5f; sl.intensity = 2.5f; sl.color = new Color(1f, 0.99f, 0.97f); sl.shadows = LightShadows.Soft;

            // Soft daylight through the window
            var sun = new GameObject("Daylight"); sun.transform.SetParent(l); sun.transform.rotation = Quaternion.Euler(28, 75, 0);
            var dl = sun.AddComponent<Light>(); dl.type = LightType.Directional; dl.intensity = 0.55f; dl.color = new Color(1f, 0.96f, 0.9f); dl.shadows = LightShadows.Soft; dl.shadowStrength = 0.6f;

            var probe = new GameObject("Reflection Probe").AddComponent<ReflectionProbe>(); probe.transform.SetParent(l); probe.transform.position = new Vector3(0, 1.4f, 0);
            probe.size = new Vector3(RX * 2, RH, RZ * 2); probe.boxProjection = true; probe.mode = ReflectionProbeMode.Baked; probe.resolution = 256;

            var lpg = new GameObject("Light Probes").AddComponent<LightProbeGroup>(); lpg.transform.SetParent(l);
            var pts = new List<Vector3>();
            for (float x = -RX + 0.3f; x <= RX - 0.3f; x += 1.0f) for (float z = -RZ + 0.3f; z <= RZ - 0.3f; z += 1.0f) foreach (var y in new[] { 0.3f, 1.2f, 2.2f }) pts.Add(new Vector3(x, y, z));
            lpg.probePositions = pts.ToArray();

            // Post-processing volume
            var vol = new GameObject("Post Processing").AddComponent<Volume>(); vol.transform.SetParent(l); vol.isGlobal = true;
            var profPath = Gen + "/MedSim_PostProfile.asset";
            var prof = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profPath);
            if (!prof) { prof = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(prof, profPath); }
            foreach (var c in prof.components.ToList()) { prof.Remove(c.GetType()); Object.DestroyImmediate(c, true); }
            var tm = prof.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.Neutral);
            var bloom = prof.Add<Bloom>(true); bloom.intensity.Override(0.25f); bloom.threshold.Override(1.1f);
            var ca = prof.Add<ColorAdjustments>(true); ca.postExposure.Override(0.15f); ca.contrast.Override(8f); ca.saturation.Override(-4f);
            var wb = prof.Add<WhiteBalance>(true); wb.temperature.Override(-4f);
            var vg = prof.Add<Vignette>(true); vg.intensity.Override(0.12f);
            foreach (var c in prof.components) AssetDatabase.AddObjectToAsset(c, prof);
            EditorUtility.SetDirty(prof);
            vol.sharedProfile = prof;
        }

        // =====================================================================================
        // Bed
        // =====================================================================================
        static Transform BuildBed(Transform env)
        {
            var bed = new GameObject("Hospital Bed").transform; bed.SetParent(env); bed.position = new Vector3(0.3f, 0, 0.9f);
            var steel = Mat("Steel_Brushed", Color.white);
            var white = Mat("Medical_White_Plastic", Color.white);
            var grey = Mat("Bed_Grey_Plastic", new Color(0.62f, 0.66f, 0.7f), 0.45f);
            var mattress = Mat("Mattress_Vinyl", new Color(0.24f, 0.4f, 0.55f), 0.45f, 0, "leather_white", new Vector2(2, 3));
            var sheet = Mat("Sheet_Cotton", new Color(0.96f, 0.97f, 0.98f), 0.1f, 0, "terry_cloth", new Vector2(14, 14), diffuse: false, twoSided: true);
            var blanket = Mat("Blanket_Cellular", new Color(0.62f, 0.78f, 0.9f), 0.05f, 0, "terry_cloth", new Vector2(14, 14), diffuse: false, twoSided: true);
            var rubber = Mat("Rubber_Black", new Color(0.08f, 0.08f, 0.09f), 0.35f);

            // Base & column
            Box("Base_Frame", bed, new Vector3(0.82f, 0.08f, 1.9f), 0.02f, grey, new Vector3(0, 0.2f, 0));
            Box("Lift_Column_Shroud", bed, new Vector3(0.5f, 0.28f, 0.5f), 0.04f, grey, new Vector3(0, 0.35f, 0.25f));
            foreach (var x in new[] { -0.36f, 0.36f }) foreach (var z in new[] { -0.85f, 0.85f })
            {
                Lathe("Castor_Wheel", bed, new[] { new Vector2(0, -0.03f), new Vector2(0.06f, -0.03f), new Vector2(0.065f, 0), new Vector2(0.06f, 0.03f), new Vector2(0, 0.03f) }, rubber, new Vector3(x, 0.065f, z), new Vector3(0, 0, 90), 20);
                Box("Castor_Fork", bed, new Vector3(0.07f, 0.1f, 0.05f), 0.01f, steel, new Vector3(x, 0.13f, z));
            }
            // Deck & mattress (head section raised 35°)
            Box("Deck_Frame", bed, new Vector3(0.92f, 0.06f, 2.0f), 0.015f, steel, new Vector3(0, 0.5f, 0));
            var foot = Box("Mattress_Foot", bed, new Vector3(0.88f, 0.15f, 1.15f), 0.06f, mattress, new Vector3(0, 0.6f, -0.4f), collider: true);
            var headPivot = new GameObject("Head_Section").transform; headPivot.SetParent(bed, false); headPivot.localPosition = new Vector3(0, 0.6f, 0.18f); headPivot.localRotation = Quaternion.Euler(-35, 0, 0);
            Box("Mattress_Head", headPivot, new Vector3(0.88f, 0.15f, 0.82f), 0.06f, mattress, new Vector3(0, 0, 0.41f), collider: true);
            Box("Sheet_Head", headPivot, new Vector3(0.9f, 0.01f, 0.8f), 0.004f, sheet, new Vector3(0, 0.08f, 0.41f));
            var pillow = Part("Pillow", headPivot, MeshKit.RoundedBox(new Vector3(0.58f, 0.1f, 0.38f), 0.05f, 4), sheet, new Vector3(0, 0.12f, 0.66f));
            pillow.transform.localScale = new Vector3(1, 1, 1);
            // Head/foot boards
            Box("Headboard", bed, new Vector3(0.95f, 0.5f, 0.05f), 0.04f, white, new Vector3(0, 0.85f, 1.02f));
            Box("Footboard", bed, new Vector3(0.95f, 0.38f, 0.05f), 0.04f, white, new Vector3(0, 0.75f, -1.02f));
            Box("Footboard_Panel", bed, new Vector3(0.5f, 0.12f, 0.012f), 0.01f, Mat("Panel_Dark", new Color(0.12f, 0.14f, 0.17f), 0.8f), new Vector3(0, 0.82f, -1.05f));
            // Side rails (split, head side raised on the far side)
            foreach (var side in new[] { -1, 1 })
            {
                float x = side * 0.49f;
                if (side < 0) Tube("SideRail_Head", bed, new[] { new Vector3(x, 0.78f, 0.25f), new Vector3(x, 1.02f, 0.45f), new Vector3(x, 1.12f, 0.75f), new Vector3(x, 1.12f, 0.85f) }, 0.016f, white);
                Tube("SideRail_Foot", bed, new[] { new Vector3(x, 0.62f, -0.2f), new Vector3(x, 0.66f, -0.9f) }, 0.014f, white);
            }
            // Blanket draped over legs
            var bmesh = MeshKit.Cloth(1.05f, 1.2f, 40, 40, (u, v) =>
            {
                float edge = Mathf.Abs(u - 0.5f) * 2; // 0 centre → 1 sides
                float drop = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.75f, 1f, edge)) * 0.22f; // hang over mattress edge
                float legs = (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.14f, 0.38f, Mathf.Abs(u - 0.5f)))) * (0.13f + 0.14f * Mathf.Max(Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.72f, 0.92f, v)), Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.28f, 0.08f, v)))); // legs underneath, tented over the feet
                float fold = Mathf.Sin(v * 12 + u * 4) * 0.008f;
                return new Vector3(0, 0, legs - drop + fold);
            });
            Part("Blanket", bed, bmesh, blanket, new Vector3(0, 0.69f, -0.42f), new Vector3(-90, 0, 0), isStatic: false);
            // Clipboard chart at foot
            Model("clipboard", bed, bed.position + new Vector3(0.2f, 0.95f, -1.07f), 180, 1f)?.transform.Rotate(-15, 0, 0);
            // Patient chart hung on the footboard: identity + triage note only (history must still be taken from the patient).
            var chart = new GameObject("Patient Chart").transform; chart.SetParent(bed, false);
            chart.position = bed.position + new Vector3(-0.22f, 0.86f, -1.075f); chart.rotation = Quaternion.Euler(10, 0, 0);
            Box("Chart_Board", chart, new Vector3(0.24f, 0.32f, 0.008f), 0.006f, Mat("Chart_Board", new Color(0.1f, 0.35f, 0.42f), 0.5f), Vector3.zero);
            Box("Chart_Paper", chart, new Vector3(0.21f, 0.28f, 0.002f), 0.001f, Mat("Poster_Paper", Color.white), new Vector3(0, -0.01f, -0.005f));
            Label(chart, "<b>EMERGENCY DEPT · BAY 3</b>\n<size=80%>Name: <b>REYES, Adam</b>  (fictional)\nAge/Sex: 58 M   MRN: SIM-0001\nTriage: chest pain, ~45 min\nTriage category: 2 (very urgent)\nAllergies: <i>to be confirmed</i>\nMonitoring: 3-lead ECG attached</size>\n\n<size=65%><color=#B71C1C>DRAFT — NOT CLINICALLY VALIDATED</color></size>",
                new Vector3(0, -0.01f, -0.007f), Vector3.zero, 0.13f, new Color(0.08f, 0.1f, 0.16f), new Vector2(0.2f, 0.26f), TextAlignmentOptions.TopLeft);
            return bed;
        }

        // =====================================================================================
        // Furniture / props
        // =====================================================================================
        static void BuildFurniture(Transform props)
        {
            var cabWhite = Mat("Cabinet_White_HPL", new Color(0.92f, 0.93f, 0.94f), 0.55f);
            var cabTeal = Mat("Cabinet_Teal_Front", new Color(0.24f, 0.58f, 0.6f), 0.55f);
            var counter = Mat("Counter_Solid_Surface", new Color(0.9f, 0.9f, 0.9f), 0.7f, 0, "terrazzo_tiles", new Vector2(0.5f, 0.3f), diffuse: false);
            var steel = Mat("Steel_Brushed", Color.white);

            // Base cabinets with sink on the right wall
            var run = new GameObject("Cabinet Run").transform; run.SetParent(props); run.position = new Vector3(RX - 0.31f, 0, 1.6f);
            Box("Plinth", run, new Vector3(0.56f, 0.1f, 1.6f), 0.005f, Mat("Skirting", Color.white), new Vector3(0.02f, 0.05f, 0));
            for (int i = 0; i < 3; i++)
            {
                float z = -0.53f + i * 0.53f;
                Box("Carcass", run, new Vector3(0.58f, 0.78f, 0.52f), 0.006f, cabWhite, new Vector3(0, 0.49f, z));
                Box("Door", run, new Vector3(0.02f, 0.72f, 0.5f), 0.006f, i == 1 ? cabWhite : cabTeal, new Vector3(-0.3f, 0.49f, z));
                Tube("Handle", run, new[] { new Vector3(-0.32f, 0.72f, z + 0.18f), new Vector3(-0.34f, 0.72f, z + 0.18f), new Vector3(-0.34f, 0.58f, z + 0.18f), new Vector3(-0.32f, 0.58f, z + 0.18f) }, 0.006f, steel);
            }
            Box("Worktop", run, new Vector3(0.62f, 0.035f, 1.62f), 0.008f, counter, new Vector3(0, 0.9f, 0));
            Lathe("Sink_Basin", run, new[] { new Vector2(0, -0.14f), new Vector2(0.16f, -0.13f), new Vector2(0.19f, -0.02f), new Vector2(0.2f, 0.005f), new Vector2(0.21f, 0.005f) }, steel, new Vector3(-0.02f, 0.92f, 0.0f), null, 32);
            Tube("Tap", run, new[] { new Vector3(0.2f, 0.92f, 0), new Vector3(0.2f, 1.2f, 0), new Vector3(0.12f, 1.25f, 0), new Vector3(0.04f, 1.17f, 0) }, 0.012f, steel);
            Tube("Elbow_Lever", run, new[] { new Vector3(0.21f, 1.05f, 0.0f), new Vector3(0.21f, 1.05f, 0.18f) }, 0.007f, steel);
            // Wall cabinets
            for (int i = 0; i < 2; i++)
            {
                float z = -0.4f + i * 0.8f;
                Box("WallCab", run, new Vector3(0.35f, 0.65f, 0.78f), 0.006f, cabWhite, new Vector3(0.12f, 1.85f, z));
                Box("WallCab_Door", run, new Vector3(0.02f, 0.62f, 0.76f), 0.006f, cabWhite, new Vector3(-0.065f, 1.85f, z));
                Tube("Handle", run, new[] { new Vector3(-0.08f, 1.6f, z - 0.25f), new Vector3(-0.095f, 1.6f, z - 0.25f), new Vector3(-0.095f, 1.6f, z + 0.25f), new Vector3(-0.08f, 1.6f, z + 0.25f) }, 0.006f, steel);
            }
            // Glove box dispenser rack on wall
            var rack = Box("Glove_Rack", props, new Vector3(0.1f, 0.42f, 0.3f), 0.01f, cabWhite, new Vector3(RX - 0.06f, 1.35f, 0.55f));
            string[] sizes = { "S", "M", "L" }; Color[] gc = { new Color(0.55f, 0.35f, 0.75f), new Color(0.25f, 0.45f, 0.85f), new Color(0.2f, 0.65f, 0.6f) };
            for (int i = 0; i < 3; i++)
            {
                var b = Box("Glove_Box_" + sizes[i], rack.transform, new Vector3(0.085f, 0.12f, 0.24f), 0.006f, Mat("GloveBox_" + sizes[i], Color.Lerp(Color.white, gc[i], 0.35f), 0.4f), new Vector3(-0.01f, 0.13f - i * 0.13f, 0));
                Label(b.transform, $"<b>NITRILE</b>\nEXAM GLOVES · {sizes[i]}", new Vector3(-0.044f, 0, 0), new Vector3(0, 90, 0), 0.25f, new Color(0.1f, 0.12f, 0.2f), new Vector2(0.22f, 0.1f));
            }

            Model("drawer_cabinet", props, new Vector3(-2.55f, 0, 2.15f), 180, 1f);
            Model("modern_arm_chair_01", props, new Vector3(-2.3f, 0, -1.6f), 60, 1f);
            Model("metal_stool_02", props, new Vector3(1.4f, 0, -0.9f), 20, 1f);
            Model("metal_trash_can", props, new Vector3(RX - 0.3f, 0, -2.1f), 0, 1f);
            Model("fire_alarm", props, new Vector3(RX - 0.01f, 1.45f, -1.9f), -90, 1f);

            // Wall clock (lathe)
            var clock = new GameObject("Wall Clock").transform; clock.SetParent(props); clock.position = new Vector3(-0.9f, 2.25f, RZ - 0.02f); clock.rotation = Quaternion.Euler(-90, 0, 0);
            Lathe("Rim", clock, new[] { new Vector2(0, 0), new Vector2(0.16f, 0), new Vector2(0.165f, 0.035f), new Vector2(0.15f, 0.04f), new Vector2(0, 0.03f) }, Mat("Clock_Rim", new Color(0.15f, 0.17f, 0.2f), 0.6f), Vector3.zero, null, 40);
            var face = Lathe("Face", clock, new[] { new Vector2(0, 0.031f), new Vector2(0.148f, 0.031f), new Vector2(0, 0.0311f) }, Mat("Clock_Face", Color.white, 0.3f), Vector3.zero, null, 40);
            var cl = Label(clock, "12\n\n9         3\n\n6", new Vector3(0, 0.033f, 0), new Vector3(90, 0, 0), 0.5f, new Color(0.1f, 0.1f, 0.12f), new Vector2(0.27f, 0.27f));
            Box("Hand_H", clock, new Vector3(0.008f, 0.003f, 0.08f), 0.001f, Mat("Clock_Rim", Color.black), new Vector3(0.02f, 0.036f, 0.03f), new Vector3(0, 35, 0));
            Box("Hand_M", clock, new Vector3(0.005f, 0.003f, 0.12f), 0.001f, Mat("Clock_Rim", Color.black), new Vector3(-0.03f, 0.038f, 0.04f), new Vector3(0, -40, 0));

            // Posters & signage (world text on mounted panels)
            Poster(props, "HAND HYGIENE", "<b>Your 5 moments</b>\n1  Before touching a patient\n2  Before clean / aseptic procedures\n3  After body fluid exposure risk\n4  After touching a patient\n5  After touching patient surroundings", new Vector3(RX - 0.01f, 1.55f, -1.2f), -90, new Color(0.0f, 0.5f, 0.55f));
            Poster(props, "CHEST PAIN", "<b>Think time-critical</b>\n• Early 12-lead ECG\n• Repeat observations\n• Escalate concerns early\n• Follow local ACS pathway\n\n<size=70%>Educational simulation poster</size>", new Vector3(-0.2f, 1.6f, -RZ + 0.01f), 0, new Color(0.65f, 0.12f, 0.15f));
            Poster(props, "MEDSIM AI", "<b>Simulation Suite</b>\nAll patients are fictional.\nScenario content is DRAFT and not\nclinically validated.", new Vector3(-1.9f, 1.6f, -RZ + 0.01f), 0, new Color(0.04f, 0.1f, 0.18f));
        }

        static void Poster(Transform parent, string title, string body, Vector3 pos, float yaw, Color accent)
        {
            var p = new GameObject("Poster " + title).transform; p.SetParent(parent); p.position = pos; p.rotation = Quaternion.Euler(0, yaw, 0);
            Box("Frame", p, new Vector3(0.62f, 0.86f, 0.012f), 0.006f, Mat("Poster_Frame", new Color(0.85f, 0.86f, 0.88f), 0.6f, 0.5f), new Vector3(0, 0, -0.006f));
            Box("Paper", p, new Vector3(0.58f, 0.82f, 0.004f), 0.002f, Mat("Poster_Paper", new Color(0.98f, 0.98f, 0.97f), 0.2f), new Vector3(0, 0, -0.013f));
            Box("Header", p, new Vector3(0.58f, 0.14f, 0.004f), 0.002f, Mat("Poster_" + Sanitize(title), accent, 0.3f), new Vector3(0, 0.34f, -0.0145f));
            Label(p, "<b>" + title + "</b>", new Vector3(0, 0.34f, -0.018f), new Vector3(0, 0, 0), 0.75f, Color.white, new Vector2(0.55f, 0.12f));
            Label(p, body, new Vector3(0, -0.07f, -0.018f), Vector3.zero, 0.36f, new Color(0.1f, 0.12f, 0.18f), new Vector2(0.52f, 0.62f), TextAlignmentOptions.TopLeft);
            // posters face -z of their local frame; rotate content to face into the room
            p.rotation = Quaternion.Euler(0, yaw + 180, 0);
        }

        // =====================================================================================
        // Equipment
        // =====================================================================================
        static PatientMonitorController BuildPatientMonitor(Transform parent)
        {
            var dark = Mat("Device_Dark", new Color(0.14f, 0.16f, 0.19f), 0.6f);
            var white = Mat("Medical_White_Plastic", Color.white);
            var steel = Mat("Steel_Brushed", Color.white);
            var mon = new GameObject("Bedside Patient Monitor").transform; mon.SetParent(parent);
            mon.position = new Vector3(-0.55f, 1.62f, RZ - 0.35f); mon.rotation = Quaternion.LookRotation(new Vector3(-0.75f, 0.12f, 1f));
            // wall arm
            Tube("Wall_Arm", parent, new[] { new Vector3(-0.55f, 1.55f, RZ - 0.01f), new Vector3(-0.55f, 1.55f, RZ - 0.2f), new Vector3(-0.55f, 1.58f, RZ - 0.3f) }, 0.02f, steel);
            Box("Body", mon, new Vector3(0.42f, 0.32f, 0.09f), 0.02f, white, new Vector3(0, 0, 0.03f));
            Box("Bezel", mon, new Vector3(0.38f, 0.26f, 0.01f), 0.008f, dark, new Vector3(-0.0f, 0.01f, -0.016f));
            var screenGo = Part("Screen", mon, MeshKit.Quad(0.25f, 0.2f), Mat("Monitor_Screen", Color.black, 0.9f, 0, emissive: true, emit: Color.white), new Vector3(-0.065f, 0.01f, -0.0215f), Vector3.zero, true, false);
            var scrMat = new Material(Mat("Monitor_Screen", Color.white)); AssetDatabase.CreateAsset(scrMat, Gen + "/Materials/Monitor_Screen_Runtime.mat");
            scrMat.SetColor("_BaseColor", Color.white); screenGo.GetComponent<MeshRenderer>().sharedMaterial = scrMat;
            var ctl = mon.gameObject.AddComponent<PatientMonitorController>(); ctl.screen = screenGo.GetComponent<MeshRenderer>();
            // numeric column (to the right of waveforms)
            var col = new Vector3(0.118f, 0, -0.024f);
            ctl.hrText = Label(mon, "", col + new Vector3(0, 0.085f, 0), Vector3.zero, 0.34f, new Color(0.3f, 1f, 0.5f), new Vector2(0.11f, 0.05f), TextAlignmentOptions.TopRight);
            ctl.spo2Text = Label(mon, "", col + new Vector3(0, 0.03f, 0), Vector3.zero, 0.32f, new Color(0.35f, 0.85f, 1f), new Vector2(0.11f, 0.05f), TextAlignmentOptions.TopRight);
            ctl.bpText = Label(mon, "", col + new Vector3(0, -0.028f, 0), Vector3.zero, 0.26f, Color.white, new Vector2(0.11f, 0.05f), TextAlignmentOptions.TopRight);
            ctl.rrText = Label(mon, "", col + new Vector3(0, -0.077f, 0), Vector3.zero, 0.2f, new Color(1f, 0.9f, 0.3f), new Vector2(0.11f, 0.04f), TextAlignmentOptions.TopRight);
            ctl.nibpStatus = Label(mon, "", new Vector3(-0.065f, -0.103f, -0.024f), Vector3.zero, 0.15f, new Color(0.7f, 0.75f, 0.8f), new Vector2(0.25f, 0.02f), TextAlignmentOptions.Left);
            ctl.footer = Label(mon, "", new Vector3(0, -0.138f, -0.024f), Vector3.zero, 0.12f, new Color(1f, 0.7f, 0.3f), new Vector2(0.38f, 0.02f));
            Label(mon, "II", new Vector3(-0.172f, 0.095f, -0.024f), Vector3.zero, 0.16f, new Color(0.3f, 1f, 0.5f), new Vector2(0.03f, 0.02f));
            Label(mon, "Pleth", new Vector3(-0.165f, 0.028f, -0.024f), Vector3.zero, 0.14f, new Color(0.35f, 0.85f, 1f), new Vector2(0.05f, 0.02f));
            Label(mon, "Resp", new Vector3(-0.165f, -0.04f, -0.024f), Vector3.zero, 0.14f, new Color(1f, 0.9f, 0.3f), new Vector2(0.05f, 0.02f));
            // Text faces -z of monitor (towards viewer): flip labels
            foreach (var t in mon.GetComponentsInChildren<TextMeshPro>()) t.transform.localRotation = Quaternion.Euler(0, 0, 0);
            var src = mon.gameObject.AddComponent<AudioSource>(); src.spatialBlend = 1; src.minDistance = 0.5f; src.volume = 0.6f; ctl.beepSource = src;
            // fix screen orientation so the texture reads correctly
            screenGo.transform.localRotation = Quaternion.Euler(0, 0, 0);
            return ctl;
        }

        static Transform BuildTrolley(Transform parent)
        {
            var steel = Mat("Steel_Brushed", Color.white);
            var rubber = Mat("Rubber_Black", Color.black);
            var t = new GameObject("Instrument Trolley").transform; t.SetParent(parent); t.position = new Vector3(1.05f, 0, 1.45f); t.rotation = Quaternion.Euler(0, -15, 0);
            float w = 0.6f, d = 0.45f;
            var top = Box("TopShelf", t, new Vector3(w, 0.02f, d), 0.008f, steel, new Vector3(0, 0.88f, 0), collider: true);
            Box("TopShelf_Lip", t, new Vector3(w, 0.03f, 0.01f), 0.003f, steel, new Vector3(0, 0.9f, d / 2));
            Box("LowerShelf", t, new Vector3(w, 0.02f, d), 0.008f, steel, new Vector3(0, 0.3f, 0), collider: true);
            foreach (var x in new[] { -w / 2 + 0.02f, w / 2 - 0.02f }) foreach (var z in new[] { -d / 2 + 0.02f, d / 2 - 0.02f })
            {
                Tube("Leg", t, new[] { new Vector3(x, 0.09f, z), new Vector3(x, 0.89f, z) }, 0.012f, steel);
                Lathe("Castor", t, new[] { new Vector2(0, -0.02f), new Vector2(0.04f, -0.02f), new Vector2(0.042f, 0), new Vector2(0.04f, 0.02f), new Vector2(0, 0.02f) }, rubber, new Vector3(x, 0.045f, z), new Vector3(0, 0, 90), 16);
            }
            Tube("Push_Handle", t, new[] { new Vector3(-w / 2, 0.88f, -d / 2), new Vector3(-w / 2 - 0.06f, 0.95f, -d / 2), new Vector3(-w / 2 - 0.06f, 0.95f, d / 2), new Vector3(-w / 2, 0.88f, d / 2) }, 0.012f, steel);
            // sharps bin & tray props
            Box("Sharps_Bin", t, new Vector3(0.16f, 0.2f, 0.12f), 0.01f, Mat("Sharps_Yellow", new Color(0.98f, 0.82f, 0.1f), 0.5f), new Vector3(0.18f, 0.41f, 0.1f));
            Box("Sharps_Lid", t, new Vector3(0.165f, 0.03f, 0.125f), 0.01f, Mat("Sharps_Lid", new Color(0.85f, 0.15f, 0.12f), 0.5f), new Vector3(0.18f, 0.52f, 0.1f));
            Box("Kidney_Tray_Stack", t, new Vector3(0.25f, 0.04f, 0.12f), 0.03f, Mat("Pulp_Grey", new Color(0.7f, 0.68f, 0.65f), 0.1f), new Vector3(-0.12f, 0.33f, 0f));
            Model("medical_tape", t, t.TransformPoint(new Vector3(-0.22f, 0.9f, -0.15f)), 0, 1f);
            Label(top.transform, "<b>INSTRUMENTS</b>", new Vector3(0, 0.012f, -d / 2 + 0.03f), new Vector3(90, 0, 0), 0.25f, new Color(0.1f, 0.4f, 0.45f), new Vector2(0.3f, 0.04f));
            return t;
        }

        static void BuildGlovesBox(Transform shelf)
        {
            var b = Box("Glove_Box_M_Trolley", shelf, new Vector3(0.24f, 0.08f, 0.12f), 0.006f, Mat("GloveBox_M", Color.white), new Vector3(-0.15f, 0.05f, -0.12f), null, false, false);
            Label(b.transform, "<b>NITRILE</b> M", new Vector3(0, 0.0405f, 0), new Vector3(90, 0, 0), 0.25f, new Color(0.1f, 0.12f, 0.3f), new Vector2(0.2f, 0.05f));
        }

        static TMP_Text BuildBPDevice(Transform trolley)
        {
            var white = Mat("Medical_White_Plastic", Color.white);
            var dark = Mat("Device_Dark", Color.black);
            var dev = new GameObject("BP Monitor (NIBP)").transform; dev.SetParent(trolley, false); dev.localPosition = new Vector3(0.18f, 0.97f, 0.15f); dev.localRotation = Quaternion.Euler(-20, 180, 0);
            Box("Body", dev, new Vector3(0.18f, 0.13f, 0.09f), 0.015f, white, Vector3.zero, null, false, false);
            Box("LCD", dev, new Vector3(0.13f, 0.07f, 0.004f), 0.003f, Mat("LCD_Green", new Color(0.55f, 0.65f, 0.55f), 0.8f), new Vector3(0, 0.015f, -0.045f), null, false, false);
            Box("Button", dev, new Vector3(0.03f, 0.015f, 0.006f), 0.004f, Mat("Button_Teal", new Color(0f, 0.6f, 0.55f), 0.5f), new Vector3(0.045f, -0.042f, -0.045f), null, false, false);
            Label(dev, "<b>NIBP</b>", new Vector3(-0.05f, -0.042f, -0.048f), new Vector3(0, 0, 0), 0.14f, new Color(0.2f, 0.25f, 0.3f), new Vector2(0.05f, 0.015f));
            var t = Label(dev, "--/--", new Vector3(0, 0.015f, -0.0485f), Vector3.zero, 0.42f, new Color(0.05f, 0.1f, 0.08f), new Vector2(0.12f, 0.065f));
            foreach (var lbl in dev.GetComponentsInChildren<TextMeshPro>()) lbl.transform.localRotation = Quaternion.Euler(0, 180, 0);
            return t;
        }

        static ECGMachineController BuildECGCart(Transform parent)
        {
            var white = Mat("Medical_White_Plastic", Color.white);
            var grey = Mat("Bed_Grey_Plastic", Color.grey);
            var dark = Mat("Device_Dark", Color.black);
            var steel = Mat("Steel_Brushed", Color.white);
            var c = new GameObject("ECG Machine Cart").transform; c.SetParent(parent); c.position = new Vector3(1.45f, 0, -0.35f); c.rotation = Quaternion.Euler(0, 166, 0);
            Box("Base", c, new Vector3(0.5f, 0.06f, 0.45f), 0.02f, grey, new Vector3(0, 0.1f, 0));
            Tube("Column", c, new[] { new Vector3(0, 0.12f, 0.1f), new Vector3(0, 0.82f, 0.1f) }, 0.03f, steel);
            foreach (var x in new[] { -0.2f, 0.2f }) foreach (var z in new[] { -0.18f, 0.18f })
                Lathe("Castor", c, new[] { new Vector2(0, -0.02f), new Vector2(0.035f, -0.02f), new Vector2(0.037f, 0), new Vector2(0.035f, 0.02f), new Vector2(0, 0.02f) }, Mat("Rubber_Black", Color.black), new Vector3(x, 0.04f, z), new Vector3(0, 0, 90), 14);
            var body = Box("ECG_Body", c, new Vector3(0.42f, 0.09f, 0.32f), 0.02f, white, new Vector3(0, 0.88f, 0), collider: true);
            var screenPivot = new GameObject("Screen_Pivot").transform; screenPivot.SetParent(c, false); screenPivot.localPosition = new Vector3(0, 0.93f, 0.13f); screenPivot.localRotation = Quaternion.Euler(15, 0, 0);
            Box("Screen_Bezel", screenPivot, new Vector3(0.4f, 0.24f, 0.025f), 0.01f, white, new Vector3(0, 0.12f, 0));
            var scr = Part("ECG_Screen", screenPivot, MeshKit.Quad(0.36f, 0.2f), Mat("ECG_Screen_Off", new Color(0.05f, 0.06f, 0.07f), 0.9f), new Vector3(0, 0.12f, -0.0135f), new Vector3(0, 0, 0), true, false);
            var scrMat = new Material(Lit); scrMat.SetColor("_BaseColor", new Color(0.9f, 0.9f, 0.9f)); scrMat.EnableKeyword("_EMISSION"); scrMat.SetColor("_EmissionColor", Color.white * 0.6f);
            AssetDatabase.CreateAsset(scrMat, Gen + "/Materials/ECG_Screen_Runtime.mat"); scr.GetComponent<MeshRenderer>().sharedMaterial = scrMat;
            // Lead cable bundle
            Tube("Lead_Cable", c, new[] { new Vector3(0.2f, 0.88f, -0.1f), new Vector3(0.28f, 0.8f, -0.14f), new Vector3(0.27f, 0.6f, -0.12f), new Vector3(0.2f, 0.55f, -0.05f), new Vector3(0.15f, 0.7f, 0.0f) }, 0.006f, Mat("Cable_Grey", new Color(0.75f, 0.77f, 0.8f), 0.4f));
            // Record button (XR simple interactable)
            var btn = Box("RECORD_Button", c, new Vector3(0.1f, 0.025f, 0.06f), 0.01f, Mat("Button_Teal", new Color(0f, 0.6f, 0.55f)), new Vector3(0.1f, 0.94f, -0.1f), null, true, false);
            var sx = btn.AddComponent<XRSimpleInteractable>();
            Label(btn.transform, "<b>RECORD</b>", new Vector3(0, 0.0135f, 0), new Vector3(90, 0, 0), 0.17f, Color.white, new Vector2(0.12f, 0.03f));
            var status = Label(c, "READY · Press RECORD", new Vector3(-0.06f, 0.926f, -0.12f), new Vector3(90, 0, 0), 0.16f, new Color(0.1f, 0.3f, 0.35f), new Vector2(0.22f, 0.03f));
            Label(c, "<b>12-LEAD ECG</b>", new Vector3(0, 0.88f, -0.161f), new Vector3(0, 0, 0), 0.25f, new Color(0.1f, 0.3f, 0.35f), new Vector2(0.3f, 0.04f)).transform.localRotation = Quaternion.identity;
            // Printout (grabbable sheet)
            var paper = Mat("ECG_Paper_Runtime", Color.white, 0.2f);
            var print = Part("ECG_Printout", c, MeshKit.Quad(0.28f, 0.14f), paper, new Vector3(-0.06f, 0.937f, -0.02f), new Vector3(90, 0, 0), true, false); print.GetComponent<MeshRenderer>().sharedMaterial.SetFloat("_Cull", 0);
            var prb = print.AddComponent<Rigidbody>(); prb.isKinematic = true; prb.useGravity = false;
            var pbc = print.AddComponent<BoxCollider>(); pbc.size = new Vector3(0.28f, 0.14f, 0.01f);
            var pg = print.AddComponent<XRGrabInteractable>(); pg.throwOnDetach = false; pg.movementType = XRBaseInteractable.MovementType.Instantaneous;
            var ctl = c.gameObject.AddComponent<ECGMachineController>();
            ctl.recordButton = sx; ctl.screen = scr.GetComponent<MeshRenderer>(); ctl.printout = print; ctl.printoutRenderer = print.GetComponent<MeshRenderer>(); ctl.statusText = status;
            var src = c.gameObject.AddComponent<AudioSource>(); src.spatialBlend = 1; ctl.sfx = src;
            return ctl;
        }

        static void BuildWallEquipment(Transform parent)
        {
            var white = Mat("Medical_White_Plastic", Color.white);
            var steel = Mat("Steel_Brushed", Color.white);
            // Medical gas / bedhead service panel
            var bh = new GameObject("Bedhead Services Panel").transform; bh.SetParent(parent); bh.position = new Vector3(0.3f, 1.45f, RZ - 0.04f);
            Box("Trunking", bh, new Vector3(2.2f, 0.26f, 0.07f), 0.02f, white, Vector3.zero);
            string[] gases = { "O2", "AIR", "VAC" }; Color[] gcol = { Color.white, new Color(0.15f, 0.15f, 0.15f), new Color(0.98f, 0.85f, 0.2f) };
            for (int i = 0; i < 3; i++)
            {
                var o = Lathe("Gas_Outlet_" + gases[i], bh, new[] { new Vector2(0, 0), new Vector2(0.03f, 0), new Vector2(0.028f, 0.02f), new Vector2(0.012f, 0.025f), new Vector2(0, 0.025f) }, Mat("GasRing_" + i, gcol[i], 0.5f), new Vector3(-0.75f + i * 0.13f, 0.02f, -0.035f), new Vector3(-90, 0, 0), 20);
                Label(bh, gases[i], new Vector3(-0.75f + i * 0.13f, -0.07f, -0.037f), Vector3.zero, 0.2f, new Color(0.1f, 0.15f, 0.2f), new Vector2(0.08f, 0.03f));
            }
            for (int i = 0; i < 4; i++) Box("Socket", bh, new Vector3(0.08f, 0.08f, 0.012f), 0.008f, Mat("Socket_White", new Color(0.97f, 0.97f, 0.97f), 0.6f), new Vector3(0.45f + i * 0.11f, 0.02f, -0.04f));
            Box("Reading_Light_Strip", bh, new Vector3(1.6f, 0.02f, 0.01f), 0.004f, Mat("LED_Panel", Color.white), new Vector3(0, 0.12f, -0.037f));
            // O2 flowmeter
            var fm = Lathe("O2_Flowmeter", bh, new[] { new Vector2(0, 0), new Vector2(0.018f, 0), new Vector2(0.018f, 0.12f), new Vector2(0.022f, 0.13f), new Vector2(0, 0.14f) }, Mat("Glass", Color.clear), new Vector3(-0.75f, -0.02f, -0.07f), null, 16);
            // Wall-mounted aneroid sphyg (decor)
            var an = new GameObject("Wall Aneroid").transform; an.SetParent(parent); an.position = new Vector3(1.15f, 1.55f, RZ - 0.02f); an.rotation = Quaternion.Euler(-90, 0, 0);
            Lathe("Gauge", an, new[] { new Vector2(0, 0), new Vector2(0.075f, 0), new Vector2(0.078f, 0.03f), new Vector2(0.07f, 0.035f), new Vector2(0, 0.03f) }, steel, Vector3.zero, null, 32);
            Lathe("Dial", an, new[] { new Vector2(0, 0.031f), new Vector2(0.068f, 0.031f), new Vector2(0, 0.0315f) }, Mat("Clock_Face", Color.white), Vector3.zero, null, 32);
            Tube("Coil_Hose", parent, new[] { new Vector3(1.15f, 1.47f, RZ - 0.03f), new Vector3(1.18f, 1.3f, RZ - 0.05f), new Vector3(1.1f, 1.15f, RZ - 0.05f), new Vector3(1.2f, 1.0f, RZ - 0.05f), new Vector3(1.12f, 0.9f, RZ - 0.04f) }, 0.005f, Mat("Rubber_Black", Color.black));
            // IV stand (decor)
            var iv = new GameObject("IV Stand").transform; iv.SetParent(parent); iv.position = new Vector3(-0.55f, 0, 1.55f);
            Tube("Pole", iv, new[] { new Vector3(0, 0.12f, 0), new Vector3(0, 1.95f, 0) }, 0.013f, steel);
            for (int i = 0; i < 5; i++) { float a = i * 72 * Mathf.Deg2Rad; Tube("Base_Leg", iv, new[] { new Vector3(0, 0.12f, 0), new Vector3(Mathf.Cos(a) * 0.3f, 0.08f, Mathf.Sin(a) * 0.3f) }, 0.012f, steel); Lathe("Castor", iv, new[] { new Vector2(0, -0.02f), new Vector2(0.03f, -0.02f), new Vector2(0.031f, 0), new Vector2(0.03f, 0.02f), new Vector2(0, 0.02f) }, Mat("Rubber_Black", Color.black), new Vector3(Mathf.Cos(a) * 0.3f, 0.035f, Mathf.Sin(a) * 0.3f), new Vector3(0, 0, 90), 12); }
            Tube("Hooks", iv, new[] { new Vector3(-0.12f, 2.0f, 0), new Vector3(-0.1f, 1.95f, 0), new Vector3(0, 1.95f, 0), new Vector3(0.1f, 1.95f, 0), new Vector3(0.12f, 2.0f, 0) }, 0.006f, steel);
            var bag = Part("Saline_Bag", iv, MeshKit.RoundedBox(new Vector3(0.13f, 0.22f, 0.035f), 0.017f, 3), Mat("IV_Bag", new Color(0.92f, 0.95f, 0.98f, 0.55f), 0.9f, 0, transparent: true), new Vector3(0.1f, 1.8f, 0));
            Label(bag.transform, "<b>SODIUM CHLORIDE</b>\n0.9%\n<size=70%>DEMO PROP</size>", new Vector3(0, 0, -0.019f), Vector3.zero, 0.12f, new Color(0.1f, 0.3f, 0.6f), new Vector2(0.11f, 0.1f)).transform.localRotation = Quaternion.Euler(0, 180, 0);
            Tube("Giving_Set_Coiled", iv, new[] { new Vector3(0.1f, 1.68f, 0), new Vector3(0.1f, 1.4f, 0.02f), new Vector3(0.04f, 1.25f, 0.02f), new Vector3(0.0f, 1.3f, 0.02f) }, 0.0025f, Mat("Glass", Color.clear));
        }

        static SanitizerDispenser BuildSanitizer(Transform parent)
        {
            var s = new GameObject("Hand Sanitiser Dispenser").transform; s.SetParent(parent); s.position = new Vector3(RX - 0.06f, 1.25f, -1.55f); s.rotation = Quaternion.Euler(0, -90, 0);
            Box("Body", s, new Vector3(0.12f, 0.24f, 0.1f), 0.02f, Mat("Medical_White_Plastic", Color.white), Vector3.zero);
            Box("Window", s, new Vector3(0.05f, 0.08f, 0.002f), 0.005f, Mat("Sanitiser_Gel", new Color(0.4f, 0.75f, 0.9f), 0.8f), new Vector3(0, 0.04f, -0.05f));
            var lever = Box("Push_Lever", s, new Vector3(0.1f, 0.06f, 0.03f), 0.012f, Mat("Button_Teal", Color.white), new Vector3(0, -0.08f, -0.055f), null, true, false);
            Label(s, "<b>ALCOHOL HAND RUB</b>\n<size=70%>Clean hands save lives</size>", new Vector3(0, 0.1f, -0.051f), new Vector3(0, 0, 0), 0.12f, new Color(0f, 0.4f, 0.45f), new Vector2(0.11f, 0.04f)).transform.localRotation = Quaternion.Euler(0, 0, 0);
            var si = lever.AddComponent<XRSimpleInteractable>();
            var d = s.gameObject.AddComponent<SanitizerDispenser>(); d.trigger = si; d.sfx = s.gameObject.AddComponent<AudioSource>(); d.sfx.spatialBlend = 1;
            return d;
        }

        static Transform BuildTabletStand(Transform env)
        {
            var steel = Mat("Steel_Brushed", Color.white);
            var t = new GameObject("Clinical Tablet Stand").transform; t.SetParent(env); t.position = new Vector3(1.6f, 0, 0.15f);
            Tube("Pole", t, new[] { new Vector3(0, 0.05f, 0), new Vector3(0, 1.05f, 0) }, 0.015f, steel);
            Lathe("Base", t, new[] { new Vector2(0, 0), new Vector2(0.18f, 0), new Vector2(0.17f, 0.03f), new Vector2(0.02f, 0.05f), new Vector2(0, 0.05f) }, Mat("Device_Dark", Color.black), Vector3.zero, null, 32);
            var holder = Box("Tablet_Body", t, new Vector3(0.49f, 0.34f, 0.02f), 0.015f, Mat("Device_Dark", Color.black), new Vector3(0, 1.18f, 0), null, false, false);
            holder.transform.rotation = Quaternion.LookRotation(new Vector3(0.55f, 0.75f, 0.65f) * -1, Vector3.up);
            holder.transform.localRotation = Quaternion.Euler(35, 141, 0);
            var anchor = new GameObject("Tablet UI Anchor").transform; anchor.SetParent(holder.transform, false); anchor.localPosition = new Vector3(0, 0, -0.0115f); anchor.localRotation = Quaternion.identity;
            return anchor;
        }

        // =====================================================================================
        // Instruments
        // =====================================================================================
        static T SetupGrab<T>(GameObject go, Vector3 colliderSize, Vector3 colliderCenter) where T : MedicalInstrumentBase
        {
            var rb = go.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false; rb.interpolation = RigidbodyInterpolation.Interpolate;
            var bc = go.AddComponent<BoxCollider>(); bc.size = colliderSize; bc.center = colliderCenter;
            var g = go.AddComponent<XRGrabInteractable>();
            g.movementType = XRBaseInteractable.MovementType.Instantaneous; g.throwOnDetach = false; g.retainTransformParent = true; g.useDynamicAttach = true;
            var inst = go.AddComponent<T>();
            var src = go.AddComponent<AudioSource>(); src.spatialBlend = 1; src.playOnAwake = false; inst.sfx = src;
            return inst;
        }

        static StethoscopeController BuildStethoscope(Transform parent, Vector3 pos)
        {
            var steel = Mat("Steel_Polished", new Color(0.85f, 0.86f, 0.88f), 0.92f, 1f);
            var tubing = Mat("Stethoscope_Tubing", new Color(0.05f, 0.18f, 0.32f), 0.55f);
            var go = new GameObject("Stethoscope"); go.transform.SetParent(parent); go.transform.position = pos; go.transform.rotation = Quaternion.Euler(0, 30, 0);
            // Chest piece (diaphragm) at origin; disc normal = -Y
            Lathe("Diaphragm", go.transform, new[] { new Vector2(0, -0.006f), new Vector2(0.021f, -0.006f), new Vector2(0.022f, 0.0f), new Vector2(0.02f, 0.006f), new Vector2(0.009f, 0.01f), new Vector2(0.006f, 0.02f), new Vector2(0, 0.022f) }, steel, Vector3.zero, null, 32, false);
            Lathe("Diaphragm_Membrane", go.transform, new[] { new Vector2(0, -0.0065f), new Vector2(0.019f, -0.0065f), new Vector2(0, -0.0066f) }, Mat("Membrane", new Color(0.15f, 0.15f, 0.16f), 0.7f), Vector3.zero, null, 32, false);
            Tube("Stem", go.transform, new[] { new Vector3(0, 0.02f, 0), new Vector3(0.03f, 0.025f, 0) }, 0.0035f, steel, false, 10);
            Tube("Tubing", go.transform, new[] { new Vector3(0.03f, 0.025f, 0), new Vector3(0.12f, 0.03f, 0.02f), new Vector3(0.2f, 0.02f, 0.1f), new Vector3(0.15f, 0.015f, 0.2f), new Vector3(0.05f, 0.015f, 0.22f), new Vector3(0.0f, 0.02f, 0.16f) }, 0.0045f, tubing, false, 10);
            Tube("Binaural_L", go.transform, new[] { new Vector3(0.0f, 0.02f, 0.16f), new Vector3(-0.05f, 0.015f, 0.12f), new Vector3(-0.1f, 0.012f, 0.1f), new Vector3(-0.12f, 0.012f, 0.13f) }, 0.0028f, steel, false, 8);
            Tube("Binaural_R", go.transform, new[] { new Vector3(0.0f, 0.02f, 0.16f), new Vector3(-0.04f, 0.015f, 0.2f), new Vector3(-0.09f, 0.012f, 0.23f), new Vector3(-0.12f, 0.012f, 0.21f) }, 0.0028f, steel, false, 8);
            foreach (var p in new[] { new Vector3(-0.122f, 0.012f, 0.13f), new Vector3(-0.122f, 0.012f, 0.21f) })
                Part("Eartip", go.transform, MeshKit.Ellipsoid(Vector3.one * 0.0065f, 12, 8), Mat("Rubber_Black", Color.black), p, null, true, false);
            var s = SetupGrab<StethoscopeController>(go, new Vector3(0.05f, 0.035f, 0.05f), new Vector3(0, 0.008f, 0));
            var col = go.AddComponent<BoxCollider>(); col.size = new Vector3(0.3f, 0.03f, 0.25f); col.center = new Vector3(0.04f, 0.015f, 0.12f);
            var attach = new GameObject("Attach").transform; attach.SetParent(go.transform, false); attach.localPosition = new Vector3(0, 0.02f, 0); attach.localRotation = Quaternion.Euler(90, 0, 0);
            go.GetComponent<XRGrabInteractable>().attachTransform = attach; go.GetComponent<XRGrabInteractable>().useDynamicAttach = false;
            var probe = new GameObject("Probe").transform; probe.SetParent(go.transform, false); probe.localPosition = new Vector3(0, -0.007f, 0);
            s.probePoint = probe; s.probeAxis = Vector3.down; s.probeRadius = 0.04f;
            return s;
        }

        static BloodPressureController BuildCuff(Transform parent, Vector3 pos, TMP_Text display, PatientMonitorController monitor)
        {
            var cuffMat = Mat("BP_Cuff_Nylon", new Color(0.08f, 0.24f, 0.45f), 0.25f, 0, "terry_cloth", new Vector2(14, 14), diffuse: false);
            var go = new GameObject("Blood Pressure Cuff"); go.transform.SetParent(parent); go.transform.position = pos; go.transform.rotation = Quaternion.Euler(0, 0, 90);
            var vis = new GameObject("Cuff_Visual").transform; vis.SetParent(go.transform, false);
            // Wrapped cuff band (lathe ring around local Y = arm axis)
            Lathe("Cuff_Band", vis, new[] { new Vector2(0.052f, -0.07f), new Vector2(0.06f, -0.072f), new Vector2(0.064f, -0.06f), new Vector2(0.066f, 0.0f), new Vector2(0.064f, 0.06f), new Vector2(0.06f, 0.072f), new Vector2(0.052f, 0.07f), new Vector2(0.05f, 0.0f), new Vector2(0.052f, -0.07f) }, cuffMat, Vector3.zero, null, 32, false);
            Box("Velcro_Strip", vis, new Vector3(0.004f, 0.12f, 0.05f), 0.002f, Mat("Velcro_Grey", new Color(0.55f, 0.57f, 0.6f), 0.1f), new Vector3(0.066f, 0, 0), null, false, false);
            Box("Artery_Marker", vis, new Vector3(0.002f, 0.03f, 0.012f), 0.001f, Mat("Marker_White", Color.white, 0.3f), new Vector3(0, 0, -0.0665f), null, false, false);
            Tube("Hose", go.transform, new[] { new Vector3(0.06f, 0.06f, 0), new Vector3(0.1f, 0.09f, 0.02f), new Vector3(0.12f, 0.15f, 0.02f), new Vector3(0.1f, 0.2f, 0.0f) }, 0.004f, Mat("Cable_Grey", Color.grey), false, 8);
            var c = SetupGrab<BloodPressureController>(go, new Vector3(0.14f, 0.15f, 0.14f), Vector3.zero);
            c.deviceDisplay = display; c.monitor = monitor; c.cuffVisual = vis; c.probePoint = go.transform; c.probeRadius = 0.07f;
            c.pumpLoop = null;
            return c;
        }

        static PulseOximeterController BuildOximeter(Transform parent, Vector3 pos, PatientMonitorController monitor)
        {
            var go = new GameObject("Pulse Oximeter"); go.transform.SetParent(parent); go.transform.position = pos;
            Box("Clip_Top", go.transform, new Vector3(0.035f, 0.016f, 0.058f), 0.007f, Mat("Oximeter_White", new Color(0.95f, 0.96f, 0.97f), 0.6f), new Vector3(0, 0.012f, 0), null, false, false);
            Box("Clip_Bottom", go.transform, new Vector3(0.034f, 0.014f, 0.055f), 0.006f, Mat("Oximeter_Teal", new Color(0.0f, 0.55f, 0.55f), 0.5f), new Vector3(0, -0.006f, 0), null, false, false);
            Box("Screen", go.transform, new Vector3(0.024f, 0.001f, 0.03f), 0.0008f, Mat("Device_Dark", Color.black), new Vector3(0, 0.0205f, -0.004f), null, false, false);
            var t = Label(go.transform, "", new Vector3(0, 0.0215f, -0.004f), new Vector3(90, 0, 0), 0.06f, new Color(0.35f, 0.9f, 1f), new Vector2(0.024f, 0.03f));
            var c = SetupGrab<PulseOximeterController>(go, new Vector3(0.036f, 0.034f, 0.06f), new Vector3(0, 0.003f, 0));
            c.deviceDisplay = t; c.monitor = monitor; c.probeRadius = 0.035f;
            return c;
        }

        static AudioSource CreateEarpiece(Transform head)
        {
            var e = new GameObject("Stethoscope Earpiece Audio"); e.transform.SetParent(head, false);
            var a = e.AddComponent<AudioSource>(); a.spatialBlend = 0; a.volume = 0.9f; a.playOnAwake = false; return a;
        }

        // =====================================================================================
        // Characters (procedural placeholder humanoids — see KNOWN_LIMITATIONS.md)
        // =====================================================================================
        class Rig { public Transform root, hips, spine, chest, neck, head, jaw, lUpperArm, lLowerArm, lHand, rUpperArm, rLowerArm, rHand, lUpLeg, lLeg, rUpLeg, rLeg; public Transform mouth; public Transform[] lids; }

        static Rig BuildHuman(string name, Transform parent, Color skin, Color top, Color bottom, Color hair, bool coat, bool gown, float height = 1.76f, bool longHair = false)
        {
            float k = height / 1.76f;
            var mSkin = Mat("Skin_" + name, skin, 0.38f);
            var mTop = Mat("Top_" + name, top, 0.15f, 0, gown ? "terry_cloth" : null, new Vector2(10, 10), diffuse: false);
            var mBottom = Mat("Bottom_" + name, bottom, 0.15f);
            var mHair = Mat("Hair_" + name, hair, 0.35f);
            var mCoat = Mat("LabCoat", new Color(0.96f, 0.97f, 0.98f), 0.18f, 0, "terry_cloth", new Vector2(14, 14), diffuse: false);
            var mShoe = Mat("Shoes_" + name, new Color(0.12f, 0.12f, 0.14f), 0.55f);
            var mEye = Mat("Eye_White", new Color(0.95f, 0.95f, 0.93f), 0.85f);
            var mIris = Mat("Eye_Iris", new Color(0.18f, 0.12f, 0.08f), 0.9f);
            var mLip = Mat("Lips_" + name, Color.Lerp(skin, new Color(0.55f, 0.25f, 0.25f), 0.45f), 0.5f);
            var mMouth = Mat("Mouth_Dark", new Color(0.18f, 0.05f, 0.06f), 0.3f);

            var r = new Rig();
            r.root = new GameObject(name).transform; r.root.SetParent(parent);
            Transform Bone(string n, Transform p, Vector3 lp) { var b = new GameObject(n).transform; b.SetParent(p, false); b.localPosition = lp * k; return b; }
            Mesh Limb(float r0, float r1, float len) => MeshKit.Lathe(new[] { new Vector2(0, 0.0f), new Vector2(r0 * 0.7f, -0.005f), new Vector2(r0, -0.03f), new Vector2((r0 + r1) / 2, -len / 2), new Vector2(r1, -len + 0.02f), new Vector2(r1 * 0.6f, -len), new Vector2(0, -len - 0.005f) }, 20);
            GameObject Seg(string n, Transform b, Mesh m, Material mat, Vector3 pos, Vector3 scale) { var g = Part(n, b, m, mat, pos * k, null, true, false); g.transform.localScale = scale * k; return g; }

            r.hips = Bone("Hips", r.root, new Vector3(0, 0.95f, 0));
            r.spine = Bone("Spine", r.hips, new Vector3(0, 0.1f, 0));
            r.chest = Bone("Chest", r.spine, new Vector3(0, 0.18f, 0));
            r.neck = Bone("Neck", r.chest, new Vector3(0, 0.2f, 0.0f));
            r.head = Bone("Head", r.neck, new Vector3(0, 0.08f, 0.01f));

            // Torso: pelvis + abdomen + chest as elliptical lathes
            var pelvis = MeshKit.Lathe(new[] { new Vector2(0, -0.1f), new Vector2(0.15f, -0.08f), new Vector2(0.17f, 0.0f), new Vector2(0.15f, 0.1f), new Vector2(0, 0.12f) }, 24);
            Seg("Pelvis", r.hips, pelvis, gown ? mTop : mBottom, new Vector3(0, 0, 0), new Vector3(1f, 1, 0.68f));
            var abdomen = MeshKit.Lathe(new[] { new Vector2(0, -0.02f), new Vector2(0.15f, 0.0f), new Vector2(0.145f, 0.1f), new Vector2(0.155f, 0.18f), new Vector2(0, 0.2f) }, 24);
            Seg("Abdomen", r.spine, abdomen, mTop, Vector3.zero, new Vector3(1f, 1, 0.66f));
            var chestM = MeshKit.Lathe(new[] { new Vector2(0, -0.02f), new Vector2(0.16f, 0.0f), new Vector2(0.18f, 0.08f), new Vector2(0.18f, 0.15f), new Vector2(0.14f, 0.2f), new Vector2(0.06f, 0.225f), new Vector2(0, 0.23f) }, 28);
            Seg("Chest", r.chest, chestM, mTop, Vector3.zero, new Vector3(1.02f, 1, 0.64f));
            Seg("Neck", r.neck, MeshKit.Lathe(new[] { new Vector2(0, -0.02f), new Vector2(0.055f, -0.02f), new Vector2(0.05f, 0.05f), new Vector2(0.05f, 0.1f), new Vector2(0, 0.1f) }, 18), mSkin, new Vector3(0, -0.02f, 0), Vector3.one);
            if (coat)
            {
                Seg("Coat_Torso", r.chest, MeshKit.Lathe(new[] { new Vector2(0.0f, -0.36f), new Vector2(0.2f, -0.6f), new Vector2(0.19f, -0.2f), new Vector2(0.175f, 0.0f), new Vector2(0.19f, 0.09f), new Vector2(0.19f, 0.15f), new Vector2(0.15f, 0.205f), new Vector2(0.07f, 0.235f), new Vector2(0, 0.24f) }, 28), mCoat, Vector3.zero, new Vector3(1.05f, 1, 0.72f));
                Seg("Coat_Lapel_L", r.chest, MeshKit.RoundedBox(new Vector3(0.05f, 0.22f, 0.01f), 0.004f, 2), mCoat, new Vector3(-0.06f, 0.1f, 0.118f), Vector3.one).transform.localRotation = Quaternion.Euler(-8, 0, -12);
                Seg("Coat_Lapel_R", r.chest, MeshKit.RoundedBox(new Vector3(0.05f, 0.22f, 0.01f), 0.004f, 2), mCoat, new Vector3(0.06f, 0.1f, 0.118f), Vector3.one).transform.localRotation = Quaternion.Euler(-8, 0, 12);
                var badge = Seg("ID_Badge", r.chest, MeshKit.RoundedBox(new Vector3(0.055f, 0.08f, 0.004f), 0.004f, 2), Mat("Badge", Color.white, 0.6f), new Vector3(0.09f, 0.09f, 0.122f), Vector3.one);
                Label(badge.transform, "<b>DR. S. HASSAN</b>\n<size=70%>EMERGENCY MEDICINE\nSIMULATION FACULTY</size>", new Vector3(0, 0, 0.0025f), new Vector3(0, 180, 0), 0.05f, new Color(0.04f, 0.1f, 0.18f), new Vector2(0.05f, 0.06f));
                Seg("Pocket_Pen", r.chest, MeshKit.Tube(new[] { Vector3.zero, new Vector3(0, 0.12f, 0) }, 0.004f, 8, 1), Mat("Pen_Blue", new Color(0.1f, 0.2f, 0.6f), 0.7f), new Vector3(-0.1f, 0.06f, 0.115f), Vector3.one);
                // Stethoscope draped around neck (decor)
                Seg("Neck_Stethoscope", r.chest, MeshKit.Tube(new[] { new Vector3(-0.07f, 0.0f, 0.11f), new Vector3(-0.08f, 0.12f, 0.1f), new Vector3(-0.06f, 0.21f, 0.02f), new Vector3(0, 0.235f, -0.05f), new Vector3(0.06f, 0.21f, 0.02f), new Vector3(0.08f, 0.12f, 0.1f), new Vector3(0.07f, 0.03f, 0.115f) }, 0.0045f, 10, 6), Mat("Stethoscope_Tubing", Color.black), Vector3.zero, Vector3.one);
            }
            if (gown)
                Seg("Gown_Neckline", r.chest, MeshKit.Lathe(new[] { new Vector2(0.06f, 0.2f), new Vector2(0.075f, 0.215f), new Vector2(0.07f, 0.225f), new Vector2(0.055f, 0.22f) }, 24), mTop, Vector3.zero, new Vector3(1.1f, 1, 0.8f));

            // Head
            var headM = MeshKit.Ellipsoid(new Vector3(0.078f, 0.11f, 0.095f), 32, 22);
            Seg("Head", r.head, headM, mSkin, new Vector3(0, 0.1f, 0.0f), Vector3.one);
            r.jaw = Bone("Jaw", r.head, new Vector3(0, 0.055f, -0.01f));
            Seg("Jaw_Chin", r.jaw, MeshKit.Ellipsoid(new Vector3(0.06f, 0.045f, 0.07f), 24, 14), mSkin, new Vector3(0, -0.015f, 0.025f), Vector3.one);
            var mouth = Seg("Mouth", r.head, MeshKit.Ellipsoid(new Vector3(0.022f, 0.006f, 0.008f), 16, 8), mMouth, new Vector3(0, 0.053f, 0.088f), Vector3.one); r.mouth = mouth.transform;
            Seg("Lip_Upper", r.head, MeshKit.Ellipsoid(new Vector3(0.024f, 0.0045f, 0.008f), 16, 8), mLip, new Vector3(0, 0.059f, 0.09f), Vector3.one);
            Seg("Lip_Lower", r.jaw, MeshKit.Ellipsoid(new Vector3(0.022f, 0.005f, 0.008f), 16, 8), mLip, new Vector3(0, -0.009f, 0.098f), Vector3.one);
            Seg("Nose", r.head, MeshKit.Lathe(new[] { new Vector2(0, 0.0f), new Vector2(0.012f, 0.0f), new Vector2(0.016f, 0.012f), new Vector2(0.008f, 0.04f), new Vector2(0, 0.045f) }, 16), mSkin, new Vector3(0, 0.072f, 0.09f), Vector3.one).transform.localRotation = Quaternion.Euler(-15, 0, 0);
            foreach (var side in new[] { -1, 1 })
            {
                Seg("Ear", r.head, MeshKit.Ellipsoid(new Vector3(0.008f, 0.028f, 0.018f), 12, 8), mSkin, new Vector3(side * 0.078f, 0.105f, 0f), Vector3.one);
                Seg("Eye", r.head, MeshKit.Ellipsoid(Vector3.one * 0.0115f, 16, 12), mEye, new Vector3(side * 0.031f, 0.118f, 0.078f), Vector3.one);
                Seg("Iris", r.head, MeshKit.Ellipsoid(new Vector3(0.0058f, 0.0058f, 0.003f), 14, 8), mIris, new Vector3(side * 0.031f, 0.118f, 0.0893f), Vector3.one);
                Seg("Brow", r.head, MeshKit.RoundedBox(new Vector3(0.03f, 0.005f, 0.008f), 0.0024f, 2), mHair, new Vector3(side * 0.031f, 0.137f, 0.086f), Vector3.one).transform.localRotation = Quaternion.Euler(0, 0, side * -6);
            }
            var lids = new List<Transform>();
            foreach (var side in new[] { -1, 1 })
            {
                var lidPivot = Bone("Eyelid", r.head, new Vector3(side * 0.031f, 0.13f, 0.08f));
                var lid = Seg("Lid", lidPivot, MeshKit.Ellipsoid(new Vector3(0.0125f, 0.012f, 0.0105f), 16, 10), mSkin, new Vector3(0, -0.012f, 0), new Vector3(1, 1, 1));
                lidPivot.localScale = new Vector3(1, 0.12f, 1); // mostly open; HumanoidLifeController scales Y towards 1 to blink
                lids.Add(lidPivot);
            }
            // invert blink logic: lid scale Y 0.12 open → we drive it via life controller (scale reduces toward 0); keep visible lid cap
            r.lids = lids.ToArray();
            Seg("Hair", r.head, MeshKit.Ellipsoid(new Vector3(0.083f, 0.085f, 0.1f), 28, 18), mHair, new Vector3(0, 0.135f, -0.008f), Vector3.one);
            if (longHair) { Seg("Hair_Bob", r.head, MeshKit.Ellipsoid(new Vector3(0.088f, 0.09f, 0.075f), 28, 18), mHair, new Vector3(0, 0.08f, -0.035f), Vector3.one); Seg("Hair_Bun", r.head, MeshKit.Ellipsoid(new Vector3(0.04f, 0.035f, 0.035f), 16, 12), mHair, new Vector3(0, 0.12f, -0.1f), Vector3.one); }

            // Arms (hang down)
            foreach (var side in new[] { -1, 1 })
            {
                var shoulder = Bone(side < 0 ? "L_Shoulder" : "R_Shoulder", r.chest, new Vector3(side * 0.175f, 0.19f, 0));
                var ua = Bone(side < 0 ? "L_UpperArm" : "R_UpperArm", shoulder, Vector3.zero);
                Seg("UpperArm", ua, Limb(0.047f, 0.038f, 0.29f), coat ? mCoat : (gown ? mTop : mSkin), Vector3.zero, new Vector3(1, 1, 1));
                var la = Bone(side < 0 ? "L_LowerArm" : "R_LowerArm", ua, new Vector3(0, -0.29f, 0));
                Seg("LowerArm", la, Limb(0.037f, 0.028f, 0.25f), coat ? mCoat : mSkin, Vector3.zero, Vector3.one);
                if (coat) Seg("Cuff_Skin", la, Limb(0.029f, 0.026f, 0.05f), mSkin, new Vector3(0, -0.22f, 0), Vector3.one);
                var hand = Bone(side < 0 ? "L_Hand" : "R_Hand", la, new Vector3(0, -0.25f, 0));
                Seg("Palm", hand, MeshKit.RoundedBox(new Vector3(0.025f, 0.09f, 0.075f), 0.012f, 3), mSkin, new Vector3(0, -0.045f, 0), Vector3.one);
                for (int f = 0; f < 4; f++) Seg("Finger", hand, Limb(0.0085f, 0.007f, 0.07f), mSkin, new Vector3(0, -0.085f, -0.027f + f * 0.018f), Vector3.one);
                Seg("Thumb", hand, Limb(0.01f, 0.008f, 0.055f), mSkin, new Vector3(0.006f, -0.03f, 0.04f), Vector3.one).transform.localRotation = Quaternion.Euler(-35, 0, 0);
                ua.localRotation = Quaternion.Euler(0, 0, side * 6);
                la.localRotation = Quaternion.Euler(-8, 0, 0);
                if (side < 0) { r.lUpperArm = ua; r.lLowerArm = la; r.lHand = hand; } else { r.rUpperArm = ua; r.rLowerArm = la; r.rHand = hand; }
            }
            // Legs
            foreach (var side in new[] { -1, 1 })
            {
                var ul = Bone(side < 0 ? "L_UpLeg" : "R_UpLeg", r.hips, new Vector3(side * 0.09f, -0.05f, 0));
                Seg("Thigh", ul, Limb(0.075f, 0.052f, 0.44f), gown ? mSkin : mBottom, Vector3.zero, Vector3.one);
                var lg = Bone(side < 0 ? "L_Leg" : "R_Leg", ul, new Vector3(0, -0.44f, 0));
                Seg("Shin", lg, Limb(0.052f, 0.036f, 0.42f), gown ? mSkin : mBottom, Vector3.zero, Vector3.one);
                var foot = Seg("Shoe", lg, MeshKit.RoundedBox(new Vector3(0.095f, 0.075f, 0.26f), 0.035f, 3), mShoe, new Vector3(0, -0.43f, 0.05f), Vector3.one);
                if (side < 0) { r.lUpLeg = ul; r.lLeg = lg; } else { r.rUpLeg = ul; r.rLeg = lg; }
            }
            if (gown) Seg("Gown_Skirt", r.hips, MeshKit.Lathe(new[] { new Vector2(0.0f, -0.42f), new Vector2(0.21f, -0.44f), new Vector2(0.19f, -0.2f), new Vector2(0.17f, 0.0f), new Vector2(0.16f, 0.1f) }, 28), mTop, Vector3.zero, new Vector3(1, 1, 0.72f));
            return r;
        }

        static (CharacterSpeechController, HumanoidLifeController) AttachBehaviour(Rig r, Persona persona, string display)
        {
            var speechGo = new GameObject("Voice"); speechGo.transform.SetParent(r.head, false); speechGo.transform.localPosition = new Vector3(0, 0.06f, 0.08f);
            var src = speechGo.AddComponent<AudioSource>();
            var sp = speechGo.AddComponent<CharacterSpeechController>(); sp.persona = persona; sp.displayName = display; sp.jaw = r.jaw; sp.jawOpenDegrees = 12; sp.mouthScaleTarget = r.mouth;
            var life = r.root.gameObject.AddComponent<HumanoidLifeController>();
            life.head = r.head; life.neck = r.neck; life.chest = r.chest; life.spine = r.spine; life.rightUpperArm = r.rUpperArm; life.rightLowerArm = r.rLowerArm; life.speech = sp;
            life.eyelids = new Transform[0]; // lids handled by simple blink component below
            var blink = r.root.gameObject.AddComponent<SimpleEyelidBlink>(); blink.lids = r.lids;
            return (sp, life);
        }

        static PatientInteractionController BuildPatient(Transform parent)
        {
            var r = BuildHuman("Patient - Mr Adam Reyes (placeholder)", parent, new Color(0.78f, 0.6f, 0.48f), new Color(0.7f, 0.82f, 0.9f), new Color(0.7f, 0.82f, 0.9f), new Color(0.24f, 0.22f, 0.2f), false, true, 1.75f);
            // Semi-recumbent at 35° on the head section; facing -z (toward the foot of the bed)
            r.root.position = new Vector3(0.3f, -0.21f, 0.98f);
            r.root.rotation = Quaternion.Euler(0, 180, 0);
            r.hips.localRotation = Quaternion.Euler(-55, 0, 0);
            r.lUpLeg.localRotation = Quaternion.Euler(-37, 0, 2); r.rUpLeg.localRotation = Quaternion.Euler(-37, 0, -2);
            r.lLeg.localRotation = Quaternion.Euler(6, 0, 0); r.rLeg.localRotation = Quaternion.Euler(6, 0, 0);
            r.neck.localRotation = Quaternion.Euler(25, 0, 0);
            // Right hand resting on the sternum (pain), left arm on the bed
            r.rUpperArm.localRotation = Quaternion.Euler(-6, 0, 12); r.rLowerArm.localRotation = Quaternion.Euler(-38, 0, 0);
            r.lUpperArm.localRotation = Quaternion.Euler(-6, 0, -12); r.lLowerArm.localRotation = Quaternion.Euler(-38, 0, 0);
            PoseArmOnLap(r, -1); PoseArmOnLap(r, 1);
            var (sp, life) = AttachBehaviour(r, Persona.Patient, "Mr. Reyes (patient)");
            sp.GetComponent<AudioSource>().volume = 1f;
            life.lyingDown = true; life.mood = CharacterMood.Discomfort; life.maxHeadYaw = 45; life.rightUpperArm = null; life.rightLowerArm = null;
            var pc = r.root.gameObject.AddComponent<PatientInteractionController>(); pc.speech = sp; pc.life = life;

            // ---- Examination zones ----
            var zoneMat = Mat("Zone_Highlight", new Color(0.1f, 0.85f, 0.75f, 0.35f), 0.2f, 0, transparent: true);
            void Zone(string id, string display, Transform bone, Vector3 localPos, Vector3 size, string[] accepts, Vector3 fwd, Transform snap = null, float maxAngle = 75)
            {
                var z = new GameObject("Zone " + id); z.transform.SetParent(bone, false); z.transform.localPosition = localPos; z.transform.localRotation = Quaternion.LookRotation(fwd);
                var bc = z.AddComponent<BoxCollider>(); bc.size = size; bc.isTrigger = true;
                var pz = z.AddComponent<PlacementZone>(); pz.zoneId = id; pz.displayName = display; pz.acceptedInstruments = accepts; pz.maxAngle = maxAngle; pz.snapPoint = snap;
                var hl = Part("Highlight", z.transform, MeshKit.Ellipsoid(new Vector3(size.x, size.y, Mathf.Min(size.z, 0.01f)) * 0.5f, 16, 10), zoneMat, Vector3.zero, null, true, false);
                pz.highlight = hl.GetComponent<MeshRenderer>(); hl.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
            string[] st = { "stethoscope" };
            float zf = 0.118f; // chest surface (local +z of chest bone, which points anteriorly)
            Zone("CHEST_AORTIC", "aortic area (R 2nd ICS)", r.chest, new Vector3(-0.035f, 0.17f, zf), new Vector3(0.05f, 0.05f, 0.06f), st, Vector3.forward);
            Zone("CHEST_PULMONARY", "pulmonary area (L 2nd ICS)", r.chest, new Vector3(0.035f, 0.17f, zf), new Vector3(0.05f, 0.05f, 0.06f), st, Vector3.forward);
            Zone("CHEST_TRICUSPID", "tricuspid area (L lower sternal edge)", r.chest, new Vector3(0.025f, 0.08f, zf), new Vector3(0.05f, 0.05f, 0.06f), st, Vector3.forward);
            Zone("CHEST_MITRAL", "mitral area (apex)", r.chest, new Vector3(0.085f, 0.05f, zf - 0.01f), new Vector3(0.06f, 0.06f, 0.06f), st, Vector3.forward);
            Zone("CHEST_LUNG_R", "right lung field", r.chest, new Vector3(-0.1f, 0.12f, zf - 0.01f), new Vector3(0.07f, 0.1f, 0.06f), st, Vector3.forward);
            Zone("CHEST_LUNG_L", "left lung field", r.chest, new Vector3(0.115f, 0.13f, zf - 0.015f), new Vector3(0.06f, 0.09f, 0.06f), st, Vector3.forward);
            foreach (var (ua, hand, id, disp) in new[] { (r.lUpperArm, r.lHand, "L", "left"), (r.rUpperArm, r.rHand, "R", "right") })
            {
                var snap = new GameObject("Cuff Snap").transform; snap.SetParent(ua, false); snap.localPosition = new Vector3(0, -0.13f, 0); snap.localRotation = Quaternion.identity;
                Zone("ARM_UPPER_" + id, disp + " upper arm", ua, new Vector3(0, -0.13f, 0), new Vector3(0.14f, 0.18f, 0.14f), new[] { "bp_cuff" }, Vector3.forward, snap, 180);
                var fsnap = new GameObject("Oximeter Snap").transform; fsnap.SetParent(hand, false); fsnap.localPosition = new Vector3(0, -0.13f, 0.0f); fsnap.localRotation = Quaternion.Euler(0, 0, 90);
                Zone("FINGER_" + id, disp + " index finger", hand, new Vector3(0, -0.13f, 0), new Vector3(0.06f, 0.06f, 0.06f), new[] { "pulse_oximeter" }, Vector3.forward, fsnap, 180);
            }
            return pc;
        }

        static void Aim(Transform bone, Vector3 target)
        {
            var cur = bone.TransformDirection(Vector3.down); var want = (target - bone.position).normalized;
            bone.rotation = Quaternion.FromToRotation(cur, want) * bone.rotation;
        }

        /// <summary>Rests the hand on the lap: elbow aimed toward the hip (slightly outward), forearm toward the thigh.</summary>
        static void PoseArmOnLap(Rig r, int side)
        {
            var ua = side < 0 ? r.lUpperArm : r.rUpperArm; var la = side < 0 ? r.lLowerArm : r.rLowerArm;
            var sideDir = r.root.right * side; var fwd = r.root.forward;
            var s = ua.position; var hips = r.hips.position;
            var elbow = s + ((hips - s).normalized + sideDir * 0.35f + Vector3.down * 0.35f).normalized * 0.29f;
            Aim(ua, elbow);
            var hand = hips + fwd * 0.24f + Vector3.up * 0.1f + sideDir * 0.07f;
            Aim(la, hand);
        }

        static MedicalInstructorController BuildInstructor(Transform parent)
        {
            var r = BuildHuman("Instructor - Dr Sara Hassan (placeholder)", parent, new Color(0.66f, 0.49f, 0.38f), new Color(0.05f, 0.33f, 0.4f), new Color(0.05f, 0.27f, 0.34f), new Color(0.1f, 0.08f, 0.07f), true, false, 1.68f, true);
            r.root.position = new Vector3(-0.85f, 0, -0.55f);
            r.root.rotation = Quaternion.LookRotation(new Vector3(1.6f, 0, 0.9f));
            var (sp, life) = AttachBehaviour(r, Persona.Instructor, "Dr. Hassan (supervisor)");
            var ic = r.root.gameObject.AddComponent<MedicalInstructorController>(); ic.speech = sp; ic.life = life;
            return ic;
        }

        // =====================================================================================
        // XR
        // =====================================================================================
        static (Transform head, XRPlayerController ctl) BuildXR(Transform env)
        {
            var im = new GameObject("XR Interaction Manager").AddComponent<XRInteractionManager>();
            var es = new GameObject("EventSystem"); es.AddComponent<UnityEngine.EventSystems.EventSystem>(); es.AddComponent<XRUIInputModule>();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Samples/XR Interaction Toolkit/3.7.0/Starter Assets/Prefabs/XR Origin (XR Rig).prefab");
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            rig.transform.position = new Vector3(1.2f, 0, 0.65f); rig.transform.rotation = Quaternion.Euler(0, -90, 0);
            var origin = rig.GetComponent<XROrigin>();
            foreach (var t in rig.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Jump" || t.name == "Grab Move" || t.name == "Climb" || t.name == "Climb Teleport") t.gameObject.SetActive(false);
            }
            var cam = origin.Camera; cam.nearClipPlane = 0.02f; cam.farClipPlane = 50f; cam.backgroundColor = new Color(0.05f, 0.08f, 0.12f);
            if (!cam.GetComponent<AudioListener>()) cam.gameObject.AddComponent<AudioListener>();
            var data = cam.GetComponent<UniversalAdditionalCameraData>(); if (!data) data = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true; data.antialiasing = AntialiasingMode.None;
            var spawn = new GameObject("Player Spawn").transform; spawn.SetParent(env); spawn.position = rig.transform.position; spawn.rotation = rig.transform.rotation;
            var ctl = rig.AddComponent<XRPlayerController>(); ctl.origin = origin; ctl.spawnPoint = spawn;
            var move = rig.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Move"); if (move) ctl.smoothMoveProvider = move.gameObject;
            return (cam.transform, ctl);
        }

        // =====================================================================================
        // Branding (icon + splash generated procedurally; no third-party artwork)
        // =====================================================================================
        [MenuItem("MedSim AI/Generate Branding")]
        public static string GenerateBranding()
        {
            EnsureFolder(Root + "/Branding");
            var icon = DrawIcon(1024);
            var p = Root + "/Branding/MedSimAI_Icon_1024.png"; File.WriteAllBytes(p, icon.EncodeToPNG());
            var splash = DrawSplash(1920, 1080, icon);
            var sp = Root + "/Branding/MedSimAI_Splash_1920x1080.png"; File.WriteAllBytes(sp, splash.EncodeToPNG());
            AssetDatabase.Refresh();
            foreach (var path in new[] { p, sp }) { var ti = (TextureImporter)AssetImporter.GetAtPath(path); ti.textureType = TextureImporterType.Default; ti.mipmapEnabled = true; ti.alphaIsTransparency = true; ti.maxTextureSize = 2048; ti.SaveAndReimport(); }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
            PlayerSettings.SetIcons(UnityEditor.Build.NamedBuildTarget.Unknown, new[] { tex }, IconKind.Any);
            PlayerSettings.companyName = "MedSim AI"; PlayerSettings.productName = "MedSim AI";
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, "com.medsimai.prototype");
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Standalone, "com.medsimai.prototype");
            return "Branding generated";
        }

        static Texture2D DrawIcon(int s)
        {
            var t = new Texture2D(s, s, TextureFormat.RGBA32, false);
            var px = new Color[s * s];
            Color navyA = new Color32(10, 25, 47, 255), navyB = new Color32(20, 50, 80, 255), teal = new Color32(0, 200, 175, 255), white = new Color32(236, 242, 248, 255);
            float c = s / 2f, rad = s * 0.22f;
            for (int y = 0; y < s; y++) for (int x = 0; x < s; x++)
            {
                // rounded-square background with diagonal gradient
                float dx = Mathf.Max(0, Mathf.Abs(x - c) - (c - rad)), dy = Mathf.Max(0, Mathf.Abs(y - c) - (c - rad));
                float inside = Mathf.Clamp01(rad - Mathf.Sqrt(dx * dx + dy * dy));
                var col = Color.Lerp(navyA, navyB, (x + y) / (2f * s)); col.a = inside;
                // medical cross (white) with soft rounded arms
                float cx = Mathf.Abs(x - c), cy = Mathf.Abs(y - c * 1.04f);
                float arm = s * 0.11f, len = s * 0.3f;
                bool cross = (cx < arm && cy < len) || (cy < arm && cx < len);
                if (cross) col = Color.Lerp(col, white, inside);
                px[y * s + x] = col;
            }
            // ECG trace across the cross in teal (thick)
            float thick = s * 0.018f;
            for (int x = (int)(s * 0.14f); x < s * 0.86f; x++)
            {
                float u = (x - s * 0.14f) / (s * 0.72f);
                float yv = 0;
                if (u > 0.36f && u < 0.42f) yv = Mathf.Lerp(0, 0.08f, (u - 0.36f) / 0.06f);
                else if (u >= 0.42f && u < 0.47f) yv = Mathf.Lerp(0.08f, -0.26f, (u - 0.42f) / 0.05f);
                else if (u >= 0.47f && u < 0.53f) yv = Mathf.Lerp(-0.26f, 0.3f, (u - 0.47f) / 0.06f);
                else if (u >= 0.53f && u < 0.58f) yv = Mathf.Lerp(0.3f, -0.06f, (u - 0.53f) / 0.05f);
                else if (u >= 0.58f && u < 0.62f) yv = Mathf.Lerp(-0.06f, 0, (u - 0.58f) / 0.04f);
                else if (u > 0.68f && u < 0.8f) yv = Mathf.Sin((u - 0.68f) / 0.12f * Mathf.PI) * 0.05f;
                int yc = (int)(c + yv * s);
                for (int yy = (int)(yc - thick * 3); yy <= yc + thick * 3; yy++) for (int xx = x - 3; xx <= x + 3; xx++)
                {
                    if (yy < 0 || yy >= s || xx < 0 || xx >= s) continue;
                    float d = Mathf.Abs(yy - yc) / thick;
                    if (d < 1.4f) { var o = px[yy * s + xx]; px[yy * s + xx] = Color.Lerp(o, teal, Mathf.Clamp01(1.4f - d) * o.a); }
                }
            }
            t.SetPixels(px); t.Apply(); return t;
        }

        static Texture2D DrawSplash(int w, int h, Texture2D icon)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false); var px = new Color[w * h];
            Color a = new Color32(6, 16, 32, 255), b = new Color32(14, 40, 66, 255), teal = new Color32(0, 168, 150, 255);
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                float g = Mathf.Clamp01((float)y / h * 0.7f + (float)x / w * 0.3f); var col = Color.Lerp(a, b, g);
                float grid = (x % 64 == 0 || y % 64 == 0) ? 0.04f : 0; col = Color.Lerp(col, teal, grid);
                px[y * w + x] = col;
            }
            int isz = 420, ox = (w - isz) / 2, oy = (h - isz) / 2 + 60;
            for (int y = 0; y < isz; y++) for (int x = 0; x < isz; x++)
            {
                var c = icon.GetPixelBilinear(x / (float)isz, y / (float)isz); var o = px[(oy + y) * w + ox + x]; px[(oy + y) * w + ox + x] = Color.Lerp(o, c, c.a);
            }
            for (int x = w / 2 - 300; x < w / 2 + 300; x++) for (int y = oy - 70; y < oy - 64; y++) px[y * w + x] = teal;
            t.SetPixels(px); t.Apply(); return t;
        }
    }

    /// <summary>Kept in the editor assembly's namespace for wiring; runtime behaviour lives in MedSimAI.Characters.</summary>
    static class BuilderShim { }
}
