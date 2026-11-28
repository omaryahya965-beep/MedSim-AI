using System;
using System.Collections;
using System.Linq;
using MedSimAI.AI;
using MedSimAI.Assessment;
using MedSimAI.Characters;
using MedSimAI.Clinical;
using MedSimAI.Instruments;
using MedSimAI.Voice;
using UnityEngine;

namespace MedSimAI.Core
{
    /// <summary>Tutorial = corridor/welcome phase (kept for compatibility); Discussion = doctor-led clinical discussion;
    /// Hospital = host-managed hospital scene with no clinical session running (no menu board, no tablet).</summary>
    public enum AppState { Loading, MainMenu, Tutorial, Running, Paused, Debrief, Discussion, Hospital }

    /// <summary>
    /// Owns the learner journey (v0.2):
    /// corridor welcome (live) → physically open the door → doctor walks in → assessment observed by the SupervisorDirector
    /// → doctor-led clinical discussion → personalised spoken debrief → optional detailed report.
    /// All character speech is generated at runtime by Live AI; nothing is pre-recorded.
    /// </summary>
    public class SessionManager : MonoBehaviour
    {
        public static SessionManager Instance { get; private set; }
        public AppState State { get; private set; } = AppState.Loading;
        public event Action<AppState> StateChanged;
        public event Action<AssessmentResult, string> DebriefReady;
        public event Action<string> SpokenDebriefReady;
        public bool trainingMode = true; // Training: all supervisor coaching. Assessment: only safety-critical interventions.
        bool welcomeInside; string welcomeOverride;
        [Tooltip("Start the clinical session automatically after loading (no menu board).")] public bool autoStart = true;
        [Tooltip("Hospital scene: a host coordinator starts and ends the clinical session (Room 01). No menu board, no auto start.")] public bool hostManaged;
        [Tooltip("Hospital scene: the host suspends the clinical pause menu while another room owns its own pause menu.")] public bool suppressPause;
        /// <summary>Hospital scene: the student asked (pause menu) to leave the clinical session / restart from the corridor.</summary>
        public event Action ReturnToHostRequested, HostRestartRequested;
        public bool ClinicalSessionActive => State == AppState.Tutorial || State == AppState.Running || State == AppState.Discussion || State == AppState.Debrief || (State == AppState.Paused && resumeState != AppState.Hospital);
        public MedicalInstructorController instructor;
        public PatientInteractionController patient;
        public MedicalInstrumentBase[] instruments;
        public ECGMachineController ecg;
        public PatientMonitorController monitor;
        [Header("Staging (optional — set by the scene builder)")]
        public DoctorMovement doctorMovement;
        public Transform doctorCorridorMark, doctorDoorwayMark, doctorObserveMark, studentCorridorSpawn;
        public HospitalDoor examDoor;
        public float loadingSeconds = 1.0f;
        SessionStore store;
        public AssessmentResult LastResult { get; private set; }
        public string LastSpokenDebrief { get; private set; }
        int discussionIndex = -1, discussionAttempts;
        Coroutine flow;

        ClinicalScenarioManager M => ClinicalScenarioManager.Instance;
        VoiceInteractionManager V => VoiceInteractionManager.Instance;

        void Awake() { Instance = this; store = new SessionStore(); }

        IEnumerator Start()
        {
            SetState(AppState.Loading);
            yield return new WaitForSeconds(loadingSeconds);
            SetState(hostManaged ? AppState.Hospital : AppState.MainMenu);
            M.Session.Stages.StageChanged += OnStageChanged;
            M.Session.Patient.ConditionChanged += OnConditionChanged;
            M.Director.Intervene += OnIntervention;
            M.Director.CanSpeak = () => V && !V.Busy && (State == AppState.Running || State == AppState.Tutorial);
            AIProviderManager.Instance.Orchestrator.TurnCompleted += OnTurn;
            if (examDoor) examDoor.Opened += OnDoorOpened;
            if (autoStart && !hostManaged && !AIProviderManager.ForceOffline) StartClinicalSession(true);
        }

        void SetState(AppState s) { State = s; StateChanged?.Invoke(s); }

