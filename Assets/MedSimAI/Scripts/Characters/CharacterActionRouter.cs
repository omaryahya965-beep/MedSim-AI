using System.Linq;
using MedSimAI.AI;
using MedSimAI.Core;
using UnityEngine;

namespace MedSimAI.Characters
{
    /// <summary>
    /// Executes VALIDATED physical actions (proxy-validated catalogue + authored scenario policy) on the right character,
    /// in sync with the spoken acknowledgement, and reports what really happened (completed / interrupted / unavailable)
    /// back to the AI so it never claims an action that did not happen. Requests and results are logged for the debrief,
    /// never scored. A declined unsafe request (e.g. asking the cardiac patient to get up) lets the supervisor step in.
    /// </summary>
    public static class CharacterActionRouter
    {
        static PatientActionController patient, subscribedPatient; static DoctorActionController doctor, subscribedDoctor; static bool wired;
        public static PatientActionController Patient { get { Wire(); return patient; } }
        public static DoctorActionController Doctor { get { Wire(); return doctor; } }

        static void Wire()
        {
            if (wired && patient && doctor) return;
            var v = MedSimAI.Voice.VoiceInteractionManager.Instance; var head = v ? v.playerHead : (Camera.main ? Camera.main.transform : null);
            var pat = Object.FindAnyObjectByType<PatientInteractionController>(); var doc = Object.FindAnyObjectByType<MedicalInstructorController>();
            if (pat && !(patient = pat.GetComponent<PatientActionController>())) patient = pat.gameObject.AddComponent<PatientActionController>();
            if (patient) { patient.student = head; if (subscribedPatient != patient) { subscribedPatient = patient; patient.Completed += (t, s) => Report(Persona.Patient, t, s, "completed"); patient.WasInterrupted += (t, s) => Report(Persona.Patient, t, s, "interrupted"); } }
            if (doc && !(doctor = doc.GetComponent<DoctorActionController>())) doctor = doc.gameObject.AddComponent<DoctorActionController>();
            if (doctor)
            {
                doctor.student = head;
                var anim = pat ? pat.GetComponent<Animator>() : null; doctor.patientChest = anim ? (anim.GetBoneTransform(HumanBodyBones.UpperChest) ?? anim.GetBoneTransform(HumanBodyBones.Chest)) : (pat ? pat.transform : null);
                if (SessionManager.Instance) doctor.observeMark = SessionManager.Instance.doctorObserveMark;
                doctor.patientRoot = pat ? pat.transform : null;
                if (subscribedDoctor != doctor) { subscribedDoctor = doctor; doctor.Completed += (t, s) => Report(Persona.Instructor, t, "none", "completed"); doctor.Failed += (t, r) => Report(Persona.Instructor, t, "none", r == "interrupted" ? "interrupted" : "unavailable"); }
            }
            wired = true;
        }

        public static void Handle(ConversationTurn turn)
        {
            var a = turn?.action; if (a == null || string.IsNullOrEmpty(a.type) || a.type == "none" || a.status == "none") return;
            if (a.status == "accepted") Execute(turn.persona, a);
            else if (a.status == "denied" && !string.IsNullOrEmpty(turn.supervisorIntervention)) SupervisorStepsIn(turn.supervisorIntervention);
        }

        public static bool Execute(Persona who, LiveAction a)
        {
            Wire(); bool ok;
            if (who == Persona.Patient) ok = patient && patient.Execute(a.type, a.side);
            else ok = doctor && doctor.Execute(a.type, a.instrument);
            Debug.Log($"[MedSim] Action {who}:{a.type}({a.side}{(a.instrument != "none" ? "," + a.instrument : "")}) → {(ok ? "started" : "unavailable")}");
            if (!ok) Report(who, a.type, a.side, "unavailable");
            return ok;
        }

        static void Report(Persona who, string type, string side, string status)
        {
            ClinicalScenarioManager.Instance?.Session?.Record("ACTION_RESULT", $"{who}:{type}:{side}:{status}");
            AIProviderManager.Instance?.Live?.PostActionResult(who, new LiveAction { type = type, side = side, status = "accepted" }, status);
        }

        /// <summary>Records the declined unsafe request; the SupervisorDirector rule (event trigger, grace/cooldown/priority) decides when the doctor steps in.</summary>
        static void SupervisorStepsIn(string interventionId)
        {
            ClinicalScenarioManager.Instance?.Session?.Record("ACTION_DENIED_UNSAFE", interventionId);
        }
    }
}
