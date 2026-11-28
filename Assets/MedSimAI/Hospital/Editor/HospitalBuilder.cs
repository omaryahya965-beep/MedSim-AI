using System.Collections.Generic;
using System.IO;
using System.Linq;
using MedSimAI.Characters;
using MedSimAI.Core;
using MedSimAI.Surgery.Anatomy;
using MedSimAI.Surgery.Characters;
using MedSimAI.Surgery.Integration;
using MedSimAI.Surgery.Mechanics;
using MedSimAI.Surgery.Shared;
using MedSimAI.Surgery.Shared.Unity;
using MedSimAI.Surgery.UI;
using MedSimAI.Surgery.AI;
using MedSimAI.UI;
using MedSimAI.Voice;
using TMPro;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace MedSimAI.Hospital.EditorTools
{
    /// <summary>
    /// Builds the integrated hospital scene (Assets/MedSimAI/Hospital/Scenes/MedSim_Hospital.unity) from a COPY of the
    /// clinical scene MedSim_ER.unity (which stays untouched as the standalone clinical experience):
    ///   corridor extended through the old fire doors → reception lobby (start) → Room 01 (existing exam room) and Room 02
    ///   (Person 1's operating room docked on the corridor through its vestibule) with Person 2's patient, instruments and
    ///   implants, Person 3's mechanics, Person 4's session layer, and the hospital coordinator + persistent doctor guide.
    /// Menu: MedSim AI → Hospital → Build Hospital Scene. Writes Docs/Surgery/FinalIntegration/LAST_HOSPITAL_BUILD.txt.
    /// </summary>
    public static class HospitalBuilder
    {
        public const string ErScene = "Assets/MedSimAI/Scenes/MedSim_ER.unity";
        public const string HospitalScene = "Assets/MedSimAI/Hospital/Scenes/MedSim_Hospital.unity";
        const string Gen = "Assets/MedSimAI/Hospital/Generated";
        const string MatDir = "Assets/MedSimAI/Generated/RedesignMaterials/";
        const string OrPrefab = "Assets/MedSimAI/Surgery/Environment/Prefabs/OperatingRoom.prefab";
        const string PatientPrefab = "Assets/MedSimAI/Surgery/Anatomy/Prefabs/SurgicalPatient.prefab";
        const string TrayPrefab = "Assets/MedSimAI/Surgery/Instruments/Prefabs/InstrumentTray_Populated.prefab";
        const string ImplantSetPrefab = "Assets/MedSimAI/Surgery/Anatomy/Prefabs/KneeImplantSet.prefab";
        const string MechanicsPrefab = "Assets/MedSimAI/Surgery/Mechanics/Prefabs/SurgeryMechanics.prefab";
        // Scrub assistant: Microsoft Rocketbox "Medical_Male_04" (MIT): scrubs, surgical cap and mask (not the Room 01 patient's body).
        const string AssistantDir = "Assets/MedSimAI/Characters/v2/Assistant", AssistantFbx = AssistantDir + "/Medical_Male_04.fbx", DoctorCtrl = "Assets/MedSimAI/Characters/v2/Animations/DrHassan.controller";

        // Layout (world metres). The clinical scene's corridor runs along X at z −5.02…−2.62; Room 01 is north of it.
        public static readonly Vector3 OrRoot = new Vector3(7.25f, 0f, 2.92f);   // vestibule ends flush with the corridor's north face
        const float CorridorZ0 = -5.02f, CorridorZ1 = -2.62f, WallT = 0.12f, WallH = 2.89f, CeilY = 2.60f;
        const float XEnd = 13.8f, LobbyX0 = 10.0f, LobbyZ0 = -7.6f;
        static readonly Vector3 SpawnPos = new Vector3(12.4f, 0, -5.5f), DoctorWelcomePos = new Vector3(10.95f, 0, -5.05f);

        static readonly List<string> report = new List<string>();
        static readonly Dictionary<string, Mesh> meshCache = new Dictionary<string, Mesh>();

        [MenuItem("MedSim AI/Hospital/Build Hospital Scene")]
        public static void BuildMenu() => Debug.Log(Build());

        public static string Build()
        {
            report.Clear(); meshCache.Clear();
            Directory.CreateDirectory(Path.GetDirectoryName(HospitalScene)); Directory.CreateDirectory(Gen + "/Meshes");
            // Start from the clinical scene. Overwrite the file contents (keep the .meta) so the hospital scene GUID stays stable.
            if (File.Exists(HospitalScene)) { File.Copy(ErScene, HospitalScene, true); AssetDatabase.ImportAsset(HospitalScene, ImportAssetOptions.ForceUpdate); }
            else if (!AssetDatabase.CopyAsset(ErScene, HospitalScene)) return "Could not copy " + ErScene;
            ConfigureAssistantImport();
            var scene = EditorSceneManager.OpenScene(HospitalScene, OpenSceneMode.Single);

            ExtendCorridor();
            BuildLobby();
            var or = PlaceOperatingRoom();
            var surgical = PlaceSurgicalContent(or);
            var people = SetUpPeople(or);
            var coordinator = SetUpSystems(or, surgical, people);
            Signage(or);
            Navigation(or);
            SetUpBuild();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            var msg = "Built " + HospitalScene + "\n" + string.Join("\n", report);
            Directory.CreateDirectory("Docs/Surgery/FinalIntegration");
            File.WriteAllText("Docs/Surgery/FinalIntegration/LAST_HOSPITAL_BUILD.txt", msg + "\n");
            return msg;
        }

        /// <summary>Rocketbox import (as the library's own FixRocketboxMaxImport, but only for this asset): humanoid avatar,
        /// normal maps typed as such, URP Lit materials from the delivered textures.</summary>
        static Dictionary<string, Material> assistantMats;
        static void ConfigureAssistantImport()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { AssistantDir + "/Textures" }))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid); var ti = (TextureImporter)AssetImporter.GetAtPath(p); bool normal = p.Contains("_normal"), spec = p.Contains("_specular");
                var want = normal ? TextureImporterType.NormalMap : TextureImporterType.Default; bool ch = false;
                if (ti.textureType != want) { ti.textureType = want; ch = true; }
                if (ti.sRGBTexture == (normal || spec)) { ti.sRGBTexture = !(normal || spec); ch = true; }
                if (ti.maxTextureSize != 2048) { ti.maxTextureSize = 2048; ch = true; }
                if (ti.anisoLevel != 4) { ti.anisoLevel = 4; ch = true; }
                bool alpha = p.Contains("head_color"); if (ti.alphaIsTransparency != alpha) { ti.alphaIsTransparency = alpha; ch = true; }
                if (ch) ti.SaveAndReimport();
            }
            var mi = (ModelImporter)AssetImporter.GetAtPath(AssistantFbx);
            if (mi.animationType != ModelImporterAnimationType.Human)
            {   // keep the FBX material slots (their names say body / head / protection); they are replaced by URP materials below
                mi.animationType = ModelImporterAnimationType.Human; mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                mi.importBlendShapes = true; mi.SaveAndReimport();
            }
            Texture2D T(string n) => AssetDatabase.LoadAssetAtPath<Texture2D>(AssistantDir + "/Textures/m162_" + n + ".png");
            Material M(string name, string tex, float smooth, bool cutout)
            {
                var path = AssistantDir + "/" + name + ".mat"; var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
                m.SetTexture("_BaseMap", T(tex + "_color")); m.SetColor("_BaseColor", Color.white);
                m.SetTexture("_BumpMap", T(tex + "_normal")); m.EnableKeyword("_NORMALMAP");
                m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", 0f);
                if (cutout) { m.SetFloat("_AlphaClip", 1); m.SetFloat("_Cutoff", 0.4f); m.EnableKeyword("_ALPHATEST_ON"); m.SetOverrideTag("RenderType", "TransparentCutout"); m.renderQueue = 2450; m.SetFloat("_Cull", 0); }
                EditorUtility.SetDirty(m); return m;
            }
            assistantMats = new Dictionary<string, Material> { ["body"] = M("Sami_Body", "body", 0.2f, false), ["head"] = M("Sami_Head", "head", 0.32f, true), ["protection"] = M("Sami_Protection", "protection", 0.15f, false) };
            AssetDatabase.SaveAssets();
        }

        // ------------------------------------------------------------------ helpers
        static GameObject Root(string name) => SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g => g.name == name);
        static Transform FindDeep(Transform t, string name) => t.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == name);
        static Material Mat(string n) => AssetDatabase.LoadAssetAtPath<Material>(MatDir + n + ".mat");

        static Mesh BoxMesh(Vector3 size)
        {
            var key = $"Box_{size.x:0.###}x{size.y:0.###}x{size.z:0.###}";
            if (meshCache.TryGetValue(key, out var m)) return m;
            var path = $"{Gen}/Meshes/{key}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (!existing)
            {
                var mesh = MeshKit.RoundedBox(size, 0.003f, 1, 1f); mesh.name = key;
                Unwrapping.GenerateSecondaryUVSet(mesh);
                AssetDatabase.CreateAsset(mesh, path); existing = mesh;
            }
            meshCache[key] = existing; return existing;
        }

        /// <summary>A static box by its world min/max corners (lightmapped, optional collider), like the clinical scene's shell.</summary>
        static GameObject Box(Transform parent, string name, Vector3 min, Vector3 max, Material mat, bool collider = true)
        {
            var size = max - min; var go = new GameObject(name);
            go.transform.SetParent(parent, false); go.transform.position = (min + max) * 0.5f;
            go.AddComponent<MeshFilter>().sharedMesh = BoxMesh(size);
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            if (collider) go.AddComponent<BoxCollider>().size = size;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
            return go;
        }

        static void Wainscot(Transform p, string side, float x0, float x1, float zFace, float dir)
        {
            // dir = +1 when the wall face looks toward +Z (north side of a room), −1 toward −Z
            float a = zFace, b = zFace + 0.008f * dir, c = zFace + 0.02f * dir, d = zFace + 0.016f * dir;
            Box(p, "Skirting_" + side, new Vector3(x0, 0, Mathf.Min(a, c)), new Vector3(x1, 0.1f, Mathf.Max(a, c)), Mat("Skirting_Coved"), false);
            Box(p, "Dado_" + side, new Vector3(x0, 0.1f, Mathf.Min(a, b)), new Vector3(x1, 0.9f, Mathf.Max(a, b)), Mat("Dado_WallProtection"), false);
            Box(p, "DadoCap_" + side, new Vector3(x0, 0.9f, Mathf.Min(a, d)), new Vector3(x1, 0.93f, Mathf.Max(a, d)), Mat("Skirting_Coved"), false);
        }

        static GameObject CloneUnder(GameObject src, Transform parent, string name, Vector3 pos, Quaternion rot)
        {
            var g = Object.Instantiate(src, parent); g.name = name; g.transform.SetPositionAndRotation(pos, rot); return g;
        }

        const float PlateScale = 1.7f, HangingScale = 1.45f;

        /// <summary>Copies every text of a flat sign onto its back face (mirrored so both faces read left-to-right the same way).</summary>
        static void DoubleSided(GameObject sign)
        {
            foreach (var t in sign.GetComponentsInChildren<TMP_Text>(true).ToList())
            {
                var lp = t.transform.localPosition;
                var back = Object.Instantiate(t.gameObject, t.transform.parent); back.name = t.name + " (back)";
                back.transform.localPosition = new Vector3(-lp.x, lp.y, -lp.z);
                back.transform.localRotation = Quaternion.Euler(0, 180, 0) * t.transform.localRotation;
                var bt = back.GetComponent<TMP_Text>();   // seen from the other side, left and right swap: mirror the alignment too
                var h = bt.horizontalAlignment;
                bt.horizontalAlignment = h == HorizontalAlignmentOptions.Left ? HorizontalAlignmentOptions.Right : h == HorizontalAlignmentOptions.Right ? HorizontalAlignmentOptions.Left : h;
            }
        }

        /// <summary>Sets a cloned sign's texts: Arabic objects are the ones using the Arabic font. Halves (by local x) get their own pair.</summary>
        static void SetTexts(GameObject sign, string en, string ar, string en2 = null, string ar2 = null)
        {
            foreach (var t in sign.GetComponentsInChildren<TMP_Text>(true))
            {
                bool isAr = t.font && t.font.name.Contains("Arabic");
                bool second = en2 != null && t.transform.localPosition.x < -0.05f;
                var text = isAr ? (second ? ar2 : ar) : (second ? en2 : en);
                if (text == null) continue;
                t.enableAutoSizing = true; t.fontSizeMax = t.fontSize; t.fontSizeMin = t.fontSize * 0.35f; t.enableWordWrapping = false;
                ArabicShaper.Apply(t, text);
            }
        }

        // ------------------------------------------------------------------ corridor extension (through the old fire doors)
        static void ExtendCorridor()
        {
            var corridor = Root("Redesign_Corridor").transform; var shell = corridor.Find("Shell");
            foreach (var n in new[] { "Wall_E_A", "Wall_E_B", "Wall_E_Head", "Door_E_Blocker" }) { var t = shell.Find(n); if (t) t.gameObject.SetActive(false); }
            foreach (var n in new[] { "Door_East_Double", "Exit_Sign", "Ambience_BeyondFireDoors" }) { var t = corridor.Find(n); if (t) t.gameObject.SetActive(false); }
            // The clinical scene's exterior roof slab would cut through the operating room: trim it at the corridor's east end.
            var roof = Root("Redesign_Exterior")?.transform.Find("Roof_Slab");
            if (roof) { var r = roof.GetComponent<Renderer>().bounds; float x0 = r.min.x, x1 = 3.52f; var s = roof.localScale; float k = (x1 - x0) / r.size.x; roof.localScale = new Vector3(s.x * k, s.y, s.z); roof.position += Vector3.right * ((x0 + x1) * 0.5f - r.center.x); }

            var ext = new GameObject("Hospital_Corridor_Extension").transform;
            float vx0 = OrRoot.x - 3.06f, vx1 = OrRoot.x - 0.78f;    // vestibule opening (Person 1's vestibule interior width)
            var wall = Mat("Wall_Paint");
            Box(ext, "Floor", new Vector3(3.40f, -0.04f, CorridorZ0), new Vector3(XEnd, 0f, CorridorZ1), Mat("Floor_SheetVinyl"));
            Box(ext, "Ceiling", new Vector3(3.40f, CeilY, CorridorZ0), new Vector3(XEnd, CeilY + 0.04f, CorridorZ1), Mat("Ceiling_Tile"), false);
            // north wall with the opening into Room 02's vestibule
            Box(ext, "Wall_N_A", new Vector3(3.52f, 0, CorridorZ1), new Vector3(vx0, WallH, CorridorZ1 + WallT), wall);
            Box(ext, "Wall_N_Head", new Vector3(vx0, 2.25f, CorridorZ1), new Vector3(vx1, WallH, CorridorZ1 + WallT), wall, false);
            Box(ext, "Wall_N_B", new Vector3(vx1, 0, CorridorZ1), new Vector3(XEnd, WallH, CorridorZ1 + WallT), wall);
            Wainscot(ext, "N_A", 3.40f, vx0, CorridorZ1, -1); Wainscot(ext, "N_B", vx1, XEnd, CorridorZ1, -1);
            // south wall (exterior) with a second window, up to the lobby
            Box(ext, "Wall_S_A", new Vector3(3.40f, 0, CorridorZ0 - WallT), new Vector3(7.4f, WallH, CorridorZ0), wall);
            Box(ext, "Wall_S_Sill", new Vector3(7.4f, 0, CorridorZ0 - WallT), new Vector3(9.4f, 0.9f, CorridorZ0), wall);
            Box(ext, "Wall_S_Head", new Vector3(7.4f, 2.1f, CorridorZ0 - WallT), new Vector3(9.4f, WallH, CorridorZ0), wall, false);
            Box(ext, "Wall_S_B", new Vector3(9.4f, 0, CorridorZ0 - WallT), new Vector3(LobbyX0, WallH, CorridorZ0), wall);
            Wainscot(ext, "S_A", 3.40f, LobbyX0, CorridorZ0, 1);
            var window = corridor.Find("Window"); if (window) CloneUnder(window.gameObject, ext, "Window_2", window.position + Vector3.right * (8.4f - 1.9f), window.rotation);
            var radiator = corridor.Find("Radiator"); if (radiator) CloneUnder(radiator.gameObject, ext, "Radiator_2", radiator.position + Vector3.right * (8.4f - 1.9f), radiator.rotation);
            var rail = shell.Find("Handrail"); if (rail) CloneUnder(rail.gameObject, ext, "Handrail_N", rail.position + Vector3.right * (vx1 + 0.3f + 1.567f), rail.rotation);
            // ceiling lights (same fitting as the clinical corridor; baked like it)
            var led = corridor.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Ceiling LED Panel");
            if (led) foreach (var x in new[] { 4.8f, 7.2f, 9.6f }) CloneUnder(led.gameObject, ext, "Ceiling LED Panel", new Vector3(x, led.position.y, -3.82f), led.rotation);
            var probe = Root("Redesign_Lighting")?.transform.Find("Reflection Probe Corridor");
            if (probe) { var p2 = CloneUnder(probe.gameObject, ext, "Reflection Probe Corridor East", new Vector3(8.2f, 1.3f, -3.82f), probe.rotation); }
            var tp = corridor.Find("Teleport Corridor");
            if (tp) { var area = Object.Instantiate(tp.gameObject, ext); area.name = "Teleport Corridor East"; area.transform.position = new Vector3((3.4f + XEnd) / 2, 0, -3.82f); var bc = area.GetComponent<BoxCollider>(); if (bc) bc.size = new Vector3(XEnd - 3.4f, bc.size.y, 2.3f); }
            report.Add($"✓ Corridor extended through the old fire doors to x = {XEnd} m (window, lights, wall protection, teleport area)");
        }

        // ------------------------------------------------------------------ reception lobby (start of the experience)
        static void BuildLobby()
        {
            var lobby = new GameObject("Hospital_Reception_Lobby").transform; var wall = Mat("Wall_Paint");
            Box(lobby, "Floor", new Vector3(LobbyX0, -0.04f, LobbyZ0), new Vector3(XEnd, 0, CorridorZ0), Mat("Floor_SheetVinyl"));
            Box(lobby, "Ceiling", new Vector3(LobbyX0, CeilY, LobbyZ0), new Vector3(XEnd, CeilY + 0.04f, CorridorZ0), Mat("Ceiling_Tile"), false);
            Box(lobby, "Wall_W", new Vector3(LobbyX0 - WallT, 0, LobbyZ0 - WallT), new Vector3(LobbyX0, WallH, CorridorZ0), wall);
            Box(lobby, "Wall_E", new Vector3(XEnd, 0, LobbyZ0 - WallT), new Vector3(XEnd + WallT, WallH, CorridorZ1 + WallT), wall);
            // south: main entrance (glazed, closed for the simulation)
            Box(lobby, "Wall_S_A", new Vector3(LobbyX0 - WallT, 0, LobbyZ0 - WallT), new Vector3(11.0f, WallH, LobbyZ0), wall);
            Box(lobby, "Wall_S_B", new Vector3(12.8f, 0, LobbyZ0 - WallT), new Vector3(XEnd + WallT, WallH, LobbyZ0), wall);
            Box(lobby, "Entrance_Head", new Vector3(11.0f, 2.2f, LobbyZ0 - WallT), new Vector3(12.8f, WallH, LobbyZ0), wall, false);
            var glass = AssetDatabase.FindAssets("Glass t:Material", new[] { "Assets/MedSimAI/Generated" }).Select(g => AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault();
            Box(lobby, "Entrance_Glass_Doors", new Vector3(11.0f, 0, LobbyZ0 - 0.06f), new Vector3(12.8f, 2.2f, LobbyZ0 - 0.02f), glass ? glass : Mat("Stainless_Brushed"));
            Box(lobby, "Entrance_Frame", new Vector3(11.85f, 0, LobbyZ0 - 0.07f), new Vector3(11.95f, 2.2f, LobbyZ0 - 0.01f), Mat("Stainless_Brushed"), false);
            Wainscot(lobby, "W", LobbyX0, LobbyX0 + 0.001f, CorridorZ0, 1);
            Wainscot(lobby, "S", LobbyX0, XEnd, LobbyZ0, 1);
            // reception desk: oak top, protected front, like the corridor's finishes
            Box(lobby, "Reception_Desk_Body", new Vector3(12.55f, 0, -7.25f), new Vector3(13.75f, 1.02f, -6.15f), Mat("Dado_WallProtection"));
            Box(lobby, "Reception_Desk_Top", new Vector3(12.5f, 1.02f, -7.3f), new Vector3(13.78f, 1.06f, -6.1f), Mat("Handrail_Oak"), false);
            Box(lobby, "Reception_Desk_Kick", new Vector3(12.56f, 0, -7.24f), new Vector3(13.74f, 0.1f, -6.16f), Mat("Skirting_Coved"), false);
            var seating = Root("Redesign_Corridor")?.transform.Find("Beam_Seating");
            if (seating) CloneUnder(seating.gameObject, lobby, "Lobby_Seating", new Vector3(10.55f, 0, -6.6f), Quaternion.Euler(0, 90, 0));
            var plant = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MedSimAI/Environment/PolyHaven/calathea_orbifolia_01/calathea_orbifolia_01.fbx");
            if (plant) { var p = (GameObject)PrefabUtility.InstantiatePrefab(plant); p.transform.SetParent(lobby); p.transform.position = new Vector3(13.45f, 0, -5.4f); }
            var led = Root("Redesign_Corridor")?.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Ceiling LED Panel");
            if (led) foreach (var p in new[] { new Vector3(11.9f, led.position.y, -4.1f), new Vector3(11.9f, led.position.y, -6.5f) }) CloneUnder(led.gameObject, lobby, "Ceiling LED Panel", p, led.rotation);
            var tp = Root("Redesign_Corridor")?.transform.Find("Teleport Corridor");
            if (tp) { var area = Object.Instantiate(tp.gameObject, lobby); area.name = "Teleport Lobby"; area.transform.position = new Vector3((LobbyX0 + XEnd) / 2, 0, (LobbyZ0 + CorridorZ0) / 2); var bc = area.GetComponent<BoxCollider>(); if (bc) bc.size = new Vector3(XEnd - LobbyX0, bc.size.y, CorridorZ0 - LobbyZ0); }
            // Start: the student arrives at reception; the doctor meets them there.
            var spawn = Root("Environment")?.transform.Find("Player Spawn");
            if (spawn) spawn.SetPositionAndRotation(SpawnPos, Quaternion.Euler(0, -90f, 0));
            var rig = Root("XR Origin (XR Rig)"); if (rig) rig.transform.SetPositionAndRotation(SpawnPos, Quaternion.Euler(0, -90f, 0));
            report.Add("✓ Reception lobby (entrance, desk, seating, directory) — the student starts here, not beside a patient");
        }

        // ------------------------------------------------------------------ Room 02: Person 1's operating room
        static GameObject PlaceOperatingRoom()
        {
            var or = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(OrPrefab));
            or.name = "Room 02 - Operating Room"; or.transform.SetPositionAndRotation(OrRoot, Quaternion.identity);
            var end = FindDeep(or.transform, "V End"); if (end) end.gameObject.SetActive(false);   // the vestibule now opens onto the corridor
            var surf = or.GetComponent<NavMeshSurface>(); if (surf) surf.enabled = false;           // one hospital NavMesh (rebaked below)
            // Keep Person 1's realtime-lit look: the room is not lightmapped by the hospital bake.
            foreach (var r in or.GetComponentsInChildren<Renderer>(true))
            {
                var f = GameObjectUtility.GetStaticEditorFlags(r.gameObject);
                GameObjectUtility.SetStaticEditorFlags(r.gameObject, f & ~StaticEditorFlags.ContributeGI);
            }
            var entrance = FindDeep(or.transform, "Sliding Operating Room Entrance");
            if (entrance)
            {
                var door = entrance.gameObject.AddComponent<AutoSlidingDoor>();
                door.leafLeft = FindDeep(entrance, "DoorLeafLeft"); door.leafRight = FindDeep(entrance, "DoorLeafRight");
                foreach (var leaf in new[] { door.leafLeft, door.leafRight }) if (leaf) { var m = leaf.gameObject.AddComponent<NavMeshModifier>(); m.ignoreFromBuild = true; }
            }
            var floor = FindDeep(or.transform, "Walkable Floor");
            if (floor && !floor.GetComponent<TeleportationArea>()) floor.gameObject.AddComponent<TeleportationArea>();
            report.Add("✓ Room 02: Person 1's OperatingRoom.prefab docked on the corridor (vestibule opened, automatic sliding doors, teleport floor)");
            return or;
        }

        struct Surgical { public GameObject patient, tray, implants, mechanics; public KneeAnatomyStateController knee; public RobotGuidanceAdapter robot; public ObjectiveHighlighter highlighter; }

        static Surgical PlaceSurgicalContent(GameObject or)
        {
            var s = new Surgical();
            Transform A(string id) => or.GetComponentsInChildren<SurgeryAnchor>(true).FirstOrDefault(a => a.anchorId == id)?.transform;
            var pr = A(AnchorIds.PatientRoot);
            s.patient = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PatientPrefab));
            s.patient.transform.SetParent(pr, false);
            s.knee = s.patient.GetComponentInChildren<KneeAnatomyStateController>(true);
            foreach (var tag in s.patient.GetComponentsInChildren<SurgeryAnatomyTag>(true)) if (!tag.GetComponent<AnatomyContactTrigger>()) tag.gameObject.AddComponent<AnatomyContactTrigger>();
            var it = A(AnchorIds.InstrumentTable);
            s.tray = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(TrayPrefab));
            s.tray.transform.SetPositionAndRotation(it.position, it.rotation);
            foreach (var tag in s.tray.GetComponentsInChildren<SurgeryInstrumentTag>(true)) { tag.gameObject.AddComponent<InstrumentRespawn>(); var m = tag.gameObject.AddComponent<NavMeshModifier>(); m.ignoreFromBuild = true; }
            var imp = A(AnchorIds.ImplantTable);
            s.implants = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ImplantSetPrefab));
            s.implants.transform.SetPositionAndRotation(imp.position, Quaternion.LookRotation(Vector3.ProjectOnPlane(Vector3.left, Vector3.up)) * Quaternion.Euler(0, 90, 0));
            foreach (var tag in s.implants.GetComponentsInChildren<SurgeryImplantTag>(true)) { tag.gameObject.AddComponent<InstrumentRespawn>(); var m = tag.gameObject.AddComponent<NavMeshModifier>(); m.ignoreFromBuild = true; }
            s.mechanics = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(MechanicsPrefab));
            s.mechanics.name = "Surgical Mechanics (Person 3)";
            var root = s.mechanics.GetComponent<SurgeryMechanicsRoot>(); root.EnsureSubsystems();
            var adapter = s.mechanics.GetComponentInChildren<SurgicalDesktopInteractionAdapter>(true); if (adapter) adapter.enabled = false;   // the bridge enables it in the OR (desktop mode)
            // Robot: Person 1's arm, driven through Person 3's controller.
            var robotRoot = FindDeep(or.transform, "RobotBase");
            if (robotRoot)
            {
                s.robot = robotRoot.gameObject.AddComponent<RobotGuidanceAdapter>();
                s.robot.controller = root.RobotController; s.robot.robotBase = robotRoot; s.robot.toolMount = FindDeep(robotRoot, "RobotToolMount");
                s.robot.joints = new[] { "J1_BaseYaw", "J2_ShoulderPitch", "J3_ElbowPitch", "J4_ForearmRoll", "J5_WristPitch", "J6_ToolRoll" }.Select(n => FindDeep(robotRoot, n)).ToArray();
                s.robot.marker = Marker(or.transform, "Robot Guidance Marker (simulated)", new Color(0.35f, 0.9f, 1f, 0.35f), 0.09f, "SIMULATED GUIDANCE · محاكاة تعليمية");
            }
            s.highlighter = new GameObject("Objective Highlighter (guided mode)").AddComponent<ObjectiveHighlighter>();
            s.highlighter.transform.SetParent(or.transform, false);
            s.highlighter.ring = Marker(s.highlighter.transform, "Target Ring", new Color(0.3f, 1f, 0.75f, 0.3f), 0.07f, null);
            report.Add("✓ Person 2 patient at ANCHOR_PatientRoot, populated tray on the Mayo stand, implants on the back table; contact triggers on " + s.patient.GetComponentsInChildren<SurgeryAnatomyTag>(true).Length + " targets");
            report.Add("✓ Person 3 mechanics prefab (real; mock mechanics disabled); robot bound to Person 1's arm (" + (s.robot ? s.robot.joints.Count(j => j) : 0) + "/6 joints)");
            return s;
        }

        static Transform Marker(Transform parent, string name, Color c, float radius, string label)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder); go.name = name; Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false); go.transform.localScale = new Vector3(radius * 2, 0.002f, radius * 2);
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            var mat = new Material(shader) { name = name + " Mat" };
            mat.SetFloat("_Surface", 1); mat.SetFloat("_Blend", 0); mat.SetColor("_BaseColor", c);
            mat.SetOverrideTag("RenderType", "Transparent"); mat.renderQueue = 3000; mat.SetInt("_ZWrite", 0); mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            var matPath = $"{Gen}/{name.Replace(' ', '_').Replace("(", "").Replace(")", "").Replace("·", "")}.mat";
            AssetDatabase.CreateAsset(mat, matPath);
            go.GetComponent<Renderer>().sharedMaterial = mat;
            go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (!string.IsNullOrEmpty(label))
            {
                var t = new GameObject("Label").AddComponent<TextMeshPro>(); t.transform.SetParent(go.transform, false);
                t.transform.localPosition = new Vector3(0, 30f, 0); t.transform.localScale = new Vector3(1f / (radius * 2) * 0.05f, 1f / 0.002f * 0.05f, 1f / (radius * 2) * 0.05f);
                ArabicShaper.Apply(t, label); t.fontSize = 2.2f; t.alignment = TextAlignmentOptions.Center; t.color = new Color(0.6f, 0.95f, 1f);
                t.gameObject.AddComponent<FaceCamera>();
            }
            return go.transform;
        }

        // ------------------------------------------------------------------ people: one doctor for the whole hospital, the OR assistant
        struct People { public GameObject doctor; public SurgicalCharacter doctorRole, assistant; public HospitalGuide guide; }

        static People SetUpPeople(GameObject or)
        {
            var p = new People();
            var instr = Object.FindAnyObjectByType<MedicalInstructorController>(FindObjectsInactive.Include);
            p.doctor = instr.gameObject;
            p.doctor.transform.SetPositionAndRotation(DoctorWelcomePos, Quaternion.LookRotation(SpawnPos - DoctorWelcomePos));
            var mover = p.doctor.GetComponent<DoctorMovement>();
            p.guide = p.doctor.GetComponent<HospitalGuide>() ? p.doctor.GetComponent<HospitalGuide>() : p.doctor.AddComponent<HospitalGuide>();
            p.guide.mover = mover; p.guide.instructor = instr;
            // The same doctor takes the surgeon role in Room 02 (no second doctor, no new persona).
            p.doctorRole = p.doctor.AddComponent<SurgicalCharacter>();
            p.doctorRole.characterId = SurgeryCharacterIds.Surgeon; p.doctorRole.homeAnchor = AnchorIds.SurgeonObserve; p.doctorRole.enabled = false;
            var nm = p.doctor.AddComponent<NavMeshModifier>(); nm.ignoreFromBuild = true;
            // Assistant (Person 4's scrub assistant, OR only).
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(AssistantFbx);
            var a = (GameObject)PrefabUtility.InstantiatePrefab(model); a.name = "Surgical Assistant - Sami";
            var home = or.GetComponentsInChildren<SurgeryAnchor>(true).First(x => x.anchorId == AnchorIds.AssistantHome).transform;
            a.transform.SetPositionAndRotation(new Vector3(home.position.x, 0, home.position.z), home.rotation);
            var anim = a.GetComponentInChildren<Animator>(); var ctrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(DoctorCtrl);
            if (anim && ctrl) { anim.runtimeAnimatorController = ctrl; anim.applyRootMotion = false; }
            var speech = a.GetComponent<CharacterSpeechController>(); if (!speech) speech = a.AddComponent<CharacterSpeechController>();
            speech.persona = MedSimAI.AI.Persona.Instructor; speech.displayName = "Sami (surgical assistant)"; speech.displayNameAr = "سامي (مساعد العمليات)";
            var src = a.GetComponent<AudioSource>(); if (!src) src = a.AddComponent<AudioSource>(); src.spatialBlend = 1; src.minDistance = 1; src.maxDistance = 12;
            var agent = a.AddComponent<NavMeshAgent>(); agent.radius = 0.25f; agent.height = 1.75f; agent.speed = 1.1f; agent.angularSpeed = 360; agent.stoppingDistance = 0.3f;
            // Rocketbox delivers several detail levels: keep the high-detail mesh, and the model's own scrubs, cap and mask.
            foreach (var smr in a.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var n = smr.name.ToLowerInvariant();
                if (n.Contains("poly") && !n.Contains("hipoly")) { smr.gameObject.SetActive(false); continue; }
                var mats = smr.sharedMaterials; for (int i = 0; i < mats.Length; i++) { var key = (mats[i] ? mats[i].name : n).ToLowerInvariant(); mats[i] = key.Contains("head") ? assistantMats["head"] : key.Contains("protection") ? assistantMats["protection"] : assistantMats["body"]; }
                smr.sharedMaterials = mats; smr.updateWhenOffscreen = false;
            }
            p.assistant = a.AddComponent<SurgicalCharacter>(); p.assistant.characterId = SurgeryCharacterIds.Assistant; p.assistant.homeAnchor = AnchorIds.AssistantHome;
            var nm2 = a.AddComponent<NavMeshModifier>(); nm2.ignoreFromBuild = true;
            report.Add("✓ One doctor: Dr. Sara Hassan (clinical scene's instructor) = hospital guide + surgeon role in Room 02; assistant Sami in Room 02");
            return p;
        }

        // ------------------------------------------------------------------ systems: surgical session (inactive until Room 02), bridge, coordinator
        static HospitalCoordinator SetUpSystems(GameObject or, Surgical s, People p)
        {
            var clinical = Object.FindAnyObjectByType<SessionManager>(FindObjectsInactive.Include);
            clinical.hostManaged = true; clinical.autoStart = false;
            var chars = new GameObject("Surgical Characters").transform;
            p.assistant.transform.SetParent(chars, true);
            var act = chars.gameObject.AddComponent<SurgeryCharacterActuator>(); act.surgeon = p.doctorRole; act.assistant = p.assistant;

            var sessionGo = new GameObject("Surgical Session (Room 02)");
            var hud = sessionGo.AddComponent<SurgeryHud>();
            var voice = sessionGo.AddComponent<SurgeryVoiceInput>();
            var ctl = sessionGo.AddComponent<SurgerySessionController>();
            ctl.hud = hud; ctl.actuator = act; ctl.voice = voice; ctl.allowMockMechanics = false; ctl.hostManaged = true;
            hud.useHostConversationPanel = true; hud.hostManaged = true;   // one conversation panel in the whole hospital
            sessionGo.SetActive(false);

            var bridge = new GameObject("Room 02 Bridge").AddComponent<SurgicalRoomBridge>();
            bridge.sessionRoot = sessionGo; bridge.controller = ctl; bridge.mechanics = s.mechanics.GetComponent<SurgeryMechanicsRoot>();
            bridge.doctorRole = p.doctorRole; bridge.assistant = p.assistant; bridge.actuator = act;
            bridge.desktopAdapter = s.mechanics.GetComponentInChildren<SurgicalDesktopInteractionAdapter>(true);
            bridge.robot = s.robot; bridge.highlighter = s.highlighter;

            var hc = new GameObject("Hospital Coordinator").AddComponent<HospitalCoordinator>();
            hc.doctor = p.doctor.GetComponent<MedicalInstructorController>(); hc.doctorMovement = p.doctor.GetComponent<DoctorMovement>();
            hc.guide = p.guide; hc.clinical = clinical; hc.surgery = bridge;
            var staging = Root("Redesign_Staging").transform;
            hc.room01DoorPoint = staging.Find("Doctor Corridor Mark");
            Transform Mark(string n, Vector3 pos, Vector3 look) { var t = new GameObject(n).transform; t.SetParent(hc.transform, false); t.position = pos; t.rotation = Quaternion.LookRotation(look - pos); return t; }
            float vxMid = OrRoot.x - 1.92f;
            hc.doctorWelcomeMark = Mark("Doctor Welcome Mark", DoctorWelcomePos, SpawnPos);
            hc.room02DoorPoint = Mark("Room 02 Door Mark", new Vector3(vxMid + 1.55f, 0, -3.35f), new Vector3(vxMid, 0, -3.9f));
            hc.room02InsidePoint = or.GetComponentsInChildren<SurgeryAnchor>(true).First(x => x.anchorId == AnchorIds.SurgeonObserve).transform;
            hc.corridorMeetPoint = Mark("Corridor Meet Point", new Vector3(3.2f, 0, -3.8f), new Vector3(2.0f, 0, -3.8f));
            hc.room01Zone = new Bounds(new Vector3(0f, 1.5f, 0f), new Vector3(6.0f, 4f, 4.95f));                           // exam room interior
            hc.room02Zone = new Bounds(new Vector3(OrRoot.x, 1.5f, OrRoot.z - 0.33f), new Vector3(7.6f, 4f, 8.46f));        // OR + inner half of the vestibule
            // Directory on the lobby's north wall, facing the student.
            var dirAnchor = new GameObject("Hospital Directory").transform; dirAnchor.SetParent(hc.transform, false);
            dirAnchor.SetPositionAndRotation(new Vector3(12.25f, 1.55f, CorridorZ1 - 0.03f), Quaternion.LookRotation(Vector3.forward));   // a world canvas is read looking along its +Z
            var dir = dirAnchor.gameObject.AddComponent<HospitalDirectory>(); dir.coordinator = hc; hc.directory = dir;
            report.Add("✓ Surgical session layer (Person 4) wired to the real mechanics; hostManaged; starts on entering Room 02, ends on leaving");
            report.Add("✓ Hospital coordinator: zones, room choice (voice / directory / keys / walking in), guided walk, session isolation, summary");
            return hc;
        }

        // ------------------------------------------------------------------ signs (bilingual, same fittings as the clinical corridor)
        static void Signage(GameObject or)
        {
            var corridor = Root("Redesign_Corridor").transform;
            var plates = corridor.Cast<Transform>().Where(t => t.name == "Room_Plate").ToList();
            var erPlate = plates.OrderBy(t => Vector3.Distance(t.position, new Vector3(2.6f, 1.5f, -2.63f))).FirstOrDefault();
            // The v0.2 corridor was entered from its west end; in the hospital the student arrives from the lobby (east). Wall plates
            // are enlarged so each door reads from the corridor, and the hanging signs get text on BOTH faces (TMP is single-sided:
            // from the lobby the original signs showed only their blank back).
            Vector3 erPlateOriginal = erPlate ? erPlate.position : Vector3.zero;
            if (erPlate) { SetTexts(erPlate.gameObject, "Room 01 · Clinical Examination", "الغرفة 01 · الفحص السريري"); erPlate.localScale = Vector3.one * PlateScale; erPlate.position = new Vector3(erPlate.position.x + 0.12f, 1.80f, erPlate.position.z); }   // beside the door, above the sanitiser sign
            if (erPlate) { var orPlate = CloneUnder(erPlate.gameObject, corridor, "Room_Plate_Room02", new Vector3(OrRoot.x - 0.55f, erPlateOriginal.y, erPlateOriginal.z), erPlate.rotation); SetTexts(orPlate, "Room 02 · Surgical Training", "الغرفة 02 · التدريب الجراحي"); }
            // The closed corridor end door (west) carried a decorative "Exam Room 2" plate: in the hospital it looked like a training
            // room that never opens. It is labelled as what it is.
            foreach (var other in plates.Where(p => p != erPlate)) SetTexts(other.gameObject, "Staff only", "للموظفين فقط");
            var erDoorSign = Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(t => t.transform.parent && t.transform.parent.name == "Door_Sign" && t.text.Contains("EXAM ROOM 3"));
            if (erDoorSign) erDoorSign.text = "<b>ROOM 01</b>  ·  CLINICAL EXAMINATION";
            var hanging = corridor.Find("Hanging_Sign");
            if (hanging)
            {
                SetTexts(hanging.gameObject, "Room 01", "الغرفة 01", "Clinical Exam", "الفحص السريري");
                var h2 = CloneUnder(hanging.gameObject, corridor, "Hanging_Sign_Room02", new Vector3(OrRoot.x - 1.92f, hanging.position.y, hanging.position.z), hanging.rotation);
                SetTexts(h2, "Room 02", "الغرفة 02", "Surgical Training", "التدريب الجراحي");
                foreach (var h in new[] { hanging.gameObject, h2 }) { DoubleSided(h); h.transform.localScale = Vector3.one * HangingScale; }
            }
            var welcome = corridor.Find("Welcome_Sign");
            if (welcome)
            {
                var w2 = CloneUnder(welcome.gameObject, corridor, "Welcome_Sign_Lobby", new Vector3(XEnd - 0.03f, 1.75f, -6.6f), Quaternion.LookRotation(Vector3.left) * Quaternion.Inverse(welcome.rotation) * welcome.rotation);
                w2.transform.rotation = Quaternion.LookRotation(Vector3.right);   // panel faces into the lobby (−X)
                SetTexts(w2, "MedSim AI Training Hospital", "مستشفى ميدسيم للتدريب");
                var small = w2.GetComponentsInChildren<TMP_Text>(true).OrderBy(t => t.fontSize).FirstOrDefault(); if (small) ArabicShaper.Apply(small, "Educational simulation · DRAFT — not clinically validated");
            }
            report.Add("✓ Bilingual room signs: Room 01 · Clinical Examination / Room 02 · Surgical Training; lobby sign");
        }

        // ------------------------------------------------------------------ one NavMesh for the whole hospital
        static void Navigation(GameObject or)
        {
            var door = Object.FindAnyObjectByType<MedSimAI.Instruments.HospitalDoor>(FindObjectsInactive.Include);
            if (door)
            {   // a closed exam-room door now blocks paths (the doctor no longer walks through it)
                var leaf = FindDeep(door.transform, "Door_Leaf");   // the swinging leaf (it carves only where it is: closed = blocked, open = free)
                var lc = leaf ? leaf.GetComponent<BoxCollider>() : null;
                if (leaf && lc && !leaf.GetComponent<NavMeshObstacle>()) { var o = leaf.gameObject.AddComponent<NavMeshObstacle>(); o.carving = true; o.carveOnlyStationary = true; o.shape = NavMeshObstacleShape.Box; o.center = lc.center; o.size = lc.size + new Vector3(0.02f, 0, 0.04f); }
                foreach (var t in door.GetComponentsInChildren<Collider>(true)) if (!t.GetComponent<NavMeshModifier>()) t.gameObject.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
            }
            var rig = Root("XR Origin (XR Rig)"); if (rig && !rig.GetComponent<NavMeshModifier>()) rig.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
            var surface = Object.FindObjectsByType<NavMeshSurface>(FindObjectsInactive.Include, FindObjectsSortMode.None).First(x => x.enabled && x.transform.root.name == "Redesign_Staging");
            Physics.SyncTransforms();   // edit mode: colliders of moved instances must be at their new poses for the bake
            surface.BuildNavMesh();
            var path = Gen + "/MedSim_Hospital_NavMesh.asset";
            AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(surface.navMeshData, path);
            surface.navMeshData = AssetDatabase.LoadAssetAtPath<NavMeshData>(path);
            var tri = NavMesh.CalculateTriangulation();
            report.Add($"✓ One hospital NavMesh rebaked ({tri.indices.Length / 3} triangles); exam door carves when closed; OR doors carve when closed");
        }

        static void SetUpBuild()
        {
            var list = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(HospitalScene, true), new EditorBuildSettingsScene(ErScene, true) };
            foreach (var s in EditorBuildSettings.scenes) if (!list.Any(x => x.path == s.path) && s.path.Contains("SampleScene") == false) list.Add(new EditorBuildSettingsScene(s.path, false));
            EditorBuildSettings.scenes = list.ToArray();
            report.Add("✓ Build scenes: MedSim_Hospital (start), MedSim_ER (standalone clinical scene, launch with -clinical-only)");
        }
    }
}
