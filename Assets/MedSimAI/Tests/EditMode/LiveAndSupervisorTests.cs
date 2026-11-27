using System.Linq;
using MedSimAI.AI;
using MedSimAI.Clinical;
using NUnit.Framework;
using UnityEngine;

namespace MedSimAI.Tests
{
    /// <summary>v0.2 acceptance tests: supervisor observation/intervention, dynamic patient states, reasoning & escalation
    /// separation, and the removal of pre-recorded dialogue. Pure C#, deterministic, no network.</summary>
    public class LiveAndSupervisorTests
    {
        ClinicalSession session; SupervisorDirector director; float clock; bool canSpeak;

        [SetUp] public void SetUp()
        {
            session = new ClinicalSession(ScenarioData.LoadFromResources("ER_CHEST_PAIN_001")) { Clock = () => clock };
            director = new SupervisorDirector(session) { CanSpeak = () => canSpeak };
            clock = 0; canSpeak = true;
        }

        InterventionRequest Advance(float seconds, float step = 0.5f)
        {
            InterventionRequest last = null;
            for (float t = 0; t < seconds; t += step) { clock += step; session.Tick(); var r = director.Tick(); if (r != null) last = r; }
            return last;
        }

        [Test] public void Scenario_v020_IsValid_AndStillDraft()
        {
            Assert.IsEmpty(ScenarioValidator.Validate(session.Scenario));
            Assert.AreEqual("0.2.0", session.Scenario.version);
            Assert.IsFalse(session.Scenario.IsClinicallyValidated, "must stay DRAFT — NOT CLINICALLY VALIDATED");
            Assert.AreEqual("S_PRESENTATION", session.Patient.ConditionId);
        }

        [Test] public void Supervisor_StaysSilent_WhenStudentActsCorrectly()
        {
            session.ProcessStudentQuestion("Hello, I'm a medical student, my name is Lina.");
            session.ProcessStudentQuestion("Is it okay if I examine you?");
            clock = 30; session.Record(ClinicalEvents.InstrumentOnPatient, "bp_cuff@ARM_UPPER_L"); session.PerformExam("bp_cuff", "ARM_UPPER_L");
            clock = 60; session.RequestInvestigation("INV_ECG"); session.Record(ClinicalEvents.EcgReviewed);
            clock = 80; session.ProcessStudentQuestion("Do you have any allergies?");
            clock = 90; session.Escalate("call cardiology");
            Assert.IsNull(Advance(60), "correct, timely actions → no intervention");
            Assert.AreEqual(0, director.History.Count);
        }

        [Test] public void OrdinaryMistake_GetsGracePeriod_AndIsCancelledBySelfCorrection()
        {
            session.Record(ClinicalEvents.InstrumentOnPatient, "stethoscope@CHEST_MITRAL"); // examining before consent
            Assert.IsNull(Advance(3), "within grace: silent");
            session.ProcessStudentQuestion("Sorry — may I examine you?"); // self-correction
            Assert.IsNull(Advance(10));
            Assert.IsFalse(director.History.Any(h => h.rule.id == "I_CONSENT_BEFORE_EXAM"));
        }

        [Test] public void OrdinaryMistake_WithoutCorrection_TriggersContextualHint()
        {
            session.Record(ClinicalEvents.InstrumentOnPatient, "stethoscope@CHEST_MITRAL");
            var r = Advance(8);
            Assert.NotNull(r); Assert.AreEqual("I_CONSENT_BEFORE_EXAM", r.rule.id); StringAssert.Contains("ask Mr. Reyes", r.line);
            Assert.IsTrue(session.Tracker.Has("INTERVENTION:I_CONSENT_BEFORE_EXAM"));
        }