        // ------------------------------------------------------------------ Step 1: corridor welcome
        /// <param name="keepDoor">Host (hospital) only: the student opened the door and walked in before the session started, so
        /// the door is left as it is (not slammed shut behind them) and the welcome does not ask them to open it.</param>
        /// <param name="welcomeOverride">Host (hospital) only: a short line instead of the full scenario welcome (the student has
        /// already been introduced to this case during this visit).</param>
        public void StartClinicalSession(bool training, bool keepDoor = false, string welcomeOverride = null)
        {
            trainingMode = training; welcomeInside = keepDoor; this.welcomeOverride = welcomeOverride;
            if (flow != null) StopCoroutine(flow);
            V?.Interrupt();
            M.ResetSession();
            MedSimAI.Characters.CharacterActionRouter.Reset();
            foreach (var i in instruments) if (i) i.ResetInstrument();
            if (ecg) ecg.ResetMachine();
            if (examDoor && !keepDoor) examDoor.ResetDoor();
            discussionIndex = -1; discussionAttempts = 0; LiveProvider.DiscussionId = null; LastSpokenDebrief = null; finishing = false;
            M.Director.AllowOrdinary = training;
            M.BeginSession();
            SetState(AppState.Tutorial);
            flow = StartCoroutine(Welcome());
        }

        IEnumerator Welcome()
        {
            if (!hostManaged && doctorMovement && doctorCorridorMark) { doctorMovement.transform.SetPositionAndRotation(doctorCorridorMark.position, doctorCorridorMark.rotation); } // hospital: she has walked here with the student
            var live = AIProviderManager.Instance.Live;
            if (live.Configured && string.IsNullOrEmpty(live.SessionId)) { var t = live.StartSession(M.Scenario); yield return new WaitUntil(() => t.IsCompleted); }
            yield return new WaitForSeconds(0.4f);
            if (instructor && instructor.life) instructor.life.mood = CharacterMood.Attentive;
            var welcome = string.IsNullOrEmpty(welcomeOverride) ? M.Scenario.instructorLines.welcome : welcomeOverride;
            if (welcomeInside) welcome = System.Text.RegularExpressions.Regex.Replace(welcome, @"\s*[^.!?؟]*\b(door|الباب)\b[^.!?؟]*[.!?؟]", "").Trim();   // already inside: no "open the door"
            var say = V.SayLive(Persona.Instructor, string.IsNullOrEmpty(welcomeOverride) ? "welcome" : "hospital", welcome);
            if (instructor && examDoor && !welcomeInside) instructor.PointAtDoor(examDoor.handle ? examDoor.handle : examDoor.transform);
            yield return new WaitUntil(() => say.IsCompleted);
            if (!examDoor) SetState(AppState.Running); // scenes without a corridor door start the assessment directly
        }

        // ------------------------------------------------------------------ Step 2: door → doctor walks in
        void OnDoorOpened()
        {
            if (M.Session.Tracker.Has(ClinicalEvents.DoorOpened)) return;
            M.Session.Record(ClinicalEvents.DoorOpened);
            if (State == AppState.Tutorial) SetState(AppState.Running);
            if (doctorMovement && doctorObserveMark) StartCoroutine(DoctorEnters());
        }

        IEnumerator DoctorEnters()
        {
            yield return new WaitForSeconds(0.6f); // lets the student lead through the door
            doctorMovement.GoTo(doctorObserveMark, doctorDoorwayMark);
            yield return new WaitUntil(() => !doctorMovement.IsMoving);
            if (patient) doctorMovement.Face(patient.Focus.position);
        }

        public void NotifyStudentEnteredRoom()
        {
            if (!M.Session.Tracker.Has(ClinicalEvents.EnteredRoom)) M.Session.Record(ClinicalEvents.EnteredRoom);
            if (State == AppState.Tutorial) SetState(AppState.Running);
        }

        // ------------------------------------------------------------------ Step 5: intelligent supervision
        Transform FocusFor(string focus)
        {
            switch (focus)
            {
                case "patient": return patient ? patient.Focus : null;
                case "ecg": return ecg ? ecg.transform : null;
                case "monitor": return monitor ? monitor.transform : null;
                case "trolley": case "instrument": return instructor ? instructor.trolleyPoint : null;
                default: return null;
            }
        }

        void OnIntervention(InterventionRequest req)
        {
            var target = FocusFor(req.focus);
            if (instructor) instructor.Gesture(req.safety ? CharacterMood.Attentive : CharacterMood.Explaining, target);
            if (doctorMovement && target && !doctorMovement.IsMoving) doctorMovement.Face(target.position);
            _ = V.SayLive(Persona.Instructor, "intervention", req.line, req.safety ? "safety" : "ordinary");
        }

        void OnConditionChanged(PatientStateDef from, PatientStateDef to, string message)
        {
            if (patient) patient.ApplyCondition(to);
            if (to != null && to.id == "S_DETERIORATION") MedSimAI.Characters.CharacterActionRouter.Patient?.ReturnToRest(); // a deteriorating patient stops holding requested poses
            // The patient voices how he feels after an authored change (e.g. deterioration) when nobody is talking.
            if (to?.statements != null && to.statements.Length > 0 && State == AppState.Running) StartCoroutine(PatientVoicesChange(to.statements[0]));
            // After escalation (handover), the doctor moves on to the clinical discussion once things settle.
            if (to != null && to.id == "S_HANDOVER" && State == AppState.Running) StartCoroutine(DiscussionAfterSettle());
        }

