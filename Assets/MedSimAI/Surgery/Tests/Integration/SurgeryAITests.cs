using System.Collections.Generic;
using System.Linq;
using MedSimAI.Surgery.AI;
using MedSimAI.Surgery.Shared;
using MedSimAI.Surgery.Shared.Mocks;
using NUnit.Framework;

namespace MedSimAI.Surgery.Tests
{
    /// <summary>Builds verified (mechanics-sourced) event sequences from the authored scenario.</summary>
    public static class Runs
    {
        public static IEnumerable<SurgeryEvent> Objective(SurgicalObjective o, string src = SurgeryEventSources.Mechanics, float posErr = 1, float angErr = 1)
        {
            yield return SurgeryEvent.Objective(SurgeryEventIds.ObjectiveStarted, o.id, src);
            foreach (var t in o.requiredTools.Take(1)) yield return SurgeryEvent.Tool(SurgeryEventIds.ToolGrabbed, t, o.id, src);
            var target = o.targets.FirstOrDefault() ?? "";
            yield return SurgeryEvent.Anatomy(target, o.requiredTools.FirstOrDefault() ?? "", o.id, src);
            if (!string.IsNullOrEmpty(o.implantId))
            {
                yield return SurgeryEvent.Implant(SurgeryEventIds.ImplantPlacementAttempted, o.implantId, target, posErr, angErr, true, o.id, src);
                yield return SurgeryEvent.Implant(SurgeryEventIds.ImplantPlacementCompleted, o.implantId, target, posErr, angErr, true, o.id, src);
            }
            if (!string.IsNullOrEmpty(o.completionState)) yield return SurgeryEvent.StateChanged(target, o.completionState, o.id, src);
            yield return SurgeryEvent.Objective(SurgeryEventIds.ObjectiveCompleted, o.id, src);
        }
    }

    public class SurgicalSupervisorTests
    {
        SurgicalScenarioData s; ManualClock clock; SurgeryEventBus bus; SurgicalSupervisor sup; List<SupervisorUtterance> said;

        [SetUp] public void SetUp()
        {
            s = TestScenarioLoader.LoadTka(); clock = new ManualClock(); bus = new SurgeryEventBus { Clock = () => clock.Now };
            sup = new SurgicalSupervisor(s, bus, () => clock.Now); said = new List<SupervisorUtterance>();
            sup.Speak += said.Add; bus.EventPublished += sup.OnEvent; bus.BeginSession("T");
            bus.Publish(new SurgeryEvent(SurgeryEventIds.SessionStarted, SurgeryEventSources.Integration));
            Step(0.1); said.Clear(); clock.Advance(30);   // welcome out of the way, global cooldown elapsed
        }
        void Pub(SurgeryEvent e) => bus.Publish(e);
        void Step(double sec) { for (double t = 0; t < sec; t += 0.25) { clock.Advance(0.25); sup.Tick(); } }

        [Test] public void Welcome_IsSpokenOnSessionStart()
        {
            var c2 = new ManualClock(); var b2 = new SurgeryEventBus(); var s2 = new SurgicalSupervisor(s, b2, () => c2.Now); var got = new List<SupervisorUtterance>();
            s2.Speak += got.Add; b2.EventPublished += s2.OnEvent; b2.BeginSession("x");
            b2.Publish(new SurgeryEvent(SurgeryEventIds.SessionStarted, SurgeryEventSources.Integration)); s2.Tick();
            Assert.AreEqual("rule.welcome", got.Single().ruleId);
        }

        [Test] public void SilentDuringCorrectWork()
        {
            foreach (var o in s.Ordered.Take(4)) { foreach (var e in Runs.Objective(o)) Pub(e); Step(5); }
            Assert.AreEqual(0, said.Count, string.Join(" | ", said));
            Assert.AreEqual(0, sup.HintsGiven); Assert.AreEqual(0, sup.Interventions);
        }

