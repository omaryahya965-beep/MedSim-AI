using System.Collections.Generic;
using System.Linq;
using MedSimAI.Surgery.Mechanics;
using MedSimAI.Surgery.Shared;
using MedSimAI.Surgery.Shared.Mocks;
using MedSimAI.Surgery.Shared.Unity;
using NUnit.Framework;
using UnityEngine;

namespace MedSimAI.Surgery.Tests.Mechanics
{
    /// <summary>
    /// Integration tests (2026-11-28): the authored OR_TKA_TRAINING_001 objectives driven through the real mechanics
    /// controllers, with the multi-step sequencing (guide → saw, seat components → impact) and wrong-side detection.
    /// </summary>
    public class SurgerySequencingTests
    {
        GameObject hostGo, rootGo; readonly List<GameObject> extra = new List<GameObject>();
        List<SurgeryEvent> log;
        SurgeryScenarioEngine engine; AnatomyInteractionController anatomy; SurgicalInstrumentController instruments; ImplantPlacementController implants;
        readonly Dictionary<string, MockAnatomyStateView> views = new Dictionary<string, MockAnatomyStateView>();

        [SetUp]
        public void SetUp()
        {
            log = new List<SurgeryEvent>();
            hostGo = new GameObject("Bus");
            var host = hostGo.AddComponent<SurgeryEventBusHost>();
            host.logToConsole = false;
            host.Bus.BeginSession("SEQ_TEST");
            host.Bus.EventPublished += e => log.Add(e.Clone());

            rootGo = new GameObject("Mechanics");
            var root = rootGo.AddComponent<SurgeryMechanicsRoot>();
            root.EnsureSubsystems();
            engine = root.ScenarioEngine; anatomy = root.AnatomyController; instruments = root.InstrumentController; implants = root.ImplantController;
            Assert.IsTrue(engine.LoadScenarioFromResources("SurgeryScenarios/OR_TKA_TRAINING_001"), "scenario JSON");
            engine.StartNextObjective();

            var scenario = JsonUtility.FromJson<SurgicalScenarioData>(Resources.Load<TextAsset>("SurgeryScenarios/OR_TKA_TRAINING_001").text);
            foreach (var t in scenario.targets)
            {
                var v = new MockAnatomyStateView(t.id, t.states);
                views[t.id] = v; anatomy.RegisterTargetView(v);
            }
            foreach (var id in scenario.instruments.Select(i => i.id))
            {
                var go = new GameObject(id); extra.Add(go);
                var tag = go.AddComponent<SurgeryInstrumentTag>(); tag.instrumentId = id;
                instruments.RegisterInstrument(tag);
            }
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var g in extra) if (g) Object.DestroyImmediate(g);
            extra.Clear(); views.Clear();
            Object.DestroyImmediate(rootGo); Object.DestroyImmediate(hostGo);
        }

        bool Use(string tool, string target, bool powered = false)
        {
            instruments.SetToolActive(tool, powered);
            return anatomy.HandleContact(tool, target);
        }

        SurgeryImplantTag MakeImplant(string id, Vector3 seat)
        {
            var targetGo = new GameObject("PlacementTarget_" + id); targetGo.transform.position = seat; extra.Add(targetGo);
            var go = new GameObject(id); extra.Add(go);
            var tag = go.AddComponent<SurgeryImplantTag>(); tag.implantId = id; tag.placementTarget = targetGo.transform;
            go.transform.position = seat + Vector3.right;   // starts on the table, 1 m away
            implants.RegisterImplant(tag);
            return tag;
        }

        bool Place(SurgeryImplantTag tag, float errorMm)
        {
            tag.transform.SetPositionAndRotation(tag.placementTarget.position + Vector3.up * (errorMm / 1000f), tag.placementTarget.rotation);
            return implants.CheckPlacementAttempt(tag.implantId);
        }

        void CompleteFirstThree()
        {
            Assert.IsTrue(Use(InstrumentIds.SkinMarker, AnatomyTargetIds.SiteMarking));
            Assert.IsTrue(Use(InstrumentIds.Scalpel, AnatomyTargetIds.IncisionLine));
            Assert.IsTrue(Use(InstrumentIds.Retractor, AnatomyTargetIds.JointCapsule));
            Assert.AreEqual("obj.femoral_resection", engine.CurrentObjectiveId);
        }

        [Test]
        public void WrongKnee_IsRejectedAsWrongSide_AndNothingChanges()
        {
            Assert.IsFalse(Use(InstrumentIds.SkinMarker, "knee_l.site_marking"));
            Assert.AreEqual(RejectReasons.WrongSide, log.Last(e => e.id == SurgeryEventIds.InteractionRejected).reason);
            Assert.AreEqual(AnatomyStates.Intact, views[AnatomyTargetIds.SiteMarking].CurrentState);
            Assert.AreEqual("obj.site_confirmation", engine.CurrentObjectiveId);
        }

