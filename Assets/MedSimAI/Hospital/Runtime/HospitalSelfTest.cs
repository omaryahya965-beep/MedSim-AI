using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MedSimAI.AI;
using MedSimAI.Characters;
using MedSimAI.Core;
using MedSimAI.Surgery.AI;
using MedSimAI.Surgery.Mechanics;
using MedSimAI.Surgery.Shared;
using MedSimAI.Surgery.Shared.Unity;
using MedSimAI.Voice;
using MedSimAI.XR;
using UnityEngine;
using UnityEngine.AI;

namespace MedSimAI.Hospital
{
    /// <summary>
    /// Standalone-executable journey through the hospital, for evidence and regression in the real build:
    ///   MedSimAI.exe -hospitalSelfTest "D:\out"   (Desktop build; forces offline AI, so it never spends money)
    /// Reception welcome → corridor → the doctor walks the student to Room 02 → the full surgical scenario through the real
    /// mechanics (desktop interaction path) with an assistant hand-over, a hint and the assessment → back to the corridor →
    /// Room 01 (clinical session, patient action) → corridor. Writes screenshots (with the UI), GIF frame folders,
    /// frame-time measurements and hospital_selftest.log. AUTOMATED INPUT, NOT A HUMAN STUDENT.
    /// </summary>
    public class HospitalSelfTest : MonoBehaviour
    {
        const string Arg = "-hospitalSelfTest";
        string outDir; readonly List<string> log = new List<string>(); float t0;

        static int ArgIndex() => Array.FindIndex(Environment.GetCommandLineArgs(), a => string.Equals(a, Arg, StringComparison.OrdinalIgnoreCase));

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void BeforeScene() { if (ArgIndex() >= 0) AIProviderManager.ForceOffline = true; }   // never billable

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            int i = ArgIndex(); if (i < 0 || !HospitalCoordinator.Instance) return;
            var args = Environment.GetCommandLineArgs();
            var go = new GameObject("[Hospital Self-Test]"); DontDestroyOnLoad(go);
            var r = go.AddComponent<HospitalSelfTest>();
            r.outDir = i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[i + 1] : Path.Combine(Application.persistentDataPath, "hospital_selftest");
        }

        void L(string k, string m) { var s = $"{Time.realtimeSinceStartup - t0,7:0.0}s [{k}] {m}"; log.Add(s); Debug.Log("[HospitalSelfTest] " + s); File.WriteAllLines(Path.Combine(outDir, "hospital_selftest.log"), log); }

        HospitalCoordinator HC => HospitalCoordinator.Instance;
        Transform Rig => FindAnyObjectByType<XRPlayerController>().transform;
        DesktopPlayerController Desk => FindAnyObjectByType<DesktopPlayerController>();
        Transform Doctor => HC.doctor.transform;
        SurgeryEventBus Bus => SurgeryEventBusHost.Instance.Bus;

        void Place(Vector3 floor, Vector3 look)
        {
            var cc = Rig.GetComponent<CharacterController>(); if (cc) cc.enabled = false;
            Rig.position = floor; if (cc) cc.enabled = true;
            if (Desk) Desk.LookAt(look);
        }

        IEnumerator Shot(string name)
        {
            yield return null;
            ScreenCapture.CaptureScreenshot(Path.Combine(outDir, name + ".png"));
            yield return null; yield return null;
            L("shot", name);
        }

