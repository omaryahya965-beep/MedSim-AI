using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MedSimAI.Clinical;

namespace MedSimAI.AI
{
    /// <summary>
    /// Student text → InputGuard → deterministic case tracking → provider (online or offline) → ResponseValidator → turn.
    /// The orchestrator, not the AI, decides which facts are disclosed and records all clinical events.
    /// </summary>
    public class ConversationOrchestrator : IConversationOrchestrator
    {
        readonly ClinicalSession session;
        readonly IClinicalContextProvider contextProvider;
        readonly IResponseValidator validator;
        readonly IAIProvider offline;
        public IAIProvider Online { get; set; }
        public bool UseOnline { get; set; }
        public string Language { get; set; } = "en";
        public int TimeoutMs { get; set; } = 12000;
        readonly Dictionary<Persona, List<ConversationMessage>> history = new Dictionary<Persona, List<ConversationMessage>> { { Persona.Patient, new List<ConversationMessage>() }, { Persona.Instructor, new List<ConversationMessage>() } };
        CancellationTokenSource current;
        public event Action<ConversationTurn> TurnCompleted;

        public ConversationOrchestrator(ClinicalSession s, IAIProvider offlineProvider, IAIProvider onlineProvider = null)
        {
            session = s; contextProvider = new ClinicalContextProvider(s); validator = new ResponseValidator(); offline = offlineProvider; Online = onlineProvider;
        }

        public IReadOnlyList<ConversationMessage> History(Persona p) => history[p];
        public void CancelCurrent() { current?.Cancel(); }
        public void Reset() { CancelCurrent(); foreach (var h in history.Values) h.Clear(); }

        public async Task<ConversationTurn> HandleAsync(Persona target, string utterance, CancellationToken externalCt)
        {
            CancelCurrent();
            current = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
            var ct = current.Token;
            var turn = new ConversationTurn { persona = target, studentText = utterance };

            // 1. Deterministic event tracking (grading never depends on the LLM).
            if (target == Persona.Patient) session.ProcessStudentQuestion(utterance);
            else
            {
                // Deterministic: investigations, explicit escalation, diagnosis named vs asked, reasoning concepts (no word-count credit).
                var inv = session.ProcessInstructorStatement(utterance);
                if (inv.HasValue) turn.investigation = new InvestigationOutcome { id = inv.Value.investigationId, name = inv.Value.name, result = inv.Value.result, imageKey = inv.Value.imageKey, available = inv.Value.available };
            }
            session.Record(ClinicalEvents.StudentSpoke, target.ToString());

            // 2. Safety guard.
            var guard = InputGuard.Classify(utterance);
            if (guard != GuardCategory.None)
            {
                turn.responseText = InputGuard.SafeResponse(guard, target, session.Scenario); turn.source = ResponseSource.SafetyGuard; turn.note = guard.ToString();
                return Finish(target, turn);
            }

            // 3. Investigation results are spoken from scenario data verbatim, never generated.
            if (turn.investigation != null && !(UseOnline && Online != null))
            {
                turn.responseText = turn.investigation.available ? ScriptedLines.InvestigationArranged(turn.investigation.name) : ScriptedLines.InvestigationUnavailable(turn.investigation.name);
                turn.source = ResponseSource.ScenarioFact;
                return Finish(target, turn);
            }

            var ctx = contextProvider.BuildContext(target);
            var req = new AIRequest { context = ctx, studentUtterance = utterance, history = history[target].ToList(), language = Language };

            // 4. Provider with timeout + fallback.
            AIResponse resp = null;
            if (UseOnline && Online != null)
            {
                try
                {
                    var task = Online.GenerateAsync(req, ct);
                    var done = await Task.WhenAny(task, Task.Delay(TimeoutMs, ct));
                    if (done != task) throw new TimeoutException("AI provider timed out");
                    resp = await task;
                    if (resp != null && resp.live)
                    {
                        turn.responseText = ApplyLive(target, resp, turn, utterance);
                        turn.audioId = resp.audioId;
                        turn.source = resp.liveSource == "ai" ? ResponseSource.AIValidated : ResponseSource.ScenarioFact;
                        turn.note = resp.liveSource;
                        return Finish(target, turn);
                    }
                    if (resp != null && resp.grounded)
                    {
                        // Structured classification: render clinical speech locally from approved content.
                        turn.responseText = ApplyGrounded(target, resp, turn, utterance, ctx);
                        turn.source = ResponseSource.AIValidated;
                        return Finish(target, turn);
                    }
                    var v = validator.Validate(resp, ctx);
                    if (v.valid) turn.source = ResponseSource.AIValidated;
                    else { turn.note = "Rejected: " + v.reason; resp = null; turn.source = ResponseSource.AIRejectedFallback; }
                }
                catch (OperationCanceledException) when (externalCt.IsCancellationRequested || ct.IsCancellationRequested) { throw; }
                catch (Exception e) { turn.note = "Provider failure: " + e.Message; turn.source = ResponseSource.ProviderFailureFallback; resp = null; }
            }
            if (resp == null)
            {
                resp = await offline.GenerateAsync(req, ct);
                if (turn.source != ResponseSource.AIRejectedFallback && turn.source != ResponseSource.ProviderFailureFallback)
                    turn.source = resp.usedFactIds != null && resp.usedFactIds.Length > 0 ? ResponseSource.ScenarioFact : ResponseSource.OfflineScript;
            }
            turn.responseText = resp.speech;
            return Finish(target, turn);
        }

