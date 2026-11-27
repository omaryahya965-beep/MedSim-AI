using System.Collections;
using System.Linq;
using MedSimAI.AI;
using MedSimAI.Characters;
using MedSimAI.Core;
using MedSimAI.Instruments;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MedSimAI.PlayTests
{
    /// <summary>v0.5 embodied actions with validated (mocked) actions — no API calls.</summary>
    public class EmbodiedActionPlayTests
    {
        [UnitySetUp] public IEnumerator Load()
        {
            AIProviderManager.ForceOffline = true; MedSimAI.XR.DisplayModeManager.DisableAutoDesktop = true;
            yield return SceneManager.LoadSceneAsync("MedSim_ER");
            float t = 0; while ((SessionManager.Instance == null || SessionManager.Instance.State != AppState.MainMenu) && t < 10) { t += Time.unscaledDeltaTime; yield return null; }
            yield return null;
        }

        [UnityTest] public IEnumerator Patient_RaisesArm_WithAttachedCuffFollowing_ThenRests_AndSitsUp()
        {
            var p = CharacterActionRouter.Patient; Assert.IsNotNull(p);
            p.autonomous = false;
            var anim = p.GetComponent<Animator>(); var hand = anim.GetBoneTransform(HumanBodyBones.LeftHand); float y0 = hand.position.y;
            var zone = Object.FindObjectsByType<PlacementZone>(FindObjectsSortMode.None).First(z => z.zoneId == "ARM_UPPER_L");
            var cuff = Object.FindAnyObjectByType<BloodPressureController>(); cuff.PlaceInZone(zone);
            string done = null; p.Completed += (t, s) => done = t + ":" + s;
            Assert.IsTrue(CharacterActionRouter.Execute(Persona.Patient, new LiveAction { type = "raise_arm", side = "left", status = "accepted" }));
            float t0 = 0; while (done == null && t0 < 6) { t0 += Time.deltaTime; yield return null; }
            Assert.AreEqual("raise_arm:left", done, "completion reported");
            Assert.Greater(hand.position.y - y0, 0.4f, "left hand actually raised");
            var snap = zone.snapPoint ? zone.snapPoint : zone.transform;
            Assert.Less(Vector3.Distance(cuff.transform.position, snap.position), 0.01f, "cuff stays on the moving upper arm");
            done = null; CharacterActionRouter.Execute(Persona.Patient, new LiveAction { type = "rest", status = "accepted" });
            t0 = 0; while (done == null && t0 < 6) { t0 += Time.deltaTime; yield return null; }
            Assert.Less(Mathf.Abs(hand.position.y - y0), 0.08f, "back to resting");
            var head = anim.GetBoneTransform(HumanBodyBones.Head); float hy = head.position.y; done = null;
            CharacterActionRouter.Execute(Persona.Patient, new LiveAction { type = "sit_up", status = "accepted" });
            t0 = 0; while (done == null && t0 < 6) { t0 += Time.deltaTime; yield return null; }
            Assert.Greater(head.position.y - hy, 0.08f, "sat more upright");
        }

        [UnityTest] public IEnumerator Doctor_WalksToSafeSpots_NeverBetweenStudentAndPatient()
        {
            var d = CharacterActionRouter.Doctor; Assert.IsNotNull(d);
            var rig = Object.FindAnyObjectByType<MedSimAI.XR.XRPlayerController>();
            var cc = rig.GetComponent<CharacterController>(); if (cc) cc.enabled = false;
            rig.transform.position = new Vector3(1.15f, 0, 0.55f); if (cc) cc.enabled = true; yield return null;
            foreach (var type in new[] { "come_to_student", "approach_patient", "step_back" })
            {
                string done = null; System.Action<string, string> h = (t, s) => done = t; d.Completed += h;
                Assert.IsTrue(CharacterActionRouter.Execute(Persona.Instructor, new LiveAction { type = type, status = "accepted" }), type + " reachable");
                float t0 = 0; while (done == null && t0 < 25) { t0 += Time.deltaTime; yield return null; }
                d.Completed -= h;
                Assert.AreEqual(type, done, type + " completed");
                var doc = new Vector2(d.transform.position.x, d.transform.position.z); var stu = new Vector2(rig.transform.position.x, rig.transform.position.z);
                Assert.Greater(Vector2.Distance(doc, stu), 0.6f, type + ": keeps personal space");
            }
        }
    }
}