        [Test] public void WrongTool_HintAfterGrace()
        {
            Pub(SurgeryEvent.Objective(SurgeryEventIds.ObjectiveStarted, "obj.site_confirmation"));
            Pub(SurgeryEvent.Rejected(RejectReasons.WrongTool, InstrumentIds.Scalpel, AnatomyTargetIds.SiteMarking, "obj.site_confirmation"));
            Step(2); Assert.AreEqual(0, said.Count, "within grace the student may self-correct");
            Step(3);
            var u = said.Single(); Assert.AreEqual("rule.wrong_tool", u.ruleId); Assert.IsTrue(u.countsAsHint);
            StringAssert.Contains(s.Objective("obj.site_confirmation").hints[0].en, u.Text);
            Assert.AreEqual(1, bus.Log.Count(e => e.id == SurgeryEventIds.HintGiven));
        }

        [Test] public void WrongTool_SelfCorrectedWithinGrace_StaysSilent()
        {
            Pub(SurgeryEvent.Objective(SurgeryEventIds.ObjectiveStarted, "obj.incision"));
            Pub(SurgeryEvent.Rejected(RejectReasons.WrongTool, InstrumentIds.OscillatingSaw, AnatomyTargetIds.IncisionLine, "obj.incision"));
            Step(1.5);
            Pub(SurgeryEvent.Anatomy(AnatomyTargetIds.IncisionLine, InstrumentIds.Scalpel, "obj.incision"));
            Step(10);
            Assert.AreEqual(0, said.Count); Assert.AreEqual(1, sup.SelfCorrections);
        }

        [Test] public void WrongSide_IsImmediateSafetyIntervention()
        {
            Pub(SurgeryEvent.Objective(SurgeryEventIds.ObjectiveStarted, "obj.site_confirmation"));
            Pub(SurgeryEvent.Rejected(RejectReasons.WrongSide, InstrumentIds.SkinMarker, "knee_l.site_marking", "obj.site_confirmation"));
            Step(0.25);
            var u = said.Single(); Assert.AreEqual("safety", u.kind); Assert.IsTrue(u.isIntervention);
            StringAssert.Contains("RIGHT", u.Text);
            Assert.AreEqual(1, bus.Log.Count(e => e.id == SurgeryEventIds.SupervisorIntervened && e.source == SurgeryEventSources.AI));
        }

        [Test] public void SafetyLine_NotCancelledBySelfCorrection()
        {
            Pub(SurgeryEvent.Objective(SurgeryEventIds.ObjectiveStarted, "obj.site_confirmation"));
            Pub(SurgeryEvent.Rejected(RejectReasons.WrongSide, InstrumentIds.SkinMarker, "", "obj.site_confirmation"));
            Pub(SurgeryEvent.Anatomy(AnatomyTargetIds.SiteMarking, InstrumentIds.SkinMarker, "obj.site_confirmation"));
            Step(0.5);
            Assert.AreEqual("rule.wrong_side", said.Single().ruleId);
        }

        [Test] public void RepeatedRejections_Intervene()
        {
            Pub(SurgeryEvent.Objective(SurgeryEventIds.ObjectiveStarted, "obj.exposure"));
            for (int i = 0; i < 3; i++) Pub(SurgeryEvent.Rejected(RejectReasons.WrongTarget, InstrumentIds.Retractor, AnatomyTargetIds.Patella, "obj.exposure"));
            Step(0.5);
            Assert.AreEqual("rule.repeated", said.First().ruleId, "higher priority intervention wins the tick");
        }

        [Test] public void Idle_GivesLadderHints_LeastSpecificFirst()
        {
            Pub(SurgeryEvent.Objective(SurgeryEventIds.ObjectiveStarted, "obj.femoral_resection"));
            Step(46);
            var o = s.Objective("obj.femoral_resection");
            StringAssert.Contains(o.hints[0].en, said.Single().Text);
            Pub(SurgeryEvent.Tool(SurgeryEventIds.ToolGrabbed, InstrumentIds.Drill, o.id));   // activity resets idle
            Step(46);
            Assert.AreEqual(2, said.Count); StringAssert.Contains(o.hints[1].en, said[1].Text);
        }

