// Desktop Mode end-to-end check in Play mode with SIMULATED KEYBOARD/MOUSE INPUT (Input System state events) and real Live AI.
//   unity command script eval -- --file AgentScripts/DesktopE2E.cs --timeout 600
using UnityEngine; using UnityEngine.InputSystem; using UnityEngine.InputSystem.LowLevel; using System; using System.IO; using System.Linq; using System.Threading.Tasks; using System.Collections.Generic;
using MedSimAI.Core; using MedSimAI.AI; using MedSimAI.Voice; using MedSimAI.Instruments; using MedSimAI.Characters; using MedSimAI.Clinical; using MedSimAI.XR;

string outDir = "D:/Medical-Test/My project/Screenshots/DesktopMode/"; Directory.CreateDirectory(outDir);
var log = new List<string>(); float T0 = Time.realtimeSinceStartup;
void L(string k, string m) { var s = $"{(Time.realtimeSinceStartup - T0):0.0}s [{k}] {m}"; log.Add(s); Debug.Log("[DesktopE2E] " + s); }
async Task W(float s) { float t = Time.realtimeSinceStartup + s; while (Time.realtimeSinceStartup < t) await Task.Yield(); }
async Task<bool> Until(Func<bool> c, float timeout) { float t = Time.realtimeSinceStartup + timeout; while (!c()) { if (Time.realtimeSinceStartup > t) return false; await Task.Yield(); } return true; }
// Automation: virtual keyboard/mouse devices (the Editor disables physical devices while it has no OS focus). The game reads Keyboard.current / Mouse.current exactly as with real devices.
var prevBg = InputSystem.settings.backgroundBehavior; InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus; // automation only: Editor has no OS focus
var kb = InputSystem.AddDevice<Keyboard>("SimKeyboard"); kb.MakeCurrent(); var mouse = InputSystem.AddDevice<Mouse>("SimMouse"); mouse.MakeCurrent();
void Keys(params Key[] k) { InputSystem.QueueStateEvent(kb, new KeyboardState(k)); }
async Task Press(Key k, float hold = 0.1f) { Keys(k); await W(hold); Keys(); await W(0.1f); }
async Task Click(int button) { InputSystem.QueueStateEvent(mouse, new MouseState { buttons = (ushort)(1 << button) }); await W(0.08f); InputSystem.QueueStateEvent(mouse, new MouseState()); await W(0.15f); }
void Shot(string name) {
  var cam = Camera.main; int Wd = 2560, Ht = 1440;
  var rt = new RenderTexture(Wd, Ht, 24, RenderTextureFormat.DefaultHDR) { antiAliasing = 4 }; var prev = cam.targetTexture; cam.targetTexture = rt; cam.Render(); cam.targetTexture = prev;
  var ldr = RenderTexture.GetTemporary(Wd, Ht, 0, RenderTextureFormat.ARGB32); Graphics.Blit(rt, ldr);
  RenderTexture.active = ldr; var tex = new Texture2D(Wd, Ht, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, Wd, Ht), 0, 0); tex.Apply(); RenderTexture.active = null;
  File.WriteAllBytes(outDir + name + ".png", tex.EncodeToPNG()); rt.Release(); RenderTexture.ReleaseTemporary(ldr); L("shot", name);
}
var spoken = new List<string>();
Action<CharacterSpeechController, string> onSub = (c, t) => { var src = c.GetComponent<AudioSource>(); bool live = src && src.clip && src.clip.name.StartsWith("live_"); spoken.Add(c.displayName); L("speech", $"{c.displayName} {(live ? "[live audio]" : "[TEXT ONLY]")}: {t}"); };
CharacterSpeechController.SubtitleStarted += onSub;
var S = SessionManager.Instance; var M = ClinicalScenarioManager.Instance; var V = VoiceInteractionManager.Instance;

