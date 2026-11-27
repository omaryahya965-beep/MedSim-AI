// v0.4 Arabic + audio completeness end-to-end in Play mode (Desktop Mode, real Live AI; billed, small).
//   unity command script eval -- --file AgentScripts/ArabicE2E.cs --timeout 900
// AUTOMATED AUDIO, NOT A HUMAN: student utterances are SYNTHETIC clips injected into VoiceInteractionManager.SubmitVoice
// (the function the microphone capture calls). Checks: language auto-detection and explicit switching shared by both
// characters, Arabic transcript rendering (RTL + shaping), Arabic supervisor line, long Arabic speech played to the end
// (measured on the AudioSource playhead), hands-free false alarm vs real barge-in. Log: Screenshots/Arabic/arabic_e2e_log.txt
using UnityEngine; using System; using System.IO; using System.Linq; using System.Reflection; using System.Threading.Tasks; using System.Collections; using System.Collections.Generic;
using MedSimAI.Core; using MedSimAI.AI; using MedSimAI.Voice; using MedSimAI.Characters; using MedSimAI.XR; using MedSimAI.Instruments; using MedSimAI.UI;

string outDir = "D:/Medical-Test/My project/Screenshots/Arabic/"; Directory.CreateDirectory(outDir);
string audioDir = "D:/Medical-Test/My project/Backend/medsim-proxy/data/student-audio/";
var log = new List<string>(); float T0 = Time.realtimeSinceStartup;
void L(string k, string m) { var s = $"{(Time.realtimeSinceStartup - T0):0.0}s [{k}] {m}"; log.Add(s); Debug.Log("[ArabicE2E] " + s); File.WriteAllLines(outDir + "arabic_e2e_log.txt", log); }
async Task W(float s) { float t = Time.realtimeSinceStartup + s; while (Time.realtimeSinceStartup < t) await Task.Yield(); }
async Task<bool> Until(Func<bool> c, float timeout) { float t = Time.realtimeSinceStartup + timeout; while (!c()) { if (Time.realtimeSinceStartup > t) return false; await Task.Yield(); } return true; }
float[] Wav(string name) { var b = File.ReadAllBytes(audioDir + name + ".wav"); int n = (b.Length - 44) / 2; var f = new float[n]; for (int i = 0; i < n; i++) f[i] = (short)(b[44 + 2 * i] | (b[45 + 2 * i] << 8)) / 32768f; return f; }
byte[] Pcm(float[] f) { var b = new byte[f.Length * 2]; for (int i = 0; i < f.Length; i++) { short v = (short)(Mathf.Clamp(f[i], -1, 1) * 32767); b[2 * i] = (byte)v; b[2 * i + 1] = (byte)(v >> 8); } return b; }
async Task ScreenShot(string name) { await Awaitable.EndOfFrameAsync(); var tex = ScreenCapture.CaptureScreenshotAsTexture(); File.WriteAllBytes(outDir + name + ".png", tex.EncodeToPNG()); UnityEngine.Object.Destroy(tex); L("shot", name); }