        public const string AiIntentTag = "AI_INTENT:";

        /// <summary>
        /// Applies a grounded-proxy classification. The AI only selects among approved fact ids / intents; every clinical
        /// sentence comes from the local authoritative scenario. Events recognised only through AI intent are tagged
        /// (AI_INTENT:) so the debrief can show them transparently.
        /// </summary>
        string ApplyGrounded(Persona target, AIResponse r, ConversationTurn turn, string utterance, PersonaContext ctx)
        {
            var s = session.Scenario;
            if (target == Persona.Patient)
            {
                var ids = (r.usedFactIds ?? new string[0]).Where(id => s.FindFact(id) != null).Distinct().Take(2).ToList();
                foreach (var id in ids)
                    if (!session.Tracker.Has(ClinicalEvents.Fact(id))) session.Record(ClinicalEvents.Fact(id), AiIntentTag + utterance);
                string lead = Language == "en" && GroundedText.ValidLeadIn(r.leadIn) ? r.leadIn.Trim() + " " : "";
                if (ids.Count == 0)
                {
                    int idx = System.Math.Abs(OfflineScriptedProvider.StableHash(utterance)) % s.unknownAnswers.Length;
                    return s.unknownAnswers[idx];
                }
                return lead + string.Join(" ", ids.Select(id => s.FindFact(id).answer));
            }
            var lines = s.instructorLines;
            switch (r.intent)
            {
                case "hint_request":
                    return session.RequestHint();
                case "investigation_request":
                    var inv = r.investigationId != null ? s.FindInvestigation(r.investigationId) : null;
                    if (inv == null) return ScriptedLines.InstructorGeneric;
                    var res = session.RequestInvestigation(inv.id);
                    turn.investigation = new InvestigationOutcome { id = res.investigationId, name = res.name, result = res.result, imageKey = res.imageKey, available = res.available };
                    return res.available ? ScriptedLines.InvestigationArranged(res.name) : ScriptedLines.InvestigationUnavailable(res.name);
                case "summary_or_reasoning":
                    if (!session.Tracker.Has(ClinicalEvents.SummaryGiven)) session.SubmitReasoning(utterance);
                    if (r.mentionsEscalation && session.Stages.Current != ClinicalStage.Introduction && !session.Tracker.Has(ClinicalEvents.EscalationRequested))
                        session.Record(ClinicalEvents.EscalationRequested, AiIntentTag + utterance);
                    return session.Tracker.Has(ClinicalEvents.EscalationRequested) ? lines.escalationAck : ScriptedLines.InstructorReasoningFollowUp;
                case "out_of_scope":
                    return lines.outOfScope;
                default:
                    if (r.mentionsEscalation && session.Stages.Current != ClinicalStage.Introduction && !session.Tracker.Has(ClinicalEvents.EscalationRequested))
                    { session.Record(ClinicalEvents.EscalationRequested, AiIntentTag + utterance); return lines.escalationAck; }
                    var check = validator.Validate(new AIResponse { speech = r.reply, usedFactIds = new string[0] }, ctx);
                    return !string.IsNullOrWhiteSpace(r.reply) && check.valid ? r.reply.Trim() : ScriptedLines.InstructorGeneric;
            }
        }

