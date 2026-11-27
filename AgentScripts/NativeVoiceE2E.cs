// Native voice end-to-end check in Play mode (Desktop Mode, real Live AI through the proxy; billed, small).
//   unity command script eval -- --file AgentScripts/NativeVoiceE2E.cs --timeout 600
// AUTOMATED AUDIO, NOT A HUMAN: utterances are SYNTHETIC student WAV clips (Backend/medsim-proxy/data/student-audio, generated
// by tools/make-student-audio.mjs). They are injected into VoiceInteractionManager.SubmitVoice — the exact function the
// microphone capture calls after push-to-talk/VAD — so everything after the microphone is the production path.
// Writes Screenshots/NativeVoice/native_voice_log.txt.
using UnityEngine; using System; using System.IO; using System.Linq; using System.Threading.Tasks; using System.Collections.Generic;
using MedSimAI.Core; using MedSimAI.AI; using MedSimAI.Voice; using MedSimAI.Characters; using MedSimAI.Clinical; using MedSimAI.XR; using MedSimAI.Instruments;

string outDir = "D:/Medical-Test/My project/Screenshots/NativeVoice/"; Directory.CreateDirectory(outDir);
string audioDir = "D:/Medical-Test/My project/Backend/medsim-proxy/data/student-audio/";
var log = new List<string>(); float T0 = Time.realtimeSinceStartup;
void L(string k, string m) { var s = $"{(Time.realtimeSinceStartup - T0):0.0}s [{k}] {m}"; log.Add(s); Debug.Log("[NativeVoiceE2E] " + s); }
async Task W(float s) { float t = Time.realtimeSinceStartup + s; while (Time.realtimeSinceStartup < t) await Task.Yield(); }
async Task<bool> Until(Func<bool> c, float timeout) { float t = Time.realtimeSinceStartup + timeout; while (!c()) { if (Time.realtimeSinceStartup > t) return false; await Task.Yield(); } return true; }
float[] Wav(string name) { var b = File.ReadAllBytes(audioDir + name + ".wav"); int n = (b.Length - 44) / 2; var f = new float[n]; for (int i = 0; i < n; i++) f[i] = (short)(b[44 + 2 * i] | (b[45 + 2 * i] << 8)) / 32768f; return f; }
void Shot(string name) {
  var cam = Camera.main; int Wd = 1920, Ht = 1080;
  var rt = new RenderTexture(Wd, Ht, 24, RenderTextureFormat.DefaultHDR) { antiAliasing = 4 }; var prev = cam.targetTexture; cam.targetTexture = rt; cam.Render(); cam.targetTexture = prev;
  var ldr = RenderTexture.GetTemporary(Wd, Ht, 0, RenderTextureFormat.ARGB32); Graphics.Blit(rt, ldr);
  RenderTexture.active = ldr; var tex = new Texture2D(Wd, Ht, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, Wd, Ht), 0, 0); tex.Apply(); RenderTexture.active = null;
  File.WriteAllBytes(outDir + name + ".png", tex.EncodeToPNG()); rt.Release(); RenderTexture.ReleaseTemporary(ldr); L("shot", name);
}
var S = SessionManager.Instance; var M = ClinicalScenarioManager.Instance; var V = VoiceInteractionManager.Instance; var AI = AIProviderManager.Instance;
var subs = new List<(float t, string who, string text, bool streamed)>(); string lastHeard = null; ConversationTurn lastTurn = null; string subtitleNow = "";
CharacterSpeechController.SubtitleStarted += (c, t) => { var src = c.GetComponent<AudioSource>(); bool st = src && src.clip && src.clip.name.StartsWith("live_"); subs.Add((Time.realtimeSinceStartup - T0, c.displayName, t, st)); subtitleNow = t; L("subtitle", $"{c.displayName} {(st ? "[streamed live audio " + src.clip.name + "]" : "[TEXT ONLY]")}: {t}"); };
V.TranscriptReady += t => { lastHeard = t; L("transcript", "You: " + t); };
V.TurnReady += t => lastTurn = t;
V.Error += e => L("error", e);
var results = new List<string>();

async Task Turn(string label, Persona p, string clip, Func<string> check = null) {
  lastHeard = null; lastTurn = null; int before = subs.Count;
  var samples = Wav(clip);
  L("student-audio", $"{label}: SYNTHETIC clip {clip}.wav ({samples.Length / 24000f:0.00}s) → {p}");
  V.SubmitVoice(p, samples); // same entry point as the microphone capture
  bool ok = await Until(() => subs.Count > before, 30);
  await Until(() => V.LastFirstAudioMs > 0 && V.State != VoiceState.Thinking, 10);
  float understood = V.LastUnderstoodMs, first = V.LastFirstAudioMs;
  await Until(() => V.State == VoiceState.Idle, 40);
  var extra = check?.Invoke() ?? "";
  var line = $"{label}: heard=\"{lastHeard}\" reply=\"{lastTurn?.responseText}\" source={lastTurn?.note} understood={understood:0}ms firstAudio={first:0}ms subtitleShown={ok} {extra}";
  results.Add(line); L("result", line);
}

