using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MedSimAI.AI;
using MedSimAI.Assessment;
using MedSimAI.Characters;
using MedSimAI.Core;
using MedSimAI.Surgery.AI;
using MedSimAI.Surgery.Integration;
using MedSimAI.Surgery.UI;
using MedSimAI.Voice;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MedSimAI.Hospital
{
    /// <summary>One training session in the hospital (shown in the summary; never mixed with another session's data).</summary>
    [Serializable]
    public class HospitalSessionRecord
    {
        public HospitalRoomId room; public string scenarioId = "", sessionId = "", outcome = "";   // completed | left unfinished
        public float startedAt, endedAt; public int scorePercent = -1, hints = -1; public bool guided;
        public float Minutes => (endedAt - startedAt) / 60f;
    }

    /// <summary>
    /// The hospital: one persistent doctor (Dr. Sara Hassan) guides the student from the corridor to Room 01 (the existing
    /// clinical examination, ER_CHEST_PAIN_001) and Room 02 (the surgical training, OR_TKA_TRAINING_001), and back.
    ///  * Room choice: natural speech (HospitalIntentParser on every understood utterance), the directory sign, keys 1/2/3,
    ///    or simply walking into a room.
    ///  * Navigation: validated doctor actions (guide_to_room, wait_for_student, walk_with_student) through the existing
    ///    CharacterActionRouter lifecycle, executed by HospitalGuide on the NavMesh.
    ///  * Sessions: entering a room starts its session (also after a teleport); leaving it ends it. Clinical and surgical
    ///    sessions are separate (own engines, own event logs, own proxy sessions); only language and the doctor persist.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class HospitalCoordinator : MonoBehaviour
    {
        public static HospitalCoordinator Instance { get; private set; }

        [Header("People and systems")]
        public MedicalInstructorController doctor;
        public DoctorMovement doctorMovement;
        public HospitalGuide guide;
        public SessionManager clinical;
        public SurgicalRoomBridge surgery;
        public HospitalDirectory directory;
        [Header("Places (built by the hospital builder)")]
        public Transform doctorWelcomeMark, room01DoorPoint, room02DoorPoint, room02InsidePoint, corridorMeetPoint;
        public Bounds room01Zone, room02Zone;
        [Header("Behaviour")]
        public bool guidedMode = true;
        public float welcomeDelay = 1.2f;

        public HospitalRoomId PlayerRoom { get; private set; } = HospitalRoomId.Corridor;
        public HospitalRoomId SessionRoom { get; private set; } = HospitalRoomId.None;
        public HospitalRoomId GuidingTo { get; private set; } = HospitalRoomId.None;
        public bool Welcomed { get; private set; }
        public readonly List<HospitalSessionRecord> Records = new List<HospitalSessionRecord>();
        public readonly List<string> Log = new List<string>();            // hospital-level events (tests, evidence, debugging)
        public event Action<HospitalRoomId, HospitalRoomId> RoomChanged;
        public event Action<string> GuideSaid;
        public string Language => AIProviderManager.Instance ? AIProviderManager.Instance.ConversationLanguage : "en";

        HospitalSessionRecord current; float leftRoomAt = -1f; HospitalRoomId candidate; float candidateSince;
        Transform Head => Camera.main ? Camera.main.transform : null;

        void Awake()
        {
            // The original clinical experience remains available on its own: MedSimAI.exe -clinical-only
            if (System.Environment.GetCommandLineArgs().Any(a => a == "-clinical-only")) { UnityEngine.SceneManagement.SceneManager.LoadScene("MedSim_ER"); return; }
            Instance = this;
            VoiceInteractionManager.UtteranceInterceptor = (p, text) => HandleUtterance(text);
            VoiceInteractionManager.TargetAvailable = p => p != Persona.Patient || PlayerRoom == HospitalRoomId.Room01;   // Mr. Reyes only in Room 01
            SurgerySessionController.UtteranceInterceptor = text => HandleUtterance(text);
            SurgerySessionController.StudentHeard = text => { if (VoiceInteractionManager.Instance) VoiceInteractionManager.Instance.ReportTranscript(text); };   // one conversation panel: the student's words from Room 02 too
            DoctorActionController.ExtensionHandler = HandleDoctorAction;
            ScenarioRouter.MenuOverride = () => RequestRoom(HospitalRoomId.Corridor, "shortcut");
            if (clinical) { clinical.hostManaged = true; clinical.autoStart = false; }
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            VoiceInteractionManager.UtteranceInterceptor = null; VoiceInteractionManager.TargetAvailable = null; SurgerySessionController.UtteranceInterceptor = null; SurgerySessionController.StudentHeard = null;
            DoctorActionController.ExtensionHandler = null; ScenarioRouter.MenuOverride = null;
        }

        IEnumerator Start()
        {
            if (guide) { guide.SayRequested += SayLine; guide.student = Head; }
            if (clinical)
            {
                clinical.DebriefReady += OnClinicalDebrief;
                clinical.ReturnToHostRequested += () => RequestRoom(HospitalRoomId.Corridor, "pause_menu");
                clinical.HostRestartRequested += RestartHospital;
            }
            if (surgery)
            {
                surgery.Completed += OnSurgicalCompleted;
                surgery.ReturnRequested += () => RequestRoom(HospitalRoomId.Corridor, "pause_menu");
                surgery.LanguageChanged += l => { if (AIProviderManager.Instance) AIProviderManager.Instance.SetLanguageMode(l == "ar" ? LanguageMode.Arabic : LanguageMode.English); directory?.Refresh(); };
            }
            if (AIProviderManager.Instance) AIProviderManager.Instance.LanguageChanged += _ => directory?.Refresh();
            while (clinical && clinical.State == AppState.Loading) yield return null;
            yield return new WaitForSeconds(welcomeDelay);
            Welcome(false);
        }

        // ------------------------------------------------------------------ welcome and speech
        void Welcome(bool again)
        {
            Welcomed = true; Note(again ? "welcome_again" : "welcome");
            if (doctorMovement && Head) doctorMovement.Face(Head.position);
            if (doctor) doctor.Gesture(CharacterMood.Explaining, null);
            SayLine(again ? "welcome_short" : "welcome");
            directory?.Refresh();
        }

        /// <summary>Speaks an authored guide line in the current language (live voice when available, otherwise subtitles).</summary>
        public void SayLine(string id)
        {
            var text = HospitalLines.Get(id, Language); if (string.IsNullOrEmpty(text)) return;
            GuideSaid?.Invoke(id); Note("say:" + id);
            if (surgery && surgery.Active) { surgery.Say(text); return; }
            var v = VoiceInteractionManager.Instance;
            if (v) _ = v.SayLive(Persona.Instructor, "hospital", text);
        }

        void Note(string s) { Log.Add($"{Time.time:0.0}s {s}"); Debug.Log("[Hospital] " + s); }

        // ------------------------------------------------------------------ understanding the student
        /// <summary>Every understood utterance passes here first; returns true when it was a hospital request (consumed).</summary>
        public bool HandleUtterance(string text)
        {
            var intent = HospitalIntentParser.Parse(text, PlayerRoom);
            if (intent.kind == HospitalIntentKind.None) return false;
            // Inside a room, only requests that clearly concern the hospital are taken; everything else stays with the case.
            if (PlayerRoom != HospitalRoomId.Corridor)
            {
                bool leaving = intent.kind == HospitalIntentKind.GoToRoom && intent.room != PlayerRoom;
                bool restart = intent.kind == HospitalIntentKind.Restart;
                if (!leaving && !restart) return false;
            }
            Note("heard '" + text + "' → " + intent);
            Execute(intent, "voice");
            return true;
        }

        public void Execute(HospitalIntent intent, string via)
        {
            switch (intent.kind)
            {
                case HospitalIntentKind.GoToRoom: RequestRoom(intent.room, via); break;
                case HospitalIntentKind.WhereToGo: SayLine(Records.Count == 0 ? "where_to_go" : NextSuggestion()); break;
                case HospitalIntentKind.ExplainRooms: SayLine("explain_rooms"); break;
                case HospitalIntentKind.Clarify: SayLine("clarify_room"); break;
                case HospitalIntentKind.Skip: SayLine("skip_ack"); break;
                case HospitalIntentKind.Wait: CharacterActionRouter.Execute(Persona.Instructor, new LiveAction { type = "wait_for_student", status = "accepted" }); break;
                case HospitalIntentKind.WalkWithMe: CharacterActionRouter.Execute(Persona.Instructor, new LiveAction { type = "walk_with_student", status = "accepted" }); break;
                case HospitalIntentKind.Restart:
                    if (SessionRoom == HospitalRoomId.Room01 && clinical) { Note("restart clinical"); clinical.StartClinicalSession(guidedMode); }
                    else if (SessionRoom == HospitalRoomId.Room02 && surgery && surgery.controller) { Note("restart surgical"); surgery.controller.Restart(); }
                    else RestartHospital();
                    break;
            }
        }

        string NextSuggestion()
        {
            bool c = Records.Any(r => r.room == HospitalRoomId.Room01 && r.outcome == "completed"), s = Records.Any(r => r.room == HospitalRoomId.Room02 && r.outcome == "completed");
            return c && s ? "both_done" : c ? "where_next_after_clinical" : s ? "where_next_after_surgical" : "where_to_go";
        }

        // ------------------------------------------------------------------ navigation (validated doctor actions)
        /// <summary>Room choice from speech, the directory, a key or the pause menu. The doctor walks the student there.</summary>
        public void RequestRoom(HospitalRoomId room, string via)
        {
            if (room == HospitalRoomId.None) return;
            if (room == PlayerRoom && GuidingTo == HospitalRoomId.None)
            {
                SayLine(room == HospitalRoomId.Room01 ? "already_room01" : room == HospitalRoomId.Room02 ? "already_room02" : "already_corridor");
                return;
            }
            Note($"request {room} via {via}");
            var arg = room == HospitalRoomId.Room01 ? "room_01" : room == HospitalRoomId.Room02 ? "room_02" : "corridor";
            CharacterActionRouter.Execute(Persona.Instructor, new LiveAction { type = "guide_to_room", instrument = arg, status = "accepted" });
        }

        /// <summary>DoctorActionController extension: the hospital actions. Returns null for anything else.</summary>
        int guideToken;
        bool? HandleDoctorAction(DoctorActionController ctl, string type, string arg)
        {
            switch (type)
            {
                case "guide_to_room":
                {
                    var room = arg == "room_01" ? HospitalRoomId.Room01 : arg == "room_02" ? HospitalRoomId.Room02 : arg == "corridor" ? HospitalRoomId.Corridor : HospitalRoomId.None;
                    if (room == HospitalRoomId.None || !guide) return false;
                    TakeDoctor();
                    var dest = room == HospitalRoomId.Room01 ? room01DoorPoint : room == HospitalRoomId.Room02 ? room02DoorPoint : NearestCorridorPoint();
                    if (!dest) return false;
                    GuidingTo = room;
                    SayLine(room == HospitalRoomId.Room01 ? "follow_room01" : room == HospitalRoomId.Room02 ? "follow_room02" : "follow_corridor");
                    directory?.Refresh();
                    int token = ++guideToken;
                    guide.GuideTo(dest.position, (ok, reason) =>
                    {
                        if (token != guideToken) return;   // a newer request replaced this walk: its outcome belongs to the new action
                        GuidingTo = HospitalRoomId.None; directory?.Refresh();
                        Note($"guide_to_room {arg} → {(ok ? "arrived" : reason)}");
                        if (!ok && reason == "released") { ctl.ReportExternal(type, false, "interrupted"); return; }   // the student got there first, or a new request took over
                        if (!ok) { SayLine("no_path"); ctl.ReportExternal(type, false, reason); return; }
                        OnArrivedAt(room);
                        ctl.ReportExternal(type, true);
                    });
                    return true;
                }
                case "wait_for_student":
                    if (!guide) return false;
                    TakeDoctor(); guide.Hold(); SayLine("wait_ack"); ctl.ReportExternal(type, true); return true;
                case "walk_with_student":
                    if (!guide) return false;
                    TakeDoctor(); guide.WalkWith(); SayLine("walk_with_ack"); ctl.ReportExternal(type, true); return true;
                default: return null;
            }
        }

        Transform NearestCorridorPoint()
        {
            if (!corridorMeetPoint) return null;
            if (Head && PlayerRoom == HospitalRoomId.Corridor)
            {   // meet the student where they are in the corridor
                var p = Head.position; p.y = corridorMeetPoint.position.y;
                if (UnityEngine.AI.NavMesh.SamplePosition(p + (corridorMeetPoint.position - p).normalized * 0.9f, out var hit, 1f, UnityEngine.AI.NavMesh.AllAreas)) { corridorMeetPoint.position = hit.position; }
            }
            return corridorMeetPoint;
        }

        /// <summary>The guide takes the doctor: the surgeon role and the clinical controller let go of her body.</summary>
        void TakeDoctor()
        {
            if (surgery && surgery.doctorRole && surgery.doctorRole.enabled) { surgery.doctorRole.Interrupt(); surgery.doctorRole.enabled = false; }
        }

        void OnArrivedAt(HospitalRoomId room)
        {
            if (room == HospitalRoomId.Room01)
            {   // the existing clinical flow takes over at the door: welcome, open the door, enter together
                if (SessionRoom != HospitalRoomId.Room01) StartClinical("arrived_at_door");
            }
            else if (room == HospitalRoomId.Room02) SayLine("arrived_room02");
            else SayLine(Records.Count > 0 ? NextSuggestion() : "back_corridor");
        }

        // ------------------------------------------------------------------ where the student is: sessions follow the rooms
        HospitalRoomId ZoneOf(Vector3 p)
        {
            if (room01Zone.Contains(p)) return HospitalRoomId.Room01;
            if (room02Zone.Contains(p)) return HospitalRoomId.Room02;
            return HospitalRoomId.Corridor;
        }

        void Update()
        {
            if (Head)
            {
                var z = ZoneOf(Head.position);
                if (z != candidate) { candidate = z; candidateSince = Time.time; }
                if (z != PlayerRoom && Time.time - candidateSince >= 0.35f) OnZoneChanged(PlayerRoom, z);
            }
            // Leaving a room ends its session once the student is really out (not just stepping into the doorway).
            if (leftRoomAt > 0 && PlayerRoom == HospitalRoomId.Corridor && Time.time - leftRoomAt > 1.2f) { leftRoomAt = -1f; EndRoomSession("left the room"); }
            Keys();
        }

        void OnZoneChanged(HospitalRoomId from, HospitalRoomId to)
        {
            PlayerRoom = to; Note($"student {from} → {to}");
            RoomChanged?.Invoke(from, to);
            if (to == HospitalRoomId.Corridor) { if (SessionRoom != HospitalRoomId.None) leftRoomAt = Time.time; }
            else
            {
                leftRoomAt = -1f;
                if (SessionRoom != HospitalRoomId.None && SessionRoom != to) EndRoomSession("changed room");
                if (to == HospitalRoomId.Room01 && SessionRoom != HospitalRoomId.Room01) StartClinical("entered");
                if (to == HospitalRoomId.Room02 && SessionRoom != HospitalRoomId.Room02) StartSurgical();
                if (to == HospitalRoomId.Room01 && clinical) clinical.NotifyStudentEnteredRoom();   // also covers a teleport past the doorway trigger
            }
            directory?.Refresh();
        }

        void StartClinical(string why)
        {
            if (!clinical) return;
            if (guide) guide.Release();
            // The student who opened the door and walked in keeps the door as it is (the v0.6 start resets it, which shut the
            // student inside the room).
            bool inside = PlayerRoom == HospitalRoomId.Room01 && clinical.examDoor && clinical.examDoor.IsOpen;
            clinical.StartClinicalSession(guidedMode, inside);
            SessionRoom = HospitalRoomId.Room01;
            current = new HospitalSessionRecord { room = HospitalRoomId.Room01, scenarioId = ClinicalScenarioManager.Instance ? ClinicalScenarioManager.Instance.Scenario.scenarioId : "ER_CHEST_PAIN_001", sessionId = "clinical-" + Guid.NewGuid().ToString("N").Substring(0, 8), startedAt = Time.time, guided = guidedMode };
            Note($"clinical session started ({why}) {current.sessionId}");
            // Student already inside with the door open (e.g. came back in): the doctor follows to her observation spot.
            if (PlayerRoom == HospitalRoomId.Room01 && clinical.examDoor && clinical.examDoor.IsOpen && clinical.doctorMovement && clinical.doctorObserveMark)
                clinical.doctorMovement.GoTo(clinical.doctorObserveMark, clinical.doctorDoorwayMark);
        }

        void StartSurgical()
        {
            if (!surgery) return;
            if (guide) guide.Release();
            surgery.Begin(Language, guidedMode);
            SessionRoom = HospitalRoomId.Room02;
            current = new HospitalSessionRecord { room = HospitalRoomId.Room02, scenarioId = "OR_TKA_TRAINING_001", sessionId = "surgical-" + Guid.NewGuid().ToString("N").Substring(0, 8), startedAt = Time.time, guided = guidedMode };
            Note("surgical session started " + current.sessionId);
        }

        /// <summary>Ends the active room session (finished or not). Nothing from it carries into the next session.</summary>
        void EndRoomSession(string why)
        {
            if (SessionRoom == HospitalRoomId.None) return;
            var room = SessionRoom; SessionRoom = HospitalRoomId.None;
            bool finished = current != null && current.outcome == "completed";
            if (room == HospitalRoomId.Room01 && clinical) clinical.EndClinicalSession();
            if (room == HospitalRoomId.Room02 && surgery) surgery.End();
            if (current != null) { if (!finished) { current.outcome = "left unfinished"; current.endedAt = Time.time; Records.Add(current); } current = null; }
            Note($"{room} session ended ({why}, {(finished ? "completed" : "unfinished")})");
            // The doctor comes back out with the student.
            if (PlayerRoom == HospitalRoomId.Corridor && GuidingTo == HospitalRoomId.None)
            {
                SayLine(finished ? NextSuggestion() : "left_unfinished");
                if (guide && doctorMovement && Head && Vector3.Distance(doctorMovement.transform.position, Head.position) > 2.5f) guide.WalkWith();
            }
            directory?.Refresh();
        }

        void OnClinicalDebrief(AssessmentResult r, string text)
        {
            if (current == null || current.room != HospitalRoomId.Room01) return;
            current.outcome = "completed"; current.endedAt = Time.time;
            current.scorePercent = r != null && r.observableMax > 0 ? Mathf.RoundToInt(100f * r.observableScore / r.observableMax) : -1; current.hints = r != null ? r.hintsRequested : -1;
            Records.Add(current); Note("clinical session completed " + current.sessionId);
            directory?.Refresh();
        }

        void OnSurgicalCompleted(SurgicalAssessmentResult r)
        {
            if (current == null || current.room != HospitalRoomId.Room02) return;
            current.outcome = "completed"; current.endedAt = Time.time;
            current.scorePercent = r != null && r.maxScore > 0 ? Mathf.RoundToInt(100f * r.score / r.maxScore) : -1; current.hints = r != null ? r.hintsGiven : -1;
            current.sessionId = surgery.SessionId.Length > 0 ? surgery.SessionId : current.sessionId;
            Records.Add(current); Note("surgical session completed " + current.sessionId);
            directory?.Refresh();
        }

        /// <summary>"Start again" from the corridor: end anything running and welcome the student again.</summary>
        public void RestartHospital()
        {
            Note("restart hospital");
            if (SessionRoom != HospitalRoomId.None) EndRoomSession("restart");
            if (guide) guide.Release();
            CharacterActionRouter.Reset();
            Welcome(true);
        }

        public void SetGuidedMode(bool guided)
        {
            guidedMode = guided; Note("mode " + (guided ? "guided" : "assessment"));
            if (surgery) surgery.SetGuidedMode(guided);
            directory?.Refresh();
        }

        // ------------------------------------------------------------------ desktop shortcuts (also listed on the directory)
        void Keys()
        {
            var kb = Keyboard.current; if (kb == null || Time.timeScale == 0f) return;
            if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame) RequestRoom(HospitalRoomId.Room01, "key");
            if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame) RequestRoom(HospitalRoomId.Room02, "key");
            if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame) RequestRoom(HospitalRoomId.Corridor, "key");
            if (kb.f2Key.wasPressedThisFrame) SetGuidedMode(!guidedMode);
        }
    }
}