        /// <summary>
        /// Applies a Live AI turn. Deterministic tracking already ran; here AI-only recognitions are added with the
        /// AI_INTENT tag (shown in the debrief as "AI-interpreted — for instructor review"). Text was validated server-side
        /// against the authoritative scenario; the client re-checks that it contains no dosing or forbidden content.
        /// </summary>
        string ApplyLive(Persona target, AIResponse r, ConversationTurn turn, string utterance)
        {
            turn.action = r.action; turn.supervisorIntervention = r.supervisorIntervention;
            if (r.action != null && r.action.status != "none" && !string.IsNullOrEmpty(r.action.type) && r.action.type != "none")
                session.Record("ACTION_REQUESTED", $"{target}:{r.action.type}:{r.action.side}:{r.action.status}"); // logged for the debrief, never scored
            var s = session.Scenario;
            if (target == Persona.Patient)
            {
                foreach (var id in (r.usedFactIds ?? new string[0]).Distinct())
                    if (s.FindFact(id) != null && !session.Tracker.Has(ClinicalEvents.Fact(id))) session.Record(ClinicalEvents.Fact(id), AiIntentTag + utterance);
            }
            else
            {
                if (!string.IsNullOrEmpty(r.investigationId) && turn.investigation == null && s.FindInvestigation(r.investigationId) != null && !session.Tracker.Has(ClinicalEvents.Inv(r.investigationId)))
                {
                    bool reasoning = session.Stages.Current == ClinicalStage.ClinicalReasoning;
                    var res = session.RequestInvestigation(r.investigationId);
                    if (reasoning) session.Stages.TryTransition(ClinicalStage.ClinicalReasoning);
                    turn.investigation = new InvestigationOutcome { id = res.investigationId, name = res.name, result = res.result, imageKey = res.imageKey, available = res.available };
                }
                if (r.escalationRequested && session.Stages.Current != ClinicalStage.Introduction && !session.Tracker.Has(ClinicalEvents.EscalationRequested))
                    session.Record(ClinicalEvents.EscalationRequested, AiIntentTag + utterance);
                if (session.Stages.Current == ClinicalStage.ClinicalReasoning && r.concepts != null && r.concepts.Length > 0)
                    session.RecordReasoning(utterance, r.concepts.Where(c => !session.Tracker.Has(ClinicalEvents.Concept(c))), true);
            }
            var text = (r.speech ?? "").Trim();
            if (InputGuard.Classify(text) == GuardCategory.Dosing || ResponseValidator.ContainsDosing(text)) text = target == Persona.Patient ? s.unknownAnswers[0] : s.instructorLines.outOfScope;
            return text;
        }

        /// <summary>
        /// Applies a native-voice turn: the proxy's Realtime session heard the student's audio and returned a validated
        /// reply. The SAME deterministic tracking runs on what was heard; AI-only recognitions are tagged as usual.
        /// </summary>
        public ConversationTurn ApplyVoiceTurn(Persona target, string heard, AIResponse resp)
        {
            var turn = new ConversationTurn { persona = target, studentText = heard ?? "" };
            if (!string.IsNullOrWhiteSpace(heard))
            {
                if (target == Persona.Patient) session.ProcessStudentQuestion(heard);
                else
                {
                    var inv = session.ProcessInstructorStatement(heard);
                    if (inv.HasValue) turn.investigation = new InvestigationOutcome { id = inv.Value.investigationId, name = inv.Value.name, result = inv.Value.result, imageKey = inv.Value.imageKey, available = inv.Value.available };
                }
                session.Record(ClinicalEvents.StudentSpoke, target.ToString());
            }
            turn.responseText = ApplyLive(target, resp, turn, heard ?? "");
            turn.source = resp.liveSource == "ai" ? ResponseSource.AIValidated : ResponseSource.ScenarioFact;
            turn.note = "voice:" + resp.liveSource;
            return Finish(target, turn);
        }

        ConversationTurn Finish(Persona p, ConversationTurn t)
        {
            history[p].Add(new ConversationMessage { role = "student", text = t.studentText });
            history[p].Add(new ConversationMessage { role = p.ToString().ToLowerInvariant(), text = t.responseText });
            if (history[p].Count > 40) history[p].RemoveRange(0, history[p].Count - 40);
            TurnCompleted?.Invoke(t);
            return t;
        }
    }
}