        IEnumerator PatientVoicesChange(string line)
        {
            float t = 0; while (V.Busy && t < 12) { t += Time.deltaTime; yield return null; }
            if (!V.Busy) _ = V.SayLive(Persona.Patient, "state", line);
        }

        IEnumerator DiscussionAfterSettle()
        {
            yield return new WaitForSeconds(2f);
            float t = 0; while (V.Busy && t < 20) { t += Time.deltaTime; yield return null; }
            if (State == AppState.Running) BeginDiscussion();
        }

        // ------------------------------------------------------------------ Step 6: clinical discussion
        public void BeginReasoning() => BeginDiscussion();

        public void BeginDiscussion()
        {
            if (State == AppState.Discussion || State == AppState.Debrief) return;
            M.Session.Stages.TryTransition(ClinicalStage.ClinicalReasoning);
            SetState(AppState.Discussion);
            discussionIndex = -1;
            flow = StartCoroutine(AskNext(true));
        }

        bool finishing;
        IEnumerator AskNext(bool intro)
        {
            if (State != AppState.Discussion) yield break;
            var qs = M.Scenario.discussion;
            float t = 0; while (V.Busy && t < 20) { t += Time.deltaTime; yield return null; }
            discussionIndex++; discussionAttempts = 0;
            if (qs == null || discussionIndex >= qs.Length) { LiveProvider.DiscussionId = null; FinishCase(); yield break; }
            var q = qs[discussionIndex];
            // Skip questions the student has already answered convincingly (concepts recorded deterministically).
            while (q.concepts != null && q.concepts.Length > 0 && q.concepts.All(c => M.Session.Tracker.Log.Any(e => e.eventId == ClinicalEvents.Concept(c) && (e.detail == null || !e.detail.StartsWith(ClinicalSession.AiTag)))))
            {
                discussionIndex++;
                if (discussionIndex >= qs.Length) { LiveProvider.DiscussionId = null; FinishCase(); yield break; }
                q = qs[discussionIndex];
            }
            LiveProvider.DiscussionId = q.id;
            if (instructor) instructor.Gesture(CharacterMood.Attentive, null);
            if (doctorMovement && V.playerHead) doctorMovement.Face(V.playerHead.position);
            var baseText = intro && !string.IsNullOrEmpty(M.Scenario.instructorLines.discussionIntro) ? M.Scenario.instructorLines.discussionIntro + " " + q.question : q.question;
            _ = V.SayLive(Persona.Instructor, "discussion", baseText);
        }

        void OnTurn(ConversationTurn t)
        {
            if (t.persona == Persona.Instructor && State == AppState.Discussion && discussionIndex >= 0)
            {
                var q = M.Scenario.discussion[discussionIndex];
                bool covered = q.concepts == null || q.concepts.Length == 0 || q.concepts.Any(c => M.Session.Tracker.Has(ClinicalEvents.Concept(c)));
                discussionAttempts++;
                if (covered || discussionAttempts >= 2) StartCoroutine(AskNextAfterReply());
            }
            if (t.persona == Persona.Instructor && State == AppState.Running && WantsToDiscuss(t.studentText)) BeginDiscussion();
        }

        static bool WantsToDiscuss(string s)
        {
            var u = HistoryMatcher.Normalize(s);
            return u.Contains("ready to summar") || u.Contains("ready to present") || u.Contains("ready to discuss") || u.Contains("i'm finished") || u.Contains("i am finished") || u.Contains("خلصت") || u.Contains("جاهز ألخص") || u.Contains("جاهزة ألخص");
        }

        IEnumerator AskNextAfterReply()
        {
            yield return new WaitForSeconds(0.5f);
            float t = 0; while (V.Busy && t < 25) { t += Time.deltaTime; yield return null; }
            yield return AskNext(false);
        }

        void OnStageChanged(ClinicalStage from, ClinicalStage to) { }

        // ------------------------------------------------------------------ help & escalation (voice or tablet)
        public void RequestHint()
        {
            M.Director.NoteStudentActivity();
            var hint = M.Session.RequestHint();
            if (instructor) instructor.Gesture(CharacterMood.Explaining, null);
            _ = V.SayLive(Persona.Instructor, "intervention", hint, "ordinary");
        }

        public void EscalateToSenior()
        {
            M.Session.Escalate("tablet");
            if (instructor) instructor.Gesture(CharacterMood.Approving, null);
            _ = V.SayLive(Persona.Instructor, "ack", M.Scenario.instructorLines.escalationAck);
        }