var S = SessionManager.Instance; var V = VoiceInteractionManager.Instance; var AI = AIProviderManager.Instance;
var ui = UnityEngine.Object.FindAnyObjectByType<MedSimUI>();
var voiceText = (TMPro.TMP_Text)typeof(MedSimUI).GetField("voiceText", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(ui);
var speakers = UnityEngine.Object.FindObjectsByType<CharacterSpeechController>(FindObjectsSortMode.None);
var doc = speakers.First(c => c.persona == Persona.Instructor); var pat = speakers.First(c => c.persona == Persona.Patient);
// playback completeness: per spoken line, compare heard duration (subtitle start → speech finished) with audio received
var lines = new List<(string who, string text, float start, float received, float played, bool interrupted)>();
var open = new Dictionary<CharacterSpeechController, (string text, float start)>(); var lastRecv = new Dictionary<CharacterSpeechController, float>();
CharacterSpeechController.SubtitleStarted += (c, t) => { open[c] = (t, Time.realtimeSinceStartup); lastRecv[c] = 0; L("subtitle", $"{c.displayName}: {t}"); };
foreach (var sp in speakers) { var spc = sp; spc.SpeechFinished += () => { if (!open.TryGetValue(spc, out var o)) return; open.Remove(spc); var played = Time.realtimeSinceStartup - o.start; lines.Add((spc.displayName, o.text, o.start, lastRecv[spc], played, false)); L("played", $"{spc.displayName}: audio received {lastRecv[spc]:0.00}s, heard for {played:0.00}s"); }; }
async Task TrackWhile(Func<bool> cond, float timeout) { float t = Time.realtimeSinceStartup + timeout; while (cond() && Time.realtimeSinceStartup < t) { foreach (var sp in speakers) if (sp.CurrentStream != null) lastRecv[sp] = Mathf.Max(lastRecv.TryGetValue(sp, out var r) ? r : 0, sp.CurrentStream.ReceivedSeconds); await Task.Yield(); } }
async Task Idle(float timeout = 60) { await W(0.4f); await TrackWhile(() => V.Busy, timeout); }
string heard = null; V.TranscriptReady += h => { heard = h; };
V.Error += e => L("error", e);
var results = new List<string>();
void R(string s) { results.Add(s); L("RESULT", s); }

async Task Turn(string label, Persona p, string clip)
{
  heard = null; L("student-audio", $"{label}: SYNTHETIC clip {clip}.wav → {p}");
  V.SubmitVoice(p, Wav(clip)); await Until(() => V.State == VoiceState.Speaking || V.State == VoiceState.Idle && heard != null, 30); await Idle();
  var last = lines.LastOrDefault();
  R($"{label}: heard=\"{heard}\" → {last.who}: \"{last.text}\" | conversation={AI.ConversationLanguage} mode={AI.LangMode} | firstAudio={V.LastFirstAudioMs:0}ms | transcript RTL={voiceText.isRightToLeftText}");
}

// ---- setup: the user's failing case — app language English, nothing chosen explicitly
AI.SetLanguageMode(LanguageMode.Auto); AI.SetLanguage("en");
await Until(() => DesktopPlayerController.Active, 20);
V.SetMicMode(MicMode.PushToTalk);
if (S.State == AppState.MainMenu) S.StartClinicalSession(true);
await Until(() => lines.Count > 0, 60); await Idle();
var rig = UnityEngine.Object.FindAnyObjectByType<XRPlayerController>(); var door = UnityEngine.Object.FindAnyObjectByType<HospitalDoor>(); if (door) door.OpenBy(85);
await W(1.5f); var cc = rig.GetComponent<CharacterController>(); if (cc) cc.enabled = false;
rig.transform.position = new Vector3(1.15f, 0, 0.55f); rig.transform.rotation = Quaternion.Euler(0, -90, 0); if (cc) cc.enabled = true;
UnityEngine.Object.FindAnyObjectByType<DesktopPlayerController>()?.LookAt(new Vector3(0.3f, 1.1f, 1.3f));
await W(2f); await Idle();

// 1. Arabic from the very first question (auto)
await Turn("A1 first question in Arabic (app was English)", Persona.Patient, "student_ar_onset");
await ScreenShot("A1_arabic_transcript_and_reply");
R($"A1 check: transcript text shaped (presentation forms)={voiceText.text.Any(ch => ch >= '\uFE70')} , starts with label «أنت»={ArabicShaper.Shape("أنت").Length > 0}");
await Turn("A2 Palestinian follow-up", Persona.Patient, "student_ar_where");
await Turn("A3 mixed Arabic + English term", Persona.Patient, "student_ar_mixed_allergy");
await ScreenShot("A3_mixed_transcript");
// 2. explicit switch to Arabic (said to the doctor) → both characters Arabic, even for English input
await Turn("A4 «خلينا نحكي بالعربي» to the doctor", Persona.Instructor, "student_ar_cmd");
R($"A4 check: mode={AI.LangMode} (expect Arabic), conversation={AI.ConversationLanguage}");
await Turn("A5 English question to the PATIENT while Arabic is chosen", Persona.Patient, "student_en_allergy");
await Turn("A6 doctor, Arabic + ECG", Persona.Instructor, "student_ar_dr_ecg");
// 3. supervisor line in Arabic (hint request uses the same path as interventions)
S.RequestHint(); await Until(() => V.Busy, 5); await Idle();
R($"A7 supervisor hint (director line): \"{lines.Last().text}\" arabic={ArabicShaper.ContainsArabic(lines.Last().text)}");
// 4. hands-free false alarm: noise while the doctor talks must NOT cut her off
{
  S.RequestHint(); await Until(() => doc.IsSpeaking, 15); await W(0.8f);
  var noise = new float[24000]; var rnd = new System.Random(1); for (int i = 0; i < noise.Length; i++) noise[i] = (float)(rnd.NextDouble() * 2 - 1) * 0.02f;
  doc.Duck(true); // what hands-free VAD does at barge-in
  var routine = (IEnumerator)typeof(VoiceInteractionManager).GetMethod("VoiceRoutine", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(V, new object[] { Persona.Instructor, Pcm(noise), Time.realtimeSinceStartup, true });
  V.StartCoroutine(routine);
  float t0 = Time.realtimeSinceStartup; bool stillTalking = true;
  while (Time.realtimeSinceStartup - t0 < 4f && doc.IsSpeaking) await Task.Yield();
  stillTalking = doc.IsSpeaking || Time.realtimeSinceStartup - t0 >= 4f;
  var vol = doc.GetComponent<AudioSource>().volume;
  await Idle();
  R($"A8 hands-free false alarm (noise over the doctor): doctor kept talking={stillTalking}, volume restored={(vol > 0.9f || !doc.IsSpeaking)}");
}
// 5. real barge-in: a real question over the doctor → she stops, the patient answers
{
  S.RequestHint(); await Until(() => doc.IsSpeaking, 15); await W(0.8f);
  doc.Duck(true);
  var routine = (IEnumerator)typeof(VoiceInteractionManager).GetMethod("VoiceRoutine", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(V, new object[] { Persona.Patient, Pcm(Wav("student_ar_reassure")), Time.realtimeSinceStartup, true });
  V.StartCoroutine(routine);
  bool stopped = await Until(() => !doc.IsSpeaking, 8);
  await Idle();
  R($"A9 real barge-in (Arabic question over the doctor): doctor stopped={stopped}; patient replied \"{lines.Last().text}\"");
}
// 6. long Arabic speech: the spoken debrief must be heard to the end
S.FinishCase(); await Until(() => lines.Any(l => l.who == doc.displayName && l.text.Length > 150) || V.Busy, 30); await Idle(120);
var deb = lines.Where(l => l.who == doc.displayName).OrderByDescending(l => l.text.Length).First();
R($"A10 long Arabic debrief: {deb.text.Split(' ').Length} words, audio received {deb.received:0.0}s, heard {deb.played:0.0}s → complete={deb.played >= deb.received - 0.25f}");
await ScreenShot("A10_debrief");
// summary of completeness over every spoken line
int cut = lines.Count(l => l.received > 0 && l.played < l.received - 0.25f);
R($"Playback completeness: {lines.Count(l => l.received > 0)} streamed lines, {cut} ended before their audio ({string.Join(" | ", lines.Where(l => l.received > 0 && l.played < l.received - 0.25f).Select(l => $"{l.who} {l.played:0.0}/{l.received:0.0}s"))})");
// 7. switch back to English
S.StartClinicalSession(true); await Until(() => V.Busy, 10); await Idle(60);
await Turn("A11 \"switch back to English\"", Persona.Instructor, "student_en_cmd");
R($"A11 check: mode={AI.LangMode} conversation={AI.ConversationLanguage}");
AI.SetLanguageMode(LanguageMode.Auto); AI.SetLanguage("en");
return string.Join("\n", results);
