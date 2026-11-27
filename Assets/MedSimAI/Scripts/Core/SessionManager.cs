using System;
using System.Collections;
using MedSimAI.AI;
using MedSimAI.Assessment;
using MedSimAI.Characters;
using MedSimAI.Clinical;
using MedSimAI.Instruments;
using MedSimAI.Voice;
using UnityEngine;

namespace MedSimAI.Core
{
    public enum AppState { Loading, MainMenu, Tutorial, Running, Paused, Debrief }

    /// <summary>Owns the learner journey: menu → welcome/tutorial → case → reasoning → debrief → restart.</summary>
    public class SessionManager : MonoBehaviour
    {
        public static SessionManager Instance { get; private set; }
        public AppState State { get; private set; } = AppState.Loading;
        public event Action<AppState> StateChanged;
        public event Action<AssessmentResult, string> DebriefReady;
        public bool trainingMode = true; // Training mode: proactive hints. Assessment mode: hints only on request.
        public MedicalInstructorController instructor;
        public PatientInteractionController patient;
        public MedicalInstrumentBase[] instruments;
        public ECGMachineController ecg;
        public PatientMonitorController monitor;
        public float loadingSeconds = 2.5f;
        SessionStore store;
        public AssessmentResult LastResult { get; private set; }

        ClinicalScenarioManager M => ClinicalScenarioManager.Instance;

        void Awake() { Instance = this; store = new SessionStore(); }

        IEnumerator Start()
        {
            SetState(AppState.Loading);
            yield return new WaitForSeconds(loadingSeconds);
            SetState(AppState.MainMenu);
            M.Session.Stages.StageChanged += OnStageChanged;
            AIProviderManager.Instance.Orchestrator.TurnCompleted += OnTurn;
        }

        void SetState(AppState s) { State = s; StateChanged?.Invoke(s); }

        public void StartClinicalSession(bool training)
        {
            trainingMode = training;
            M.ResetSession();
            foreach (var i in instruments) if (i) i.ResetInstrument();
            if (ecg) ecg.ResetMachine();
            M.BeginSession();
            SetState(AppState.Tutorial);
            StartCoroutine(Welcome());
        }

        IEnumerator Welcome()
        {
            var lines = M.Scenario.instructorLines;
            yield return new WaitForSeconds(0.6f);
            // Short doctor-led onboarding: welcome → role → how to talk/controls → where the instruments are.
            instructor.Say(lines.welcome, CharacterMood.Explaining, pointAt: patient ? patient.Focus : null);
            yield return new WaitUntil(() => !instructor.IsSpeaking);
            yield return new WaitForSeconds(0.35f);
            if (!string.IsNullOrEmpty(lines.onboardingRole)) { instructor.Say(lines.onboardingRole, CharacterMood.Attentive); yield return new WaitUntil(() => !instructor.IsSpeaking); yield return new WaitForSeconds(0.35f); }
            instructor.Say(lines.tutorial, CharacterMood.Explaining);
            yield return new WaitUntil(() => !instructor.IsSpeaking);
            yield return new WaitForSeconds(0.35f);
            SetState(AppState.Running); // the student may start while the last onboarding line plays
            if (!string.IsNullOrEmpty(lines.onboardingInstruments)) instructor.Say(lines.onboardingInstruments, CharacterMood.Explaining, pointAt: instructor.trolleyPoint);
        }

        void OnStageChanged(ClinicalStage from, ClinicalStage to)
        {
            if (State != AppState.Running) return;
            if (to == ClinicalStage.ClinicalReasoning) instructor.Say(M.Scenario.instructorLines.reasoningPrompt, CharacterMood.Attentive);
        }

        int lastProgressNudge;
        void OnTurn(ConversationTurn t)
        {
            if (!trainingMode || State != AppState.Running) return;
            var s = M.Session;
            int keyFacts = 0; foreach (var f in s.Scenario.historyFacts) if (f.keyFact && s.Tracker.Has(ClinicalEvents.Fact(f.id))) keyFacts++;
            if (lastProgressNudge == 0 && keyFacts >= 10) { lastProgressNudge = 1; StartCoroutine(SayAfterPatient(s.Scenario.instructorLines.historyDone)); }
        }

        IEnumerator SayAfterPatient(string line)
        {
            yield return new WaitUntil(() => !patient.IsSpeaking);
            yield return new WaitForSeconds(0.8f);
            instructor.Say(line, CharacterMood.Attentive);
        }

        void Update()
        {
            if (State != AppState.Running || !trainingMode) return;
            var s = M.Session;
            if (lastProgressNudge == 1 && s.Tracker.Has(ClinicalEvents.Exam("EX_BP")) && s.Tracker.Has(ClinicalEvents.Exam("EX_SPO2")) && s.Tracker.Has(ClinicalEvents.Exam("EX_AUSC_HEART")))
            { lastProgressNudge = 2; instructor.Say(s.Scenario.instructorLines.examDone, CharacterMood.Attentive, pointAt: ecg ? ecg.transform : null); }
            if (lastProgressNudge < 3 && s.Tracker.Has(ClinicalEvents.Inv("INV_ECG")))
            { lastProgressNudge = 3; instructor.Say(s.Scenario.instructorLines.ecgShown, CharacterMood.Explaining, pointAt: ecg ? ecg.transform : null); }
        }

        public void RequestHint()
        {
            var hint = M.Session.RequestHint();
            instructor.Say(hint, CharacterMood.Explaining);
        }

        public void EscalateToSenior()
        {
            M.Session.Escalate("tablet");
            instructor.Say(M.Scenario.instructorLines.escalationAck, CharacterMood.Approving);
        }

        public void BeginReasoning() => M.Session.Stages.TryTransition(ClinicalStage.ClinicalReasoning);

        public void FinishCase()
        {
            StartCoroutine(FinishRoutine());
        }

        IEnumerator FinishRoutine()
        {
            VoiceInteractionManager.Instance?.Interrupt();
            M.Session.AdvanceToFeedback();
            instructor.Say(M.Scenario.instructorLines.debriefIntro, CharacterMood.Approving);
            yield return new WaitForSeconds(0.5f);
            M.Session.AdvanceToDebrief();
            M.EndSession();
            var r = AssessmentEngine.Evaluate(M.Scenario, M.Session.Tracker.Log, M.Session.HintsRequested, M.Elapsed, M.SessionStartUtc);
            store.Save(r); LastResult = r;
            var text = FeedbackComposer.Compose(M.Scenario, r, store.Previous(M.Scenario.scenarioId));
            SetState(AppState.Debrief);
            DebriefReady?.Invoke(r, text);
        }

        public void Pause(bool on)
        {
            if (on && State == AppState.Running) { SetState(AppState.Paused); Time.timeScale = 0; VoiceInteractionManager.Instance?.Interrupt(); }
            else if (!on && State == AppState.Paused) { Time.timeScale = 1; SetState(AppState.Running); }
        }

        public void Restart() { Time.timeScale = 1; lastProgressNudge = 0; StartClinicalSession(trainingMode); }

        public void ReturnToMenu() { Time.timeScale = 1; VoiceInteractionManager.Instance?.Interrupt(); M.ResetSession(); lastProgressNudge = 0; SetState(AppState.MainMenu); }

        public void ExitSimulation()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
