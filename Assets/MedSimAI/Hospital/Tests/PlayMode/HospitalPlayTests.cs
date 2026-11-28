using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MedSimAI.AI;
using MedSimAI.Characters;
using MedSimAI.Clinical;
using MedSimAI.Core;
using MedSimAI.Surgery.AI;
using MedSimAI.Surgery.Anatomy;
using MedSimAI.Surgery.Mechanics;
using MedSimAI.Surgery.Shared;
using MedSimAI.Surgery.Shared.Unity;
using MedSimAI.Surgery.UI;
using MedSimAI.Voice;
using NUnit.Framework;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MedSimAI.Hospital.PlayTests
{
    /// <summary>
    /// The integrated hospital (MedSim_Hospital) in Play Mode, offline (no billable AI calls): reception welcome, the
    /// doctor walking the student to a room, the real surgical simulation on Person 2's assets and Person 3's mechanics,
    /// the verified hand-over, leaving a room, session isolation, corridor commands and failure reporting.
    /// </summary>
    public class HospitalPlayTests
    {
        HospitalCoordinator HC => HospitalCoordinator.Instance;
        Transform Rig => UnityEngine.Object.FindAnyObjectByType<XROrigin>().transform;
        Transform Head => Camera.main.transform;
        Transform Doctor => HC.doctor.transform;

        [UnitySetUp]
        public IEnumerator Load()
        {
            AIProviderManager.ForceOffline = true; MedSimAI.XR.DisplayModeManager.DisableAutoDesktop = true;   // never billable
            yield return SceneManager.LoadSceneAsync("MedSim_Hospital");
            float t = 0; while ((HC == null || !HC.Welcomed) && t < 20f) { t += Time.unscaledDeltaTime; yield return null; }
            Assert.IsNotNull(HC, "hospital coordinator"); Assert.IsTrue(HC.Welcomed, "the doctor welcomed the student");
        }

        [UnityTearDown]
        public IEnumerator Unload() { Time.timeScale = 1; yield return null; }

        // ------------------------------------------------------------------ helpers
        void PlaceStudent(Vector3 floorPos, Vector3 lookAt)
        {
            var cc = Rig.GetComponent<CharacterController>(); if (cc) cc.enabled = false;
            var look = lookAt - floorPos; look.y = 0;
            Rig.SetPositionAndRotation(floorPos, look.sqrMagnitude > 1e-3f ? Quaternion.LookRotation(look) : Rig.rotation);
            if (cc) cc.enabled = true;
            Physics.SyncTransforms();
        }

        /// <summary>Student follows 1.4 m behind the doctor until done(); records the largest per-frame jump of the doctor.</summary>
        IEnumerator Follow(Func<bool> done, float timeout, Action<float> maxStep)
        {
            float t = 0, max = 0; var last = Doctor.position;
            while (!done() && t < timeout)
            {
                var d = Doctor.position; max = Mathf.Max(max, Vector3.Distance(d, last)); last = d;
                var behind = d - Doctor.forward * 1.4f;
                if (NavMesh.SamplePosition(behind, out var hit, 1f, NavMesh.AllAreas) && Time.frameCount % 15 == 0) PlaceStudent(hit.position, d);
                t += Time.deltaTime; yield return null;
            }
            maxStep(max);
        }

        IEnumerator EnterOperatingRoom()
        {
            var student = SurgeryAnchor.Find(AnchorIds.StudentOperatingPosition);
            PlaceStudent(student.position, SurgeryAnchor.Find(AnchorIds.SurgicalSiteKneeR).position);
            float t = 0; while (HC.SessionRoom != HospitalRoomId.Room02 && t < 5f) { t += Time.deltaTime; yield return null; }
            Assert.AreEqual(HospitalRoomId.Room02, HC.SessionRoom, "entering Room 02 starts the surgical session");
            var ctl = HC.surgery.controller;
            t = 0; while ((ctl.Core == null || !ctl.Core.Active || string.IsNullOrEmpty(ctl.Core.Runtime?.CurrentObjectiveId)) && t < 15f) { t += Time.deltaTime; yield return null; }
            Assert.IsTrue(ctl.Core.Active, "surgical core active");
        }

        SurgeryEventBus Bus => SurgeryEventBusHost.Instance.Bus;
        SurgeryMechanicsRoot Mech => HC.surgery.mechanics;
        SurgicalDesktopInteractionAdapter Adapter => HC.surgery.desktopAdapter;
        SurgeryInstrumentTag Tool(string id) => UnityEngine.Object.FindObjectsByType<SurgeryInstrumentTag>(FindObjectsSortMode.None).First(t => t.instrumentId == id);
        SurgeryImplantTag Implant(string id) => UnityEngine.Object.FindObjectsByType<SurgeryImplantTag>(FindObjectsSortMode.None).First(t => t.implantId == id);
        AnatomyTargetStateView View(string id) => UnityEngine.Object.FindObjectsByType<AnatomyTargetStateView>(FindObjectsInactive.Include, FindObjectsSortMode.None).First(v => v.TargetId == id);

        /// <summary>The desktop path: pick the instrument, (power it), apply it to the target, put it down.</summary>
        bool Use(string tool, string target, bool powered = false)
        {
            Adapter.PickupInstrument(Tool(tool));
            if (powered) Mech.InstrumentController.SetToolActive(tool, true);
            bool ok = Mech.AnatomyController.HandleContact(tool, target);
            Adapter.DropHeldItem();
            return ok;
        }

        bool Seat(string implantId, float errorMm)
        {
            var tag = Implant(implantId); Adapter.PickupImplant(tag);
            var target = GameObject.Find("PlacementTarget_" + implantId).transform;
            tag.transform.SetPositionAndRotation(target.position + target.up * (errorMm / 1000f), target.rotation);
            int before = Bus.Log.Count(e => e.id == SurgeryEventIds.ImplantPlacementCompleted && e.implantId == implantId);
            Adapter.DropHeldItem();
            return Bus.Log.Count(e => e.id == SurgeryEventIds.ImplantPlacementCompleted && e.implantId == implantId) > before;
        }

        string LastReject() => Bus.Log.LastOrDefault(e => e.id == SurgeryEventIds.InteractionRejected)?.reason;

        // ------------------------------------------------------------------ tests
        [UnityTest]
        public IEnumerator Start_InReception_DoctorWelcomes_NoSessionRunning()
        {
            yield return null;
            Assert.Greater(Head.position.x, 9.5f, "the student starts in the reception lobby");
            Assert.Less(Vector3.Distance(new Vector3(Doctor.position.x, 0, Doctor.position.z), new Vector3(Head.position.x, 0, Head.position.z)), 2.6f, "the doctor meets the student");
            Assert.IsTrue(HC.Log.Any(l => l.Contains("say:welcome")), "welcome spoken");
            Assert.AreEqual(AppState.Hospital, SessionManager.Instance.State, "no clinical session yet");
            Assert.AreEqual(HospitalRoomId.None, HC.SessionRoom);
            Assert.IsFalse(HC.surgery.Active); Assert.IsFalse(HC.surgery.sessionRoot.activeSelf, "surgical HUD/session not running in the corridor");
            var erPatient = UnityEngine.Object.FindAnyObjectByType<PatientInteractionController>().transform.position;
            Assert.Greater(Vector3.Distance(Head.position, erPatient), 6f, "not spawned beside a patient");
            Assert.IsNotNull(UnityEngine.Object.FindAnyObjectByType<HospitalDirectory>(), "directory sign");
            Assert.AreEqual(1, UnityEngine.Object.FindObjectsByType<MedicalInstructorController>(FindObjectsSortMode.None).Length, "exactly one doctor in the hospital");
        }

        [UnityTest]
        public IEnumerator Guide_WalksStudentToOperatingRoom_NoTeleport_ThenRealSurgicalSession()
        {
            Assert.IsTrue(HC.HandleUtterance("وين غرفة العمليات؟"), "navigation request understood");
            Assert.AreEqual(HospitalRoomId.Room02, HC.GuidingTo);
            float maxStep = 0; yield return Follow(() => HC.GuidingTo == HospitalRoomId.None, 45f, m => maxStep = m);
            Assert.AreEqual(HospitalRoomId.None, HC.GuidingTo, "arrived");
            Assert.Less(maxStep, 0.25f, "she walks (no teleport)");
            Assert.Less(Vector3.Distance(Doctor.position, HC.room02DoorPoint.position), 1.0f, "at the operating-room door");
            var rec = CharacterActionRouter.History.LastOrDefault(r => r.action.type == "guide_to_room");
            Assert.IsNotNull(rec); Assert.AreEqual(ActionState.Completed, rec.state, "the action reports its real outcome");

            yield return EnterOperatingRoom();
            var ctl = HC.surgery.controller;
            Assert.IsFalse(ctl.UsingMockMechanics, "real mechanics, not the mock");
            Assert.IsInstanceOf<SurgeryMechanicsRoot>(ctl.Core.Runtime);
            var started = Bus.Log.ToList().FindIndex(e => e.id == SurgeryEventIds.SessionStarted);
            var obj1 = Bus.Log.ToList().FindIndex(e => e.id == SurgeryEventIds.ObjectiveStarted && e.objectiveId == "obj.site_confirmation" && e.source == SurgeryEventSources.Mechanics);
            Assert.GreaterOrEqual(started, 0); Assert.Greater(obj1, started, "objective 1 starts inside the session");
            Assert.IsTrue(HC.surgery.doctorRole.enabled, "the same doctor now acts as the supervising surgeon");
            Assert.IsFalse(VoiceInteractionManager.Instance.enabled, "one voice pipeline at a time");
        }

        [UnityTest]
        public IEnumerator Surgery_FullScenario_RealInteractions_AnatomyChanges_Assessment()
        {
            yield return EnterOperatingRoom();
            var ctl = HC.surgery.controller;
            Assert.IsFalse(Use(InstrumentIds.Scalpel, AnatomyTargetIds.SiteMarking), "wrong tool");
            Assert.AreEqual(RejectReasons.WrongTool, LastReject());
            Assert.AreEqual(AnatomyStates.Intact, View(AnatomyTargetIds.SiteMarking).CurrentState, "rejected attempt changes nothing");
            Assert.IsFalse(Use(InstrumentIds.SkinMarker, "knee_l.site_marking"), "wrong knee");
            Assert.AreEqual(RejectReasons.WrongSide, LastReject());

            Assert.IsTrue(Use(InstrumentIds.SkinMarker, AnatomyTargetIds.SiteMarking));
            Assert.AreEqual(AnatomyStates.Marked, View(AnatomyTargetIds.SiteMarking).CurrentState);
            Assert.IsTrue(Use(InstrumentIds.Scalpel, AnatomyTargetIds.IncisionLine));
            Assert.IsTrue(Use(InstrumentIds.Retractor, AnatomyTargetIds.JointCapsule));
            yield return null;
            Assert.IsFalse(Use(InstrumentIds.OscillatingSaw, AnatomyTargetIds.DistalFemur, true), "saw before the guide");
            Assert.AreEqual(RejectReasons.PrerequisiteMissing, LastReject());
            Assert.IsTrue(Use(InstrumentIds.FemoralCuttingGuide, AnatomyTargetIds.DistalFemur));
            if (HC.surgery.robot) { yield return new WaitForSeconds(0.2f); Assert.IsTrue(HC.surgery.robot.GuidanceActive, "robot guidance shown during the resection"); Assert.Less(HC.surgery.robot.LastSolveErrorMeters, 0.2f, "the arm reaches over the knee"); }
            Assert.IsTrue(Use(InstrumentIds.OscillatingSaw, AnatomyTargetIds.DistalFemur, true));
            Assert.AreEqual(AnatomyStates.Resected, View(AnatomyTargetIds.DistalFemur).CurrentState, "visible femoral resection");
            Assert.IsTrue(Use(InstrumentIds.TibialCuttingGuide, AnatomyTargetIds.ProximalTibia));
            Assert.IsTrue(Use(InstrumentIds.OscillatingSaw, AnatomyTargetIds.ProximalTibia, true));
            Assert.IsFalse(Seat(ImplantIds.FemoralTrial, 9f), "9 mm out of tolerance"); Assert.AreEqual(RejectReasons.OutOfTolerance, LastReject());
            Assert.IsTrue(Seat(ImplantIds.FemoralTrial, 1f)); Assert.IsTrue(Seat(ImplantIds.TibialTrial, 1f));
            Assert.AreEqual(AnatomyStates.TrialFitted, View(AnatomyTargetIds.ProximalTibia).CurrentState);
            Assert.IsTrue(Seat(ImplantIds.FemoralComponent, 1f)); Assert.IsTrue(Use(InstrumentIds.Impactor, AnatomyTargetIds.DistalFemur));
            Assert.AreEqual(AnatomyStates.Implanted, View(AnatomyTargetIds.DistalFemur).CurrentState);
            Assert.IsTrue(Seat(ImplantIds.TibialTray, 1f)); Assert.IsTrue(Seat(ImplantIds.PolyInsert, 1f)); Assert.IsTrue(Use(InstrumentIds.Mallet, AnatomyTargetIds.ProximalTibia));
            Assert.IsTrue(Use(InstrumentIds.SkinStapler, AnatomyTargetIds.Wound));
            Assert.AreEqual(AnatomyStates.Closed, View(AnatomyTargetIds.Wound).CurrentState);
            float t = 0; while (!ctl.Core.Completed && t < 5f) { t += Time.deltaTime; yield return null; }
            Assert.IsTrue(ctl.Core.Completed, "session completed by the mechanics' objective events");
            var r = ctl.Core.Result;
            Assert.IsTrue(r.sessionCompleted); Assert.Greater(r.score, 0); Assert.GreaterOrEqual(r.invalidInteractions, 4);
            Assert.AreEqual(9, Bus.Log.Count(e => e.id == SurgeryEventIds.ObjectiveCompleted && e.source == SurgeryEventSources.Mechanics), "each objective credited once, from mechanics");
            Assert.IsFalse(Bus.Log.Any(e => e.source == SurgeryEventSources.Mock), "no mock events");
            var rec = HC.Records.LastOrDefault(); Assert.IsNotNull(rec); Assert.AreEqual("completed", rec.outcome); Assert.AreEqual(HospitalRoomId.Room02, rec.room);
        }

        [UnityTest]
        public IEnumerator Handoff_CompletesOnlyAfterTheStudentReallyTakesTheInstrument()
        {
            yield return EnterOperatingRoom();
            var core = HC.surgery.controller.Core; bool completed = false; core.Handoff.Completed += _ => completed = true;
            var intent = new SurgeryIntentParser.Intent { addressed = SurgeryCharacterIds.Assistant };
            intent.action.characterId = SurgeryCharacterIds.Assistant; intent.action.action = SurgeryActionCatalog.HandInstrument; intent.action.instrumentId = InstrumentIds.SkinMarker;
            core.HandleRequest(intent);
            float t = 0; while (core.Handoff.PendingTool != InstrumentIds.SkinMarker && t < 40f) { t += Time.deltaTime; yield return null; }
            Assert.AreEqual(InstrumentIds.SkinMarker, core.Handoff.PendingTool, "the assistant is holding the marker out");
            Assert.IsFalse(completed, "not handed over until the student takes it");
            Assert.IsFalse(Bus.Log.Any(e => e.id == SurgeryEventIds.ToolGrabbed && e.toolId == InstrumentIds.SkinMarker), "no grab yet");
            Adapter.PickupInstrument(Tool(InstrumentIds.SkinMarker));   // the student takes it (desktop path)
            yield return null; yield return null;
            Assert.IsTrue(completed, "completed on the verified tool.grabbed");
            Assert.IsNull(HC.surgery.assistant.Held, "the assistant let go");
            Assert.AreNotEqual(HC.surgery.assistant.handSocket, Tool(InstrumentIds.SkinMarker).transform.parent, "the marker is no longer parented to the assistant's hand");
            Adapter.DropHeldItem();
        }

        [UnityTest]
        public IEnumerator LeavingTheOperatingRoom_EndsTheSession_ThenRoom01StartsClean()
        {
            yield return EnterOperatingRoom();
            Assert.IsTrue(Use(InstrumentIds.SkinMarker, AnatomyTargetIds.SiteMarking));
            PlaceStudent(new Vector3(5.0f, 0, -3.8f), new Vector3(2f, 0, -3.8f));   // back out to the corridor
            float t = 0; while (HC.SessionRoom != HospitalRoomId.None && t < 6f) { t += Time.deltaTime; yield return null; }
            Assert.AreEqual(HospitalRoomId.None, HC.SessionRoom, "leaving ends the surgical session");
            Assert.IsFalse(HC.surgery.Active); Assert.IsFalse(HC.surgery.sessionRoot.activeSelf);
            Assert.AreEqual(AnatomyStates.Intact, View(AnatomyTargetIds.SiteMarking).CurrentState, "anatomy reset");
            Assert.IsTrue(VoiceInteractionManager.Instance.enabled, "clinical voice pipeline back");
            Assert.IsFalse(HC.surgery.doctorRole.enabled, "surgeon role released");
            Assert.AreEqual("left unfinished", HC.Records.Last().outcome);

            PlaceStudent(new Vector3(0.6f, 0, -1.2f), new Vector3(0.3f, 0, 0.9f));   // straight into Room 01 (also past the doorway trigger)
            t = 0; while (HC.SessionRoom != HospitalRoomId.Room01 && t < 5f) { t += Time.deltaTime; yield return null; }
            Assert.AreEqual(HospitalRoomId.Room01, HC.SessionRoom, "entering Room 01 starts the clinical session (teleport-safe)");
            yield return new WaitForSeconds(0.3f);
            var s = SessionManager.Instance.State; Assert.IsTrue(s == AppState.Running || s == AppState.Tutorial, "clinical session running: " + s);
            var log = ClinicalScenarioManager.Instance.Session.Tracker.Log.Select(x => x.eventId + ":" + x.detail).ToList();
            Assert.IsFalse(log.Any(l => l.Contains("inst.") || l.Contains("knee_r") || l.Contains("implant")), "no surgical events in the clinical session");
        }

        [UnityTest]
        public IEnumerator Corridor_Wait_WalkWithMe_Restart_AndMenuShortcut()
        {
            Assert.IsTrue(HC.HandleUtterance("استني شوي"));
            yield return null;
            Assert.AreEqual(HospitalGuide.Mode.Holding, HC.guide.State);
            Assert.AreEqual(ActionState.Completed, CharacterActionRouter.History.Last(r => r.action.type == "wait_for_student").state);
            Assert.IsTrue(HC.HandleUtterance("امشي معي"));
            PlaceStudent(new Vector3(7.5f, 0, -3.8f), new Vector3(2f, 0, -3.8f));
            float t = 0; while (Vector3.Distance(Doctor.position, Head.position) > 2.2f && t < 12f) { t += Time.deltaTime; yield return null; }
            Assert.LessOrEqual(Vector3.Distance(new Vector3(Doctor.position.x, 0, Doctor.position.z), new Vector3(Head.position.x, 0, Head.position.z)), 2.2f, "she walks with the student");
            Assert.IsTrue(HC.HandleUtterance("بدي أبدأ من جديد"));
            Assert.IsTrue(HC.Log.Any(l => l.Contains("welcome_again")));
            Assert.IsNotNull(ScenarioRouter.MenuOverride, "F10 / hold menu returns to the corridor, not to a menu scene");
        }

        [UnityTest]
        public IEnumerator Navigation_ThroughAClosedDoor_IsReportedAsUnavailable_NotFaked()
        {
            var agent = HC.doctor.GetComponent<NavMeshAgent>();
            agent.Warp(new Vector3(-0.8f, 0, -0.45f));                     // doctor inside Room 01
            PlaceStudent(new Vector3(9f, 0, -3.8f), new Vector3(2f, 0, -3.8f)); // student in the corridor
            var door = UnityEngine.Object.FindAnyObjectByType<MedSimAI.Instruments.HospitalDoor>(); door.ResetDoor();
            yield return new WaitForSeconds(1.0f);                         // closed leaf carves the NavMesh
            Assert.IsFalse(door.IsOpen);
            HC.RequestRoom(HospitalRoomId.Room02, "test");
            yield return null; yield return null;
            var rec = CharacterActionRouter.History.LastOrDefault(r => r.action.type == "guide_to_room");
            Assert.IsNotNull(rec); Assert.AreNotEqual(ActionState.Completed, rec.state, "no path through the closed door: not reported as done");
            Assert.IsTrue(HC.Log.Any(l => l.Contains("say:no_path")), "she says so truthfully");
        }

        [UnityTest]
        public IEnumerator Language_PersistsAcrossRooms()
        {
            AIProviderManager.Instance.SetLanguageMode(LanguageMode.Arabic);
            yield return EnterOperatingRoom();
            Assert.AreEqual("ar", ScenarioRouter.Language); Assert.AreEqual("ar", HC.surgery.controller.Core.Language);
            HC.surgery.controller.Core.Language = "en";   // the student switches language in the operating room
            yield return null; yield return null;
            Assert.AreEqual("en", AIProviderManager.Instance.ConversationLanguage, "the hospital follows");
        }
    }
}