        [Test]
        public void Resection_NeedsTheGuideFirst_ThenThePoweredSaw()
        {
            CompleteFirstThree();
            var femur = views[AnatomyTargetIds.DistalFemur];

            Assert.IsFalse(Use(InstrumentIds.OscillatingSaw, AnatomyTargetIds.DistalFemur, powered: true), "saw before guide");
            Assert.AreEqual(RejectReasons.PrerequisiteMissing, log.Last(e => e.id == SurgeryEventIds.InteractionRejected).reason);
            Assert.AreEqual(AnatomyStates.Intact, femur.CurrentState);

            Assert.IsTrue(Use(InstrumentIds.FemoralCuttingGuide, AnatomyTargetIds.DistalFemur));
            Assert.AreEqual(AnatomyStates.GuidePlaced, femur.CurrentState);
            Assert.IsFalse(engine.IsObjectiveComplete("obj.femoral_resection"), "the guide alone does not resect");

            Assert.IsFalse(Use(InstrumentIds.OscillatingSaw, AnatomyTargetIds.DistalFemur, powered: false), "saw switched off");
            Assert.AreEqual(RejectReasons.ToolNotActive, log.Last(e => e.id == SurgeryEventIds.InteractionRejected).reason);

            Assert.IsTrue(Use(InstrumentIds.OscillatingSaw, AnatomyTargetIds.DistalFemur, powered: true));
            Assert.AreEqual(AnatomyStates.Resected, femur.CurrentState);
            Assert.IsTrue(engine.IsObjectiveComplete("obj.femoral_resection"));
            Assert.AreEqual("obj.tibial_resection", engine.CurrentObjectiveId);
            Assert.AreEqual(1, log.Count(e => e.id == SurgeryEventIds.ObjectiveCompleted && e.objectiveId == "obj.femoral_resection"));
        }

        [Test]
        public void FullAuthoredScenario_CompletesOnlyThroughRealInteractions()
        {
            CompleteFirstThree();
            Assert.IsTrue(Use(InstrumentIds.FemoralCuttingGuide, AnatomyTargetIds.DistalFemur));
            Assert.IsTrue(Use(InstrumentIds.OscillatingSaw, AnatomyTargetIds.DistalFemur, true));
            Assert.IsTrue(Use(InstrumentIds.TibialCuttingGuide, AnatomyTargetIds.ProximalTibia));
            Assert.IsTrue(Use(InstrumentIds.OscillatingSaw, AnatomyTargetIds.ProximalTibia, true));
            Assert.AreEqual("obj.trial_reduction", engine.CurrentObjectiveId);

            // Trials: both must be seated; out of tolerance is rejected; then they are taken out automatically.
            var femTrial = MakeImplant(ImplantIds.FemoralTrial, new Vector3(0, 1, 0));
            var tibTrial = MakeImplant(ImplantIds.TibialTrial, new Vector3(0, 0.95f, 0));
            var femComp = MakeImplant(ImplantIds.FemoralComponent, new Vector3(0, 1, 0));
            var tray = MakeImplant(ImplantIds.TibialTray, new Vector3(0, 0.95f, 0));
            var insert = MakeImplant(ImplantIds.PolyInsert, new Vector3(0, 0.96f, 0));
            Assert.IsFalse(Place(femComp, 0.5f), "final component during the trial step");
            Assert.AreEqual(RejectReasons.WrongTool, log.Last(e => e.id == SurgeryEventIds.InteractionRejected).reason);
            Assert.IsFalse(Place(femTrial, 9f), "9 mm off");
            Assert.AreEqual(RejectReasons.OutOfTolerance, log.Last(e => e.id == SurgeryEventIds.InteractionRejected).reason);
            Assert.IsTrue(Place(femTrial, 1f));
            Assert.IsFalse(engine.IsObjectiveComplete("obj.trial_reduction"), "one trial is not a trial reduction");
            Assert.IsTrue(Place(tibTrial, 1f));
            Assert.IsTrue(engine.IsObjectiveComplete("obj.trial_reduction"));
            Assert.AreEqual(AnatomyStates.TrialFitted, views[AnatomyTargetIds.DistalFemur].CurrentState);
            Assert.Greater(Vector3.Distance(femTrial.transform.position, femTrial.placementTarget.position), 0.5f, "trial taken out after the step");

            // Femoral component: seat, then impact.
            Assert.IsFalse(Use(InstrumentIds.Impactor, AnatomyTargetIds.DistalFemur), "impact before seating");
            Assert.IsTrue(Place(femComp, 1f));
            Assert.IsFalse(engine.IsObjectiveComplete("obj.implant_femoral"));
            Assert.IsTrue(Use(InstrumentIds.Impactor, AnatomyTargetIds.DistalFemur));
            Assert.AreEqual(AnatomyStates.Implanted, views[AnatomyTargetIds.DistalFemur].CurrentState);
            Assert.IsTrue(engine.IsObjectiveComplete("obj.implant_femoral"));

            // Tibia: insert needs the tray first.
            Assert.IsFalse(Place(insert, 1f));
            Assert.AreEqual(RejectReasons.PrerequisiteMissing, log.Last(e => e.id == SurgeryEventIds.InteractionRejected).reason);
            Assert.IsTrue(Place(tray, 1f));
            Assert.IsTrue(Place(insert, 1f));
            Assert.IsTrue(Use(InstrumentIds.Mallet, AnatomyTargetIds.ProximalTibia));
            Assert.IsTrue(engine.IsObjectiveComplete("obj.implant_tibial"));

            Assert.IsTrue(Use(InstrumentIds.SkinStapler, AnatomyTargetIds.Wound));
            Assert.IsTrue(engine.IsFinished);
            Assert.AreEqual(9, log.Count(e => e.id == SurgeryEventIds.ObjectiveCompleted), "each objective completed exactly once");
            Assert.IsTrue(log.All(e => e.source == SurgeryEventSources.Mechanics), "every event comes from the real mechanics");

            // Reset: everything back, ready for a new session.
            rootGo.GetComponent<SurgeryResetController>().ExecuteReset();
            Assert.AreEqual("obj.site_confirmation", engine.CurrentObjectiveId);
            Assert.IsTrue(views.Values.All(v => v.CurrentState == AnatomyStates.Intact));
            Assert.Greater(Vector3.Distance(femComp.transform.position, femComp.placementTarget.position), 0.5f, "components back on the table");
            Assert.AreEqual(0, engine.Progress.PlacedImplants.Count);
        }
    }
}