await Until(() => DesktopPlayerController.Active, 15);
var dpc = UnityEngine.Object.FindAnyObjectByType<DesktopPlayerController>();
var rig = UnityEngine.Object.FindAnyObjectByType<XRPlayerController>();
L("mode", $"display={DisplayModeManager.Mode} ({DisplayModeManager.Instance.Detail}) desktop={DesktopPlayerController.Active} AI={AIProviderManager.Instance.ModeLabel} renderScale={((UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset)UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline).renderScale} eye={(Camera.main.transform.position.y - rig.transform.position.y):0.00}m");
await Until(() => spoken.Count > 0, 40); await Until(() => !V.Busy, 40);
Shot("D01_corridor_start");
// walk to the door with W and open it with E
var door = UnityEngine.Object.FindAnyObjectByType<HospitalDoor>();
var handle = door.handle ? door.handle.position : door.leaf.position;
dpc.LookAt(new Vector3(handle.x, handle.y, handle.z));
Vector3 p0 = rig.transform.position;
Keys(Key.W); float tw = Time.realtimeSinceStartup; while (Vector3.Distance(new Vector3(rig.transform.position.x, 0, rig.transform.position.z), new Vector3(handle.x, 0, handle.z)) > 1.0f && Time.realtimeSinceStartup - tw < 8) { dpc.LookAt(handle); await Task.Yield(); } Keys();
L("walk", $"moved {Vector3.Distance(p0, rig.transform.position):0.00} m with W toward the door");
dpc.LookAt(handle); await W(0.3f); Shot("D02_door_prompt");
await Press(Key.E); await Until(() => door.Angle > 60, 5); L("door", $"E opened door: angle={door.Angle:0} event={M.Session.Tracker.Has(ClinicalEvents.DoorOpened)}");
await W(0.8f); Shot("D03_door_open");
// walk in (Shift run)
var bed = GameObject.Find("Hospital Bed").transform.position;
var leafC = door.leaf.GetComponentInChildren<Collider>().bounds.center; var doorFrame = door.hinge.position; // walk straight through the doorway, then to the bedside
var doorway = new Vector3(doorFrame.x - 0.5f, 0, doorFrame.z + 0.9f); var stand = new Vector3(1.15f, 0, 0.55f);
var navPath = new UnityEngine.AI.NavMeshPath(); var wps = new List<Vector3> { new Vector3(doorFrame.x - 0.5f, 0, doorFrame.z - 0.6f), doorway };
if (UnityEngine.AI.NavMesh.SamplePosition(stand, out var sh, 1.0f, UnityEngine.AI.NavMesh.AllAreas) && UnityEngine.AI.NavMesh.CalculatePath(doorway, sh.position, UnityEngine.AI.NavMesh.AllAreas, navPath)) { wps.AddRange(navPath.corners.Skip(1)); stand = sh.position; } else wps.Add(stand);
foreach (var wp in wps) { tw = Time.realtimeSinceStartup; Keys(Key.W, Key.LeftShift);
  while (Vector3.Distance(new Vector3(rig.transform.position.x, 0, rig.transform.position.z), wp) > 0.3f && Time.realtimeSinceStartup - tw < 10) { dpc.LookAt(new Vector3(wp.x, Camera.main.transform.position.y, wp.z)); await Task.Yield(); }
  Keys(); L("walk", $"waypoint {wp:F2} reached at {rig.transform.position:F2}"); } L("walk", $"in room at {rig.transform.position:F2} entered={M.Session.Tracker.Has(ClinicalEvents.EnteredRoom)}");
