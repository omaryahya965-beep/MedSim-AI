using System.Collections;
using System.Linq;
using MedSimAI.AI;
using MedSimAI.Characters;
using MedSimAI.Surgery.AI;
using MedSimAI.Surgery.Anatomy;
using MedSimAI.Surgery.Mechanics;
using MedSimAI.Surgery.Shared;
using MedSimAI.Surgery.Shared.Unity;
using MedSimAI.Surgery.UI;
using NUnit.Framework;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MedSimAI.Hospital.PlayTests
{
    /// <summary>
    /// Teaching and clarity in the operating room (offline, no billable calls): what to pick up is highlighted, the site mark is
    /// physically drawn along a projected guide (wrong places explained), nested knee targets resolve to the step's target,
    /// a demonstration is performed visibly by a character and then fully undone (no credit, logged as assistance),
    /// the whole procedure can be demonstrated, the student can stop it, R needs a confirmation, and the HUD labels are clean.
    /// </summary>
    public class TeachingPlayTests
    {
        HospitalCoordinator HC => HospitalCoordinator.Instance;
        Transform Rig => Object.FindAnyObjectByType<XROrigin>().transform;
        SurgeryEventBus Bus => SurgeryEventBusHost.Instance.Bus;
        SurgeryMechanicsRoot Mech => HC.surgery.mechanics;
        SurgicalDesktopInteractionAdapter Adapter => HC.surgery.desktopAdapter;
        SurgeryInstrumentTag Tool(string id) => Object.FindObjectsByType<SurgeryInstrumentTag>(FindObjectsSortMode.None).First(t => t.instrumentId == id);
        AnatomyTargetStateView View(string id) => Object.FindObjectsByType<AnatomyTargetStateView>(FindObjectsInactive.Include, FindObjectsSortMode.None).First(v => v.TargetId == id);
        string Objective => Mech.CurrentObjectiveId;

        [UnitySetUp]
        public IEnumerator Load()
        {
            AIProviderManager.ForceOffline = true; MedSimAI.XR.DisplayModeManager.DisableAutoDesktop = true;
            yield return SceneManager.LoadSceneAsync("MedSim_Hospital");
            float t = 0; while ((HC == null || !HC.Welcomed) && t < 20f) { t += Time.unscaledDeltaTime; yield return null; }
            Assert.IsTrue(HC.Welcomed);
        }
        [UnityTearDown]
        public IEnumerator Unload() { Time.timeScale = 1; SiteMarkingGuide.TestHeld = false; yield return null; }

        IEnumerator EnterOperatingRoom()
        {
            var cc = Rig.GetComponent<CharacterController>(); if (cc) cc.enabled = false;
            var p = SurgeryAnchor.Find(AnchorIds.StudentOperatingPosition).position; var k = SurgeryAnchor.Find(AnchorIds.SurgicalSiteKneeR).position; var look = k - p; look.y = 0;
            Rig.SetPositionAndRotation(p, Quaternion.LookRotation(look)); if (cc) cc.enabled = true; Physics.SyncTransforms();
            float t = 0; while (HC.SessionRoom != HospitalRoomId.Room02 && t < 5f) { t += Time.deltaTime; yield return null; }
            var ctl = HC.surgery.controller;
            t = 0; while ((ctl.Core == null || !ctl.Core.Active || string.IsNullOrEmpty(Objective)) && t < 15f) { t += Time.deltaTime; yield return null; }
            Assert.AreEqual("obj.site_confirmation", Objective);
            yield return null;
        }

        [UnityTest]
        public IEnumerator FirstTool_IsHighlighted_ThenTheTarget_ForEveryStep()
        {
            yield return EnterOperatingRoom();
            var hl = HC.surgery.highlighter; Assert.IsNotNull(hl);
            Assert.AreEqual(InstrumentIds.SkinMarker, hl.CueId, "the marker is shown as the tool to pick up first");
            Adapter.PickupInstrument(Tool(InstrumentIds.SkinMarker)); yield return null; yield return null;
            Assert.AreEqual("", hl.CueId, "holding the right tool: the pick-up cue goes away");
            Assert.IsTrue(hl.TargetCueVisible, "and the target on the knee is marked");
            Adapter.DropHeldItem(); yield return null; yield return null;
            Assert.AreEqual(InstrumentIds.SkinMarker, hl.CueId, "put down again: the cue returns");
        }

        [UnityTest]
        public IEnumerator PenOnKnee_ResolvesToTheMarkingSite_NotTheEnclosingWound()
        {
            yield return EnterOperatingRoom();
            var wound = Object.FindObjectsByType<SurgeryAnatomyTag>(FindObjectsSortMode.None).First(t => t.targetId == AnatomyTargetIds.Wound).GetComponent<Collider>();
            var site = Object.FindObjectsByType<SurgeryAnatomyTag>(FindObjectsSortMode.None).First(t => t.targetId == AnatomyTargetIds.SiteMarking).GetComponent<Collider>();
            Assert.AreEqual(AnatomyTargetIds.SiteMarking, Mech.AnatomyController.ResolveTarget(new[] { (AnatomyTargetIds.Wound, wound), (AnatomyTargetIds.SiteMarking, site) }), "the step's target wins over the box around it");
            var left = Object.FindObjectsByType<SurgeryAnatomyTag>(FindObjectsSortMode.None).First(t => t.targetId == "knee_l.site_marking").GetComponent<Collider>();
            Assert.AreEqual("knee_l.site_marking", Mech.AnatomyController.ResolveTarget(new[] { (AnatomyTargetIds.Wound, wound), ("knee_l.site_marking", left) }), "a left-knee touch is still caught");
            int rejected = Bus.Log.Count(e => e.id == SurgeryEventIds.InteractionRejected);
            Adapter.PickupInstrument(Tool(InstrumentIds.SkinMarker));
            Assert.IsFalse(Mech.AnatomyController.HandleContact(InstrumentIds.SkinMarker, AnatomyTargetIds.Wound), "touching the knee with the pen starts drawing, it is not a rejection");
            Assert.AreEqual(rejected, Bus.Log.Count(e => e.id == SurgeryEventIds.InteractionRejected), "no wrong_target for the pen on the right knee (manual-test bug)");
            Adapter.DropHeldItem();
        }

        [UnityTest]
        public IEnumerator SiteMark_IsDrawnAlongTheGuide_WrongPlaceIsExplained()
        {
            yield return EnterOperatingRoom();
            var g = SiteMarkingGuide.Instance; Assert.IsNotNull(g); yield return null;
            Assert.GreaterOrEqual(g.Total, 5, "dotted guide projected on the knee skin");
            string fb = null; System.Action<string> on = s => fb = s; SiteMarkingGuide.Feedback += on;
            try
            {
                Adapter.PickupInstrument(Tool(InstrumentIds.SkinMarker));
                // a stroke 6 cm to the side of the guide: red ink, wiped, explained; nothing completed
                var off = g.GuideWorldPoints.Select(p => g.SkinPoint(p + Vector3.right * 0.06f)).Where(p => p.HasValue).Select(p => p.Value).ToList();
                Assert.Greater(off.Count, 3, "skin there to draw on");
                g.TestStroke(off.Concat(off.AsEnumerable().Reverse()).Concat(off));
                Assert.AreEqual("off_guide", fb, "drawing off the site is explained");
                Assert.AreEqual("obj.site_confirmation", Objective, "not completed");
                Assert.AreEqual(AnatomyStates.Intact, View(AnatomyTargetIds.SiteMarking).CurrentState);
                // half the line: progress only
                g.TestStroke(g.GuideWorldPoints.Take(g.Total / 2));
                Assert.AreEqual("obj.site_confirmation", Objective, "half a mark is not a mark");
                Assert.Greater(g.Covered, 0);
                // the rest of the line: the mark is complete and validated by the mechanics
                g.TestStroke(g.GuideWorldPoints.Skip(g.Total / 2 - 1));
                yield return null;
                Assert.AreEqual(AnatomyStates.Marked, View(AnatomyTargetIds.SiteMarking).CurrentState, "marked");
                Assert.AreEqual("obj.incision", Objective, "next step");
                Assert.IsTrue(Bus.Log.Any(e => e.id == SurgeryEventIds.ObjectiveCompleted && e.objectiveId == "obj.site_confirmation" && e.source == SurgeryEventSources.Mechanics), "credited through the normal mechanics path");
                Adapter.DropHeldItem();
            }
            finally { SiteMarkingGuide.Feedback -= on; }
        }

        [UnityTest]
        public IEnumerator SiteMark_VrPenTip_DrawsOnTheSkin()
        {
            yield return EnterOperatingRoom();
            var g = SiteMarkingGuide.Instance; var pen = Tool(InstrumentIds.SkinMarker);
            var rb = pen.GetComponent<Rigidbody>(); if (rb) rb.isKinematic = true;
            SiteMarkingGuide.TestHeld = true;
            var tip = pen.tip ? pen.tip : pen.transform;
            foreach (var p in g.GuideWorldPoints)
                for (int k = 0; k < 4; k++)
                {   // the pen's tip moves along the skin, frame by frame (what a VR hand does)
                    var next = Vector3.Lerp(p, p, k / 4f) + Vector3.up * 0.003f;
                    pen.transform.position += next - tip.position; yield return null;
                }
            SiteMarkingGuide.TestHeld = false;
            yield return null;
            Assert.AreEqual(AnatomyStates.Marked, View(AnatomyTargetIds.SiteMarking).CurrentState, "drawn with the VR pen tip");
        }

        [UnityTest]
        public IEnumerator Demonstration_OfTheStep_IsPerformedVisibly_ThenUndone_NoCredit()
        {
            yield return EnterOperatingRoom();
            var ctl = HC.surgery.controller; var demo = HC.surgery.demonstrator; Assert.IsNotNull(demo, "demonstrator present");
            var pen = Tool(InstrumentIds.SkinMarker); var penHome = pen.transform.position;
            int completedBefore = Bus.Log.Count(e => e.id == SurgeryEventIds.ObjectiveCompleted);
            var v = ctl.Core.HandleRequest(SurgeryIntentParser.Parse("Dr. Sara, show me how to mark the knee", ctl.Scenario));
            Assert.AreEqual(SurgeryActionCatalog.DemonstrateStep, v.action, "understood as a demonstration request");
            Assert.AreEqual(ActionStatus.Accepted, v.status);
            Assert.IsTrue(demo.Running, "the demonstration started");
            Time.timeScale = 3f;
            float t = 0, minErr = 9f; bool penInHand = false, sawInk = false, marked = false;
            while (demo.Running && t < 90f)
            {
                var ik = demo.Demonstrator.GetComponent<ArmReachIK>();
                if (ik && ik.Reaching && pen.transform.IsChildOf(demo.Demonstrator.transform)) minErr = Mathf.Min(minErr, ik.LastError);
                penInHand |= pen.transform.IsChildOf(demo.Demonstrator.transform);
                sawInk |= Mech.GetComponentsInChildren<LineRenderer>().Any(l => l.name == "ink stroke" && l.positionCount > 3);
                marked |= View(AnatomyTargetIds.SiteMarking).CurrentState == AnatomyStates.Marked;
                t += Time.unscaledDeltaTime; yield return null;
            }
            Time.timeScale = 1f;
            Assert.IsFalse(demo.Running, "finished within the time limit");
            Assert.IsTrue(penInHand, "she took the marker in her hand");
            Assert.Less(minErr, 0.06f, "the tip of the marker reached the knee (arm reach)");
            Assert.IsTrue(sawInk, "she drew the mark (ink on the skin)");
            Assert.IsTrue(marked, "the step's visible result was shown");
            // undone: the student's turn
            Assert.AreEqual("obj.site_confirmation", Objective, "the student's step is still to do");
            Assert.AreEqual(AnatomyStates.Intact, View(AnatomyTargetIds.SiteMarking).CurrentState, "the demonstration mark was removed");
            Assert.Less(Vector3.Distance(pen.transform.position, penHome), 0.05f, "the marker is back on the tray");
            Assert.AreEqual(completedBefore, Bus.Log.Count(e => e.id == SurgeryEventIds.ObjectiveCompleted), "no credit from a demonstration");
            Assert.IsTrue(Bus.Log.Any(e => e.id == SurgeryEventIds.HintGiven && e.reason.StartsWith("demonstration:")), "logged as assistance");
            Assert.IsTrue(demo.Log.Any(l => l.StartsWith("say ") && l.Contains("your turn")), "she hands over: now it's your turn");
        }

        [UnityTest]
        public IEnumerator Demonstration_OfTheWholeProcedure_ShowsAllNineSteps_ThenRestores()
        {
            yield return EnterOperatingRoom();
            var ctl = HC.surgery.controller; var demo = HC.surgery.demonstrator;
            var v = ctl.Core.HandleRequest(SurgeryIntentParser.Parse("اعمليها قدامي، العملية كاملة من البداية للنهاية", ctl.Scenario));
            Assert.AreEqual(SurgeryActionCatalog.DemonstrateProcedure, v.action);
            Assert.IsTrue(demo.Running);
            Time.timeScale = 5f;
            var shown = new System.Collections.Generic.HashSet<string>(); bool femurResected = false, closed = false;
            float t = 0; while (demo.Running && t < 200f)
            {
                if (!string.IsNullOrEmpty(demo.CurrentObjective)) shown.Add(demo.CurrentObjective);
                femurResected |= View(AnatomyTargetIds.DistalFemur).CurrentState == AnatomyStates.Resected;
                closed |= View(AnatomyTargetIds.Wound).CurrentState == AnatomyStates.Closed;
                t += Time.unscaledDeltaTime; yield return null;
            }
            Time.timeScale = 1f;
            Assert.IsFalse(demo.Running, "the full demonstration finished");
            Assert.AreEqual(9, shown.Count, "all nine steps were demonstrated: " + string.Join(",", shown));
            Assert.IsTrue(femurResected && closed, "visible anatomy changes during the demonstration");
            Assert.AreEqual("obj.site_confirmation", Objective, "then the student starts from step 1");
            Assert.AreEqual(AnatomyStates.Intact, View(AnatomyTargetIds.DistalFemur).CurrentState, "restored");
            Assert.AreEqual(AnatomyStates.Intact, View(AnatomyTargetIds.Wound).CurrentState, "restored");
            Assert.IsFalse(Bus.Log.Any(e => e.id == SurgeryEventIds.ObjectiveCompleted), "nothing credited");
        }

        [UnityTest]
        public IEnumerator Demonstration_CanBeStopped_LetMeTry()
        {
            yield return EnterOperatingRoom();
            var ctl = HC.surgery.controller; var demo = HC.surgery.demonstrator;
            ctl.Core.HandleRequest(SurgeryIntentParser.Parse("ورجيني كيف أعلم على الركبة", ctl.Scenario));
            Assert.IsTrue(demo.Running);
            Time.timeScale = 3f; float t = 0; while (t < 4f) { t += Time.unscaledDeltaTime; yield return null; } Time.timeScale = 1f;
            Assert.IsTrue(SurgeryIntentParser.Parse("خلص، خليني أجرب", ctl.Scenario).stopDemo);
            demo.Abort("stopped_by_student", true);
            yield return null;
            Assert.IsFalse(demo.Running);
            Assert.AreEqual(AnatomyStates.Intact, View(AnatomyTargetIds.SiteMarking).CurrentState);
            Assert.AreEqual("obj.site_confirmation", Objective);
        }

        // ------------------------------------------------------------------ clinical room
        IEnumerator EnterRoom01WithDoctor()
        {
            var cc = Rig.GetComponent<CharacterController>(); if (cc) cc.enabled = false;
            Rig.SetPositionAndRotation(new Vector3(1.15f, 0f, 0.75f), Quaternion.LookRotation(new Vector3(-0.85f, 0, 0.55f))); if (cc) cc.enabled = true; Physics.SyncTransforms();
            float t = 0; while (HC.SessionRoom != HospitalRoomId.Room01 && t < 10f) { t += Time.deltaTime; yield return null; }
            Assert.AreEqual(HospitalRoomId.Room01, HC.SessionRoom);
            if (HC.clinical.examDoor) HC.clinical.examDoor.OpenBy();
            var agent = HC.doctor.GetComponent<UnityEngine.AI.NavMeshAgent>();   // in the app she walks in with the student
            var spot = HC.clinical.doctorObserveMark ? HC.clinical.doctorObserveMark.position : new Vector3(1.0f, 0, 1.6f);
            if (agent && agent.enabled) agent.Warp(spot); else HC.doctor.transform.position = spot;
            yield return new WaitForSeconds(1f);
        }

        [UnityTest]
        public IEnumerator Clinical_DoctorDemonstratesBloodPressure_AtTheBedside_NoCreditForTheStudent()
        {
            yield return EnterRoom01WithDoctor();
            var session = MedSimAI.Core.ClinicalScenarioManager.Instance.Session; int hints = session.HintsRequested;
            Assert.IsTrue(HC.HandleDemonstrationRequest(MedSimAI.AI.Persona.Instructor, "ورجيني كيف بنستخدم جهاز الضغط"), "understood as a demonstration request");
            var demo = MedSimAI.Characters.ClinicalDemonstrator.Instance; Assert.IsNotNull(demo); Assert.IsTrue(demo.Running); Assert.AreEqual("bp_cuff", demo.Topic);
            var cuff = Object.FindAnyObjectByType<MedSimAI.Instruments.BloodPressureController>(); var home = cuff.HomePosition;
            var roll = Object.FindAnyObjectByType<MedSimAI.Characters.GownSleeveRoll>();
            Time.timeScale = 3f; float t = 0; bool inHand = false, onArm = false, measured = false, card = false, rolled = false;
            while (demo.Running && t < 120f)
            {
                inHand |= cuff.transform.IsChildOf(HC.doctor.transform);
                onArm |= cuff.Attached; measured |= cuff.Measuring;
                card |= MedSimAI.UI.VitalsResultPanel.Instance && MedSimAI.UI.VitalsResultPanel.Instance.Visible;
                rolled |= roll && (roll.LeftRolled || roll.RightRolled);
                t += Time.unscaledDeltaTime; yield return null;
            }
            Time.timeScale = 1f;
            Assert.IsFalse(demo.Running, "finished");
            Assert.IsTrue(inHand, "she carried the cuff in her hand");
            Assert.IsTrue(onArm, "she applied it to the arm");
            Assert.IsTrue(rolled, "the sleeve was rolled up for the cuff");
            Assert.IsTrue(measured, "she pressed START and it measured");
            Assert.IsTrue(card, "the reading was shown on the result card");
            Assert.Less(Vector3.Distance(cuff.transform.position, home), 0.05f, "the cuff is back on the trolley");
            Assert.IsFalse(cuff.DemonstrationUse);
            Assert.IsFalse(session.Tracker.Log.Any(e => e.eventId.StartsWith("EXAM:")), "no exam recorded for the student");
            Assert.AreEqual(hints + 1, session.HintsRequested, "the demonstration counts as assistance");
            Assert.IsTrue(demo.Log.Any(l => l.Contains("bp_cuff")));
        }

        [UnityTest]
        public IEnumerator Clinical_InterviewDemonstration_PatientAnswersFromTheScenario()
        {
            yield return EnterRoom01WithDoctor();
            HC.StartClinicalDemo("interview");
            var demo = MedSimAI.Characters.ClinicalDemonstrator.Instance;
            Time.timeScale = 4f; float t = 0; while (demo.Running && t < 120f) { t += Time.unscaledDeltaTime; yield return null; } Time.timeScale = 1f;
            Assert.IsFalse(demo.Running);
            var onset = MedSimAI.Core.ClinicalScenarioManager.Instance.Scenario.FindFact("H_ONSET").answer;
            Assert.IsTrue(demo.Log.Any(l => l.StartsWith("say Instructor") && l.Contains("When did it start")), "the doctor asks the patient");
            Assert.IsTrue(demo.Log.Any(l => l == "say Patient: " + onset), "the patient answers with his authored history");
            Assert.IsFalse(MedSimAI.Core.ClinicalScenarioManager.Instance.Session.Tracker.Log.Any(e => e.eventId == "FACT:H_ONSET"), "not credited as the student's question");
        }

        [UnityTest]
        public IEnumerator Clinical_DemonstrationAskedInTheCorridor_WalksToRoom01First()
        {
            Assert.AreEqual(HospitalRoomId.Corridor, HC.PlayerRoom);
            Assert.IsTrue(HC.HandleDemonstrationRequest(MedSimAI.AI.Persona.Instructor, "أنا خبرة العملية كتير ضعيفة، بدي إياك تروح تشوفي المريض وتحكي معاه وتطبق عملياً، وأنا أتعلّم منك"), "the manual-test sentence is understood");
            yield return null;
            Assert.IsTrue(HC.Log.Any(l => l.Contains("request Room01 via demonstration")), "she walks the student to Room 01 first");
            Assert.AreEqual(HospitalRoomId.Room01, HC.GuidingTo);
        }

        [UnityTest]
        public IEnumerator Clinical_PatientHeadRestsOnThePillow_FaceVisible()
        {
            yield return EnterRoom01WithDoctor();
            var s = Object.FindAnyObjectByType<MedSimAI.Characters.HeadPillowSupport>(); Assert.IsNotNull(s, "head support installed");
            yield return new WaitForSeconds(0.5f);
            Assert.Less(Mathf.Abs(s.LastGap), 0.03f, $"the head rests on the pillow surface (not sunk into it): d={s.LastDistance:F3} flex={s.LastFlex:F1} need={s.pillowThickness + s.headRadius:F3} thick={s.pillowThickness:F3} k={s.LastK:F4} n={s.Instances} pillow={s.pillow.name}");
        }

        [UnityTest]
        public IEnumerator FromTheClosedOperatingRoom_TheDoctorCanStillGuideToRoom01()
        {
            yield return EnterOperatingRoom();
            var agent = HC.doctor.GetComponent<UnityEngine.AI.NavMeshAgent>(); var spot = SurgeryAnchor.Find(AnchorIds.SurgeonObserve).position;
            if (agent && agent.enabled) agent.Warp(spot);
            yield return new WaitForSeconds(4f);   // nobody near the doorway: the automatic doors close (manual-test bug: no_path)
            HC.RequestRoom(HospitalRoomId.Room01, "test");
            yield return new WaitForSeconds(3f);
            Assert.IsFalse(HC.Log.Any(l => l.Contains("no_path")), "the doors open for her: " + string.Join(" | ", HC.Log.Skip(System.Math.Max(0, HC.Log.Count - 5))));
            Assert.AreEqual(HospitalRoomId.Room01, HC.GuidingTo, "she is walking the student to Room 01");
        }

        [UnityTest]
        public IEnumerator Restart_NeedsRTwice_AndHudLabelsAreEnglishWithoutDraftBanner()
        {
            AIProviderManager.Instance.SetLanguageMode(LanguageMode.Arabic);
            yield return EnterOperatingRoom();
            var hud = HC.surgery.controller.hud; Assert.IsNotNull(hud); yield return null;
            var texts = Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None).Where(x => x.isActiveAndEnabled).Select(x => x.text).ToList();
            Assert.IsFalse(texts.Any(x => x.Contains("NOT CLINICALLY") || x.Contains("DRAFT")), "no draft banner in the live UI");
            Assert.IsFalse(texts.Any(x => x.Contains(MedSimAI.UI.ArabicShaper.PrepareRtl("غير معتمدة")) || x.Contains("غير معتمدة")), "no Arabic draft banner");
            Assert.IsTrue(texts.Any(x => x.StartsWith("Step 1 of 9")), "step counter in English even in Arabic mode");
            Assert.IsTrue(texts.Any(x => x == "Confirm the operative site"), "step name in English");
            Assert.IsTrue(texts.Any(x => x.StartsWith("AI ")), "short English AI status");
            // R once: only a prompt; R twice: restart
            var oldSession = Bus.SessionId;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            Adapter.enabled = true;
            IEnumerator PressR() { InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.R)); yield return null; InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState()); yield return null; }
            yield return PressR();
            yield return null;
            Assert.AreEqual(oldSession, Bus.SessionId, "one R does not restart the operation");
            Assert.IsTrue(Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None).Any(x => x.isActiveAndEnabled && x.text.Contains("Press R again")), "confirmation prompt shown");
            yield return PressR();
            float t = 0; while (Bus.SessionId == oldSession && t < 3f) { t += Time.deltaTime; yield return null; }
            Assert.AreNotEqual(oldSession, Bus.SessionId, "the second R restarts");
        }
    }
}
