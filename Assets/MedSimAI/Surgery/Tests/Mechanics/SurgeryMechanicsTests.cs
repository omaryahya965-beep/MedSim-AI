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
    public class SurgeryMechanicsTests
    {
        private GameObject hostGo;
        private SurgeryEventBusHost busHost;
        private List<SurgeryEvent> eventLog;

        [SetUp]
        public void SetUp()
        {
            eventLog = new List<SurgeryEvent>();
            hostGo = new GameObject("SurgeryEventBusHost_Test");
            busHost = hostGo.AddComponent<SurgeryEventBusHost>();
            busHost.logToConsole = false;
            busHost.Bus.BeginSession("TEST_SESSION_001");
            busHost.Bus.EventPublished += e => eventLog.Add(e.Clone());
        }

        [TearDown]
        public void TearDown()
        {
            if (hostGo != null)
                Object.DestroyImmediate(hostGo);
        }

        [Test]
        public void ToolGrabAndRelease_UpdatesRegistryAndEmitsEvents()
        {
            var rootGo = new GameObject("MechanicsRoot");
            var instGo = new GameObject("Scalpel");
            var tag = instGo.AddComponent<SurgeryInstrumentTag>();
            tag.instrumentId = InstrumentIds.Scalpel;

            var instCtrl = rootGo.AddComponent<SurgicalInstrumentController>();
            instCtrl.RegisterInstrument(tag);

            Assert.IsTrue(instCtrl.IsAvailable(InstrumentIds.Scalpel));
            Assert.AreEqual(InstrumentHolder.OnTable, instCtrl.HolderOf(InstrumentIds.Scalpel));

            // Grab
            instCtrl.HandleGrab(InstrumentIds.Scalpel, InstrumentHolder.Player);
            Assert.IsFalse(instCtrl.IsAvailable(InstrumentIds.Scalpel));
            Assert.AreEqual(InstrumentHolder.Player, instCtrl.HolderOf(InstrumentIds.Scalpel));

            var grabEvent = eventLog.FirstOrDefault(e => e.id == SurgeryEventIds.ToolGrabbed);
            Assert.IsNotNull(grabEvent);
            Assert.AreEqual(InstrumentIds.Scalpel, grabEvent.toolId);

            // Release
            instCtrl.HandleRelease(InstrumentIds.Scalpel);
            Assert.IsTrue(instCtrl.IsAvailable(InstrumentIds.Scalpel));
            Assert.AreEqual(InstrumentHolder.OnTable, instCtrl.HolderOf(InstrumentIds.Scalpel));

            var releaseEvent = eventLog.FirstOrDefault(e => e.id == SurgeryEventIds.ToolReleased);
            Assert.IsNotNull(releaseEvent);

            Object.DestroyImmediate(rootGo);
            Object.DestroyImmediate(instGo);
        }

        [Test]
        public void PoweredTool_RequiresActivation_BeforeContact()
        {
            var obj = new SurgicalObjective
            {
                id = "obj.femoral_resection",
                requiredTools = new[] { InstrumentIds.OscillatingSaw },
                targets = new[] { AnatomyTargetIds.DistalFemur },
                completionState = AnatomyStates.Resected
            };

            // Inactive saw -> should reject with ToolNotActive
            var resultInactive = SurgeryInteractionValidator.ValidateToolInteraction(
                obj, InstrumentIds.OscillatingSaw, AnatomyTargetIds.DistalFemur, isToolActive: false);
            Assert.IsFalse(resultInactive.Success);
            Assert.AreEqual(RejectReasons.ToolNotActive, resultInactive.Reason);

            // Active saw -> should succeed
            var resultActive = SurgeryInteractionValidator.ValidateToolInteraction(
                obj, InstrumentIds.OscillatingSaw, AnatomyTargetIds.DistalFemur, isToolActive: true);
            Assert.IsTrue(resultActive.Success);
            Assert.AreEqual(AnatomyStates.Resected, resultActive.NextAnatomyState);
        }

        [Test]
        public void InvalidTool_EmitsRejectedWithWrongTool()
        {
            var obj = new SurgicalObjective
            {
                id = "obj.site_confirmation",
                requiredTools = new[] { InstrumentIds.SkinMarker },
                targets = new[] { AnatomyTargetIds.SiteMarking },
                completionState = AnatomyStates.Marked
            };

            // Scalpel used instead of skin marker
            var result = SurgeryInteractionValidator.ValidateToolInteraction(
                obj, InstrumentIds.Scalpel, AnatomyTargetIds.SiteMarking, isToolActive: false);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(RejectReasons.WrongTool, result.Reason);
        }

        [Test]
        public void InvalidTarget_EmitsRejectedWithWrongTarget()
        {
            var obj = new SurgicalObjective
            {
                id = "obj.incision",
                requiredTools = new[] { InstrumentIds.Scalpel },
                targets = new[] { AnatomyTargetIds.IncisionLine },
                completionState = AnatomyStates.Incised
            };

            // Scalpel applied to patella instead of incision line
            var result = SurgeryInteractionValidator.ValidateToolInteraction(
                obj, InstrumentIds.Scalpel, AnatomyTargetIds.Patella, isToolActive: false);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(RejectReasons.WrongTarget, result.Reason);
        }

        [Test]
        public void WrongSide_RejectedWithWrongSide()
        {
            var obj = new SurgicalObjective
            {
                id = "obj.incision",
                requiredTools = new[] { InstrumentIds.Scalpel },
                targets = new[] { AnatomyTargetIds.IncisionLine }
            };

            var result = SurgeryInteractionValidator.ValidateToolInteraction(
                obj, InstrumentIds.Scalpel, AnatomyTargetIds.IncisionLine, isToolActive: false, isRightSide: false);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(RejectReasons.WrongSide, result.Reason);
        }

        [Test]
        public void AnatomyInteraction_ValidContact_ChangesStateAndCompletesObjective()
        {
            var rootGo = new GameObject("MechanicsRoot");
            var engine = rootGo.AddComponent<SurgeryScenarioEngine>();
            var anatomyCtrl = rootGo.AddComponent<AnatomyInteractionController>();
            var instCtrl = rootGo.AddComponent<SurgicalInstrumentController>();

            var scenario = new SurgicalScenarioData
            {
                scenarioId = "TEST_SCENARIO",
                objectives = new[]
                {
                    new SurgicalObjective
                    {
                        id = "obj.site_confirmation",
                        order = 1,
                        requiredTools = new[] { InstrumentIds.SkinMarker },
                        targets = new[] { AnatomyTargetIds.SiteMarking },
                        completionState = AnatomyStates.Marked
                    }
                }
            };

            engine.InitializeWithScenario(scenario);
            engine.StartNextObjective();

            // Set up mock anatomy view
            var mockView = new MockAnatomyStateView(AnatomyTargetIds.SiteMarking, AnatomyStates.Intact, AnatomyStates.Marked);
            anatomyCtrl.RegisterTargetView(mockView);

            // Execute valid contact
            bool success = anatomyCtrl.HandleContact(InstrumentIds.SkinMarker, AnatomyTargetIds.SiteMarking);
            Assert.IsTrue(success);

            // Anatomy state changed
            Assert.AreEqual(AnatomyStates.Marked, mockView.CurrentState);

            // Objective completed
            Assert.IsTrue(engine.IsObjectiveComplete("obj.site_confirmation"));

            // Event log verifies correct event order
            var stateEvent = eventLog.FirstOrDefault(e => e.id == SurgeryEventIds.AnatomyStateChanged);
            Assert.IsNotNull(stateEvent);
            Assert.AreEqual(AnatomyStates.Marked, stateEvent.state);

            var completedEvent = eventLog.FirstOrDefault(e => e.id == SurgeryEventIds.ObjectiveCompleted);
            Assert.IsNotNull(completedEvent);
            Assert.AreEqual("obj.site_confirmation", completedEvent.objectiveId);

            Object.DestroyImmediate(rootGo);
        }

        [Test]
        public void OrderingGuarantee_RejectedAttempt_DoesNotChangeAnatomyState()
        {
            var rootGo = new GameObject("MechanicsRoot");
            var engine = rootGo.AddComponent<SurgeryScenarioEngine>();
            var anatomyCtrl = rootGo.AddComponent<AnatomyInteractionController>();

            var scenario = new SurgicalScenarioData
            {
                scenarioId = "TEST_SCENARIO",
                objectives = new[]
                {
                    new SurgicalObjective
                    {
                        id = "obj.site_confirmation",
                        order = 1,
                        requiredTools = new[] { InstrumentIds.SkinMarker },
                        targets = new[] { AnatomyTargetIds.SiteMarking },
                        completionState = AnatomyStates.Marked
                    }
                }
            };

            engine.InitializeWithScenario(scenario);
            engine.StartNextObjective();

            var mockView = new MockAnatomyStateView(AnatomyTargetIds.SiteMarking, AnatomyStates.Intact, AnatomyStates.Marked);
            anatomyCtrl.RegisterTargetView(mockView);

            // Apply wrong tool (Scalpel)
            bool success = anatomyCtrl.HandleContact(InstrumentIds.Scalpel, AnatomyTargetIds.SiteMarking);
            Assert.IsFalse(success);

            // Crucial contract guarantee §7.3: anatomy state MUST remain intact on rejection
            Assert.AreEqual(AnatomyStates.Intact, mockView.CurrentState);

            var rejectEvent = eventLog.FirstOrDefault(e => e.id == SurgeryEventIds.InteractionRejected);
            Assert.IsNotNull(rejectEvent);
            Assert.AreEqual(RejectReasons.WrongTool, rejectEvent.reason);
            Assert.IsFalse(rejectEvent.success);

            Object.DestroyImmediate(rootGo);
        }

        [Test]
        public void ImplantPlacement_WithinTolerance_Succeeds()
        {
            var obj = new SurgicalObjective
            {
                id = "obj.trial_reduction",
                implantId = ImplantIds.FemoralTrial,
                targets = new[] { AnatomyTargetIds.DistalFemur },
                toleranceMm = 4f,
                toleranceDeg = 4f,
                completionState = AnatomyStates.TrialFitted
            };

            Vector3 targetPos = new Vector3(0, 1, 0);
            Quaternion targetRot = Quaternion.identity;

            // Actual pos is 2 mm away (0.002 m), 1.5 degrees rotated -> within 4mm / 4deg tolerance
            Vector3 actualPos = targetPos + new Vector3(0.002f, 0, 0);
            Quaternion actualRot = Quaternion.Euler(1.5f, 0, 0);

            var result = SurgeryInteractionValidator.ValidateImplantPlacement(
                obj, ImplantIds.FemoralTrial, AnatomyTargetIds.DistalFemur,
                actualPos, actualRot, targetPos, targetRot);

            Assert.IsTrue(result.Success);
            Assert.That(result.PositionErrorMm, Is.EqualTo(2.0f).Within(0.01f));
            Assert.AreEqual(AnatomyStates.TrialFitted, result.NextAnatomyState);
        }

        [Test]
        public void ImplantPlacement_OutOfTolerance_Rejected()
        {
            var obj = new SurgicalObjective
            {
                id = "obj.implant_femoral",
                implantId = ImplantIds.FemoralComponent,
                targets = new[] { AnatomyTargetIds.DistalFemur },
                toleranceMm = 2f,
                toleranceDeg = 3f,
                completionState = AnatomyStates.Implanted
            };

            Vector3 targetPos = new Vector3(0, 1, 0);
            Quaternion targetRot = Quaternion.identity;

            // Actual pos is 5 mm away (0.005 m) -> exceeds 2mm tolerance
            Vector3 actualPos = targetPos + new Vector3(0.005f, 0, 0);
            Quaternion actualRot = targetRot;

            var result = SurgeryInteractionValidator.ValidateImplantPlacement(
                obj, ImplantIds.FemoralComponent, AnatomyTargetIds.DistalFemur,
                actualPos, actualRot, targetPos, targetRot);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(RejectReasons.OutOfTolerance, result.Reason);
            Assert.That(result.PositionErrorMm, Is.EqualTo(5.0f).Within(0.01f));
        }

        [Test]
        public void RobotGuidance_EmitsToolActivated_AndChecksWorkspace()
        {
            var robotGo = new GameObject("Robot");
            var robotCtrl = robotGo.AddComponent<SurgicalRobotController>();

            Assert.IsFalse(robotCtrl.IsGuidanceActive);

            robotCtrl.ActivateGuidance();
            Assert.IsTrue(robotCtrl.IsGuidanceActive);

            var activatedEvent = eventLog.FirstOrDefault(e => e.id == SurgeryEventIds.ToolActivated && e.toolId == InstrumentIds.RobotArm);
            Assert.IsNotNull(activatedEvent);

            Assert.IsTrue(robotCtrl.IsWithinWorkspace(robotGo.transform.position + Vector3.forward * 0.5f));
            Assert.IsFalse(robotCtrl.IsWithinWorkspace(robotGo.transform.position + Vector3.forward * 5.0f));

            robotCtrl.ResetRobot();
            Assert.IsFalse(robotCtrl.IsGuidanceActive);

            Object.DestroyImmediate(robotGo);
        }

        [Test]
        public void FullReset_RestoresAllSubsystems_CleanlyForRepeatedSessions()
        {
            var rootGo = new GameObject("MechanicsRoot");
            var root = rootGo.AddComponent<SurgeryMechanicsRoot>();
            var engine = rootGo.GetComponent<SurgeryScenarioEngine>();
            var anatomyCtrl = rootGo.GetComponent<AnatomyInteractionController>();
            var instCtrl = rootGo.GetComponent<SurgicalInstrumentController>();
            var resetCtrl = rootGo.GetComponent<SurgeryResetController>();

            var scenario = new SurgicalScenarioData
            {
                scenarioId = "TEST_SCENARIO",
                objectives = new[]
                {
                    new SurgicalObjective { id = "obj.step1", order = 1, requiredTools = new[] { InstrumentIds.Scalpel }, targets = new[] { AnatomyTargetIds.IncisionLine } },
                    new SurgicalObjective { id = "obj.step2", order = 2, requiredTools = new[] { InstrumentIds.Retractor }, targets = new[] { AnatomyTargetIds.JointCapsule } }
                }
            };
            engine.InitializeWithScenario(scenario);
            engine.StartNextObjective();

            // Set up mock target
            var mockView = new MockAnatomyStateView(AnatomyTargetIds.IncisionLine, AnatomyStates.Intact, AnatomyStates.Incised);
            anatomyCtrl.RegisterTargetView(mockView);

            // Step 1: interact and change state
            anatomyCtrl.HandleContact(InstrumentIds.Scalpel, AnatomyTargetIds.IncisionLine);
            Assert.AreEqual("obj.step2", engine.CurrentObjectiveId);
            Assert.AreEqual(AnatomyStates.Incised, mockView.CurrentState);

            // Execute reset
            resetCtrl.ExecuteReset();

            // Verify clean state
            Assert.AreEqual("obj.step1", engine.CurrentObjectiveId);
            Assert.AreEqual(0, engine.CompletedObjectives.Count);
            Assert.IsFalse(engine.IsFinished);
            Assert.AreEqual(AnatomyStates.Intact, mockView.CurrentState);

            // Run a second repeated session to verify no stale state
            anatomyCtrl.HandleContact(InstrumentIds.Scalpel, AnatomyTargetIds.IncisionLine);
            Assert.AreEqual("obj.step2", engine.CurrentObjectiveId);
            Assert.AreEqual(1, engine.CompletedObjectives.Count);

            Object.DestroyImmediate(rootGo);
        }

        [Test]
        public void OfflineOperation_FunctioningWithoutAIOrNetwork()
        {
            // Verify mechanics operates 100% deterministically with null/offline AI
            var rootGo = new GameObject("MechanicsRoot");
            var engine = rootGo.AddComponent<SurgeryScenarioEngine>();

            var scenario = new SurgicalScenarioData
            {
                scenarioId = "TEST_OFFLINE",
                objectives = new[]
                {
                    new SurgicalObjective { id = "obj.1", order = 1 }
                }
            };
            engine.InitializeWithScenario(scenario);
            Assert.IsTrue(engine.StartNextObjective());
            Assert.AreEqual("obj.1", engine.CurrentObjectiveId);

            Assert.IsTrue(engine.CompleteCurrentObjective());
            Assert.IsTrue(engine.IsFinished);

            Object.DestroyImmediate(rootGo);
        }
    }
}