        [Test] public void HelpRequest_AnsweredImmediately_EvenRightAfterAnotherLine()
        {
            Pub(SurgeryEvent.Objective(SurgeryEventIds.ObjectiveStarted, "obj.incision"));
            Pub(SurgeryEvent.Rejected(RejectReasons.WrongSide, InstrumentIds.Scalpel, "", "obj.incision")); Step(0.25);
            Pub(new SurgeryEvent(SurgeryEventIds.HelpRequested, SurgeryEventSources.AI)); Step(0.25);
            Assert.AreEqual("rule.help", said.Last().ruleId);
            StringAssert.Contains(s.Objective("obj.incision").hints[0].en, said.Last().Text);
        }

        [Test] public void Arabic_UsesArabicApprovedText()
        {
            sup.Language = "ar";
            Pub(SurgeryEvent.Objective(SurgeryEventIds.ObjectiveStarted, "obj.site_confirmation"));
            Pub(SurgeryEvent.Rejected(RejectReasons.WrongSide, "", "", "obj.site_confirmation")); Step(0.25);
            StringAssert.Contains("الركبة اليمنى", said.Single().Text);
        }

        [Test] public void MaxTimes_Respected()
        {
            Pub(SurgeryEvent.Objective(SurgeryEventIds.ObjectiveStarted, "obj.site_confirmation"));
            for (int i = 0; i < 6; i++) { Pub(SurgeryEvent.Rejected(RejectReasons.WrongSide, "", "", "obj.site_confirmation")); Step(6); }
            Assert.AreEqual(s.supervisorRules.First(r => r.id == "rule.wrong_side").maxTimes, said.Count(u => u.ruleId == "rule.wrong_side"));
        }

        [Test] public void IgnoresGameplayEventsNotFromMechanics()
        {
            Pub(SurgeryEvent.Objective(SurgeryEventIds.ObjectiveStarted, "obj.incision"));
            Pub(SurgeryEvent.Rejected(RejectReasons.WrongSide, "", "", "obj.incision", SurgeryEventSources.AI));
            Step(2); Assert.AreEqual(0, said.Count);
        }
    }

    public class SurgicalAssessmentTests
    {
        SurgicalScenarioData s; ManualClock clock; SurgeryEventBus bus;
        [SetUp] public void SetUp() { s = TestScenarioLoader.LoadTka(); clock = new ManualClock(); bus = new SurgeryEventBus { Clock = () => clock.Now }; bus.BeginSession("A"); bus.Publish(new SurgeryEvent(SurgeryEventIds.SessionStarted, SurgeryEventSources.Integration)); }
        void Pub(SurgeryEvent e) { clock.Advance(5); bus.Publish(e); }
        void Perfect(float posErr = 1) { foreach (var o in s.Ordered) foreach (var e in Runs.Objective(o, posErr: posErr, angErr: posErr)) Pub(e); Pub(new SurgeryEvent(SurgeryEventIds.SessionCompleted, SurgeryEventSources.Integration)); }

        [Test] public void PerfectRun_AllCriteriaMet()
        {
            Perfect();
            var r = SurgicalAssessmentEngine.Evaluate(s, bus.Log);
            Assert.IsTrue(r.sessionCompleted);
            CollectionAssert.IsEmpty(r.criteria.Where(c => !c.Met).Select(c => c.id + ":" + c.note));
            Assert.AreEqual(r.maxScore, r.score); Assert.AreEqual(0, r.invalidInteractions); Assert.AreEqual(s.objectives.Length, r.Completed.Count());
            Assert.AreEqual(SurgeryContract.VALIDATION_BANNER, r.validationBanner);
        }

        [Test] public void VerbalOrAIClaims_EarnNothing()
        {
            foreach (var o in s.objectives) Pub(SurgeryEvent.Objective(SurgeryEventIds.ObjectiveCompleted, o.id, SurgeryEventSources.AI));
            Pub(new SurgeryEvent(SurgeryEventIds.SessionCompleted, SurgeryEventSources.Integration));
            var r = SurgicalAssessmentEngine.Evaluate(s, bus.Log);
            Assert.AreEqual(0, r.score); Assert.AreEqual(0, r.Completed.Count()); Assert.IsFalse(r.sessionCompleted);
            Assert.AreEqual(s.objectives.Length, r.ignoredUnverifiedEvents);
        }

