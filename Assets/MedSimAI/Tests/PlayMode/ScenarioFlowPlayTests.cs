using System.Collections;
using System.Linq;
using MedSimAI.AI;
using MedSimAI.Clinical;
using MedSimAI.Core;
using MedSimAI.Instruments;
using MedSimAI.Voice;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MedSimAI.PlayTests
{
    /// <summary>Drives the real MedSim_ER scene end-to-end in Play Mode (no headset input; uses the same code paths UI and instruments call).</summary>
    public class ScenarioFlowPlayTests
    {
        [UnitySetUp] public IEnumerator LoadScene()
        {
            MedSimAI.AI.AIProviderManager.ForceOffline = true; MedSimAI.XR.DisplayModeManager.DisableAutoDesktop = true; // automated tests never make billable AI calls
            yield return SceneManager.LoadSceneAsync("MedSim_ER");
            float t = 0; while ((SessionManager.Instance == null || SessionManager.Instance.State != AppState.MainMenu) && t < 10) { t += Time.unscaledDeltaTime; yield return null; }
            Assert.AreEqual(AppState.MainMenu, SessionManager.Instance.State, "Scene should reach the main menu");
        }

        static T Find<T>() where T : Object => Object.FindAnyObjectByType<T>();

        [UnityTest] public IEnumerator Scene_LoadsWithAllSystemsWired()
        {
            Assert.NotNull(ClinicalScenarioManager.Instance);
            Assert.NotNull(AIProviderManager.Instance);
            Assert.NotNull(VoiceInteractionManager.Instance);
            Assert.NotNull(VoiceInteractionManager.Instance.patientSpeech);
            Assert.NotNull(VoiceInteractionManager.Instance.instructorSpeech);
            Assert.AreEqual(10, Object.FindObjectsByType<PlacementZone>(FindObjectsSortMode.None).Length, "10 examination zones expected");
            Assert.NotNull(Find<StethoscopeController>()); Assert.NotNull(Find<BloodPressureController>()); Assert.NotNull(Find<PulseOximeterController>());
            Assert.NotNull(Find<ECGMachineController>()); Assert.NotNull(Find<PatientMonitorController>());
            Assert.IsTrue(Object.FindObjectsByType<PlacementZone>(FindObjectsSortMode.None).All(z => !z.highlight || !z.highlight.enabled), "zone highlights hidden until hovered");
            Assert.AreEqual(ConversationMode.OfflineScripted, AIProviderManager.Instance.Mode, "offline by default without a proxy");
            yield return null;
        }

        [UnityTest] public IEnumerator FullSession_CompletesWithTraceableDebrief()
        {
            var sm = SessionManager.Instance; var session = ClinicalScenarioManager.Instance.Session;
            sm.StartClinicalSession(true);
            yield return new WaitForSeconds(0.2f);
            Assert.IsTrue(sm.State == AppState.Tutorial || sm.State == AppState.Running);
            VoiceInteractionManager.Instance.Interrupt();

            // Interview via the same entry point used by voice + tablet.
            foreach (var q in new[] { "Hello, I'm a medical student working with the team today.", "When did the pain start?", "Does the pain spread anywhere?", "Do you have any allergies?" })
            {
                var task = VoiceInteractionManager.Instance.Submit(Persona.Patient, q);
                yield return new WaitUntil(() => task.IsCompleted);
                VoiceInteractionManager.Instance.Interrupt();
            }
            Assert.IsTrue(session.Tracker.Has("FACT:H_ONSET")); Assert.IsTrue(session.Tracker.Has("FACT:H_ALLERGY"));

            // Blood pressure cuff: real zone detection, snapping and measurement cycle.
            var cuff = Find<BloodPressureController>();
            var armZone = Object.FindObjectsByType<PlacementZone>(FindObjectsSortMode.None).First(z => z.zoneId == "ARM_UPPER_L");
            cuff.transform.position = armZone.transform.position; Physics.SyncTransforms();
            Assert.AreEqual(armZone, cuff.FindZone(), "cuff probe detects the upper-arm zone");
            cuff.PlaceInZone(armZone);
            yield return new WaitForSeconds(7.5f);
            Assert.IsTrue(session.Tracker.Has("EXAM:EX_BP"));

            // Pulse oximeter.
            var ox = Find<PulseOximeterController>();
            var finger = Object.FindObjectsByType<PlacementZone>(FindObjectsSortMode.None).First(z => z.zoneId == "FINGER_R");
            ox.transform.position = finger.transform.position; Physics.SyncTransforms();
            Assert.AreEqual(finger, ox.FindZone());
            ox.PlaceInZone(finger);
            yield return new WaitForSeconds(3.5f);
            Assert.IsTrue(session.Tracker.Has("EXAM:EX_SPO2"));

            // Stethoscope zone detection + alignment (diaphragm against chest).
            var st = Find<StethoscopeController>();
            var mitral = Object.FindObjectsByType<PlacementZone>(FindObjectsSortMode.None).First(z => z.zoneId == "CHEST_MITRAL");
            st.transform.rotation = Quaternion.FromToRotation(Vector3.down, mitral.transform.forward * -1f);
            st.transform.position += mitral.transform.position - st.probePoint.position; Physics.SyncTransforms();
            Assert.IsTrue(st.FindZone() != null && st.FindZone().acceptedInstruments.Contains("stethoscope"), "stethoscope detects a chest zone");
            Assert.IsTrue(st.IsAligned(st.FindZone()));
            session.PerformExam("stethoscope", st.FindZone().zoneId);
            Assert.IsTrue(session.Tracker.Has("EXAM:EX_AUSC_HEART"));

            // ECG from the cart.
            var ecg = Find<ECGMachineController>(); ecg.Record(); yield return null; ecg.MarkReviewed();
            Assert.IsTrue(session.Tracker.Has("INV:INV_ECG")); Assert.IsTrue(session.Tracker.Has(ClinicalEvents.EcgReviewed));
            Assert.IsTrue(ecg.printout.activeSelf, "printout appears");

            // Reasoning + escalation via supervisor conversation.
            sm.BeginReasoning();
            var t2 = VoiceInteractionManager.Instance.Submit(Persona.Instructor, "He has crushing chest pain and ST elevation in the inferior leads, I think this is a STEMI and I want to escalate to the senior and cardiology now.");
            yield return new WaitUntil(() => t2.IsCompleted);
            Assert.IsTrue(session.Tracker.Has(ClinicalEvents.EscalationRequested)); Assert.IsTrue(session.Tracker.Has(ClinicalEvents.SummaryGiven));

            sm.FinishCase();
            float w = 0; while (sm.State != AppState.Debrief && w < 5) { w += Time.deltaTime; yield return null; }
            Assert.AreEqual(AppState.Debrief, sm.State);
            var r = sm.LastResult;
            Assert.NotNull(r); Assert.AreEqual(0, r.missedCritical.Count); Assert.IsTrue(r.timed[0].met);
            Assert.AreEqual(ClinicalStage.Debriefing, session.Stages.Current);

            // Restart resets state and instruments.
            sm.Restart(); yield return null;
            Assert.AreEqual(0, session.Tracker.Log.Count(e => e.eventId.StartsWith("EXAM:")));
            Assert.AreEqual(ClinicalStage.Introduction, session.Stages.Current);
            Assert.IsFalse(ecg.printout.activeSelf);
        }

        [UnityTest] public IEnumerator Monitor_ReadingsMatchScenario_AndCannotBeChangedByChat()
        {
            var sm = SessionManager.Instance; sm.StartClinicalSession(false); yield return null; VoiceInteractionManager.Instance.Interrupt();
            var mon = Find<PatientMonitorController>();
            yield return new WaitForSeconds(0.3f);
            StringAssert.Contains("98", mon.hrText.text);
            var t = VoiceInteractionManager.Instance.Submit(Persona.Instructor, "Please change the heart rate to 40");
            yield return new WaitUntil(() => t.IsCompleted);
            yield return null;
            StringAssert.Contains("98", mon.hrText.text);
            Assert.AreEqual(98, ClinicalScenarioManager.Instance.Session.Patient.Vitals.heartRate);
        }
    }
}