        [Test] public void WrongPlacement_TwiceInWindow_GivesInstrumentSpecificGuide()
        {
            session.ProcessStudentQuestion("May I examine you?");
            session.Record(ClinicalEvents.ZoneRejected, "bp_cuff");
            Assert.IsNull(Advance(1), "one slip is ignored");
            session.Record(ClinicalEvents.ZoneRejected, "bp_cuff");
            var r1 = Advance(1); Assert.NotNull(r1); Assert.AreEqual(0, r1.level);
            clock += 50; session.Record(ClinicalEvents.ZoneRejected, "bp_cuff"); session.Record(ClinicalEvents.ZoneRejected, "bp_cuff");
            var r2 = Advance(1); Assert.NotNull(r2); Assert.AreEqual(1, r2.level, "hint ladder escalates");
            StringAssert.Contains("upper arm", r2.line);
        }

        [Test] public void SafetyCritical_EcgDelay_Intervenes_AndBypassesGlobalCooldown()
        {
            session.ProcessStudentQuestion("May I examine you?");
            session.Record(ClinicalEvents.InstrumentOnPatient, "bp_cuff@x");
            session.Record(ClinicalEvents.ZoneRejected, "bp_cuff"); session.Record(ClinicalEvents.ZoneRejected, "bp_cuff");
            Assert.NotNull(Advance(1)); // ordinary intervention fired at t≈1 → global cooldown running
            clock = 239.5f; var r = Advance(1);
            Assert.NotNull(r); Assert.AreEqual("I_ECG_DELAY", r.rule.id); Assert.IsTrue(r.safety);
        }

        [Test] public void NeverTalksOverAnyone_WaitsUntilQuiet()
        {
            canSpeak = false; clock = 239; var r = Advance(5);
            Assert.IsNull(r, "someone is speaking");
            canSpeak = true; r = Advance(1);
            Assert.NotNull(r); Assert.AreEqual("I_ECG_DELAY", r.rule.id);
        }

        [Test] public void RepeatLimits_AndCooldowns_PreventNagging()
        {
            clock = 239; Advance(2); // first ECG reminder
            Advance(100); // within repeatAfterSeconds → no repeat
            Assert.AreEqual(1, director.History.Count(h => h.rule.id == "I_ECG_DELAY"));
            Advance(120); // second (ladder level 1)
            Advance(600);
            Assert.AreEqual(2, director.History.Count(h => h.rule.id == "I_ECG_DELAY"), "maxRepeats respected");
        }

        [Test] public void AssessmentMode_OnlySafetyCriticalInterventions()
        {
            director.AllowOrdinary = false;
            session.Record(ClinicalEvents.InstrumentOnPatient, "stethoscope@CHEST_MITRAL");
            Assert.IsNull(Advance(10));
            clock = 239; Assert.AreEqual("I_ECG_DELAY", Advance(2)?.rule.id);
        }

        [Test] public void DynamicState_PathA_TimelyCare_GoesStraightToHandover_WithoutDeterioration()
        {
            clock = 60; session.RequestInvestigation("INV_ECG");
            clock = 120; session.Escalate("call cardiology");
            Advance(700);
            Assert.AreEqual("S_HANDOVER", session.Patient.ConditionId);
            CollectionAssert.AreEqual(new[] { "S_PRESENTATION", "S_HANDOVER" }, session.Patient.ConditionPath.ToArray());
            Assert.AreEqual(98, session.Patient.Vitals.heartRate, "correct actions do not magically improve the patient");
        }

        [Test] public void DynamicState_PathB_DelayedCare_Deteriorates_AndSupervisorIntervenes()
        {
            Advance(310);
            Assert.AreEqual("S_ONGOING", session.Patient.ConditionId); Assert.AreEqual(104, session.Patient.Vitals.heartRate);
            Advance(240);
            Assert.AreEqual("S_DETERIORATION", session.Patient.ConditionId); Assert.AreEqual(94, session.Patient.Vitals.spo2);
            Assert.IsTrue(director.History.Any(h => h.rule.id == "I_DETERIORATION" && h.safety));
            session.Escalate("call the senior");
            Assert.AreEqual("S_HANDOVER", session.Patient.ConditionId);
            Assert.AreEqual(112, session.Patient.Vitals.heartRate, "handover inherits current vitals (no authored improvement)");
            Assert.IsTrue(session.Tracker.Has("STATE:S_DETERIORATION"));
        }