        // ------------------------------------------------------------------ Step 7: personalised spoken debrief
        public void FinishCase()
        {
            if (State == AppState.Debrief || finishing) return; // idempotent: the last answer and the last question can both end the case
            finishing = true;
            if (flow != null) StopCoroutine(flow);
            flow = StartCoroutine(FinishRoutine());
        }

        IEnumerator FinishRoutine()
        {
            V?.Interrupt();
            LiveProvider.DiscussionId = null;
            M.Session.AdvanceToFeedback();
            M.Session.AdvanceToDebrief();
            M.EndSession();
            var r = AssessmentEngine.Evaluate(M.Scenario, M.Session.Tracker.Log, M.Session.HintsRequested, M.Elapsed, M.SessionStartUtc);
            store.Save(r); LastResult = r;
            var text = FeedbackComposer.Compose(M.Scenario, r, store.Previous(M.Scenario.scenarioId));
            SetState(AppState.Debrief);
            var payload = BuildDebriefPayload(r);
            if (instructor) instructor.Gesture(CharacterMood.Approving, null);
            if (doctorMovement && V.playerHead) doctorMovement.Face(V.playerHead.position);
            var say = V.SayLive(Persona.Instructor, "debrief", "", null, payload, FeedbackComposer.ComposeSpoken(M.Scenario, r));
            DebriefReady?.Invoke(r, text);
            yield return new WaitUntil(() => say.IsCompleted);
            LastSpokenDebrief = say.Result;
            SpokenDebriefReady?.Invoke(say.Result);
            var live = AIProviderManager.Instance.Live; _ = live.EndSession();
        }

        DebriefPayload BuildDebriefPayload(AssessmentResult r)
        {
            var interventions = M.Director.History.Select(h => h.rule.purpose).Where(p => !string.IsNullOrEmpty(p)).Distinct().ToArray();
            return new DebriefPayload
            {
                met = r.criteria.Where(c => c.met && !c.viaAIIntent).Select(c => c.description).ToArray(),
                missed = r.criteria.Where(c => !c.met).Select(c => c.description).ToArray(),
                missedCritical = r.missedCritical.ToArray(),
                interventions = interventions,
                aiInterpreted = r.criteria.Where(c => c.met && c.viaAIIntent).Select(c => c.description).ToArray(),
                escalated = r.escalated, ecgSeconds = r.timed.Count > 0 ? r.timed[0].actualSeconds : -1, hints = r.hintsRequested
            };
        }

        // ------------------------------------------------------------------ pause / restart
        public void Pause(bool on)
        {
            if (on && suppressPause) return;
            if (on && (State == AppState.Running || State == AppState.Discussion || State == AppState.Tutorial || State == AppState.Hospital)) { resumeState = State; SetState(AppState.Paused); Time.timeScale = 0; V?.Interrupt(); }
            else if (!on && State == AppState.Paused) { Time.timeScale = 1; SetState(resumeState); }
        }
        AppState resumeState = AppState.Running;

        public void Restart()
        {
            Time.timeScale = 1;
            if (hostManaged && resumeState == AppState.Hospital && State == AppState.Paused) { SetState(AppState.Hospital); HostRestartRequested?.Invoke(); return; }
            StartClinicalSession(trainingMode);
        }

        public void ReturnToMenu()
        {
            if (hostManaged) { EndClinicalSession(); ReturnToHostRequested?.Invoke(); return; }
            Time.timeScale = 1; V?.Interrupt(); if (flow != null) StopCoroutine(flow); M.ResetSession(); SetState(AppState.MainMenu);
        }

        /// <summary>
        /// Hospital scene: ends (or abandons) the clinical session cleanly when the student leaves Room 01 or chooses another
        /// room: stops speech and the flow, clears queued character actions, resets the patient, instruments, ECG and door,
        /// and closes the live clinical session. The clinical record of a finished case is already saved by FinishRoutine.
        /// </summary>
        public void EndClinicalSession()
        {
            Time.timeScale = 1;
            V?.Interrupt();
            if (flow != null) { StopCoroutine(flow); flow = null; }
            finishing = false; LiveProvider.DiscussionId = null; discussionIndex = -1; discussionAttempts = 0;
            MedSimAI.Characters.CharacterActionRouter.Reset();
            M.ResetSession();
            foreach (var i in instruments) if (i) i.ResetInstrument();
            if (ecg) ecg.ResetMachine();
            if (examDoor) examDoor.ResetDoor();
            var live = AIProviderManager.Instance ? AIProviderManager.Instance.Live : null; if (live != null && !string.IsNullOrEmpty(live.SessionId)) _ = live.EndSession();
            SetState(hostManaged ? AppState.Hospital : AppState.MainMenu);
        }

        public void ExitSimulation()
        {
            Debug.Log("[MedSim] Exit Simulation requested by: " + System.Environment.StackTrace);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