await Until(() => DesktopPlayerController.Active, 20);
V.SetMicMode(MicMode.PushToTalk); // deterministic turns; hands-free echo soak at the end
L("mode", $"display={DisplayModeManager.Mode} desktop={DesktopPlayerController.Active} AI={AI.ModeLabel} live={V.LiveOn} mic={V.Mic} micDevices=[{string.Join(", ", Microphone.devices)}]");
bool fresh = S.State == AppState.MainMenu; if (fresh) S.StartClinicalSession(true); else L("info", "session already started (state " + S.State + "), welcome already spoken");
if (fresh && !await Until(() => subs.Count > 0, 60)) { L("error", "no welcome line within 60 s"); return string.Join(" | ", log); }
if (fresh) L("welcome", $"first line streamed={subs[0].streamed} after {subs[0].t:0.0}s");
await Until(() => !V.Busy, 60);
// skip the walk: put the student in the room and let the session run
var rig = UnityEngine.Object.FindAnyObjectByType<XRPlayerController>();
var door = UnityEngine.Object.FindAnyObjectByType<HospitalDoor>(); if (door) door.OpenBy(85);
await W(1.5f);
rig.origin.transform.position = new Vector3(1.15f, 0, 0.55f); rig.origin.transform.rotation = Quaternion.Euler(0, -90, 0);
await W(2f); await Until(() => !V.Busy, 40);

await Turn("EN patient intro", Persona.Patient, "student_en_intro");
await Turn("EN patient allergy", Persona.Patient, "student_en_allergy", () => $"factAllergy={M.Session.Tracker.Log.Any(e => e.eventId.Contains("ALLERG"))}");
Shot("V01_patient_voice_turn");
await Turn("EN doctor ECG", Persona.Instructor, "student_en_dr_ecg", () => $"ecgRequested={M.Session.Tracker.Has("INV:INV_ECG")}");
await Turn("EN doctor MI impression", Persona.Instructor, "student_en_dr_mi");
await Turn("EN doctor STEMI question (must not confirm)", Persona.Instructor, "student_en_stemi_q", () => $"escalationRecorded={M.Session.Tracker.Has(ClinicalEvents.EscalationRequested)}");
await Turn("EN doctor escalation", Persona.Instructor, "student_en_escalate", () => $"escalationRecorded={M.Session.Tracker.Has(ClinicalEvents.EscalationRequested)}");

// Interruption: the doctor is answering; the student starts speaking (barge-in calls Interrupt) → speech stops, request aborted.
{
  var doc = UnityEngine.Object.FindObjectsByType<CharacterSpeechController>(FindObjectsSortMode.None).First(c => c.persona == Persona.Instructor);
  int before = subs.Count; V.SubmitVoice(Persona.Instructor, Wav("student_en_dr_hint"));
  await Until(() => subs.Count > before && doc.IsSpeaking, 30); await W(0.7f);
  float t = Time.realtimeSinceStartup; V.Interrupt();
  bool stopped = await Until(() => !doc.IsSpeaking, 2);
  var line = $"Interruption: doctor speaking → student barge-in → speech stopped={stopped} in {(Time.realtimeSinceStartup - t) * 1000:0}ms, voiceState={V.State}";
  results.Add(line); L("result", line); await W(1f);
}

// Arabic (Palestinian) — switch the session language, one patient turn, then back.
AI.SetLanguage("ar"); await W(0.5f);
await Turn("AR patient onset", Persona.Patient, "student_ar_onset");
AI.SetLanguage("en");

// Mic modes (real microphone device opens only in memory; nothing is recorded to disk)
V.SetMicMode(MicMode.HandsFree); await W(1.5f);
L("mic", $"hands-free: micRecording={(Microphone.devices.Length > 0 && Microphone.IsRecording(Microphone.devices[0]))} level={V.MicLevel:0.0000}");
V.SetMicMode(MicMode.Muted); await W(0.5f);
L("mic", $"muted: micRecording={(Microphone.devices.Length > 0 && Microphone.IsRecording(Microphone.devices[0]))}");
// Hands-free echo soak (REAL laptop microphone + laptop speakers, no human speaking): characters talk; count spurious captures.
V.SetMicMode(MicMode.HandsFree); await W(2f);
int capturesBefore = 0; V.UtteranceCaptured += _ => capturesBefore++;
int transcriptsBefore = log.Count(x => x.Contains("[transcript]"));
await Turn("Hands-free soak: patient pain question", Persona.Patient, "student_en_allergy");
await Turn("Hands-free soak: doctor hello", Persona.Instructor, "student_en_dr_hello");
await W(12f);
var soak = $"Hands-free echo soak (mic {(V.MicDevice ?? "system default")}): spurious captures from speaker echo/room noise={capturesBefore}, extra transcripts={log.Count(x => x.Contains("[transcript]")) - transcriptsBefore - 2}";
results.Add(soak); L("result", soak);
V.SetMicMode(MicMode.PushToTalk); PlayerPrefs.DeleteKey(VoiceInteractionManager.MicModePref);
Shot("V02_after_voice_turns");
File.WriteAllLines(outDir + "native_voice_log.txt", log);
return string.Join("\n", results);
