// Desktop-driven END-TO-END Live AI session (Test G/E/F). Run in Play mode via:
//   unity command script eval -- --file AgentScripts/LiveE2E.cs --timeout 900
// Uses REAL OpenAI calls through the MedSim proxy (billed against the in-app budget). Requires the proxy running and the
// Unity client config (Backend/medsim-proxy/tools/write-unity-config.mjs). Writes Screenshots/CompleteRedesign/live/e2e_log.json.
using UnityEngine; using UnityEngine.Networking; using System; using System.IO; using System.Linq; using System.Text; using System.Threading.Tasks; using System.Collections.Generic;
using MedSimAI.Core; using MedSimAI.AI; using MedSimAI.Voice; using MedSimAI.Instruments; using MedSimAI.Characters; using MedSimAI.Clinical;

string outDir = "D:/Medical-Test/My project/Screenshots/CompleteRedesign/live/"; Directory.CreateDirectory(outDir);
var log = new List<string>(); float T0 = Time.realtimeSinceStartup;
void L(string kind, string msg) { var line = $"{(Time.realtimeSinceStartup - T0):0.0}s [{kind}] {msg}"; log.Add(line); Debug.Log("[E2E] " + line); }
async Task W(float s) { float t = Time.realtimeSinceStartup + s; while (Time.realtimeSinceStartup < t) await Task.Yield(); }
async Task<bool> Until(Func<bool> c, float timeout) { float t = Time.realtimeSinceStartup + timeout; while (!c()) { if (Time.realtimeSinceStartup > t) return false; await Task.Yield(); } return true; }
void Shot(string name) {
  var cam = Camera.main; if (!cam) return; int Wd = 1920, Ht = 1080;
  var rt = new RenderTexture(Wd, Ht, 24) { antiAliasing = 4 }; var prev = cam.targetTexture; cam.targetTexture = rt; cam.Render(); cam.targetTexture = prev;
  RenderTexture.active = rt; var tex = new Texture2D(Wd, Ht, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, Wd, Ht), 0, 0); tex.Apply(); RenderTexture.active = null;
  File.WriteAllBytes(outDir + name + ".png", tex.EncodeToPNG()); rt.Release(); L("shot", name);
}
// Record every spoken line: who, text, whether audio was runtime-generated, and its length.
var spoken = new List<(float t, string who, string text, string clip, float len)>();
Action<CharacterSpeechController, string> onSub = (c, text) => {
  var src = c.GetComponent<AudioSource>(); var clip = src ? src.clip : null;
  bool live = clip && clip.name.StartsWith("live_") && src.isPlaying;
  spoken.Add((Time.realtimeSinceStartup - T0, c.displayName, text, live ? clip.name : "(text-only)", live ? clip.length : 0));
  L("speech", $"{c.displayName}: {(live ? $"[{clip.name} {clip.length:0.0}s]" : "[TEXT ONLY]")} {text}");
};
CharacterSpeechController.SubtitleStarted += onSub;
var V = VoiceInteractionManager.Instance; var AI = AIProviderManager.Instance; var M = ClinicalScenarioManager.Instance; var S = SessionManager.Instance;
V.Error += e => L("error", e);
async Task<bool> QuietWait(float timeout = 40) { await W(0.3f); return await Until(() => !V.Busy, timeout); }
async Task Ask(Persona p, string text) {
  int before = spoken.Count; float t0 = Time.realtimeSinceStartup;
  L("student->" + p, text);
  await V.Submit(p, text);
  await Until(() => spoken.Count > before, 40);
  L("latency", $"{p} reply started after {(Time.realtimeSinceStartup - t0):0.00}s");
  await QuietWait();
}
async Task<AudioClip> LoadWav(string path) {
  using var www = UnityWebRequestMultimedia.GetAudioClip("file:///" + path.Replace("\\", "/"), AudioType.WAV);
  var op = www.SendWebRequest(); while (!op.isDone) await Task.Yield();
  return DownloadHandlerAudioClip.GetContent(www);
}
async Task MicTurn(Persona p, string wavPath) {
  var clip = await LoadWav(wavPath); float t0 = Time.realtimeSinceStartup;
  var text = await AI.Live.Transcribe(clip, default);
  L("stt", $"{(Time.realtimeSinceStartup - t0):0.00}s → \"{text}\"");
  await Ask(p, text);
}

