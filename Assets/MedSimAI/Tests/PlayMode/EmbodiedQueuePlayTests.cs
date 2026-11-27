using System.Collections;
using System.Linq;
using MedSimAI.AI;
using MedSimAI.Characters;
using MedSimAI.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MedSimAI.PlayTests
{
    /// <summary>v0.6 action lifecycle: per-character queue, multi-action turns, states, held poses, nearest arm, doctor repositioning — validated (mocked) actions, no API calls.</summary>
    public class EmbodiedQueuePlayTests
    {
        [UnitySetUp] public IEnumerator Load()
        {
            AIProviderManager.ForceOffline = true; MedSimAI.XR.DisplayModeManager.DisableAutoDesktop = true;
            yield return SceneManager.LoadSceneAsync("MedSim_ER");
            float t = 0; while ((SessionManager.Instance == null || SessionManager.Instance.State != AppState.MainMenu) && t < 10) { t += Time.unscaledDeltaTime; yield return null; }
            yield return null; CharacterActionRouter.Reset();
        }

        static LiveAction A(string type, string side = "none", string status = "accepted") => new LiveAction { type = type, side = side, instrument = "none", status = status };
        static IEnumerator Until(System.Func<bool> c, float max) { float t = 0; while (!c() && t < max) { t += Time.deltaTime; yield return null; } }

        [UnityTest] public IEnumerator MultiActionTurn_RunsInOrder_AndUnsafeStepIsRejected()
        {
            var p = CharacterActionRouter.Patient; p.autonomous = false;
            CharacterActionRouter.Handle(new ConversationTurn { persona = Persona.Patient, actions = new[] { A("sit_up"), A("raise_arm", "left"), A("stand_up", "none", "denied") }, supervisorIntervention = "I_UNSAFE_MOBILISE" });
            var h = CharacterActionRouter.History;
            Assert.AreEqual(3, h.Count);
            Assert.AreEqual(ActionState.InProgress, h[0].state, "first action starts immediately");
            Assert.AreEqual(ActionState.Validated, h[1].state, "second waits in the queue");
            Assert.AreEqual(ActionState.Rejected, h[2].state, "unsafe request is rejected, never executed");
            yield return Until(() => h[1].state == ActionState.Completed, 14);
            Assert.AreEqual(ActionState.Completed, h[0].state); Assert.AreEqual(ActionState.Completed, h[1].state);
            Assert.LessOrEqual(h[0].endedAt, h[1].startedAt, "sit_up finished before the arm started");
            Assert.Greater(p.Sit, 0.9f); Assert.Greater(p.LeftArm, 0.9f);
            yield return new WaitForSeconds(3f);
            Assert.Greater(p.LeftArm, 0.9f, "raised arm is held until told otherwise");
        }

        [UnityTest] public IEnumerator NewInstruction_InterruptsRunningAction_AndClarifyIsNotExecuted()
        {
            var p = CharacterActionRouter.Patient; p.autonomous = false;
            CharacterActionRouter.Handle(new ConversationTurn { persona = Persona.Patient, actions = new[] { A("sit_up") } });
            yield return null; yield return null;
            CharacterActionRouter.Handle(new ConversationTurn { persona = Persona.Patient, actions = new[] { A("recline") } });
            var h = CharacterActionRouter.History;
            Assert.AreEqual(ActionState.Interrupted, h[0].state, "the student changed their mind");
            Assert.AreEqual(ActionState.InProgress, h[1].state);
            yield return Until(() => h[1].state == ActionState.Completed, 8);
            Assert.AreEqual(ActionState.Completed, h[1].state);
            CharacterActionRouter.Handle(new ConversationTurn { persona = Persona.Patient, actions = new[] { A("raise_arm", "none", "clarify") } });
            Assert.AreEqual(ActionState.NeedsClarification, h[2].state);
            yield return new WaitForSeconds(0.5f);
            Assert.Less(p.LeftArm + p.RightArm, 0.05f, "nothing moves until the side is clarified");
        }

        [UnityTest] public IEnumerator ExtendNearestArm_PicksTheArmClosestToTheStudent()
        {
            var p = CharacterActionRouter.Patient; p.autonomous = false;
            var anim = p.GetComponent<Animator>(); var lua = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm); var rua = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
            var probe = new GameObject("StudentProbe").transform; p.student = probe;
            probe.position = lua.position + (lua.position - rua.position).normalized * 0.6f + Vector3.up * 0.4f;
            CharacterActionRouter.Execute(Persona.Patient, A("extend_arm", "nearest"));
            yield return Until(() => p.LeftArm > 0.95f || p.RightArm > 0.95f, 6);
            Assert.Greater(p.LeftArm, 0.9f, "student on the left → left arm"); Assert.Less(p.RightArm, 0.1f);
            CharacterActionRouter.Execute(Persona.Patient, A("lower_arm", "both")); yield return Until(() => p.LeftArm < 0.05f, 6);
            probe.position = rua.position + (rua.position - lua.position).normalized * 0.6f + Vector3.up * 0.4f;
            CharacterActionRouter.Execute(Persona.Patient, A("extend_arm", "nearest"));
            yield return Until(() => p.RightArm > 0.95f, 6);
            Assert.Greater(p.RightArm, 0.9f, "student on the right → right arm");
            Object.Destroy(probe.gameObject);
        }

        [UnityTest] public IEnumerator DeepBreath_IsVisible_UntilBreatheNormally_AndDeteriorationReturnsToRest()
        {
            var p = CharacterActionRouter.Patient; p.autonomous = false; var life = p.GetComponent<HumanoidLifeController>();
            CharacterActionRouter.Execute(Persona.Patient, A("deep_breath"));
            var h = CharacterActionRouter.History;
            yield return Until(() => h[0].state == ActionState.Completed, 8);
            Assert.AreEqual(ActionState.Completed, h[0].state, "deep breath reported only once it is visibly deeper");
            Assert.Greater(life.breathDepth, 3.0f);
            CharacterActionRouter.Execute(Persona.Patient, A("breathe_normally"));
            yield return Until(() => life.breathDepth < 1.5f, 8);
            Assert.Less(life.breathDepth, 1.5f, "back to normal breathing");
            CharacterActionRouter.Execute(Persona.Patient, A("sit_up")); yield return Until(() => p.Sit > 0.95f, 6);
            p.ReturnToRest(); yield return Until(() => p.Sit < 0.05f, 6);
            Assert.Less(p.Sit, 0.05f, "a clinical state change ends held poses");
        }

        [UnityTest] public IEnumerator Doctor_GoesToOtherSide_AndLeavesTheStudentToExamine()
        {
            var d = CharacterActionRouter.Doctor; var p = CharacterActionRouter.Patient;
            var rig = Object.FindAnyObjectByType<MedSimAI.XR.XRPlayerController>(); var cc = rig.GetComponent<CharacterController>(); if (cc) cc.enabled = false;
            rig.transform.position = new Vector3(1.15f, 0, 0.55f); if (cc) cc.enabled = true; yield return null;
            var h = CharacterActionRouter.History;
            CharacterActionRouter.Execute(Persona.Instructor, A("approach_patient"));
            yield return Until(() => h.Last().state != ActionState.InProgress, 25);
            Assert.AreEqual(ActionState.Completed, h.Last().state, "approach_patient");
            var bedX = p.transform.position.x; float before = d.transform.position.x - bedX;
            CharacterActionRouter.Execute(Persona.Instructor, A("go_other_side"));
            yield return Until(() => h.Last().state != ActionState.InProgress, 30);
            Assert.AreEqual(ActionState.Completed, h.Last().state, "go_other_side");
            Assert.Less(before * (d.transform.position.x - bedX), 0, "she is now on the opposite side of the bed");
            CharacterActionRouter.Execute(Persona.Instructor, A("observe_from_distance"));
            yield return Until(() => h.Last().state != ActionState.InProgress, 30);
            Assert.AreEqual(ActionState.Completed, h.Last().state, "observe_from_distance");
            Assert.Greater(Vector3.Distance(d.transform.position, p.transform.position), 1.5f, "gives the student room to examine alone");
        }
    }
}
