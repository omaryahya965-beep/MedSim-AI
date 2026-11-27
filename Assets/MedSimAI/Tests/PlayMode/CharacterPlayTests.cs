using System.Collections;
using System.Linq;
using MedSimAI.AI;
using MedSimAI.Characters;
using MedSimAI.Core;
using MedSimAI.Instruments;
using MedSimAI.Voice;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MedSimAI.PlayTests
{
    /// <summary>Phase 2: realistic characters in the real scene — zone alignment on the body, facial animation, lip sync.</summary>
    public class CharacterPlayTests
    {
        [UnitySetUp] public IEnumerator Load()
        {
            yield return SceneManager.LoadSceneAsync("MedSim_ER");
            float t = 0; while ((SessionManager.Instance == null || SessionManager.Instance.State != AppState.MainMenu) && t < 10) { t += Time.unscaledDeltaTime; yield return null; }
        }

        [UnityTest] public IEnumerator Characters_AreRealisticHumanoids_NotMannequins()
        {
            var p = Object.FindAnyObjectByType<PatientInteractionController>(); var d = Object.FindAnyObjectByType<MedicalInstructorController>();
            Assert.IsTrue(p.GetComponent<Animator>().isHuman); Assert.IsTrue(d.GetComponent<Animator>().isHuman);
            Assert.NotNull(p.GetComponent<FaceRig>()); Assert.Greater(p.GetComponent<FaceRig>().ShapeCount, 60);
            Assert.NotNull(d.GetComponent<FaceRig>());
            yield return null;
        }

        [UnityTest] public IEnumerator ExaminationZones_SitOnTheBodySurface_AtAnatomicalPositions()
        {
            var a = Object.FindAnyObjectByType<PatientInteractionController>().GetComponent<Animator>();
            var zones = Object.FindObjectsByType<PlacementZone>(FindObjectsSortMode.None).ToDictionary(z => z.zoneId);
            Assert.AreEqual(10, zones.Count);
            var chest = a.GetBoneTransform(HumanBodyBones.UpperChest).position;
            foreach (var id in new[] { "CHEST_AORTIC", "CHEST_PULMONARY", "CHEST_TRICUSPID", "CHEST_MITRAL", "CHEST_LUNG_L", "CHEST_LUNG_R" })
                Assert.Less(Vector3.Distance(zones[id].transform.position, chest), 0.32f, id + " near the chest");
            var lPos = a.GetBoneTransform(HumanBodyBones.LeftUpperArm).position; var rPos = a.GetBoneTransform(HumanBodyBones.RightUpperArm).position;
            // mitral (apex) is on the patient's LEFT side, aortic on the RIGHT
            Assert.Less(Vector3.Distance(zones["CHEST_MITRAL"].transform.position, lPos), Vector3.Distance(zones["CHEST_MITRAL"].transform.position, rPos));
            Assert.Less(Vector3.Distance(zones["CHEST_AORTIC"].transform.position, rPos), Vector3.Distance(zones["CHEST_AORTIC"].transform.position, lPos));
            Assert.Less(Vector3.Distance(zones["FINGER_L"].transform.position, a.GetBoneTransform(HumanBodyBones.LeftIndexDistal).position), 0.05f);
            Assert.Less(Vector3.Distance(zones["ARM_UPPER_R"].transform.position, a.GetBoneTransform(HumanBodyBones.RightUpperArm).position), 0.25f);
            // zone forward points out of the body (away from the spine)
            var spine = a.GetBoneTransform(HumanBodyBones.Spine).position;
            Assert.Greater(Vector3.Dot(zones["CHEST_TRICUSPID"].transform.forward, (zones["CHEST_TRICUSPID"].transform.position - spine).normalized), 0f);
            yield return null;
        }

        [UnityTest] public IEnumerator Patient_Blinks_ShowsDiscomfort_AndLipSyncsWhileSpeaking()
        {
            var p = Object.FindAnyObjectByType<PatientInteractionController>();
            var body = p.GetComponentsInChildren<SkinnedMeshRenderer>().First(r => r.name == "Human");
            int blinkL = body.sharedMesh.GetBlendShapeIndex("eyeBlinkLeft"), brow = body.sharedMesh.GetBlendShapeIndex("browInnerUp");
            float maxBlink = 0, t = 0;
            while (t < 7f) { maxBlink = Mathf.Max(maxBlink, body.GetBlendShapeWeight(blinkL)); t += Time.deltaTime; yield return null; }
            Assert.Greater(maxBlink, 50f, "blinked within 7 s");
            Assert.Greater(body.GetBlendShapeWeight(brow), 10f, "discomfort expression active");
            SessionManager.Instance.StartClinicalSession(true); yield return null; VoiceInteractionManager.Instance.Interrupt();
            var task = VoiceInteractionManager.Instance.Submit(Persona.Patient, "Does the pain spread anywhere?");
            float maxViseme = 0; t = 0;
            string[] vis = FaceRig.Visemes.Where(v => v != "sil_00").ToArray();
            while (t < 4f) { foreach (var v in vis) maxViseme = Mathf.Max(maxViseme, body.GetBlendShapeWeight(body.sharedMesh.GetBlendShapeIndex(v))); t += Time.deltaTime; yield return null; }
            Assert.Greater(maxViseme, 20f, "visemes driven while speaking");
            Assert.IsTrue(p.IsSpeaking || task.IsCompleted);
        }
    }
}