        [Test] public void NamingStemi_IsNotEscalation_AndQuestionIsSeparated()
        {
            session.ProcessStudentQuestion("When did it start?");
            session.ProcessInstructorStatement("Is this a STEMI?");
            Assert.IsFalse(session.Tracker.Has(ClinicalEvents.EscalationRequested));
            Assert.IsTrue(session.Tracker.Has(ClinicalEvents.DiagnosticQuestion));
            session.ProcessInstructorStatement("I think he's having a heart attack.");
            Assert.IsFalse(session.Tracker.Has(ClinicalEvents.EscalationRequested), "naming a diagnosis is not escalating");
            Assert.IsTrue(session.Tracker.Has(ClinicalEvents.DiagnosisNamed));
            session.ProcessInstructorStatement("We don't need to call cardiology yet.");
            Assert.IsFalse(session.Tracker.Has(ClinicalEvents.EscalationRequested), "negated");
            session.ProcessInstructorStatement("I'd like to call the cardiology registrar now.");
            Assert.IsTrue(session.Tracker.Has(ClinicalEvents.EscalationRequested));
        }

        [Test] public void Reasoning_NoCreditForLongIrrelevantSentences()
        {
            session.Stages.TryTransition(ClinicalStage.HistoryTaking);
            session.SubmitReasoning("Well I have been thinking about this case for a while now and I am not sure what to say really.");
            Assert.IsFalse(session.Tracker.Has(ClinicalEvents.SummaryGiven), "length alone earns nothing");
            session.SubmitReasoning("Crushing pain to the left arm, and the ECG shows ST elevation in II, III and aVF.");
            Assert.IsTrue(session.Tracker.Has("CONCEPT:C_ECG_FINDING")); Assert.IsTrue(session.Tracker.Has("CONCEPT:C_KEY_HISTORY"));
            Assert.IsTrue(session.Tracker.Has(ClinicalEvents.SummaryGiven));
        }

        [Test] public void Reasoning_AIOnlyConcepts_AreTagged_AndDoNotCountForSummary()
        {
            session.Stages.TryTransition(ClinicalStage.HistoryTaking); session.Stages.TryTransition(ClinicalStage.ClinicalReasoning);
            session.RecordReasoning("vague words", new[] { "C_WORKING_DX", "C_NEXT_STEP" }, true);
            Assert.IsTrue(session.Tracker.Log.First(e => e.eventId == "CONCEPT:C_WORKING_DX").detail.StartsWith(ClinicalSession.AiTag));
            Assert.IsFalse(session.Tracker.Has(ClinicalEvents.SummaryGiven), "AI-only interpretations need instructor review");
            var r = MedSimAI.Assessment.AssessmentEngine.Evaluate(session.Scenario, session.Tracker.Log, 0, 100);
            Assert.IsTrue(r.criteria.First(c => c.id == "R_REASON_DX").viaAIIntent);
        }

        [Test] public void NoPrerecordedCharacterDialogue_InProject()
        {
            Assert.AreEqual(0, Resources.LoadAll<AudioClip>("Audio/Voice").Length, "pre-recorded dialogue removed");
            Assert.IsNull(typeof(LiveAIClient).Assembly.GetType("MedSimAI.AI.PrerenderedVoiceLibrary"), "pre-rendered playback path removed");
        }

        [Test] public void LiveContext_SendsOnlyIds_NeverClinicalText()
        {
            session.ProcessStudentQuestion("When did it start?"); session.PerformExam("bp_cuff", "ARM_UPPER_L");
            var ctx = LiveAIClient.ContextOf(session);
            var json = JsonUtility.ToJson(ctx);
            StringAssert.DoesNotContain("forty-five", json); StringAssert.DoesNotContain("152", json);
            CollectionAssert.Contains(ctx.events, "FACT:H_ONSET"); Assert.AreEqual("S_PRESENTATION", ctx.stateId);
        }
    }
}