L("info", $"mode={AI.ModeLabel} configured={AI.Live.Configured} lang={AI.Config.language}");
// ---------- Step 1: corridor welcome (autoStart)
if (S.State == AppState.MainMenu) S.StartClinicalSession(true);
await Until(() => spoken.Any(s => s.who.Contains("Hassan")), 45); await QuietWait();
Shot("G01_corridor_welcome");
// ---------- Step 2: door + doctor walks in
var door = UnityEngine.Object.FindAnyObjectByType<HospitalDoor>();
if (door) { door.OpenBy(85); await Until(() => M.Session.Tracker.Has(ClinicalEvents.DoorOpened), 10); L("door", $"opened angle={door.Angle:0}"); await W(1.2f); Shot("G02_door_open"); }
var dm = UnityEngine.Object.FindAnyObjectByType<DoctorMovement>();
if (dm) { await Until(() => dm.IsMoving, 5); float tw = Time.realtimeSinceStartup; await Until(() => !dm.IsMoving, 30); L("doctor", $"walked in {(Time.realtimeSinceStartup - tw):0.0}s, now at {dm.transform.position}"); }
var rig = UnityEngine.Object.FindAnyObjectByType<MedSimAI.XR.XRPlayerController>();
rig.origin.transform.position = new Vector3(1.15f, 0, 0.55f); rig.origin.transform.rotation = Quaternion.Euler(0, -90, 0);
await W(1.0f); L("room", "entered=" + M.Session.Tracker.Has(ClinicalEvents.EnteredRoom)); Shot("G03_in_room");
// ---------- Step 3: meet the patient (real STT path from a recorded utterance)
await MicTurn(Persona.Patient, "D:/Medical-Test/My project/Backend/medsim-proxy/data/student-audio/student_en_intro.wav");
Shot("G04_patient_conversation");
foreach (var q in new[] { "Is it alright if I ask you some questions and examine you?", "Can you describe the pain for me?", "Does the pain spread anywhere?", "Have you had anything like this before?", "Are you allergic to any medicines?", "Do you smoke?", "Do you take any regular medication?" })
  await Ask(Persona.Patient, q);
// ---------- Step 4/5: examination incl. a deliberate wrong placement → supervisor intervention
var zones = UnityEngine.Object.FindObjectsByType<PlacementZone>(FindObjectsSortMode.None);
var cuff = UnityEngine.Object.FindAnyObjectByType<BloodPressureController>(); var ox = UnityEngine.Object.FindAnyObjectByType<PulseOximeterController>();
int ivBefore = M.Director.History.Count;
cuff.PlaceInZone(zones.First(z => z.zoneId == "FINGER_L")); await W(1.5f); cuff.PlaceInZone(zones.First(z => z.zoneId == "CHEST_MITRAL"));
await Until(() => M.Director.History.Count > ivBefore, 12);
L("supervisor", "interventions: " + string.Join(", ", M.Director.History.Skip(ivBefore).Select(h => h.rule.id + "/L" + h.level)));
await QuietWait(); Shot("G05_supervisor_intervention");
cuff.PlaceInZone(zones.First(z => z.zoneId == "ARM_UPPER_L")); ox.PlaceInZone(zones.First(z => z.zoneId == "FINGER_L"));
M.Session.Record(ClinicalEvents.InstrumentOnPatient, "stethoscope@CHEST_MITRAL"); M.Session.PerformExam("stethoscope", "CHEST_MITRAL"); M.Session.PerformExam("stethoscope", "CHEST_LUNG_L");
await W(9f); Shot("G06_examination");
int quietCheck = M.Director.History.Count;
// ---------- investigations + reasoning
await Ask(Persona.Instructor, "Could we get a 12-lead ECG on him please?");
var ecg = UnityEngine.Object.FindAnyObjectByType<ECGMachineController>(); if (!M.Session.Tracker.Has("INV:INV_ECG")) ecg.Record(); await W(1); ecg.MarkReviewed(); Shot("G07_ecg");
L("supervisor", $"silent after correct actions: {M.Director.History.Count == quietCheck} (history {quietCheck}→{M.Director.History.Count})");
await Ask(Persona.Instructor, "Is this a STEMI?");
L("engine", $"diagnosticQuestion={M.Session.Tracker.Has(ClinicalEvents.DiagnosticQuestion)} escalated={M.Session.Tracker.Has(ClinicalEvents.EscalationRequested)}");
await Ask(Persona.Instructor, "I want to call the cardiology registrar and activate the cath lab now.");
L("engine", $"escalated={M.Session.Tracker.Has(ClinicalEvents.EscalationRequested)} state={M.Session.Patient.ConditionId}");
// ---------- Step 6: discussion (auto after handover)
await Until(() => S.State == AppState.Discussion, 40); L("flow", "state=" + S.State);
var answers = new Queue<string>(new[] {
  "He has crushing central chest pain radiating to his left arm and jaw with sweating and nausea; he smokes, has high blood pressure and cholesterol, and his father had a heart attack.",
  "The ECG shows ST elevation in II, III and aVF with reciprocal ST depression in I and aVL.",
  "I think this is an inferior STEMI.",
  "He needs urgent cardiology for the cath lab and reperfusion, and we should not wait for the troponin." });
