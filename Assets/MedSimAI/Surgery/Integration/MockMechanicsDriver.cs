using System.Linq;
using MedSimAI.Surgery.Shared;
using MedSimAI.Surgery.Shared.Mocks;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MedSimAI.Surgery.Integration
{
    /// <summary>
    /// Keyboard stand-in for Person 3's mechanics, active ONLY while the session controller uses the mock runtime.
    /// Publishes contract-shaped events with source "mock" (never credited in a real build) so the AI surgeon,
    /// assistant, assessment and debrief can be tested end-to-end before feature/surgery-mechanics is merged.
    ///   N = complete the current step correctly · W = wrong tool · S = wrong side · P = out-of-order attempt
    ///   O = placement out of tolerance · G = player takes the instrument the assistant is holding out
    /// </summary>
    public class MockMechanicsDriver : MonoBehaviour
    {
        public SurgerySessionController controller;
        bool started;

        void Update()
        {
            if (!controller) controller = SurgerySessionController.Instance;
            var core = controller ? controller.Core : null;
            if (core == null || !controller.UsingMockMechanics || !core.Active || !(core.Runtime is MockScenarioRuntime rt)) return;
            var bus = core.Bus; var s = controller.Scenario; var kb = Keyboard.current; if (kb == null) return;
            if (!started && bus.Log.Any(e => e.id == SurgeryEventIds.SessionStarted)) { started = true; StartCurrent(bus, rt); }
            if (bus.Log.Count > 0 && bus.Log.Last().id == SurgeryEventIds.SessionStarted && bus.Log.Count == 1) started = false;   // after a restart
            var cur = s.Objective(rt.CurrentObjectiveId);
            if (cur == null) return;
            string tool = cur.requiredTools.FirstOrDefault() ?? "", target = cur.targets.FirstOrDefault() ?? "";
            if (kb.nKey.wasPressedThisFrame)
            {
                if (!string.IsNullOrEmpty(tool)) bus.Publish(SurgeryEvent.Tool(SurgeryEventIds.ToolGrabbed, tool, cur.id, SurgeryEventSources.Mock));
                bus.Publish(SurgeryEvent.Anatomy(target, tool, cur.id, SurgeryEventSources.Mock));
                if (!string.IsNullOrEmpty(cur.implantId)) bus.Publish(SurgeryEvent.Implant(SurgeryEventIds.ImplantPlacementCompleted, cur.implantId, target, 1.2f, 1.5f, true, cur.id, SurgeryEventSources.Mock));
                if (!string.IsNullOrEmpty(cur.completionState)) bus.Publish(SurgeryEvent.StateChanged(target, cur.completionState, cur.id, SurgeryEventSources.Mock));
                foreach (var e in rt.CompleteCurrent()) bus.Publish(e);
            }
            if (kb.wKey.wasPressedThisFrame) bus.Publish(SurgeryEvent.Rejected(RejectReasons.WrongTool, s.instruments.Select(i => i.id).FirstOrDefault(i => !cur.requiredTools.Contains(i)) ?? "", target, cur.id, SurgeryEventSources.Mock));
            if (kb.sKey.wasPressedThisFrame) bus.Publish(SurgeryEvent.Rejected(RejectReasons.WrongSide, tool, target.Replace("knee_r", "knee_l"), cur.id, SurgeryEventSources.Mock));
            if (kb.pKey.wasPressedThisFrame) bus.Publish(SurgeryEvent.Rejected(RejectReasons.PrerequisiteMissing, tool, target, cur.id, SurgeryEventSources.Mock));
            if (kb.oKey.wasPressedThisFrame) bus.Publish(SurgeryEvent.Rejected(RejectReasons.OutOfTolerance, tool, target, cur.id, SurgeryEventSources.Mock));
            if (kb.gKey.wasPressedThisFrame && !string.IsNullOrEmpty(core.Handoff.PendingTool)) bus.Publish(SurgeryEvent.Tool(SurgeryEventIds.ToolGrabbed, core.Handoff.PendingTool, cur.id, SurgeryEventSources.Mock));
        }

        static void StartCurrent(SurgeryEventBus bus, MockScenarioRuntime rt)
        {
            if (!string.IsNullOrEmpty(rt.CurrentObjectiveId)) bus.Publish(SurgeryEvent.Objective(SurgeryEventIds.ObjectiveStarted, rt.CurrentObjectiveId, SurgeryEventSources.Mock));
        }

        void OnGUI()
        {
            if (!controller || !controller.UsingMockMechanics) return;
            GUI.Label(new Rect(10, Screen.height - 46, 900, 40), "MOCK MECHANICS (not scored as real performance) — N: complete step · W: wrong tool · S: wrong side · P: out of order · O: out of tolerance · G: take offered instrument · H: help · J: explain");
        }
    }
}
