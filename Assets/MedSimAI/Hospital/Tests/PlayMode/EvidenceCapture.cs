using System.Collections;
using System.IO;
using System.Linq;
using MedSimAI.AI;
using MedSimAI.Surgery.AI;
using MedSimAI.Surgery.Mechanics;
using MedSimAI.Surgery.Shared;
using MedSimAI.Surgery.Shared.Unity;
using NUnit.Framework;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MedSimAI.Hospital.PlayTests
{
    /// <summary>
    /// [Explicit] evidence capture (not part of the normal run): renders first-person (student head) and side views of the
    /// operating-room teaching features into Screenshots/Surgery/RealismUpgrade/increment2/. Offline, no billable calls.
    /// </summary>
    [Explicit]
    public class EvidenceCapture
    {
        static string Dir => Path.GetFullPath(Path.Combine(Application.dataPath, "../Screenshots/Surgery/RealismUpgrade/increment2"));
        HospitalCoordinator HC => HospitalCoordinator.Instance;
        Transform Rig => Object.FindAnyObjectByType<XROrigin>().transform;

        [UnitySetUp]
        public IEnumerator Load()
        {
            AIProviderManager.ForceOffline = true; MedSimAI.XR.DisplayModeManager.DisableAutoDesktop = true;
            yield return SceneManager.LoadSceneAsync("MedSim_Hospital");
            float t = 0; while ((HC == null || !HC.Welcomed) && t < 20f) { t += Time.unscaledDeltaTime; yield return null; }
            Directory.CreateDirectory(Dir);
        }
        [UnityTearDown] public IEnumerator Unload() { Time.timeScale = 1; yield return null; }

        public static void Shot(string name, Vector3? pos = null, Vector3? look = null, float fov = 60f)
        {
            var main = Camera.main; var go = new GameObject("evidence cam"); var cam = go.AddComponent<Camera>();
            if (main) { cam.CopyFrom(main); }
            cam.stereoTargetEye = StereoTargetEyeMask.None; cam.fieldOfView = fov; cam.nearClipPlane = 0.02f;
            if (pos.HasValue) { cam.transform.position = pos.Value; cam.transform.LookAt(look ?? pos.Value + Vector3.forward); }
            else if (main) cam.transform.SetPositionAndRotation(main.transform.position, main.transform.rotation);
            var d = go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>(); d.renderPostProcessing = true;
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 }; cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
            RenderTexture.active = rt; var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply(); RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(Dir, name + ".jpg"), tex.EncodeToJPG(88));
            Object.Destroy(tex); rt.Release(); Object.Destroy(go);
        }

        IEnumerator EnterOR()
        {
            var cc = Rig.GetComponent<CharacterController>(); if (cc) cc.enabled = false;
            var p = SurgeryAnchor.Find(AnchorIds.StudentOperatingPosition).position; var k = SurgeryAnchor.Find(AnchorIds.SurgicalSiteKneeR).position; var look = k - p; look.y = 0;
            Rig.SetPositionAndRotation(p, Quaternion.LookRotation(look)); if (cc) cc.enabled = true; Physics.SyncTransforms();
            float t = 0; while ((HC.SessionRoom != HospitalRoomId.Room02 || HC.surgery.controller.Core == null || !HC.surgery.controller.Core.Active || string.IsNullOrEmpty(HC.surgery.mechanics.CurrentObjectiveId)) && t < 20f) { t += Time.deltaTime; yield return null; }
            // in the app Dr. Hassan walks in with the student; here the student was placed directly, so she is placed at her OR spot
            var spot = SurgeryAnchor.Find(AnchorIds.SurgeonObserve); var agent = HC.doctor.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (spot && agent && agent.enabled) agent.Warp(spot.position); else if (spot) HC.doctor.transform.position = spot.position;
            if (HC.surgery.desktopAdapter) HC.surgery.desktopAdapter.enabled = true;   // desktop interaction, as in the Desktop build
            Camera.main.transform.position = p + Vector3.up * 1.65f;                     // standing eye height (no headset in tests)
            yield return new WaitForSeconds(0.5f);
        }
        static Vector3 KneeLook => SurgeryAnchor.Find(AnchorIds.SurgicalSiteKneeR).position + Vector3.down * 0.1f;
        static Vector3 Eye => SurgeryAnchor.Find(AnchorIds.StudentOperatingPosition).position + Vector3.up * 1.65f;

        void LookAtKnee(float down = 0f)
        {
            var cam = Camera.main.transform; var k = SurgeryAnchor.Find(AnchorIds.SurgicalSiteKneeR).position;
            cam.position = Eye; cam.rotation = Quaternion.LookRotation(k + Vector3.down * down - cam.position);
        }

        [UnityTest]
        public IEnumerator OperatingRoom_TeachingEvidence()
        {
            yield return EnterOR();
            var cam = Camera.main.transform; var tray = SurgeryAnchor.Find(AnchorIds.InstrumentTable).position;
            yield return null;
            Shot("or_01_first_tool_highlighted_skin_marker", Eye, tray, 60);
            Shot("or_02_hud_english_step_and_status", Eye, KneeLook, 70);
            var adapter = HC.surgery.desktopAdapter; adapter.PickupInstrument(Object.FindObjectsByType<SurgeryInstrumentTag>(FindObjectsSortMode.None).First(t => t.instrumentId == InstrumentIds.SkinMarker));
            yield return new WaitForSeconds(0.3f);
            Shot("or_03_marking_guide_dotted_line", Eye, KneeLook, 60);
            var g = SiteMarkingGuide.Instance; var k = SurgeryAnchor.Find(AnchorIds.SurgicalSiteKneeR).position;
            var close = new Vector3(k.x - 0.38f, k.y + 0.32f, k.z - 0.12f);
            Shot("or_03b_marking_guide_closeup", close, k, 50);
            var off = g.GuideWorldPoints.Select(p => g.SkinPoint(p + Vector3.right * 0.06f)).Where(p => p.HasValue).Select(p => p.Value).ToList();
            g.TestStroke(off.Concat(off.AsEnumerable().Reverse()).Concat(off)); yield return null;
            Shot("or_04_wrong_place_red_ink_feedback", close, k, 50);
            yield return new WaitForSeconds(1.8f);
            g.TestStroke(g.GuideWorldPoints.Take(g.Total - 2)); yield return null;
            Shot("or_05_drawing_along_guide_progress", close, k, 50);
            adapter.DropHeldItem(); yield return null;

            // demonstration of the current step by Dr. Hassan
            var ctl = HC.surgery.controller; var demo = HC.surgery.demonstrator;
            ctl.Core.HandleRequest(SurgeryIntentParser.Parse("Dr. Sara, show me how to mark the knee", ctl.Scenario));
            int n = 0; float t = 0, lastShot = -10f;
            while (demo.Running && t < 90f)
            {
                t += Time.deltaTime;
                var ik = demo.Demonstrator ? demo.Demonstrator.GetComponent<MedSimAI.Characters.ArmReachIK>() : null;
                bool reaching = ik && ik.Reaching && ik.Weight > 0.85f;
                if ((reaching && t - lastShot > 1.2f || t - lastShot > 6f) && n < 24)
                {
                    lastShot = t;
                    var pen = Object.FindObjectsByType<SurgeryInstrumentTag>(FindObjectsSortMode.None).First(x => x.instrumentId == InstrumentIds.SkinMarker);
                    var an = demo.Demonstrator.GetComponentInChildren<Animator>();
                    string hand = ik ? ik.Hand.position.ToString("F2") : "-", tipS = pen.tip ? pen.tip.position.ToString("F2") : "none", par = pen.transform.parent ? pen.transform.parent.name : "none";
                    File.AppendAllText(Path.Combine(Dir, "demo_debug.txt"), n.ToString("00") + " t=" + t.ToString("0.0") + " obj=" + demo.CurrentObjective + " who=" + demo.Demonstrator.characterId + " root=" + demo.Demonstrator.transform.position.ToString("F2") + " hand=" + hand
                        + " shoulder=" + an.GetBoneTransform(HumanBodyBones.RightUpperArm).position.ToString("F2") + " pen=" + pen.transform.position.ToString("F2") + " penParent=" + par + " tip=" + tipS
                        + " err=" + (ik ? ik.LastError : -1f).ToString("F3") + " w=" + (ik ? ik.Weight : 0f).ToString("F2") + " knee=" + k.ToString("F2") + "\n");
                    Shot($"or_06_demo_{n:00}_fp", Eye, KneeLook, 60);
                    Shot($"or_06_demo_{n:00}_close", new Vector3(k.x - 0.55f, k.y + 0.45f, k.z - 0.35f), k, 55);
                    n++;
                }
                yield return null;
            }
            Shot("or_07_after_demo_your_turn_restored", Eye, KneeLook, 60);
        }

        IEnumerator EnterRoom01()
        {
            var cc = Rig.GetComponent<CharacterController>(); if (cc) cc.enabled = false;
            var p = new Vector3(1.15f, 0f, 0.75f);
            Rig.SetPositionAndRotation(p, Quaternion.LookRotation(new Vector3(-0.85f, 0, 0.55f))); if (cc) cc.enabled = true; Physics.SyncTransforms();
            float t = 0; while (HC.SessionRoom != HospitalRoomId.Room01 && t < 10f) { t += Time.deltaTime; yield return null; }
            yield return new WaitForSeconds(3f);
        }
        /// <summary>No headset in tests: hold the main camera at a standing eye position looking at a point (as a real head would).</summary>
        static void Head(Vector3 eye, Vector3 look)
        {
            var cam = Camera.main; foreach (var b in cam.GetComponents<Behaviour>()) if (b.GetType().Name.Contains("TrackedPoseDriver")) b.enabled = false;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
        }
        static Vector3 PatientHead => Object.FindAnyObjectByType<MedSimAI.Characters.PatientInteractionController>().GetComponentInChildren<Animator>().GetBoneTransform(HumanBodyBones.Head).position;
        static Vector3 PatientChest => Object.FindAnyObjectByType<MedSimAI.Characters.PatientInteractionController>().GetComponentInChildren<Animator>().GetBoneTransform(HumanBodyBones.Chest).position;

        [UnityTest]
        public IEnumerator ClinicalRoom_PatientEvidence()
        {
            yield return EnterRoom01();
            var eye = new Vector3(1.15f, 1.65f, 0.75f);
            Shot("cl_01_patient_from_standing_position", eye, PatientChest + Vector3.up * 0.1f, 60);
            var h = PatientHead;
            Shot("cl_02_head_and_pillow_side", h + new Vector3(0.75f, 0.15f, -0.1f), h, 45);
            Shot("cl_03_head_and_pillow_front", h + new Vector3(0.05f, 0.35f, -0.85f), h, 45);
            var arm = Object.FindObjectsByType<MedSimAI.Instruments.PlacementZone>(FindObjectsSortMode.None).First(z => z.zoneId == "ARM_UPPER_L").transform.position;
            Shot("cl_04_gown_left_arm", arm + new Vector3(0.6f, 0.35f, -0.35f), arm, 45);
            // blood pressure: cuff on the left upper arm (sleeve rolls up), START, readable result card
            var cuff = Object.FindAnyObjectByType<MedSimAI.Instruments.BloodPressureController>();
            var zone = Object.FindObjectsByType<MedSimAI.Instruments.PlacementZone>(FindObjectsSortMode.None).First(z => z.zoneId == "ARM_UPPER_L");
            Head(eye, PatientChest + Vector3.up * 0.1f);
            cuff.PlaceInZone(zone); yield return new WaitForSeconds(0.6f);
            arm = zone.transform.position;
            Shot("cl_05_sleeve_rolled_cuff_on_bare_arm", arm + new Vector3(0.6f, 0.35f, -0.35f), arm, 45);
            Assert.IsTrue(cuff.StartMeasurement(), "START accepted");
            float t = 0; while (cuff.Measuring && t < 15f) { t += Time.deltaTime; yield return null; }
            yield return new WaitForSeconds(0.3f);
            Shot("cl_06_bp_result_card_first_person");
            Shot("cl_07_bp_result_card_vr_distance", eye + new Vector3(-0.25f, 0, 0.2f), MedSimAI.UI.VitalsResultPanel.Instance.transform.GetChild(0).position, 50);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ClinicalRoom_DemonstrationEvidence()
        {
            yield return EnterRoom01();
            if (HC.clinical.examDoor) HC.clinical.examDoor.OpenBy();
            var agent = HC.doctor.GetComponent<UnityEngine.AI.NavMeshAgent>(); var spot = HC.clinical.doctorObserveMark ? HC.clinical.doctorObserveMark.position : new Vector3(1.0f, 0, 1.6f);
            if (agent && agent.enabled) agent.Warp(spot); else HC.doctor.transform.position = spot;
            var eye = new Vector3(1.15f, 1.65f, 0.75f);
            Head(eye, PatientChest); yield return new WaitForSeconds(1f);
            var demo = MedSimAI.Characters.ClinicalDemonstrator.Ensure();
            foreach (var topic in new[] { "bp_cuff", "stethoscope" })
            {
                HC.StartClinicalDemo(topic);
                float t = 0, last = -10f; int n = 0;
                var ik = HC.doctor.GetComponent<MedSimAI.Characters.ArmReachIK>();
                while (demo.Running && t < 150f)
                {
                    t += Time.deltaTime; Head(eye, PatientChest);
                    bool reaching = ik && ik.Reaching && ik.Weight > 0.85f;
                    bool card = MedSimAI.UI.VitalsResultPanel.Instance && MedSimAI.UI.VitalsResultPanel.Instance.Visible;
                    if (((reaching || card) && t - last > 1.5f || t - last > 7f) && n < 16)
                    {
                        last = t; Shot($"cl_demo_{topic}_{n:00}_fp"); Shot($"cl_demo_{topic}_{n:00}_side", new Vector3(1.55f, 1.75f, 1.75f), PatientChest, 55); n++;
                    }
                    yield return null;
                }
            }
            Shot("cl_demo_after_your_turn");
            var h = PatientHead;
            Shot("cl_08_head_and_pillow_side_final", h + new Vector3(0.75f, 0.15f, -0.1f), h, 45);
        }

        [UnityTest]
        public IEnumerator Desktop_GlovedHandHoldingInstruments()
        {
            MedSimAI.XR.DisplayModeManager.ForcedPreference = MedSimAI.XR.DisplayModePreference.Desktop;
            try
            {
                yield return SceneManager.LoadSceneAsync("MedSim_Hospital");
                float w = 0; while ((HC == null || !HC.Welcomed) && w < 20f) { w += Time.unscaledDeltaTime; yield return null; }
                yield return EnterOR();
                var adapter = HC.surgery.desktopAdapter; adapter.enabled = true;
                adapter.PickupInstrument(Object.FindObjectsByType<SurgeryInstrumentTag>(FindObjectsSortMode.None).First(t => t.instrumentId == InstrumentIds.SkinMarker));
                yield return new WaitForSeconds(0.8f);
                Assert.IsTrue(MedSimAI.XR.HandPresence.Instance && MedSimAI.XR.HandPresence.Instance.DesktopGloveVisible, "a gloved hand holds the marker");
                for (int i = 0; i < 30; i++) { Head(Eye, KneeLook); yield return null; }
                Shot("hand_01_desktop_glove_holding_marker");
                adapter.DropHeldItem();
                adapter.PickupInstrument(Object.FindObjectsByType<SurgeryInstrumentTag>(FindObjectsSortMode.None).First(t => t.instrumentId == InstrumentIds.Scalpel));
                for (int i = 0; i < 30; i++) { Head(Eye, KneeLook); yield return null; }
                Shot("hand_02_desktop_glove_holding_scalpel");
                adapter.DropHeldItem();
            }
            finally { MedSimAI.XR.DisplayModeManager.ForcedPreference = null; }
        }

        [UnityTest]
        public IEnumerator OperatingRoom_FullProcedureDemoEvidence()
        {
            yield return EnterOR();
            var ctl = HC.surgery.controller; var demo = HC.surgery.demonstrator; var k = SurgeryAnchor.Find(AnchorIds.SurgicalSiteKneeR).position;
            ctl.Core.HandleRequest(SurgeryIntentParser.Parse("Demonstrate the procedure from start to finish", ctl.Scenario));
            Time.timeScale = 2f; string last = ""; float t = 0, lastShot = -10f; int n = 0;
            while (demo.Running && t < 400f)
            {
                t += Time.unscaledDeltaTime;
                bool reaching = demo.Demonstrator && demo.Demonstrator.GetComponent<MedSimAI.Characters.ArmReachIK>() is var ik && ik && ik.Reaching && ik.Weight > 0.9f;
                if (demo.CurrentObjective != last && reaching || (reaching && t - lastShot > 6f && n < 40))
                {
                    if (reaching) { last = demo.CurrentObjective; lastShot = t; Shot($"or_08_full_{n:00}_{last.Replace("obj.", "")}", new Vector3(k.x - 1.25f, 1.85f, k.z - 0.55f), k + Vector3.up * 0.1f, 52); n++; }
                }
                yield return null;
            }
            Time.timeScale = 1f;
        }
    }
}