        [Test] public void MockEvents_NotCreditedUnlessExplicitlyAllowed()
        {
            foreach (var e in MockEventScripts.PerfectIncision("obj.incision")) Pub(e);
            Assert.AreEqual(0, SurgicalAssessmentEngine.Evaluate(s, bus.Log).Completed.Count());
            Assert.AreEqual(1, SurgicalAssessmentEngine.Evaluate(s, bus.Log, acceptMockAsMechanics: true).Completed.Count());
        }

        [Test] public void WrongSide_IsSafetyObservation_AndSiteNotClean()
        {
            Pub(SurgeryEvent.Objective(SurgeryEventIds.ObjectiveStarted, "obj.site_confirmation"));
            Pub(SurgeryEvent.Rejected(RejectReasons.WrongSide, InstrumentIds.SkinMarker, "", "obj.site_confirmation"));
            Perfect();
            var r = SurgicalAssessmentEngine.Evaluate(s, bus.Log);
            Assert.IsFalse(r.criteria.First(c => c.id == "crit.site_clean").Met);
            Assert.IsTrue(r.criteria.First(c => c.id == "crit.site_confirmed").Met);
            Assert.IsTrue(r.safetyObservationsEn.Any(x => x.Contains("wrong side")));
            Assert.IsTrue(r.improvementsEn.Any(x => x.Contains("operative side")));
        }

        [Test] public void Placement_OutOfTolerance_NotMet_NotMeasured_NotAssessed()
        {
            Perfect(posErr: 9);
            var r = SurgicalAssessmentEngine.Evaluate(s, bus.Log);
            Assert.IsFalse(r.criteria.First(c => c.id == "crit.femoral_accuracy").Met);

            SetUp(); Perfect(posErr: -1);
            var r2 = SurgicalAssessmentEngine.Evaluate(s, bus.Log);
            var c2 = r2.criteria.First(c => c.id == "crit.femoral_accuracy");
            Assert.AreEqual(CriterionStatus.NotAssessed, c2.status); Assert.AreEqual(r2.score, r2.maxScore, "not-assessed criteria are excluded from the maximum");
        }

        [Test] public void OutOfOrderAttempt_FailsPrerequisiteCriterion()
        {
            Pub(SurgeryEvent.Rejected(RejectReasons.PrerequisiteMissing, InstrumentIds.Scalpel, AnatomyTargetIds.IncisionLine, "obj.incision"));
            Perfect();
            Assert.IsFalse(SurgicalAssessmentEngine.Evaluate(s, bus.Log).criteria.First(c => c.id == "crit.order").Met);
        }

        [Test] public void IncompleteSession_ProcessCriteriaNotMet()
        {
            foreach (var e in Runs.Objective(s.Ordered.First())) Pub(e);
            var r = SurgicalAssessmentEngine.Evaluate(s, bus.Log);
            Assert.IsFalse(r.sessionCompleted);
            Assert.IsFalse(r.criteria.First(c => c.id == "crit.hints").Met, "zero hints in an unfinished session is not an achievement");
            Assert.AreEqual(1, r.Completed.Count()); Assert.AreEqual(s.objectives.Length - 1, r.Missed.Count());
        }

        [Test] public void HintsAndInterventions_CountedFromSupervisorEvents()
        {
            for (int i = 0; i < 5; i++) Pub(new SurgeryEvent(SurgeryEventIds.HintGiven, SurgeryEventSources.AI));
            Pub(new SurgeryEvent(SurgeryEventIds.SupervisorIntervened, SurgeryEventSources.AI));
            Perfect();
            var r = SurgicalAssessmentEngine.Evaluate(s, bus.Log);
            Assert.AreEqual(5, r.hintsGiven); Assert.AreEqual(1, r.interventions);
            Assert.IsFalse(r.criteria.First(c => c.id == "crit.hints").Met);
            Assert.IsTrue(r.criteria.First(c => c.id == "crit.interventions").Met);
        }

