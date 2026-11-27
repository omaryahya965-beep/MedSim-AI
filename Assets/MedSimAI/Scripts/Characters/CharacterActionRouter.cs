using System.Collections.Generic;
using System.Linq;
using MedSimAI.AI;
using MedSimAI.Core;
using UnityEngine;

namespace MedSimAI.Characters
{
    /// <summary>Lifecycle of one physical action.</summary>
    public enum ActionState { Requested, Validated, InProgress, Completed, Interrupted, Rejected, NeedsClarification, Unavailable }

    /// <summary>One tracked action (for tests, the debrief log and the AI's "what really happened" context).</summary>
    public class ActionRecord
    {
        public Persona who; public LiveAction action; public ActionState state; public float requestedAt, startedAt, endedAt;
        public override string ToString() => $"{who}:{action.type}({action.side}{(action.instrument != "none" ? "," + action.instrument : "")}) {state}";
    }

    /// <summary>
    /// Executes VALIDATED physical actions (proxy-validated catalogue + authored scenario policy) on the right character,
    /// in order (one queue per character: "sit up and raise your left arm" runs sit_up, then raise_arm), in sync with the
    /// spoken acknowledgement. Every action goes Requested → Validated → InProgress → Completed / Interrupted / Unavailable
    /// (or Rejected / NeedsClarification), and the final state is reported to the AI so it never claims an action that did
    /// not happen. Requests and results are logged for the debrief and are never scored. A declined unsafe request lets the
    /// supervisor rule step in.
    /// </summary>
    public static class CharacterActionRouter
    {
        static PatientActionController patient, subscribedPatient; static DoctorActionController doctor, subscribedDoctor;
        static readonly Dictionary<Persona, Queue<ActionRecord>> queues = new Dictionary<Persona, Queue<ActionRecord>> { { Persona.Patient, new Queue<ActionRecord>() }, { Persona.Instructor, new Queue<ActionRecord>() } };
        static readonly Dictionary<Persona, ActionRecord> current = new Dictionary<Persona, ActionRecord>();
        public static readonly List<ActionRecord> History = new List<ActionRecord>();
        public static event System.Action<ActionRecord> StateChanged;
        public static PatientActionController Patient { get { Wire(); return patient; } }
        public static DoctorActionController Doctor { get { Wire(); return doctor; } }

        static void Wire()
        {
            if (patient && doctor) return;
            var v = MedSimAI.Voice.VoiceInteractionManager.Instance; var head = v ? v.playerHead : (Camera.main ? Camera.main.transform : null);
            var pat = Object.FindAnyObjectByType<PatientInteractionController>(); var doc = Object.FindAnyObjectByType<MedicalInstructorController>();
            if (pat && !(patient = pat.GetComponent<PatientActionController>())) patient = pat.gameObject.AddComponent<PatientActionController>();
            if (patient)
            {
                patient.student = head;
                if (subscribedPatient != patient) { subscribedPatient = patient; patient.Completed += (t, s) => Finish(Persona.Patient, t, ActionState.Completed); patient.WasInterrupted += (t, s) => Finish(Persona.Patient, t, ActionState.Interrupted); }
            }
            if (doc && !(doctor = doc.GetComponent<DoctorActionController>())) doctor = doc.gameObject.AddComponent<DoctorActionController>();
            if (doctor)
            {
                doctor.student = head;
                var anim = pat ? pat.GetComponent<Animator>() : null; doctor.patientChest = anim ? (anim.GetBoneTransform(HumanBodyBones.UpperChest) ?? anim.GetBoneTransform(HumanBodyBones.Chest)) : (pat ? pat.transform : null);
                doctor.patientRoot = pat ? pat.transform : null;
                if (SessionManager.Instance) { doctor.observeMark = SessionManager.Instance.doctorObserveMark; doctor.distanceMark = SessionManager.Instance.doctorDoorwayMark; }
                if (subscribedDoctor != doctor) { subscribedDoctor = doctor; doctor.Completed += (t, s) => Finish(Persona.Instructor, t, ActionState.Completed); doctor.Failed += (t, r) => Finish(Persona.Instructor, t, r == "interrupted" ? ActionState.Interrupted : ActionState.Unavailable); }
            }
            if (pat && doc && !pat.GetComponent<ActiveSpeakerAwareness>()) pat.gameObject.AddComponent<ActiveSpeakerAwareness>();
        }

        /// <summary>Handles the validated actions of a turn (voice or typed).</summary>
        public static void Handle(ConversationTurn turn)
        {
            if (turn == null) return;
            var list = (turn.actions != null && turn.actions.Length > 0 ? turn.actions : (turn.action != null ? new[] { turn.action } : new LiveAction[0])).Where(a => a != null && a.type != "none" && a.status != "none").ToList();
            if (list.Count == 0) return;
            Wire();
            // a new instruction to a character replaces what was still queued for them (the student changed their mind)
            queues[turn.persona].Clear();
            if (list.Any(x => x.status == "accepted") && current.TryGetValue(turn.persona, out var running) && running != null && running.state == ActionState.InProgress)
            { running.endedAt = Time.time; Set(running, ActionState.Interrupted); Report(running); current[turn.persona] = null; } // the new instruction takes over
            foreach (var a in list)
            {
                var rec = new ActionRecord { who = turn.persona, action = a, state = ActionState.Requested, requestedAt = Time.time }; History.Add(rec); Set(rec, ActionState.Requested);
                if (a.status == "accepted") { Set(rec, ActionState.Validated); queues[turn.persona].Enqueue(rec); }
                else Set(rec, a.status == "denied" ? ActionState.Rejected : ActionState.NeedsClarification);
            }
            if (list.Any(a => a.status == "denied") && !string.IsNullOrEmpty(turn.supervisorIntervention)) ClinicalScenarioManager.Instance?.Session?.Record("ACTION_DENIED_UNSAFE", turn.supervisorIntervention);
            Next(turn.persona);
        }