while (S.State == AppState.Discussion && answers.Count > 0) {
  int n = spoken.Count; await Until(() => spoken.Count > n || S.State != AppState.Discussion, 40); await QuietWait();
  if (S.State != AppState.Discussion) break;
  await Ask(Persona.Instructor, answers.Dequeue());
}
// ---------- Step 7: spoken debrief
await Until(() => S.State == AppState.Debrief, 60); await Until(() => S.LastSpokenDebrief != null, 90); await QuietWait(60);
L("debrief", S.LastSpokenDebrief ?? "(none)"); Shot("G08_debrief");
var r = S.LastResult; L("assessment", $"observable {r.observableScore}/{r.observableMax} communication {r.communicationScore}/{r.communicationMax} missedCritical={r.missedCritical.Count} aiInterpreted={r.criteria.Count(c => c.met && c.viaAIIntent)}");
L("events", string.Join(" ", M.Session.Tracker.Log.Select(e => e.eventId).Distinct()));
// ---------- Test E: Arabic + Test F: deterioration path (new session)
AI.SetLanguage("ar"); S.Restart();
await Until(() => spoken.Count(s => s.who.Contains("Hassan")) >= 2 && !V.Busy, 60); await QuietWait();
if (door) { door.OpenBy(85); await W(1); }
rig.origin.transform.position = new Vector3(1.15f, 0, 0.55f);
await MicTurn(Persona.Patient, "D:/Medical-Test/My project/Backend/medsim-proxy/data/student-audio/student_ar_onset.wav");
Shot("E01_arabic_conversation");
await Ask(Persona.Patient, "بتوخذ أي أدوية؟");
int ivB = M.Director.History.Count;
M.AdvanceScenarioTime(560f); // authored delayed-care path: presentation → deterioration
L("state", "condition=" + M.Session.Patient.ConditionId + " path=" + string.Join(">", M.Session.Patient.ConditionPath) + $" HR={M.Session.Patient.Vitals.heartRate} SpO2={M.Session.Patient.Vitals.spo2}");
await Until(() => M.Director.History.Skip(ivB).Any(h => h.rule.id == "I_DETERIORATION"), 20); await QuietWait(60);
L("supervisor", "after deterioration: " + string.Join(", ", M.Director.History.Skip(ivB).Select(h => h.rule.id)));
await Ask(Persona.Patient, "كيف حاسس هلأ؟");
Shot("F01_deterioration");
AI.SetLanguage("en");
CharacterSpeechController.SubtitleStarted -= onSub;
var sb = new StringBuilder(); foreach (var l in log) sb.AppendLine(l);
sb.AppendLine("--- spoken lines: " + spoken.Count + ", runtime-generated audio: " + spoken.Count(s => s.clip.StartsWith("live_")) + ", text-only: " + spoken.Count(s => !s.clip.StartsWith("live_")));
File.WriteAllText(outDir + "e2e_log.txt", sb.ToString());
return $"done: {spoken.Count} lines, live audio {spoken.Count(s => s.clip.StartsWith("live_"))}, text-only {spoken.Count(s => !s.clip.StartsWith("live_"))}";