        [Test] public void Debrief_Bilingual_Grounded_WithDisclaimer()
        {
            foreach (var o in s.Ordered.Take(2)) foreach (var e in Runs.Objective(o)) Pub(e);
            var r = SurgicalAssessmentEngine.Evaluate(s, bus.Log);
            var en = SurgicalDebriefComposer.Written(r, "en"); var ar = SurgicalDebriefComposer.Written(r, "ar");
            StringAssert.Contains(SurgeryContract.VALIDATION_BANNER, en); StringAssert.Contains(SurgeryContract.VALIDATION_BANNER_AR, ar);
            StringAssert.Contains("✓ " + s.Objective("obj.incision").title.en, en);
            StringAssert.Contains("✗ " + s.Objective("obj.closure").title.en, en);
            StringAssert.Contains("✓ " + s.Objective("obj.incision").title.ar, ar);
            StringAssert.Contains("not a surgical competency certification", en);
            StringAssert.Contains("2 of 9", SurgicalDebriefComposer.SpokenBase(r, "en"));
            var f = SurgicalDebriefComposer.Facts(r); Assert.AreEqual(2, f.completed.Length);
        }
    }

    public class SurgeryActionTests
    {
        SurgicalScenarioData s; MockInstrumentRegistry reg;
        [SetUp] public void SetUp() { s = TestScenarioLoader.LoadTka(); reg = new MockInstrumentRegistry(s.instruments.Select(i => i.id)); }
        ValidatedAction V(string utter, string obj = "obj.incision") => SurgeryActionValidator.Validate(SurgeryIntentParser.Parse(utter, s).action, new ActionContext { Scenario = s, CurrentObjectiveId = obj, Instruments = reg });

        [TestCase("دكتور، ممكن تساعدني؟", true, false, "none")]
        [TestCase("ممكن توضحلي المطلوب؟", false, true, "explain_objective")]
        [TestCase("دكتور، تعال هون شوي.", false, false, "come_to_student")]
        [TestCase("استنى شوي.", false, false, "wait")]
        [TestCase("Please move beside me.", false, false, "stand_beside_student")]
        [TestCase("Can you help me?", true, false, "none")]
        public void Parser_UnderstandsSupportedRequests(string utter, bool help, bool explain, string action)
        {
            var it = SurgeryIntentParser.Parse(utter, s);
            Assert.AreEqual(help, it.helpRequest, it.ToString()); Assert.AreEqual(explain, it.explainRequest, it.ToString()); Assert.AreEqual(action, it.action.action, it.ToString());
        }

        [Test] public void Parser_LanguageCommands()
        {
            Assert.AreEqual("ar", SurgeryIntentParser.Parse("احكي عربي", s).languageCommand);
            Assert.AreEqual("en", SurgeryIntentParser.Parse("speak English please", s).languageCommand);
        }

        [Test] public void PassInstrument_English_Accepted()
        {
            var v = V("Can you pass me the scalpel?");
            Assert.AreEqual(ActionStatus.Accepted, v.status, v.ToString()); Assert.AreEqual(InstrumentIds.Scalpel, v.instrumentId); Assert.AreEqual(SurgeryCharacterIds.Assistant, v.characterId);
        }

        [Test] public void PassInstrument_ArabicToDoctor_RoutedToAssistant()
        {
            var v = V("دكتور، ممكن تعطيني المشرط؟");
            Assert.AreEqual(ActionStatus.Accepted, v.status, v.ToString()); Assert.AreEqual(SurgeryCharacterIds.Assistant, v.characterId); Assert.AreEqual(InstrumentIds.Scalpel, v.instrumentId);
        }

        [Test] public void GiveMeTheInstrument_Unspecified_AsksWhich()
        {
            var v = V("ممكن تعطيني الأداة؟");
            Assert.AreEqual(ActionStatus.Clarify, v.status, v.ToString()); Assert.AreEqual("which_instrument", v.reason);
        }

        [Test] public void WhereIsTheToolINeed_RedirectsToSupervisorHint()
        {
            var v = V("وين الأداة اللي بحتاجها؟");
            Assert.AreEqual(ActionStatus.RedirectToSupervisor, v.status, v.ToString());
        }

