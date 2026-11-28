using System;
using System.Linq;
using MedSimAI.Surgery.Shared;
using MedSimAI.Surgery.Shared.Mocks;
using NUnit.Framework;

namespace MedSimAI.Surgery.Tests
{
    public class SurgeryContractTests
    {
        [Test] public void EventIds_AreUnique_AndDotted()
        {
            Assert.AreEqual(SurgeryEventIds.All.Length, SurgeryEventIds.All.Distinct().Count());
            foreach (var id in SurgeryEventIds.All) StringAssert.Contains(".", id);
            foreach (var g in SurgeryEventIds.Gameplay) Assert.IsTrue(SurgeryEventIds.IsKnown(g), g);
        }

        [Test] public void InstrumentAnatomyImplantIds_AreUnique()
        {
            var all = InstrumentIds.All.Concat(AnatomyTargetIds.All).Concat(ImplantIds.All).ToArray();
            Assert.AreEqual(all.Length, all.Distinct().Count());
        }

        [Test] public void Bus_AssignsSequenceSessionAndTime()
        {
            var clock = new ManualClock { Now = 100 };
            var bus = new SurgeryEventBus { Clock = () => clock.Now };
            bus.BeginSession("S1");
            clock.Advance(2.5);
            bus.Publish(SurgeryEvent.Tool(SurgeryEventIds.ToolGrabbed, InstrumentIds.Scalpel));
            bus.Publish(SurgeryEvent.Tool(SurgeryEventIds.ToolReleased, InstrumentIds.Scalpel));
            Assert.AreEqual(2, bus.Log.Count);
            Assert.AreEqual(1, bus.Log[0].seq); Assert.AreEqual(2, bus.Log[1].seq);
            Assert.AreEqual("S1", bus.Log[0].sessionId);
            Assert.AreEqual(2.5f, bus.Log[0].t, 0.001f);
        }

        [Test] public void Bus_RejectsUnknownIds()
        {
            string err = null; var bus = new SurgeryEventBus { OnSubscriberError = m => err = m };
            bus.BeginSession("S");
            bus.Publish(new SurgeryEvent("tool.teleported", SurgeryEventSources.Mechanics));
            Assert.AreEqual(0, bus.Log.Count); StringAssert.Contains("unknown", err);
        }

        [Test] public void Bus_SubscriberException_DoesNotBreakOthersOrLog()
        {
            var bus = new SurgeryEventBus(); int got = 0; string err = null; bus.OnSubscriberError = m => err = m;
            bus.EventPublished += _ => throw new InvalidOperationException("AI offline");
            bus.EventPublished += _ => got++;
            bus.BeginSession("S");
            bus.Publish(SurgeryEvent.Objective(SurgeryEventIds.ObjectiveStarted, "obj.x"));
            Assert.AreEqual(1, got); Assert.AreEqual(1, bus.Log.Count); StringAssert.Contains("AI offline", err);
        }

        [Test] public void Bus_SubscribersCannotMutateLog()
        {
            var bus = new SurgeryEventBus(); bus.BeginSession("S");
            bus.EventPublished += e => e.objectiveId = "hacked";
            bus.Publish(SurgeryEvent.Objective(SurgeryEventIds.ObjectiveCompleted, "obj.real"));
            Assert.AreEqual("obj.real", bus.Log[0].objectiveId);
        }

        [Test] public void Bus_BeginSession_ClearsPreviousSession()
        {
            var bus = new SurgeryEventBus(); bus.BeginSession("A");
            bus.Publish(SurgeryEvent.Objective(SurgeryEventIds.ObjectiveCompleted, "obj.a"));
            bus.BeginSession("B");
            Assert.AreEqual(0, bus.Log.Count); Assert.AreEqual("B", bus.SessionId);
        }

        [Test] public void MockScenarioRuntime_AdvancesAndResets()
        {
            var rt = new MockScenarioRuntime("X", new[] { "a", "b" });
            Assert.AreEqual("a", rt.CurrentObjectiveId);
            var ev = rt.CompleteCurrent().ToList();
            Assert.AreEqual(SurgeryEventIds.ObjectiveCompleted, ev[0].id); Assert.AreEqual("b", ev[1].objectiveId);
            rt.CompleteCurrent().ToList(); Assert.IsTrue(rt.IsFinished);
            rt.ResetForNewSession(); Assert.AreEqual("a", rt.CurrentObjectiveId); Assert.AreEqual(0, rt.CompletedObjectives.Count);
        }

        [Test] public void MockAnatomy_RejectsUnsupportedState()
        {
            var v = new MockAnatomyStateView(AnatomyTargetIds.IncisionLine, AnatomyStates.Intact, AnatomyStates.Incised);
            Assert.IsFalse(v.SetState(AnatomyStates.Implanted)); Assert.AreEqual(AnatomyStates.Intact, v.CurrentState);
            Assert.IsTrue(v.SetState(AnatomyStates.Incised));
        }
    }
}