        /// <summary>Direct execution of one validated action (tests and tools).</summary>
        public static bool Execute(Persona who, LiveAction a)
        {
            Wire();
            var rec = new ActionRecord { who = who, action = a, state = ActionState.Validated, requestedAt = Time.time }; History.Add(rec);
            queues[who].Clear();
            if (current.TryGetValue(who, out var running) && running != null && running.state == ActionState.InProgress) { running.endedAt = Time.time; Set(running, ActionState.Interrupted); current[who] = null; }
            queues[who].Enqueue(rec); Next(who);
            return rec.state == ActionState.InProgress || rec.state == ActionState.Completed;
        }

        static void Next(Persona who)
        {
            if (current.TryGetValue(who, out var cur) && cur != null && cur.state == ActionState.InProgress) return; // wait for it to finish
            while (queues[who].Count > 0)
            {
                var rec = queues[who].Dequeue(); var a = rec.action;
                current[who] = rec; rec.startedAt = Time.time; Set(rec, ActionState.InProgress);
                bool ok = who == Persona.Patient ? patient && patient.Execute(a.type, a.side) : doctor && doctor.Execute(a.type, a.instrument);
                Debug.Log($"[MedSim] Action {rec} {(ok ? "" : "→ unavailable")}");
                if (ok) return;
                Set(rec, ActionState.Unavailable); Report(rec);
            }
        }

        static void Finish(Persona who, string type, ActionState state)
        {
            if (!current.TryGetValue(who, out var rec) || rec == null || rec.action.type != type || rec.state != ActionState.InProgress) return;
            rec.endedAt = Time.time; Set(rec, state); Report(rec);
            if (state == ActionState.Interrupted) queues[who].Clear();
            Next(who);
        }

        static void Set(ActionRecord rec, ActionState s) { rec.state = s; StateChanged?.Invoke(rec); }

        static void Report(ActionRecord rec)
        {
            string status = rec.state == ActionState.Completed ? "completed" : rec.state == ActionState.Interrupted ? "interrupted" : "unavailable";
            ClinicalScenarioManager.Instance?.Session?.Record("ACTION_RESULT", $"{rec.who}:{rec.action.type}:{rec.action.side}:{status}");
            AIProviderManager.Instance?.Live?.PostActionResult(rec.who, rec.action, status);
        }

        /// <summary>Session reset: forget queued/running actions.</summary>
        public static void Reset() { foreach (var q in queues.Values) q.Clear(); current.Clear(); History.Clear(); }
    }

    /// <summary>
    /// Social gaze: whoever is speaking gets looked at. When the patient talks, the doctor watches him; when the doctor
    /// talks, the patient glances at her; otherwise both attend to the student (their default look target).
    /// </summary>
    public class ActiveSpeakerAwareness : MonoBehaviour
    {
        HumanoidLifeController patientLife, doctorLife; CharacterSpeechController patientSpeech, doctorSpeech; Transform patientHead, doctorHead;
        void Start()
        {
            var pat = GetComponent<PatientInteractionController>(); var doc = FindAnyObjectByType<MedicalInstructorController>();
            patientLife = pat ? pat.GetComponent<HumanoidLifeController>() : null; doctorLife = doc ? doc.GetComponent<HumanoidLifeController>() : null;
            patientSpeech = pat ? pat.GetComponentInChildren<CharacterSpeechController>() : null; doctorSpeech = doc ? doc.GetComponentInChildren<CharacterSpeechController>() : null;
            patientHead = pat ? pat.GetComponent<Animator>()?.GetBoneTransform(HumanBodyBones.Head) : null; doctorHead = doc ? doc.GetComponent<Animator>()?.GetBoneTransform(HumanBodyBones.Head) : null;
        }
        Transform setOnDoctor, setOnPatient;
        void Update()
        {
            bool patTalks = patientSpeech && patientSpeech.IsSpeaking, docTalks = doctorSpeech && doctorSpeech.IsSpeaking;
            Apply(doctorLife, patTalks && !docTalks ? patientHead : null, ref setOnDoctor);
            Apply(patientLife, docTalks && !patTalks ? doctorHead : null, ref setOnPatient);
        }
        // only touches the gaze override it set itself (an action's gaze, e.g. "look at the patient", has priority)
        static void Apply(HumanoidLifeController life, Transform want, ref Transform mine)
        {
            if (!life) return;
            if (want) { if (!life.gazeOverride || life.gazeOverride == mine) { life.gazeOverride = want; mine = want; } }
            else if (mine && life.gazeOverride == mine) { life.gazeOverride = null; mine = null; }
        }
    }
}