        [Test] public void AmbiguousGuide_AsksForClarification()
        {
            var v = V("pass me the guide");
            Assert.AreEqual(ActionStatus.Clarify, v.status, v.ToString()); Assert.AreEqual("ambiguous_instrument", v.reason);
            StringAssert.Contains(InstrumentIds.FemoralCuttingGuide, v.instrumentId); StringAssert.Contains(InstrumentIds.TibialCuttingGuide, v.instrumentId);
        }

        [Test] public void SpecificGuide_Resolves() => Assert.AreEqual(InstrumentIds.TibialCuttingGuide, V("pass me the tibial cutting guide").instrumentId);

        [Test] public void AlreadyHolding_Denied() { reg.Set(InstrumentIds.Scalpel, InstrumentHolder.Player); Assert.AreEqual("already_holding", V("pass me the scalpel").reason); }
        [Test] public void Missing_Denied() { reg.Set(InstrumentIds.Scalpel, InstrumentHolder.Missing); Assert.AreEqual(ActionStatus.Denied, V("pass me the scalpel").status); }
        [Test] public void NotInCatalogue_Clarify()
        {
            var v = SurgeryActionValidator.Validate(new ProposedAction { characterId = SurgeryCharacterIds.Assistant, action = "operate_on_patient" }, new ActionContext { Scenario = s });
            Assert.AreEqual(ActionStatus.Clarify, v.status);
        }
        [Test] public void HandoverOnlyForCurrentObjective_WhenPolicySaysSo()
        {
            s.assistantPolicy.handoverOnlyForCurrentObjective = true;
            Assert.AreEqual("not_needed_now", V("pass me the saw", "obj.incision").reason);
            Assert.AreEqual(ActionStatus.Accepted, V("pass me the scalpel", "obj.incision").status);
        }
        [Test] public void UnknownCharacter_Denied() => Assert.AreEqual(ActionStatus.Denied, SurgeryActionValidator.Validate(new ProposedAction { characterId = "patient", action = "wait" }, new ActionContext { Scenario = s }).status);
    }

    public class SurgerySessionCoreTests
    {
        SurgicalScenarioData s; ManualClock clock; SurgerySessionCore core; MockCharacterActuator act; MockInstrumentRegistry reg; List<CharacterLine> lines;
        [SetUp] public void SetUp()
        {
            s = TestScenarioLoader.LoadTka(); clock = new ManualClock(); act = new MockCharacterActuator(); reg = new MockInstrumentRegistry(s.instruments.Select(i => i.id));
            core = new SurgerySessionCore(s, () => clock.Now) { Actuator = act, Instruments = reg, Runtime = new MockScenarioRuntime(s.scenarioId, s.Ordered.Select(o => o.id)) };
            lines = new List<CharacterLine>(); core.Line += lines.Add;
        }
        void Tick(double sec) { for (double t = 0; t < sec; t += 0.25) { clock.Advance(0.25); core.Tick(); } }

        [Test] public void FullRun_CompletesAndAssesses()
        {
            core.Start("en"); Tick(1);
            Assert.AreEqual("rule.welcome", lines.First().ruleId);
            foreach (var o in s.Ordered) { foreach (var e in Runs.Objective(o)) core.Bus.Publish(e); Tick(3); }
            Assert.IsTrue(core.Completed); Assert.IsNotNull(core.Result); Assert.IsTrue(core.Result.sessionCompleted);
            Assert.AreEqual(core.Result.maxScore, core.Result.score);
            Assert.AreEqual(1, core.Bus.Log.Count(e => e.id == SurgeryEventIds.SessionCompleted));
        }

