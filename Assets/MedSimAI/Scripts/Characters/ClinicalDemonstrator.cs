using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MedSimAI.AI;
using MedSimAI.Clinical;
using MedSimAI.Core;
using MedSimAI.Instruments;
using MedSimAI.Voice;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace MedSimAI.Characters
{
    /// <summary>
    /// Embodied teaching in the clinical room: explain → DEMONSTRATE → let the student try.
    /// When the student asks Dr. Hassan to show how («ورجيني كيف بنستخدم جهاز الضغط», "show me how to do it", "I don't have
    /// enough information", «خليني أشوف كيف بتعملي الفحص»), she really does it at the bedside: walks to the instrument, takes
    /// it in her hand (arm IK), walks to the patient, talks to Mr. Reyes (who answers audibly and moves his arm / breathes on
    /// request), applies the instrument to the correct site, presses START, reads the result shown on the device and on the
    /// result card, explains each step, takes it off, puts it back and hands over: "now it's your turn".
    /// Topics: bp_cuff, pulse_oximeter, stethoscope, interview, ecg; "" = the next useful part; "full" = interview + BP + SpO₂ +
    /// auscultation. Every spoken line is an approved, authored line (spoken through the normal live voice route, which keeps
    /// meaning and grounding, or shown as subtitles offline); every number comes from the patient's current authored state.
    /// A demonstration records NO exam for the student (the student still has to do it) and counts as one hint.
    /// </summary>
    public class ClinicalDemonstrator : MonoBehaviour
    {
        public static ClinicalDemonstrator Instance { get; private set; }
        public DoctorMovement movement; public HumanoidLifeController life;
        public bool Running => run != null;
        public string Topic { get; private set; } = "";
        public readonly List<string> Log = new List<string>();
        ArmReachIK ik; Coroutine run; Action<bool, string> done; MedicalInstrumentBase carried; Transform patient; Animator patientAnim;
        bool Ar => AIProviderManager.Instance && AIProviderManager.Instance.ConversationLanguage == "ar";
        ClinicalSession Session => ClinicalScenarioManager.Instance ? ClinicalScenarioManager.Instance.Session : null;

        /// <summary>Finds the doctor and attaches the demonstrator (idempotent).</summary>
        public static ClinicalDemonstrator Ensure()
        {
            if (Instance) return Instance;
            var doc = FindAnyObjectByType<MedicalInstructorController>(); if (!doc) return null;
            if (!doc.TryGetComponent<ClinicalDemonstrator>(out var d)) d = doc.gameObject.AddComponent<ClinicalDemonstrator>();
            return d;
        }

        void Awake()
        {
            Instance = this;
            if (!movement) movement = GetComponent<DoctorMovement>();
            if (!life) life = GetComponent<HumanoidLifeController>();
            if (!TryGetComponent(out ik)) ik = gameObject.AddComponent<ArmReachIK>();
        }
        void OnDestroy() { if (Instance == this) Instance = null; }

        // ------------------------------------------------------------------ entry
        public bool Begin(string topic, Action<bool, string> onDone = null)
        {
            if (Session == null || !movement) return false;
            if (Running) Abort("replaced", false);
            var p = FindAnyObjectByType<PatientInteractionController>(); if (!p) return false;
            patient = p.transform; patientAnim = p.GetComponentInChildren<Animator>();
            topic = string.IsNullOrEmpty(topic) || topic == "none" ? NextTopic() : topic;
            Topic = topic; done = onDone;
            Session.RequestHint();   // a demonstration is assistance: counted like a hint in the debrief
            Log.Add("demonstration: " + topic);
            Debug.Log("[MedSim] Demonstration by Dr. Hassan: " + topic);
            ClinicalScenarioManager.PauseCoaching = true;
            run = StartCoroutine(Run(topic));
            return true;
        }

        /// <summary>The next useful part of the examination the student has not done yet.</summary>
        string NextTopic()
        {
            var t = Session.Tracker;
            int facts = t.Log.Count(e => e.eventId.StartsWith("FACT:"));
            bool Did(string inst) => t.Log.Any(e => e.eventId == ClinicalEvents.InstrumentOnPatient && (e.detail ?? "").StartsWith(inst));
            if (facts < 3) return "interview";
            if (!Did("bp_cuff")) return "bp_cuff";
            if (!Did("pulse_oximeter")) return "pulse_oximeter";
            return "stethoscope";
        }

        IEnumerator Run(string topic)
        {
            movement.Stop();
            yield return Say(Persona.Instructor, L("open", topic == "full" ? "full" : ""));
            var parts = topic == "full" ? new[] { "interview", "bp_cuff", "pulse_oximeter", "stethoscope" } : new[] { topic };
            foreach (var part in parts)
            {
                Log.Add("part: " + part);
                switch (part)
                {
                    case "interview": yield return Interview(); break;
                    case "bp_cuff": yield return BloodPressure(); break;
                    case "pulse_oximeter": yield return Oximeter(); break;
                    case "stethoscope": yield return Stethoscope(); break;
                    case "ecg": yield return Ecg(); break;
                }
            }
            ik.Release();
            yield return Say(Persona.Instructor, L("your_turn_" + (topic == "full" ? "full" : topic)));
            StepAside();
            Finish(true, "");
        }

        // ------------------------------------------------------------------ parts
        IEnumerator Interview()
        {
            yield return WalkTo(Bedside(0.75f, true), PatientChest);
            Face(PatientHead);
            foreach (var (q, fact) in new[] { ("q_greet", "H_CONSENT"), ("q_onset", "H_ONSET"), ("q_character", "H_CHARACTER"), ("q_radiation", "H_RADIATION"), ("q_associated", "H_ASSOCIATED") })
            {
                yield return Say(Persona.Instructor, L(q));
                var f = Session.Scenario.FindFact(fact);
                if (f != null) yield return Say(Persona.Patient, Ar && AIProviderManager.Instance.Language != null ? AIProviderManager.Instance.Language.Localize(f.answer) : f.answer);
            }
            Face(Student);
            yield return Say(Persona.Instructor, L("interview_point"));
        }

        IEnumerator BloodPressure()
        {
            var cuff = FindAnyObjectByType<BloodPressureController>(); if (!cuff) yield break;
            var zone = NearestZone("bp_cuff");
            yield return Take(cuff);
            yield return WalkTo(Bedside(0.55f, IsLeft(zone)), zone.transform.position);
            yield return Say(Persona.Instructor, L("bp_to_patient"));
            yield return Say(Persona.Patient, L("patient_ok"));
            CharacterActionRouter.Execute(Persona.Patient, new LiveAction { type = "extend_arm", side = IsLeft(zone) ? "left" : "right", status = "accepted" });
            yield return new WaitForSeconds(1.2f);
            var say = StartCoroutine(SayCo(Persona.Instructor, L("bp_apply")));
            yield return Apply(cuff, zone);
            yield return say;
            var dev = cuff.Device;
            if (dev) { Face(dev.position); ik.ReachTo(dev.position + (Head - dev.position).normalized * 0.04f); yield return new WaitForSeconds(0.7f); }
            yield return Say(Persona.Instructor, L("bp_start"));
            if (!cuff.StartMeasurement()) { Log.Add("start refused"); }
            ik.Release();
            yield return Say(Persona.Patient, L("patient_still_ok"));
            float t0 = Time.time; while (cuff.Measuring && Time.time - t0 < 15f) yield return null;
            var v = Session.Patient.Vitals;
            Face(Student);
            yield return Say(Persona.Instructor, L("bp_read", v.systolic.ToString(), v.diastolic.ToString(), v.heartRate.ToString()));
            yield return TakeOffAndReturn(cuff, zone);
            CharacterActionRouter.Execute(Persona.Patient, new LiveAction { type = "lower_arm", side = IsLeft(zone) ? "left" : "right", status = "accepted" });
        }

        IEnumerator Oximeter()
        {
            var ox = FindAnyObjectByType<PulseOximeterController>(); if (!ox) yield break;
            var zone = NearestZone("pulse_oximeter");
            yield return Take(ox);
            yield return WalkTo(Bedside(0.4f, IsLeft(zone)), zone.transform.position);
            yield return Say(Persona.Instructor, L("ox_to_patient"));
            yield return Say(Persona.Patient, L("patient_ok"));
            var say = StartCoroutine(SayCo(Persona.Instructor, L("ox_apply")));
            yield return Apply(ox, zone);
            yield return say;
            yield return new WaitForSeconds(ox.settleSeconds + 0.6f);
            var v = Session.Patient.Vitals;
            Face(Student);
            yield return Say(Persona.Instructor, L("ox_read", v.spo2.ToString(), v.heartRate.ToString()));
            yield return TakeOffAndReturn(ox, zone);
        }

        IEnumerator Stethoscope()
        {
            var st = FindAnyObjectByType<StethoscopeController>(); if (!st) yield break;
            yield return Take(st);
            var zones = FindObjectsByType<PlacementZone>(FindObjectsSortMode.None).Where(z => z.Accepts("stethoscope")).ToList();
            var chest = zones.FirstOrDefault(); if (!chest) yield break;
            yield return WalkTo(Bedside(0.5f, true), chest.transform.position);
            yield return Say(Persona.Instructor, L("st_to_patient"));
            yield return Say(Persona.Patient, L("patient_ok"));
            var tip = st.probePoint ? st.probePoint : st.transform;
            foreach (var id in new[] { "CHEST_AORTIC", "CHEST_PULMONARY", "CHEST_TRICUSPID", "CHEST_MITRAL" })
            {
                var z = zones.FirstOrDefault(x => x.zoneId == id); if (!z) continue;
                var say = StartCoroutine(SayCo(Persona.Instructor, L("st_" + id.Substring(6).ToLowerInvariant())));
                ik.ReachWithTool(st.transform, tip, z.transform.position + z.transform.forward * 0.005f, (z.transform.position - Head).normalized);
                yield return new WaitForSeconds(1.6f);
                yield return say;
            }
            yield return Say(Persona.Instructor, L("st_breathe"));
            CharacterActionRouter.Execute(Persona.Patient, new LiveAction { type = "deep_breath", side = "none", status = "accepted" });
            foreach (var id in new[] { "CHEST_LUNG_R", "CHEST_LUNG_L" })
            {
                var z = zones.FirstOrDefault(x => x.zoneId == id); if (!z) continue;
                ik.ReachWithTool(st.transform, tip, z.transform.position, (z.transform.position - Head).normalized);
                yield return new WaitForSeconds(1.8f);
            }
            yield return Say(Persona.Instructor, L("st_lungs"));
            CharacterActionRouter.Execute(Persona.Patient, new LiveAction { type = "breathe_normally", side = "none", status = "accepted" });
            ik.Release();
            yield return Return(st);
        }

        IEnumerator Ecg()
        {
            var ecg = FindAnyObjectByType<ECGMachineController>();
            if (ecg) { yield return WalkTo(NearPoint(ecg.transform.position, 0.6f), ecg.transform.position); if (life) life.PointAt(ecg.recordButton ? ecg.recordButton.transform : ecg.transform, 3f); }
            yield return Say(Persona.Instructor, L("ecg"));
        }

        // ------------------------------------------------------------------ physical pieces
        IEnumerator Take(MedicalInstrumentBase inst)
        {
            yield return WalkTo(NearPoint(inst.transform.position, 0.45f), inst.transform.position);
            ik.ReachTo(inst.transform.position + Vector3.up * 0.03f); yield return new WaitForSeconds(0.6f);
            inst.DemonstrationUse = true;
            var grab = inst.GetComponent<XRGrabInteractable>(); if (grab) grab.enabled = false;   // nobody else grabs it during the demonstration
            inst.DemoTakeOff();
            var hand = ik.Hand ? ik.Hand : transform;
            inst.transform.SetParent(hand, true);
            inst.transform.position = hand.position + (hand.position - ShoulderPos).normalized * 0.05f;
            carried = inst;
            ik.Release(); yield return new WaitForSeconds(0.3f);
        }

        IEnumerator Apply(MedicalInstrumentBase inst, PlacementZone zone)
        {
            var target = zone.snapPoint ? zone.snapPoint : zone.transform;
            ik.ReachTo(target.position); yield return new WaitForSeconds(0.8f);
            inst.transform.SetParent(null, true);
            inst.PlaceInZone(zone);   // the instrument's own placement flow (snaps to the site, shows READY / starts reading)
            carried = null;
            yield return new WaitForSeconds(0.4f);
            ik.Release();
        }

        IEnumerator TakeOffAndReturn(MedicalInstrumentBase inst, PlacementZone zone)
        {
            ik.ReachTo(inst.transform.position); yield return new WaitForSeconds(0.6f);
            inst.DemoTakeOff();
            var hand = ik.Hand ? ik.Hand : transform; inst.transform.SetParent(hand, true); carried = inst;
            ik.Release(); yield return new WaitForSeconds(0.3f);
            yield return Return(inst);
        }

        IEnumerator Return(MedicalInstrumentBase inst)
        {
            yield return WalkTo(NearPoint(inst.HomePosition, 0.45f), inst.HomePosition);
            ik.ReachTo(inst.HomePosition + Vector3.up * 0.04f); yield return new WaitForSeconds(0.5f);
            inst.transform.SetParent(null, true);
            inst.ResetInstrument();
            var grab = inst.GetComponent<XRGrabInteractable>(); if (grab) grab.enabled = true;
            inst.DemonstrationUse = false; carried = null;
            ik.Release(); yield return new WaitForSeconds(0.2f);
        }

        IEnumerator WalkTo(Vector3 p, Vector3 face)
        {
            if (!NavMesh.SamplePosition(p, out var hit, 0.5f, NavMesh.AllAreas)) { Face(face); yield break; }
            movement.GoToPoint(hit.position, face);
            yield return null; yield return null;
            float t0 = Time.time; while (movement.IsMoving && Time.time - t0 < 15f) yield return null;
            Face(face);
            yield return new WaitForSeconds(0.25f);
        }

        // ------------------------------------------------------------------ speech
        IEnumerator Say(Persona who, string text) { yield return SayCo(who, text); }
        IEnumerator SayCo(Persona who, string text)
        {
            if (string.IsNullOrEmpty(text)) yield break;
            Log.Add("say " + who + ": " + text);
            var vim = VoiceInteractionManager.Instance;
            if (!vim) { yield return new WaitForSeconds(Mathf.Clamp(text.Length / 14f, 1.2f, 8f)); yield break; }
            float t0 = Time.time;
            while (vim.Busy && Time.time - t0 < 12f) yield return null;   // never talk over another line
            var task = vim.SayLive(who, "line", text);
            while (!task.IsCompleted && Time.time - t0 < 25f) yield return null;
            yield return null;
            while (vim.Busy && Time.time - t0 < 40f) yield return null;
        }

        // ------------------------------------------------------------------ geometry
        Transform Student => Camera.main ? Camera.main.transform : null;
        Vector3 Head => Camera.main ? Camera.main.transform.position : transform.position + Vector3.up * 1.6f;
        Vector3 ShoulderPos { get { var a = GetComponentInChildren<Animator>(); var u = a && a.isHuman ? a.GetBoneTransform(HumanBodyBones.RightUpperArm) : null; return u ? u.position : transform.position + Vector3.up * 1.4f; } }
        Vector3 PatientChest => patientAnim && patientAnim.isHuman ? patientAnim.GetBoneTransform(HumanBodyBones.Chest).position : patient.position + Vector3.up * 0.5f;
        Vector3 PatientHead => patientAnim && patientAnim.isHuman ? patientAnim.GetBoneTransform(HumanBodyBones.Head).position : patient.position + Vector3.up * 0.6f;
        void Face(Vector3 p) { movement.Face(p); if (life) life.gazeOverride = null; }
        void Face(Transform t) { if (t) Face(t.position); }

        /// <summary>A spot at the side of the bed beside the patient's chest/arm (patient's left = +X in Room 01), away from the student's line of sight.</summary>
        Vector3 Bedside(float offsetAlong, bool patientsLeft)
        {
            var c = PatientChest; var right = patient ? patient.right : Vector3.right;   // patient faces the foot of the bed
            var side = patientsLeft ? -right : right;
            var p = new Vector3(c.x, 0, c.z) + side * 0.75f + (patient ? -patient.forward : Vector3.forward) * (offsetAlong - 0.5f);
            return p;
        }
        static bool IsLeft(PlacementZone z) => z && z.zoneId.EndsWith("_L");
        PlacementZone NearestZone(string instrument)
        {
            var zs = FindObjectsByType<PlacementZone>(FindObjectsSortMode.None).Where(z => z.Accepts(instrument)).ToList();
            var s = Student ? Student.position : transform.position;
            return zs.OrderBy(z => Vector3.Distance(z.transform.position, s)).FirstOrDefault();   // the side the student can see best
        }
        static Vector3 NearPoint(Vector3 point, float dist)
        {
            for (float d = dist; d < dist + 0.5f; d += 0.15f)
                for (int i = 0; i < 16; i++)
                {
                    var p = new Vector3(point.x, 0, point.z) + Quaternion.AngleAxis(i * 22.5f, Vector3.up) * Vector3.forward * d;
                    if (NavMesh.SamplePosition(p, out var hit, 0.12f, NavMesh.AllAreas)) return hit.position;
                }
            return new Vector3(point.x, 0, point.z);
        }

        void StepAside()
        {
            var s = Student; if (!s) return;
            var away = Vector3.ProjectOnPlane(transform.position - PatientChest, Vector3.up).normalized;
            var p = transform.position + away * 0.5f;
            if (NavMesh.SamplePosition(p, out var hit, 0.6f, NavMesh.AllAreas)) movement.GoToPoint(hit.position, s.position);
        }

        /// <summary>Stops now and puts everything back (student said "enough, let me try", left the room, or a new request).</summary>
        public void Abort(string why, bool sayYourTurn)
        {
            if (!Running) return;
            StopCoroutine(run); run = null; ik.Release();
            if (carried) { carried.transform.SetParent(null, true); carried.ResetInstrument(); var g = carried.GetComponent<XRGrabInteractable>(); if (g) g.enabled = true; carried.DemonstrationUse = false; carried = null; }
            foreach (var inst in FindObjectsByType<MedicalInstrumentBase>(FindObjectsSortMode.None)) if (inst.DemonstrationUse) { inst.ResetInstrument(); inst.DemonstrationUse = false; var g = inst.GetComponent<XRGrabInteractable>(); if (g) g.enabled = true; }
            Log.Add("aborted: " + why);
            if (sayYourTurn) StartCoroutine(SayCo(Persona.Instructor, L("your_turn_" + Topic)));
            Finish(false, why);
        }
        void Finish(bool ok, string why) { run = null; ClinicalScenarioManager.PauseCoaching = false; var d = done; done = null; d?.Invoke(ok, why); }

        // ------------------------------------------------------------------ approved lines (authored; simulation-level teaching)
        static readonly Dictionary<string, (string en, string ar)> Lines = new Dictionary<string, (string, string)>
        {
            ["open"] = ("Of course. Watch me first, then you'll do it yourself.", "أكيد. تابعني أول، وبعدين بتعملها إنت."),
            ["open_full"] = ("Of course. I'll take you through the examination step by step. Watch, and then you'll do it.", "أكيد. رح أمشي معك بالفحص خطوة خطوة. تابع، وبعدين بتعمله إنت."),
            ["q_greet"] = ("Hello Mr. Reyes, I'm Dr. Hassan. May I ask you a few questions about the pain?", "مرحبا سيد آدم، أنا الدكتورة سارة حسن. بقدر أسألك كم سؤال عن الوجع؟"),
            ["q_onset"] = ("When did it start, and what were you doing at the time?", "إمتى بلّش الوجع، وشو كنت عم تعمل وقتها؟"),
            ["q_character"] = ("What does the pain feel like?", "كيف بتوصف الوجع؟ شو إحساسه؟"),
            ["q_radiation"] = ("Does it spread anywhere else?", "بيمتد لمكان تاني؟"),
            ["q_associated"] = ("Have you noticed anything else, like sweating, nausea or shortness of breath?", "لاحظت إشي تاني، متل عرق أو غثيان أو ضيق نفس؟"),
            ["interview_point"] = ("Notice the order: an open question first, then onset, character, spread and associated symptoms. Now you continue: ask about his past illnesses, medicines and allergies.", "لاحظ الترتيب: سؤال مفتوح أول، بعدين البداية، وطبيعة الوجع، وامتداده، والأعراض المرافقة. هلّق كمّل إنت: اسأله عن أمراضه السابقة وأدويته والحساسية."),
            ["patient_ok"] = ("Okay, doctor.", "ماشي يا دكتورة."),
            ["patient_still_ok"] = ("Alright, I'll keep still.", "ماشي، رح أضل ثابت."),
            ["bp_to_patient"] = ("Mr. Reyes, I'm going to check your blood pressure. Could you rest your arm out a little and keep it relaxed?", "سيد آدم، رح أقيسلك الضغط. ممكن تريّح إيدك وتبعدها شوي عن جسمك؟"),
            ["bp_apply"] = ("First the sleeve goes up, so the cuff sits on bare skin. The cuff wraps around the upper arm, a little above the elbow, snug but not tight.", "أول إشي بنرفع الكم، حتى تكون الكفّة على الجلد مباشرة. بنلف الكفّة حوالين أعلى الذراع، فوق الكوع بشوي، مشدودة بس مش ضاغطة."),
            ["bp_start"] = ("Now I press START on the monitor. He should keep still and not talk while it measures.", "هلّق بضغط START على الجهاز. لازم يضل ثابت وما يحكي وهو بقيس."),
            ["bp_read"] = ("There's the reading on the monitor: {0} over {1}, pulse {2}. Always note which arm you used.", "هاي القراءة على الجهاز: {0} على {1}، والنبض {2}. دايماً سجّل أي إيد استخدمت."),
            ["ox_to_patient"] = ("Mr. Reyes, I'm going to put a small clip on your finger to measure your oxygen. It doesn't hurt.", "سيد آدم، رح أحطلك ملقط صغير على إصبعك لنقيس الأكسجين، ما بوجع."),
            ["ox_apply"] = ("The probe goes on a fingertip, nail facing up, and we wait a few seconds for a steady reading.", "الحسّاس بنحطه على طرف الإصبع والظفر لفوق، وبنستنى كم ثانية لحتى تثبت القراءة."),
            ["ox_read"] = ("Oxygen saturation is {0} percent, pulse {1}.", "نسبة الأكسجين {0} بالمية، والنبض {1}."),
            ["st_to_patient"] = ("Mr. Reyes, I'm going to listen to your heart and chest. Just breathe normally.", "سيد آدم، رح أسمع قلبك وصدرك. تنفّس عادي."),
            ["st_aortic"] = ("Aortic area: second space, to the right of the breastbone.", "منطقة الأبهر: المسافة الضلعية التانية يمين عظمة القص."),
            ["st_pulmonary"] = ("Pulmonary area: second space, to the left of the breastbone.", "المنطقة الرئوية: المسافة التانية يسار عظمة القص."),
            ["st_tricuspid"] = ("Tricuspid area: lower left edge of the breastbone.", "منطقة الصمام ثلاثي الشُّرف: أسفل يسار عظمة القص."),
            ["st_mitral"] = ("Mitral area: at the apex of the heart.", "منطقة الصمام التاجي: عند قمة القلب."),
            ["st_breathe"] = ("Now the lungs. Mr. Reyes, take slow deep breaths through your mouth, please.", "هلّق الرئتين. سيد آدم، خود نفس عميق وبطيء من تمّك لو سمحت."),
            ["st_lungs"] = ("Both lung fields, comparing left and right. Hold the chest piece still on each spot.", "الرئتين الثنتين، وبنقارن اليمين مع الشمال. ثبّت السمّاعة على كل نقطة."),
            ["ecg"] = ("For the ECG, the electrodes go on the chest and limbs first; then you press RECORD on this cart and wait for the tracing.", "لتخطيط القلب، بنحط الأقطاب على الصدر والأطراف أول، وبعدين بنضغط RECORD على هالجهاز وبنستنى التخطيط."),
            ["your_turn_interview"] = ("", ""),
            ["your_turn_bp_cuff"] = ("Now it's your turn: apply the cuff to his arm and press START.", "هلّق دورك: ركّب الكفّة على إيده واضغط START."),
            ["your_turn_pulse_oximeter"] = ("Your turn: clip the probe on his finger and wait for the reading.", "دورك: حط الملقط على إصبعه واستنى القراءة."),
            ["your_turn_stethoscope"] = ("Your turn: listen over the same areas, and hold the chest piece still on each one.", "دورك: اسمع على نفس المناطق، وثبّت السمّاعة على كل وحدة."),
            ["your_turn_ecg"] = ("Your turn when you're ready.", "دورك لما تكون جاهز."),
            ["your_turn_full"] = ("That's the examination. Now it's your turn, step by step. I'll be right here.", "هيك بكون الفحص. هلّق دورك، خطوة خطوة، وأنا جنبك."),
        };
        string L(string key, params string[] args)
        {
            if (key == "open" && args.Length > 0 && args[0] == "full") key = "open_full";
            if (!Lines.TryGetValue(key, out var l)) return "";
            var s = Ar ? l.ar : l.en;
            return args.Length > 0 && s.Contains("{0}") ? string.Format(s, args) : s;
        }
    }
}