        IEnumerator Frames(string clip, int count, float every, Action perFrame)
        {
            var dir = Path.Combine(outDir, clip); Directory.CreateDirectory(dir);
            for (int i = 0; i < count; i++) { perFrame?.Invoke(); yield return new WaitForSeconds(every * 0.5f); ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"f{i:000}.png")); yield return new WaitForSeconds(every * 0.5f); }
            L("clip", clip + $" ({count} frames)");
        }

        IEnumerator Perf(string where, float seconds)
        {
            var ft = new List<float>(); var gpu = new List<float>(); float end = Time.realtimeSinceStartup + seconds; var pm = MedSimAI.Core.PerformanceManager.Instance;
            while (Time.realtimeSinceStartup < end) { ft.Add(Time.unscaledDeltaTime * 1000f); if (pm && pm.GpuFrameMs > 0) gpu.Add(pm.GpuFrameMs); yield return null; }
            gpu.Sort(); float G(float q) => gpu.Count == 0 ? 0 : gpu[Mathf.Clamp((int)(q * gpu.Count), 0, gpu.Count - 1)];
            ft.Sort(); float P(float q) => ft[Mathf.Clamp((int)(q * ft.Count), 0, ft.Count - 1)];
            L("perf", $"{where}: {ft.Count} frames, avg {ft.Average():0.00} ms ({1000f / ft.Average():0} fps), p50 {P(0.5f):0.00} ms, p95 {P(0.95f):0.00} ms, p99 {P(0.99f):0.00} ms, max {ft.Max():0.0} ms; GPU p50 {G(0.5f):0.00} ms, p95 {G(0.95f):0.00} ms ({gpu.Count} samples); vSync={QualitySettings.vSyncCount}");
        }

        IEnumerator Until(Func<bool> c, float timeout) { float t = 0; while (!c() && t < timeout) { t += Time.deltaTime; yield return null; } }

        IEnumerator FollowDoctor(Func<bool> done, float timeout, string clip = null)
        {
            float t = 0; int frame = 0; string dir = clip != null ? Path.Combine(outDir, clip) : null; if (dir != null) Directory.CreateDirectory(dir);
            float nextShot = 0;
            while (!done() && t < timeout)
            {
                var behind = Doctor.position - Doctor.forward * 1.6f;
                if (Time.frameCount % 10 == 0 && NavMesh.SamplePosition(behind, out var hit, 1f, NavMesh.AllAreas)) Place(hit.position, Doctor.position + Vector3.up * 1.35f);
                if (dir != null && t >= nextShot && frame < 40) { ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"f{frame++:000}.png")); nextShot = t + 0.25f; }
                t += Time.deltaTime; yield return null;
            }
        }

        IEnumerator Start()
        {
            t0 = Time.realtimeSinceStartup; Directory.CreateDirectory(outDir);
            if (Environment.GetCommandLineArgs().Contains("-uncapped")) { QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1; }   // measure real frame cost, not the display cap
            yield return Until(() => HC && HC.Welcomed && DesktopPlayerController.Active, 40f);
            L("info", $"hospital exe self-test · {Application.version} · {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsMemorySize} MB) · {Screen.width}x{Screen.height} · display={DisplayModeManager.Mode} · AI={AIProviderManager.Instance.ModeLabel}");
            CharacterSpeechController.SubtitleStarted += (c, s) => L("subtitle", c.displayName + ": " + s);
            HC.GuideSaid += id => L("guide", id);
            HC.RoomChanged += (a, b) => L("room", a + " → " + b);
            CharacterActionRouter.StateChanged += r => { if (r.state != ActionState.Requested) L("action", r.ToString()); };

            // ---- reception
            yield return new WaitForSeconds(2.5f);
            yield return Perf("reception lobby", 5f);
            Place(new Vector3(13.2f, 0, -7.1f), new Vector3(10.6f, 1.3f, -4.2f)); yield return Shot("01_hospital_entrance");
            Place(new Vector3(12.4f, 0, -5.5f), Doctor.position + Vector3.up * 1.55f); yield return new WaitForSeconds(0.4f); yield return Shot("02_doctor_welcome");
            Place(new Vector3(9.8f, 0, -4.2f), new Vector3(2.0f, 1.4f, -3.0f)); yield return Shot("03_corridor_both_room_entrances");
            yield return Perf("corridor", 4f);

            // ---- the doctor walks the student to Room 02 (request understood from the transcript, as from speech)
            Place(new Vector3(11.6f, 0, -5.0f), Doctor.position + Vector3.up * 1.4f);
            var submit = VoiceInteractionManager.Instance.Submit(Persona.Instructor, "وين غرفة العمليات؟");
            yield return Until(() => submit.IsCompleted, 5f);
            L("hospital", "request consumed → guiding to " + HC.GuidingTo);
            yield return new WaitForSeconds(1.2f);
            yield return Shot("04_doctor_walking_to_room");
            yield return FollowDoctor(() => HC.GuidingTo == HospitalRoomId.None, 45f, "gif_doctor_guides_to_or");
            L("hospital", $"doctor at OR door: {Vector3.Distance(Doctor.position, HC.room02DoorPoint.position):0.00} m from the mark");

            // ---- Room 02
            var site = SurgeryAnchor.Find(AnchorIds.SurgicalSiteKneeR);
            Place(SurgeryAnchor.Find(AnchorIds.StudentOperatingPosition).position, site.position);
            yield return Until(() => HC.SessionRoom == HospitalRoomId.Room02 && HC.surgery.controller.Core != null && HC.surgery.controller.Core.Active, 20f);
            var ctl = HC.surgery.controller; var mech = HC.surgery.mechanics; var ad = HC.surgery.desktopAdapter;
            L("surgery", $"session active, mock={ctl.UsingMockMechanics}, runtime={ctl.Core.Runtime?.GetType().Name}, objective={ctl.Core.Runtime?.CurrentObjectiveId}");
            yield return new WaitForSeconds(3f);
            yield return Perf("operating room, surgeon view", 5f);
            Place(SurgeryAnchor.Find(AnchorIds.OperatingRoomRoot).position + new Vector3(-3.1f, 0, -2.9f), site.position); yield return Shot("07_integrated_operating_room");
            Place(SurgeryAnchor.Find(AnchorIds.OperatingRoomRoot).position + new Vector3(-1.9f, 0, 1.6f), site.position + Vector3.down * 0.25f); yield return Shot("08_surgical_patient_on_table");
            var stud = SurgeryAnchor.Find(AnchorIds.StudentOperatingPosition).position;
            Place(stud, SurgeryAnchor.Find(AnchorIds.InstrumentTable).position); yield return Shot("10_instrument_tray");

            SurgeryInstrumentTag Tool(string id) => FindObjectsByType<SurgeryInstrumentTag>(FindObjectsSortMode.None).First(t => t.instrumentId == id);
            SurgeryImplantTag Imp(string id) => FindObjectsByType<SurgeryImplantTag>(FindObjectsSortMode.None).First(t => t.implantId == id);
            bool Use(string tool, string target, bool power = false) { ad.PickupInstrument(Tool(tool)); if (power) mech.InstrumentController.SetToolActive(tool, true); var ok = mech.AnatomyController.HandleContact(tool, target); ad.DropHeldItem(); L("use", $"{tool} → {target}: {(ok ? "valid" : "rejected")}"); return ok; }
            bool Seat(string id) { var tag = Imp(id); ad.PickupImplant(tag); var t = GameObject.Find("PlacementTarget_" + id).transform; tag.transform.SetPositionAndRotation(t.position + t.up * 0.001f, t.rotation); ad.DropHeldItem(); var ok = Bus.Log.Any(e => e.id == SurgeryEventIds.ImplantPlacementCompleted && e.implantId == id); L("seat", id + ": " + (ok ? "seated" : "rejected")); return ok; }

            // Student holding an instrument, close to the knee.
            Place(stud, site.position); ad.PickupInstrument(Tool(InstrumentIds.SkinMarker)); yield return new WaitForSeconds(0.6f); yield return Shot("11_student_holding_instrument"); ad.DropHeldItem();
            Use(InstrumentIds.Scalpel, AnatomyTargetIds.SiteMarking);   // deliberate wrong tool
            yield return new WaitForSeconds(2.5f);
            Use(InstrumentIds.SkinMarker, AnatomyTargetIds.SiteMarking); Use(InstrumentIds.Scalpel, AnatomyTargetIds.IncisionLine);
            Use(InstrumentIds.Retractor, AnatomyTargetIds.JointCapsule);
            yield return new WaitForSeconds(1.6f);
            Place(stud + (site.position - stud).normalized * 0.45f, site.position); yield return Shot("09_knee_anatomy_closeup");
            // Hint request (H): the doctor explains the current step.
            ctl.Core.HandleRequest(new SurgeryIntentParser.Intent { helpRequest = true, addressed = SurgeryCharacterIds.Surgeon });
            yield return new WaitForSeconds(1.5f); Place(stud, Doctor.position + Vector3.up * 1.4f); yield return Shot("14_doctor_providing_assistance");
            // Assistant hand-over: completed only when the student takes it.
            var intent = new SurgeryIntentParser.Intent { addressed = SurgeryCharacterIds.Assistant };
            intent.action.characterId = SurgeryCharacterIds.Assistant; intent.action.action = SurgeryActionCatalog.HandInstrument; intent.action.instrumentId = InstrumentIds.FemoralCuttingGuide;
            bool handed = false; ctl.Core.Handoff.Completed += _ => handed = true;
            ctl.Core.HandleRequest(intent);
            yield return Until(() => ctl.Core.Handoff.PendingTool == InstrumentIds.FemoralCuttingGuide, 40f);
            Place(stud, HC.surgery.assistant.transform.position + Vector3.up * 1.2f); yield return new WaitForSeconds(0.4f); yield return Shot("14b_assistant_offers_instrument");
            L("handoff", $"offered={ctl.Core.Handoff.PendingTool} completed-before-grab={handed}");
            ad.PickupInstrument(Tool(InstrumentIds.FemoralCuttingGuide)); yield return null; yield return null;
            L("handoff", "completed after the student's grab = " + handed); ad.DropHeldItem();
            Place(stud, site.position);
            yield return Frames("gif_femoral_resection", 8, 0.35f, null);
            Use(InstrumentIds.FemoralCuttingGuide, AnatomyTargetIds.DistalFemur);
            yield return new WaitForSeconds(2.6f);   // the robot moves its arm over the knee (simulated guidance)
            yield return Shot("12a_guide_placed_robot_guidance");
            Use(InstrumentIds.OscillatingSaw, AnatomyTargetIds.DistalFemur, true);
            yield return new WaitForSeconds(0.6f);
            Place(stud + (site.position - stud).normalized * 0.45f, site.position); yield return Shot("12_anatomy_state_change_resected");
            Use(InstrumentIds.TibialCuttingGuide, AnatomyTargetIds.ProximalTibia); Use(InstrumentIds.OscillatingSaw, AnatomyTargetIds.ProximalTibia, true);
            Seat(ImplantIds.FemoralTrial); Seat(ImplantIds.TibialTrial);
            Seat(ImplantIds.FemoralComponent); Use(InstrumentIds.Impactor, AnatomyTargetIds.DistalFemur);
            yield return new WaitForSeconds(0.5f); yield return Shot("13_virtual_implant_interaction");
            Seat(ImplantIds.TibialTray); Seat(ImplantIds.PolyInsert); Use(InstrumentIds.Mallet, AnatomyTargetIds.ProximalTibia);
            Use(InstrumentIds.SkinStapler, AnatomyTargetIds.Wound);
            yield return Until(() => ctl.Core.Completed, 8f);
            yield return new WaitForSeconds(2.5f);
            Place(stud, site.position); yield return Shot("15_surgical_assessment");
            var r = ctl.Core.Result;
            L("assessment", r == null ? "none" : $"score {r.score}/{r.maxScore}, completed={r.sessionCompleted}, valid={r.validInteractions}, invalid={r.invalidInteractions}, hints={r.hintsGiven}");
            L("events", "bus: " + string.Join(", ", Bus.Log.GroupBy(e => e.id).Select(g => g.Key + "×" + g.Count())));

            // ---- back to the corridor (leaving ends the surgical session)
            Place(new Vector3(5.3f, 0, -3.9f), new Vector3(9f, 1.4f, -3.9f));
            yield return Until(() => HC.SessionRoom == HospitalRoomId.None, 8f);
            yield return new WaitForSeconds(4f);
            Place(new Vector3(6.6f, 0, -4.5f), Doctor.position + Vector3.up * 1.4f); yield return Shot("16_return_to_corridor");

            // ---- Room 01 (the original clinical case)
            var go1 = VoiceInteractionManager.Instance.Submit(Persona.Instructor, "رجعني عالغرفة الأولى");
            yield return Until(() => go1.IsCompleted, 5f);
            yield return FollowDoctor(() => HC.GuidingTo == HospitalRoomId.None, 45f);
            L("clinical", "at Room 01 door; session state " + SessionManager.Instance.State);
            yield return new WaitForSeconds(3f);
            var door = FindAnyObjectByType<MedSimAI.Instruments.HospitalDoor>(); if (door) door.OpenBy(85f);
            yield return new WaitForSeconds(1.5f);
            Place(new Vector3(1.6f, 0, -1.7f), new Vector3(0.3f, 1.0f, 0.9f));
            yield return Until(() => HC.SessionRoom == HospitalRoomId.Room01, 6f);
            yield return new WaitForSeconds(3f);
            yield return Perf("Room 01 (clinical)", 5f);
            yield return Shot("05_clinical_examination_room");
            CharacterActionRouter.Execute(Persona.Patient, new LiveAction { type = "raise_arm", side = "left", status = "accepted" });
            yield return new WaitForSeconds(3f);
            Place(new Vector3(1.4f, 0, 0.2f), new Vector3(0.3f, 1.0f, 1.1f)); yield return Shot("06_clinical_patient_interaction");
            L("clinical", "tracker events: " + ClinicalScenarioManager.Instance.Session.Tracker.Log.Count + ", surgical ids present: " + ClinicalScenarioManager.Instance.Session.Tracker.Log.Any(e => (e.eventId + e.detail).Contains("knee_r")));
            Place(new Vector3(3.5f, 0, -3.8f), new Vector3(9f, 1.4f, -3.8f));
            yield return Until(() => HC.SessionRoom == HospitalRoomId.None, 8f);
            L("hospital", "records: " + string.Join(" | ", HC.Records.Select(x => $"{x.room} {x.scenarioId} {x.outcome} {x.scorePercent}% {x.Minutes:0.0} min")));
            L("hospital", "log tail: " + string.Join(" ; ", HC.Log.Skip(Math.Max(0, HC.Log.Count - 12))));
            L("done", "hospital self-test finished");
            yield return new WaitForSecondsRealtime(1f);
            Application.Quit();
        }
    }
}