        [Test] public void HandOver_CompletesOnlyWhenPlayerActuallyGrabs()
        {
            core.Start("en"); Tick(1);
            var v = core.HandleRequest(SurgeryIntentParser.Parse("Sami, pass me the scalpel", s));
            Assert.AreEqual(ActionStatus.Accepted, v.status);
            Assert.AreEqual(1, act.Executed.Count);
            Assert.AreEqual(1, core.Bus.Log.Count(e => e.id == SurgeryEventIds.AssistantHandoffOffered));
            Assert.AreEqual(0, core.Bus.Log.Count(e => e.id == SurgeryEventIds.AssistantHandoffCompleted), "not claimed before the grab");
            core.Bus.Publish(SurgeryEvent.Tool(SurgeryEventIds.ToolGrabbed, InstrumentIds.Scalpel, "", SurgeryEventSources.AI));
            Assert.AreEqual(0, core.Bus.Log.Count(e => e.id == SurgeryEventIds.AssistantHandoffCompleted), "AI-sourced grab is not evidence");
            core.Bus.Publish(SurgeryEvent.Tool(SurgeryEventIds.ToolGrabbed, InstrumentIds.Scalpel));
            Assert.AreEqual(1, core.Bus.Log.Count(e => e.id == SurgeryEventIds.AssistantHandoffCompleted));
        }

        [Test] public void HandOver_NotTaken_FailsAfterTimeout()
        {
            core.Start("en"); Tick(1);
            core.HandleRequest(SurgeryIntentParser.Parse("pass me the retractor", s));
            Tick(25);
            Assert.AreEqual(1, core.Bus.Log.Count(e => e.id == SurgeryEventIds.AssistantActionFailed && e.reason == "not_taken"));
            Assert.AreEqual(0, core.Bus.Log.Count(e => e.id == SurgeryEventIds.AssistantHandoffCompleted));
        }

        [Test] public void ActuatorFailure_ReportedHonestly()
        {
            act.Outcome = _ => ActionOutcome.Unavailable;
            core.Start("en"); Tick(1);
            core.HandleRequest(SurgeryIntentParser.Parse("Sami come here", s));
            Assert.AreEqual(1, core.Bus.Log.Count(e => e.id == SurgeryEventIds.AssistantActionFailed));
            Assert.IsTrue(lines.Any(l => l.text == SurgeryActionLines.Get("action_unavailable", "en")));
        }

        [Test] public void Restart_GivesFreshIndependentSession()
        {
            var first = core.Start("en"); Tick(1);
            core.Bus.Publish(SurgeryEvent.Rejected(RejectReasons.WrongSide)); Tick(1);
            var second = core.Restart(); Tick(1);
            Assert.AreNotEqual(first, second);
            Assert.AreEqual(0, core.Bus.Log.Count(e => e.id == SurgeryEventIds.InteractionRejected));
            Assert.AreEqual(0, core.Supervisor.Interventions);
            Assert.AreEqual(SurgeryEventIds.SessionStarted, core.Bus.Log[0].id);
        }

        [Test] public void HelpAndExplain_FromVoiceRequests()
        {
            core.Start("ar"); Tick(13);
            core.Bus.Publish(SurgeryEvent.Objective(SurgeryEventIds.ObjectiveStarted, "obj.site_confirmation"));
            core.HandleRequest(SurgeryIntentParser.Parse("ممكن توضحلي المطلوب؟", s));
            Assert.IsTrue(lines.Last().text.Contains(s.Objective("obj.site_confirmation").explanation.ar));
            core.HandleRequest(SurgeryIntentParser.Parse("دكتور، ممكن تساعدني؟", s)); Tick(0.5);
            Assert.AreEqual("rule.help", lines.Last().ruleId);
        }

        [Test] public void LanguageCommand_SwitchesSupervisorLanguage()
        {
            core.Start("en"); core.HandleRequest(SurgeryIntentParser.Parse("احكي عربي", s));
            Assert.AreEqual("ar", core.Language); Assert.AreEqual("ar", core.Supervisor.Language);
        }

        [Test] public void SubscriberCrash_DoesNotCorruptSession()
        {
            core.Start("en");
            core.Bus.EventPublished += _ => throw new System.Exception("network down");
            foreach (var e in Runs.Objective(s.Ordered.First())) core.Bus.Publish(e);
            Assert.AreEqual(1, SurgicalAssessmentEngine.Evaluate(s, core.Bus.Log).Completed.Count());
        }

        [Test] public void InvalidScenario_Refused()
        {
            s.objectives[0].prerequisites = new[] { "obj.nope" };
            Assert.Throws<System.InvalidOperationException>(() => new SurgerySessionCore(s, () => 0));
        }
    }
}
