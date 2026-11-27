using System.IO;
using MedSimAI.Surgery.Shared;
using MedSimAI.Surgery.Shared.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MedSimAI.Surgery.Mechanics.Editor
{
    public static class SurgeryMechanicsBuilder
    {
        public const string MechanicsPrefabPath = "Assets/MedSimAI/Surgery/Mechanics/Prefabs/SurgeryMechanics.prefab";
        public const string TestScenePath = "Assets/MedSimAI/Surgery/Scenes/SurgeryMechanics_Test.unity";
        public const string ContractScenePath = "Assets/MedSimAI/Surgery/Scenes/Mechanics_Test.unity";
        public const string OperatingRoomPrefabPath = "Assets/MedSimAI/Surgery/Environment/Prefabs/OperatingRoom.prefab";

        [MenuItem("MedSim AI/Surgery/Build Mechanics Prefab and Scene")]
        public static void BuildAll()
        {
            EnsureDirectories();
            var prefab = BuildSurgeryMechanicsPrefab();
            BuildMechanicsTestScene(prefab);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[SurgeryMechanicsBuilder] Successfully built SurgeryMechanics.prefab and test scenes.");
        }

        public static void EnsureDirectories()
        {
            if (!Directory.Exists("Assets/MedSimAI/Surgery/Mechanics/Prefabs"))
                Directory.CreateDirectory("Assets/MedSimAI/Surgery/Mechanics/Prefabs");
            if (!Directory.Exists("Assets/MedSimAI/Surgery/Scenes"))
                Directory.CreateDirectory("Assets/MedSimAI/Surgery/Scenes");
        }

        public static GameObject BuildSurgeryMechanicsPrefab()
        {
            EnsureDirectories();

            var go = new GameObject("SurgeryMechanics");
            go.AddComponent<SurgeryEventBusHost>();
            var root = go.AddComponent<SurgeryMechanicsRoot>();
            var scenarioEngine = go.AddComponent<SurgeryScenarioEngine>();
            var instrumentCtrl = go.AddComponent<SurgicalInstrumentController>();
            var anatomyCtrl = go.AddComponent<AnatomyInteractionController>();
            var implantCtrl = go.AddComponent<ImplantPlacementController>();
            var robotCtrl = go.AddComponent<SurgicalRobotController>();
            var resetCtrl = go.AddComponent<SurgeryResetController>();
            var desktopAdapter = go.AddComponent<SurgicalDesktopInteractionAdapter>();
            var feedback = go.AddComponent<SurgeryInteractionFeedback>();

            // Save prefab
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, MechanicsPrefabPath);
            Object.DestroyImmediate(go);
            return prefab;
        }

        public static void BuildMechanicsTestScene(GameObject mechanicsPrefab = null)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 1. Environment / Anchors Root
            var orPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(OperatingRoomPrefabPath);
            GameObject envInstance = null;
            if (orPrefab != null)
            {
                envInstance = (GameObject)PrefabUtility.InstantiatePrefab(orPrefab);
                envInstance.name = "OperatingRoom";
            }
            else
            {
                envInstance = new GameObject("Environment_Fallback");
                CreateAnchor(AnchorIds.OperatingRoomRoot, Vector3.zero, envInstance.transform);
                CreateAnchor(AnchorIds.PlayerSpawn, new Vector3(0, 0, -3), envInstance.transform);
                CreateAnchor(AnchorIds.OperatingTable, new Vector3(0, 0.95f, 0), envInstance.transform);
                CreateAnchor(AnchorIds.PatientRoot, new Vector3(0, 0.95f, 0), envInstance.transform);
                CreateAnchor(AnchorIds.SurgicalSiteKneeR, new Vector3(-0.35f, 1.0f, 0.2f), envInstance.transform);
                CreateAnchor(AnchorIds.InstrumentTable, new Vector3(-1.2f, 0.95f, 0.5f), envInstance.transform);
                CreateAnchor(AnchorIds.ImplantTable, new Vector3(-1.2f, 0.95f, -0.5f), envInstance.transform);
                CreateAnchor(AnchorIds.RobotBase, new Vector3(1.2f, 0, 0), envInstance.transform);
                CreateAnchor(AnchorIds.StudentOperatingPosition, new Vector3(-0.85f, 0, 0.2f), envInstance.transform);
            }

            // 2. Light & Camera
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            lightGo.transform.rotation = Quaternion.Euler(50, -30, 0);

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            camGo.transform.position = new Vector3(-0.85f, 1.65f, 0.2f);
            camGo.transform.rotation = Quaternion.Euler(20, 75, 0);

            // 3. Anatomy Patient & Targets
            var patientAnchor = SurgeryAnchor.Find(AnchorIds.SurgicalSiteKneeR);
            Vector3 kneePos = patientAnchor != null ? patientAnchor.position : new Vector3(-0.35f, 1.0f, 0.2f);

            var kneeRoot = new GameObject("KneeAnatomy_R");
            kneeRoot.transform.position = kneePos;

            CreateAnatomyTarget(kneeRoot.transform, AnatomyTargetIds.SiteMarking, new Vector3(0, 0.05f, 0), new Vector3(0.08f, 0.02f, 0.08f));
            CreateAnatomyTarget(kneeRoot.transform, AnatomyTargetIds.IncisionLine, new Vector3(0, 0.05f, 0), new Vector3(0.02f, 0.02f, 0.15f));
            CreateAnatomyTarget(kneeRoot.transform, AnatomyTargetIds.JointCapsule, new Vector3(0, 0.03f, 0), new Vector3(0.06f, 0.03f, 0.12f));
            CreateAnatomyTarget(kneeRoot.transform, AnatomyTargetIds.DistalFemur, new Vector3(0, 0.08f, 0.10f), new Vector3(0.09f, 0.08f, 0.09f));
            CreateAnatomyTarget(kneeRoot.transform, AnatomyTargetIds.ProximalTibia, new Vector3(0, -0.05f, -0.10f), new Vector3(0.09f, 0.08f, 0.09f));
            CreateAnatomyTarget(kneeRoot.transform, AnatomyTargetIds.Wound, new Vector3(0, 0.05f, 0), new Vector3(0.03f, 0.02f, 0.16f));

            // Placement target poses
            CreatePlacementTarget(kneeRoot.transform, "PlacementTarget_" + ImplantIds.FemoralTrial, new Vector3(0, 0.08f, 0.10f));
            CreatePlacementTarget(kneeRoot.transform, "PlacementTarget_" + ImplantIds.TibialTrial, new Vector3(0, -0.05f, -0.10f));
            CreatePlacementTarget(kneeRoot.transform, "PlacementTarget_" + ImplantIds.FemoralComponent, new Vector3(0, 0.08f, 0.10f));
            CreatePlacementTarget(kneeRoot.transform, "PlacementTarget_" + ImplantIds.TibialTray, new Vector3(0, -0.05f, -0.10f));
            CreatePlacementTarget(kneeRoot.transform, "PlacementTarget_" + ImplantIds.PolyInsert, new Vector3(0, -0.03f, -0.10f));

            // 4. Instruments
            var instTableAnchor = SurgeryAnchor.Find(AnchorIds.InstrumentTable);
            Vector3 tableBase = instTableAnchor != null ? instTableAnchor.position + Vector3.up * 0.05f : new Vector3(-1.2f, 1.0f, 0.5f);

            var instRoot = new GameObject("InstrumentTray");
            instRoot.transform.position = tableBase;

            float xOff = -0.3f;
            foreach (var instId in InstrumentIds.All)
            {
                if (instId == InstrumentIds.RobotArm) continue;
                CreateInstrumentFixture(instRoot.transform, instId, tableBase + new Vector3(xOff, 0, 0));
                xOff += 0.08f;
            }

            // 5. Implants
            var impTableAnchor = SurgeryAnchor.Find(AnchorIds.ImplantTable);
            Vector3 impBase = impTableAnchor != null ? impTableAnchor.position + Vector3.up * 0.05f : new Vector3(-1.2f, 1.0f, -0.5f);

            var impRoot = new GameObject("ImplantTray");
            impRoot.transform.position = impBase;

            float zOff = -0.2f;
            foreach (var impId in ImplantIds.All)
            {
                CreateImplantFixture(impRoot.transform, impId, impBase + new Vector3(0, 0, zOff));
                zOff += 0.1f;
            }

            // 6. SurgeryMechanics Master Instance
            GameObject mechInstance = null;
            if (mechanicsPrefab != null)
            {
                mechInstance = (GameObject)PrefabUtility.InstantiatePrefab(mechanicsPrefab);
            }
            else
            {
                var loaded = AssetDatabase.LoadAssetAtPath<GameObject>(MechanicsPrefabPath);
                if (loaded != null) mechInstance = (GameObject)PrefabUtility.InstantiatePrefab(loaded);
            }
            if (mechInstance == null)
            {
                mechInstance = new GameObject("SurgeryMechanics");
                mechInstance.AddComponent<SurgeryEventBusHost>();
                mechInstance.AddComponent<SurgeryMechanicsRoot>();
            }

            // Save scenes
            EditorSceneManager.SaveScene(scene, TestScenePath);
            // Also save contract path alias
            File.Copy(TestScenePath, ContractScenePath, true);
            AssetDatabase.ImportAsset(ContractScenePath);
        }

        private static GameObject CreateAnchor(string anchorId, Vector3 pos, Transform parent)
        {
            var go = new GameObject(anchorId);
            go.transform.position = pos;
            go.transform.SetParent(parent);
            var anchor = go.AddComponent<SurgeryAnchor>();
            anchor.anchorId = anchorId;
            return go;
        }

        private static GameObject CreateAnatomyTarget(Transform parent, string targetId, Vector3 localPos, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = targetId;
            go.transform.SetParent(parent);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;

            var tag = go.AddComponent<SurgeryAnatomyTag>();
            tag.targetId = targetId;
            go.AddComponent<AnatomyStateController>();
            go.AddComponent<AnatomyContactTrigger>();

            var col = go.GetComponent<Collider>();
            if (col != null) col.isTrigger = true;

            var rend = go.GetComponent<Renderer>();
            if (rend != null) rend.enabled = false; // invisible trigger volume

            return go;
        }

        private static GameObject CreatePlacementTarget(Transform parent, string name, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent);
            go.transform.localPosition = localPos;
            return go;
        }

        private static GameObject CreateInstrumentFixture(Transform parent, string instrumentId, Vector3 worldPos)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = instrumentId;
            go.transform.SetParent(parent);
            go.transform.position = worldPos;
            go.transform.localScale = new Vector3(0.02f, 0.08f, 0.02f);
            go.transform.rotation = Quaternion.Euler(90, 0, 0);

            var tag = go.AddComponent<SurgeryInstrumentTag>();
            tag.instrumentId = instrumentId;
            tag.displayNameEn = instrumentId.Replace("inst.", "");

            var tip = new GameObject("Tip");
            tip.transform.SetParent(go.transform);
            tip.transform.localPosition = new Vector3(0, 1.0f, 0);
            tag.tip = tip.transform;

            var tipCol = tip.AddComponent<SphereCollider>();
            tipCol.radius = 0.02f;
            tipCol.isTrigger = true;
            tip.AddComponent<AnatomyContactTrigger>();

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.3f;

            // Slot anchor for reset
            var slotAnchor = new GameObject(AnchorIds.InstrumentSlot(instrumentId));
            slotAnchor.transform.position = worldPos;
            slotAnchor.transform.rotation = go.transform.rotation;
            var sa = slotAnchor.AddComponent<SurgeryAnchor>();
            sa.anchorId = AnchorIds.InstrumentSlot(instrumentId);

            return go;
        }

        private static GameObject CreateImplantFixture(Transform parent, string implantId, Vector3 worldPos)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = implantId;
            go.transform.SetParent(parent);
            go.transform.position = worldPos;
            go.transform.localScale = new Vector3(0.06f, 0.03f, 0.06f);

            var tag = go.AddComponent<SurgeryImplantTag>();
            tag.implantId = implantId;

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.2f;

            return go;
        }
    }
}
