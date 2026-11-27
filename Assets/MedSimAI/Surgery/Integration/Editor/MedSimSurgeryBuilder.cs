using System.Collections.Generic;
using System.IO;
using System.Linq;
using MedSimAI.AI;
using MedSimAI.Characters;
using MedSimAI.Surgery.Characters;
using MedSimAI.Surgery.Integration;
using MedSimAI.Surgery.Shared;
using MedSimAI.Surgery.Shared.Unity;
using MedSimAI.Surgery.AI;
using MedSimAI.Surgery.UI;
using MedSimAI.XR;
using Unity.AI.Navigation;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace MedSimAI.Surgery.EditorTools
{
    /// <summary>
    /// Builds the surgical module's scenes from code (Person 4 / integration lead). Menu: MedSim AI → Surgery.
    ///  * ScenarioSelect.unity — bilingual scenario menu (first scene in the build)
    ///  * SurgeryAI_Test.unity — placeholder OR + mock mechanics, for AI/assessment testing without other branches
    ///  * OperatingRoom.unity  — FINAL integration: uses the contract prefabs from Persons 1–3 when present and reports
    ///    every missing piece (placeholders are created only so the scene still runs; they are clearly named).
    /// </summary>
    public static class MedSimSurgeryBuilder
    {
        const string Scenes = "Assets/MedSimAI/Surgery/Scenes";
        const string XrRigPrefab = "Assets/Samples/XR Interaction Toolkit/3.7.0/Starter Assets/Prefabs/XR Origin (XR Rig).prefab";
        const string DoctorFbx = "Assets/MedSimAI/Characters/v2/Doctor/DrSaraHassan.fbx", AssistantFbx = "Assets/MedSimAI/Characters/v2/Patient/AdamReyes.fbx";
        const string DoctorCtrl = "Assets/MedSimAI/Characters/v2/Animations/DrHassan.controller";
        // Contract prefab paths (Docs/Surgery/SURGERY_CONTRACT.md §6)
        const string EnvPrefab = "Assets/MedSimAI/Surgery/Environment/Prefabs/OperatingRoom.prefab", EnvPrefabAlt = "Assets/MedSimAI/Surgery/Environment/Prefabs/OperatingRoom_Env.prefab";
        const string PatientPrefab = "Assets/MedSimAI/Surgery/Anatomy/Prefabs/SurgicalPatient.prefab";
        const string InstrumentDir = "Assets/MedSimAI/Surgery/Instruments/Prefabs";
        const string MechanicsPrefab = "Assets/MedSimAI/Surgery/Mechanics/Prefabs/SurgeryMechanics.prefab";

        [MenuItem("MedSim AI/Surgery/Build All Surgery Scenes")]
        public static void BuildAll() { BuildMenuScene(); BuildTestScene(); BuildOperatingRoom(); AddScenesToBuild(); }

        [MenuItem("MedSim AI/Surgery/1. Build Scenario Select Scene")]
        public static string BuildMenuScene()
        {
            Directory.CreateDirectory(Scenes);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var light = new GameObject("Directional Light").AddComponent<Light>(); light.type = LightType.Directional; light.transform.rotation = Quaternion.Euler(50, -30, 0);
            BuildXR(Vector3.zero, Quaternion.identity, withPlayerController: false, background: new Color(0.04f, 0.07f, 0.12f));
            new GameObject("Scenario Menu").AddComponent<ScenarioSelectMenu>();
            var path = Scenes + "/ScenarioSelect.unity"; EditorSceneManager.SaveScene(scene, path);
            return "Built " + path;
        }

        [MenuItem("MedSim AI/Surgery/2. Build SurgeryAI Test Scene (mock)")]
        public static string BuildTestScene()
        {
            var report = new List<string>();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var env = PlaceholderRoom(report);
            EnvironmentAnchorAdapter.EnsureDerived();
            PlaceholderPatient(report);
            PlaceholderInstruments(report);
            Systems(mock: true);
            Bake(env);
            var path = Scenes + "/SurgeryAI_Test.unity"; EditorSceneManager.SaveScene(scene, path);
            return "Built " + path + "\n" + string.Join("\n", report);
        }

        [MenuItem("MedSim AI/Surgery/3. Build Operating Room (integration)")]
        public static string BuildOperatingRoom()
        {
            var report = new List<string>();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Transform env;
            var envP = AssetDatabase.LoadAssetAtPath<GameObject>(EnvPrefab); if (!envP) envP = AssetDatabase.LoadAssetAtPath<GameObject>(EnvPrefabAlt);
            if (envP) { env = ((GameObject)PrefabUtility.InstantiatePrefab(envP)).transform; report.Add("✓ Environment prefab (Person 1)"); }
            else { env = PlaceholderRoom(report); report.Add("✗ MISSING " + EnvPrefab + " (Person 1) — placeholder room used"); }

            var patP = AssetDatabase.LoadAssetAtPath<GameObject>(PatientPrefab);
            var patientRoot = SurgeryAnchorFind(AnchorIds.PatientRoot);
            if (patP) { var p = (GameObject)PrefabUtility.InstantiatePrefab(patP); if (patientRoot) { p.transform.SetParent(patientRoot, false); } report.Add("✓ Surgical patient prefab (Person 2)"); }
            else { PlaceholderPatient(report); report.Add("✗ MISSING " + PatientPrefab + " (Person 2) — placeholder patient used"); }

            int found = 0;
            foreach (var id in InstrumentIds.All)
            {
                var ip = AssetDatabase.LoadAssetAtPath<GameObject>($"{InstrumentDir}/{id}.prefab"); if (!ip) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(ip); var slot = SurgeryAnchorFind(AnchorIds.InstrumentSlot(id));
                if (slot) go.transform.SetPositionAndRotation(slot.position, slot.rotation); found++;
            }
            foreach (var id in ImplantIds.All)
            {
                var ip = AssetDatabase.LoadAssetAtPath<GameObject>($"{InstrumentDir}/Implants/{id}.prefab"); if (!ip) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(ip); var t = SurgeryAnchorFind(AnchorIds.ImplantTable); if (t) go.transform.position = t.position + Vector3.up * 0.03f;
            }
            if (found > 0) report.Add($"✓ {found} instrument prefabs (Person 2)"); else { PlaceholderInstruments(report); report.Add("✗ MISSING instrument prefabs in " + InstrumentDir + " (Person 2) — placeholders used"); }

            var mechP = AssetDatabase.LoadAssetAtPath<GameObject>(MechanicsPrefab);
            if (mechP) { PrefabUtility.InstantiatePrefab(mechP); report.Add("✓ Mechanics prefab (Person 3)"); }
            else report.Add("✗ MISSING " + MechanicsPrefab + " (Person 3) — the session falls back to MOCK mechanics (labelled, not scored as real)");

            report.Add(EnvironmentAnchorAdapter.EnsureDerived().Trim());
            Systems(mock: !mechP);
            if (!envP) Bake(env);
            var missingAnchors = new[] { AnchorIds.PlayerSpawn, AnchorIds.OperatingTable, AnchorIds.PatientRoot, AnchorIds.InstrumentTable, AnchorIds.SurgeonObserve, AnchorIds.AssistantHome, AnchorIds.AssistantAtTable, AnchorIds.StudentOperatingPosition }
                .Where(a => !SurgeryAnchorFind(a)).ToList();
            report.Add(missingAnchors.Count == 0 ? "✓ All required anchors present" : "✗ Missing anchors: " + string.Join(", ", missingAnchors));
            var path = Scenes + "/OperatingRoom.unity"; EditorSceneManager.SaveScene(scene, path);
            var msg = "Built " + path + "\n" + string.Join("\n", report);
            Debug.Log("[Surgery] Integration report\n" + msg);
            File.WriteAllText("Docs/Surgery/Integration/LAST_SCENE_BUILD.txt", msg);
            return msg;
        }

        [MenuItem("MedSim AI/Surgery/4. Add Scenes To Build (menu first)")]
        public static string AddScenesToBuild()
        {
            var wanted = new[] { Scenes + "/ScenarioSelect.unity", "Assets/MedSimAI/Scenes/MedSim_ER.unity", Scenes + "/OperatingRoom.unity" };
            var list = wanted.Where(File.Exists).Select(p => new EditorBuildSettingsScene(p, true)).ToList();
            foreach (var s in EditorBuildSettings.scenes) if (!list.Any(x => x.path == s.path)) list.Add(s);   // keep existing entries after ours
            EditorBuildSettings.scenes = list.ToArray();
            return "Build scenes: " + string.Join(", ", list.Select(s => Path.GetFileNameWithoutExtension(s.path)));
        }

        // ------------------------------------------------------------------ systems (characters, session, HUD, voice, XR)
        static void Systems(bool mock)
        {
            var spawn = SurgeryAnchorFind(AnchorIds.PlayerSpawn);
            BuildXR(spawn ? spawn.position : new Vector3(-1.6f, 0, -2.2f), spawn ? spawn.rotation : Quaternion.Euler(0, 30, 0), true, new Color(0.05f, 0.08f, 0.12f));
            var chars = new GameObject("Surgical Characters").transform;
            var surgeon = Character(chars, "Surgeon - Dr Lina Haddad", DoctorFbx, SurgeryCharacterIds.Surgeon, AnchorIds.SurgeonObserve, Persona.Instructor, new Color(0.20f, 0.45f, 0.52f));
            var assistant = Character(chars, "Assistant - Sami", AssistantFbx, SurgeryCharacterIds.Assistant, AnchorIds.AssistantHome, Persona.Instructor, new Color(0.18f, 0.36f, 0.55f));
            var sys = new GameObject("Surgery Systems");
            sys.AddComponent<SurgeryEventBusHost>();
            var act = sys.AddComponent<SurgeryCharacterActuator>(); act.surgeon = surgeon; act.assistant = assistant;
            var hud = sys.AddComponent<SurgeryHud>();
            var voice = sys.AddComponent<SurgeryVoiceInput>();
            var ctl = sys.AddComponent<SurgerySessionController>(); ctl.hud = hud; ctl.actuator = act; ctl.voice = voice; ctl.allowMockMechanics = true;
            if (mock) sys.AddComponent<MockMechanicsDriver>().controller = ctl;
        }

        static SurgicalCharacter Character(Transform parent, string name, string fbx, string id, string anchor, Persona persona, Color scrubs)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            GameObject go;
            if (model) { go = (GameObject)PrefabUtility.InstantiatePrefab(model); go.name = name; }
            else { go = GameObject.CreatePrimitive(PrimitiveType.Capsule); go.name = name + " (PLACEHOLDER — model missing)"; go.transform.localScale = new Vector3(0.45f, 0.88f, 0.45f); }
            go.transform.SetParent(parent, true);
            var a = SurgeryAnchorFind(anchor);
            if (a) go.transform.SetPositionAndRotation(new Vector3(a.position.x, 0, a.position.z), a.rotation);
            var anim = go.GetComponentInChildren<Animator>();
            var ctrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(DoctorCtrl);
            if (anim && ctrl) anim.runtimeAnimatorController = ctrl;
            if (anim) anim.applyRootMotion = false;
            var speech = go.GetComponent<CharacterSpeechController>(); if (!speech) speech = go.AddComponent<CharacterSpeechController>();   // no '??' on Unity objects (fake null)
            speech.persona = persona; speech.displayName = id == SurgeryCharacterIds.Surgeon ? "Dr. Lina Haddad" : "Sami";
            var src = go.GetComponent<AudioSource>(); if (!src) src = go.AddComponent<AudioSource>(); src.spatialBlend = 1; src.minDistance = 1; src.maxDistance = 12;
            var agent = go.AddComponent<NavMeshAgent>(); agent.radius = 0.25f; agent.height = 1.75f; agent.speed = 1.1f; agent.angularSpeed = 360; agent.stoppingDistance = 0.3f;
            var attire = go.AddComponent<SurgicalAttire>(); attire.scrubs = scrubs;
            var c = go.AddComponent<SurgicalCharacter>(); c.characterId = id; c.homeAnchor = anchor;
            return c;
        }

        static void BuildXR(Vector3 pos, Quaternion rot, bool withPlayerController, Color background)
        {
            new GameObject("XR Interaction Manager").AddComponent<XRInteractionManager>();
            var es = new GameObject("EventSystem"); es.AddComponent<UnityEngine.EventSystems.EventSystem>(); es.AddComponent<XRUIInputModule>();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(XrRigPrefab);
            if (!prefab) { var cam = new GameObject("Main Camera").AddComponent<Camera>(); cam.tag = "MainCamera"; cam.transform.SetPositionAndRotation(pos + Vector3.up * 1.65f, rot); cam.gameObject.AddComponent<AudioListener>(); Debug.LogWarning("[Surgery] XR rig prefab not found; desktop camera only."); return; }
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab); rig.transform.SetPositionAndRotation(pos, rot);
            foreach (var t in rig.GetComponentsInChildren<Transform>(true)) if (t.name == "Jump" || t.name == "Grab Move" || t.name == "Climb" || t.name == "Climb Teleport") t.gameObject.SetActive(false);
            var origin = rig.GetComponent<XROrigin>(); var camera = origin.Camera;
            camera.nearClipPlane = 0.02f; camera.farClipPlane = 60f; camera.backgroundColor = background;
            if (!camera.GetComponent<AudioListener>()) camera.gameObject.AddComponent<AudioListener>();
            var data = camera.GetComponent<UniversalAdditionalCameraData>(); if (!data) data = camera.gameObject.AddComponent<UniversalAdditionalCameraData>(); data.renderPostProcessing = true;
            if (!withPlayerController) return;
            var spawn = new GameObject("Player Spawn (runtime)").transform; spawn.SetPositionAndRotation(pos, rot);
            var ctl = rig.AddComponent<XRPlayerController>(); ctl.origin = origin; ctl.spawnPoint = spawn;
            var move = rig.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Move"); if (move) ctl.smoothMoveProvider = move.gameObject;
        }

        // ------------------------------------------------------------------ placeholders (clearly named; replaced by Persons 1–2's prefabs)
        static Transform Anchor(Transform parent, string id, Vector3 pos, float yaw = 0)
        {
            var go = new GameObject(id); go.transform.SetParent(parent, false); go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            go.AddComponent<SurgeryAnchor>().anchorId = id; return go.transform;
        }
        static Transform SurgeryAnchorFind(string id) => SurgeryAnchor.Find(id);   // contract 1.1: also resolves alias names (Person 1's IntegrationAnchors)

        static GameObject Box(Transform p, string name, Vector3 pos, Vector3 size, Color c)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.name = name; g.transform.SetParent(p, false); g.transform.position = pos; g.transform.localScale = size;
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")); m.SetColor("_BaseColor", c); g.GetComponent<Renderer>().sharedMaterial = m; return g;
        }

        static Transform PlaceholderRoom(List<string> report)
        {
            var root = new GameObject("PLACEHOLDER Operating Room (replace with Person 1 prefab)").transform;
            var or = Anchor(root, AnchorIds.OperatingRoomRoot, Vector3.zero);
            Box(root, "Floor", new Vector3(0, -0.05f, 0), new Vector3(7, 0.1f, 8), new Color(0.55f, 0.62f, 0.62f)).isStatic = true;
            foreach (var (n, p, s) in new[] { ("Wall N", new Vector3(0, 1.5f, 4), new Vector3(7, 3, 0.1f)), ("Wall S", new Vector3(0, 1.5f, -4), new Vector3(7, 3, 0.1f)), ("Wall E", new Vector3(3.5f, 1.5f, 0), new Vector3(0.1f, 3, 8)), ("Wall W", new Vector3(-3.5f, 1.5f, 0), new Vector3(0.1f, 3, 8)) })
                Box(root, n, p, s, new Color(0.70f, 0.80f, 0.80f)).isStatic = true;
            Box(root, "Operating Table", new Vector3(0, 0.45f, 0), new Vector3(0.6f, 0.9f, 2.0f), new Color(0.3f, 0.32f, 0.35f));
            Box(root, "Instrument Table", new Vector3(-1.2f, 0.45f, -0.6f), new Vector3(0.9f, 0.9f, 0.5f), new Color(0.75f, 0.78f, 0.80f));
            Box(root, "Implant Table", new Vector3(1.3f, 0.45f, -0.9f), new Vector3(0.6f, 0.9f, 0.45f), new Color(0.75f, 0.78f, 0.80f));
            var l = new GameObject("OR Light").AddComponent<Light>(); l.type = LightType.Spot; l.transform.SetParent(root); l.transform.SetPositionAndRotation(new Vector3(0, 2.6f, 0), Quaternion.Euler(90, 0, 0)); l.spotAngle = 60; l.intensity = 25; l.range = 6;
            var d = new GameObject("Ambient Light").AddComponent<Light>(); d.type = LightType.Directional; d.transform.SetParent(root); d.transform.rotation = Quaternion.Euler(60, 20, 0); d.intensity = 0.7f;
            // Patient's head at +Z; patient's RIGHT side = world −X (contract §4).
            Anchor(root, AnchorIds.PlayerSpawn, new Vector3(-1.6f, 0, -2.6f), 20);
            Anchor(root, AnchorIds.OperatingTable, new Vector3(0, 0.9f, 0));
            Anchor(root, AnchorIds.PatientRoot, new Vector3(0, 0.9f, 0));
            Anchor(root, AnchorIds.InstrumentTable, new Vector3(-1.2f, 0.9f, -0.6f));
            Anchor(root, AnchorIds.ImplantTable, new Vector3(1.3f, 0.9f, -0.9f));
            Anchor(root, AnchorIds.SurgeonObserve, new Vector3(0.75f, 0, -0.1f), -90);
            Anchor(root, AnchorIds.AssistantHome, new Vector3(-0.9f, 0, 0.9f), 120);
            Anchor(root, AnchorIds.AssistantAtTable, new Vector3(-1.2f, 0, -1.05f), 0);
            Anchor(root, AnchorIds.StudentOperatingPosition, new Vector3(-0.65f, 0, -0.45f), 90);
            Anchor(root, AnchorIds.OverheadMonitor, new Vector3(1.0f, 1.9f, 1.2f), -135);
            report.Add("placeholder room + anchors");
            return root;
        }

        static void PlaceholderPatient(List<string> report)
        {
            var root = SurgeryAnchorFind(AnchorIds.PatientRoot); if (!root) return;
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule); body.name = "PLACEHOLDER Surgical Patient (replace with Person 2 prefab)";
            body.transform.SetParent(root, false); body.transform.localPosition = new Vector3(0, 0.12f, 0); body.transform.localRotation = Quaternion.Euler(90, 0, 0); body.transform.localScale = new Vector3(0.45f, 0.85f, 0.25f);
            var drape = GameObject.CreatePrimitive(PrimitiveType.Cube); drape.name = "Drape"; drape.transform.SetParent(root, false); drape.transform.localPosition = new Vector3(0, 0.25f, 0.1f); drape.transform.localScale = new Vector3(0.75f, 0.02f, 1.6f);
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); mat.SetColor("_BaseColor", new Color(0.25f, 0.5f, 0.6f)); drape.GetComponent<Renderer>().sharedMaterial = mat;
            var site = Anchor(root, AnchorIds.SurgicalSiteKneeR, root.position + new Vector3(-0.12f, 0.2f, -0.45f));
            foreach (var t in AnatomyTargetIds.All) { var g = GameObject.CreatePrimitive(PrimitiveType.Sphere); g.name = t; g.transform.SetParent(site, false); g.transform.localScale = Vector3.one * 0.06f; g.GetComponent<Collider>().isTrigger = true; g.AddComponent<SurgeryAnatomyTag>().targetId = t; }
            report.Add("placeholder patient + anatomy targets");
        }

        static void PlaceholderInstruments(List<string> report)
        {
            var table = SurgeryAnchorFind(AnchorIds.InstrumentTable); if (!table) return;
            var root = new GameObject("PLACEHOLDER Instruments (replace with Person 2 prefabs)").transform;
            int i = 0;
            foreach (var id in InstrumentIds.All.Where(x => x != InstrumentIds.RobotArm))
            {
                var pos = table.position + new Vector3(-0.38f + (i % 6) * 0.15f, 0.02f, -0.12f + (i / 6) * 0.22f);
                var slot = Anchor(root, AnchorIds.InstrumentSlot(id), pos);
                var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.name = id; g.transform.SetParent(root, false); g.transform.position = pos + Vector3.up * 0.01f; g.transform.localScale = new Vector3(0.025f, 0.015f, 0.16f);
                var tag = g.AddComponent<SurgeryInstrumentTag>(); tag.instrumentId = id;
                var rb = g.AddComponent<Rigidbody>(); rb.mass = 0.2f;
                i++;
            }
            report.Add("placeholder instruments with tags + slots");
        }

        static void Bake(Transform env)
        {
            var surface = env.gameObject.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children; surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();
        }
    }
}