var patient = UnityEngine.Object.FindAnyObjectByType<PatientInteractionController>();
dpc.LookAt(patient.Focus.position); await W(0.6f); Shot("D04_patient_view");
// talk (same live pipeline as hold-T; microphone audio is not available to automation)
await V.Submit(Persona.Patient, "Hello Mr. Reyes, I'm a medical student. May I ask you a few questions and examine you?"); await Until(() => spoken.Count >= 2, 30); await Until(() => !V.Busy, 30);
await V.Submit(Persona.Patient, "Where exactly is the pain?"); await W(1); await Until(() => !V.Busy, 30);
// pick up the cuff with left click, aim at the upper arm, apply with left click
var cuff = UnityEngine.Object.FindAnyObjectByType<BloodPressureController>();
dpc.LookAt(cuff.transform.position); await W(0.3f); await Click(0);
L("grab", $"cuff held={cuff.IsHeld} desktopHeld={cuff.DesktopHeld}");
var zones = UnityEngine.Object.FindObjectsByType<PlacementZone>(FindObjectsSortMode.None);
var wrong = zones.First(z => z.zoneId == "CHEST_MITRAL"); dpc.LookAt(wrong.transform.position); await W(0.4f); await Click(0); await W(1.5f); await Click(0);
L("wrong", $"ZONE_REJECTED={M.Session.Tracker.Count(ClinicalEvents.ZoneRejected)}");
var arm = zones.First(z => z.zoneId == "ARM_UPPER_L"); dpc.LookAt(arm.transform.position); await W(0.5f); Shot("D05_cuff_aim_arm");
await Click(0); L("apply", $"cuff attached={cuff.Attached}");
await Until(() => M.Session.Tracker.Has("EXAM:EX_BP"), 12); L("exam", "EX_BP=" + M.Session.Tracker.Has("EXAM:EX_BP"));
await Until(() => !V.Busy, 30); await W(1); Shot("D06_after_cuff");
// stethoscope: pick up, hold on the mitral area to listen
var st = UnityEngine.Object.FindAnyObjectByType<StethoscopeController>();
dpc.LookAt(st.transform.position); await W(0.3f); await Click(0);
var mitral = zones.First(z => z.zoneId == "CHEST_MITRAL"); dpc.LookAt(mitral.transform.position); await W(2.2f);
L("ausc", "EX_AUSC_HEART=" + M.Session.Tracker.Has("EXAM:EX_AUSC_HEART")); Shot("D07_stethoscope");
await Click(1); L("drop", $"stethoscope back on trolley held={st.IsHeld}");
// ECG: look at the cart, E to record, keep looking to review
var ecg = UnityEngine.Object.FindAnyObjectByType<ECGMachineController>();
dpc.LookAt(ecg.transform.position + Vector3.up * 0.9f); await W(0.4f); await Press(Key.E); await W(2.2f);
L("ecg", $"recorded={M.Session.Tracker.Has("INV:INV_ECG")} reviewed={M.Session.Tracker.Has(ClinicalEvents.EcgReviewed)}"); Shot("D08_ecg");
await Until(() => !V.Busy, 30);
// escalate to the doctor, discussion, debrief (same as VR)
var doc = UnityEngine.Object.FindAnyObjectByType<MedicalInstructorController>(); dpc.LookAt(doc.Focus.position); await W(0.5f);
await V.Submit(Persona.Instructor, "Are you allergic to anything?"); await W(0.2f);
await V.Submit(Persona.Patient, "Are you allergic to any medicines?"); await Until(() => !V.Busy, 30);
await V.Submit(Persona.Instructor, "The ECG shows ST elevation in II, III and aVF. I want to call cardiology and activate the cath lab now."); await Until(() => S.State == AppState.Discussion, 40);
L("flow", "state=" + S.State + " condition=" + M.Session.Patient.ConditionId); Shot("D09_doctor_discussion");
var answers = new Queue<string>(new[] { "Crushing central chest pain going to his left arm and jaw, and he smokes with high cholesterol.", "ST elevation in II, III and aVF with reciprocal depression.", "An inferior STEMI.", "Urgent cath lab for reperfusion without waiting for troponin." });
while (S.State == AppState.Discussion && answers.Count > 0) { int n = spoken.Count; await Until(() => spoken.Count > n || S.State != AppState.Discussion, 40); await Until(() => !V.Busy, 40); if (S.State != AppState.Discussion) break; await V.Submit(Persona.Instructor, answers.Dequeue()); await W(0.3f); }
await Until(() => S.LastSpokenDebrief != null, 90); await W(1.5f);
L("debrief", S.LastSpokenDebrief ?? "(none)"); L("cursor", $"lock={Cursor.lockState} (debrief report should be clickable)"); Shot("D10_debrief_report");
// pause with Esc
await Until(() => !V.Busy, 60);
CharacterSpeechController.SubtitleStarted -= onSub;
InputSystem.RemoveDevice(kb); InputSystem.RemoveDevice(mouse); InputSystem.settings.backgroundBehavior = prevBg;
File.WriteAllLines(outDir + "desktop_e2e_log.txt", log);
return "done " + log.Count + " log lines";
