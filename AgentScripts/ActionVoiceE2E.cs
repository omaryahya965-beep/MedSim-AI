// Spoken physical instructions end to end (real Realtime API, SYNTHETIC student clips — automated audio, not a human):
// audio → Realtime understanding → validated action → character moves while the acknowledgement is spoken → result
// reported back to the AI. Sequential screenshots in Screenshots/Actions/voice_*.png.
//   unity command script eval -- --file AgentScripts/ActionVoiceE2E.cs --timeout 400
using UnityEngine; using System; using System.IO; using System.Linq; using System.Threading.Tasks; using System.Collections.Generic;
using MedSimAI.AI; using MedSimAI.Characters; using MedSimAI.Voice; using MedSimAI.Core; using MedSimAI.Instruments;
string dir = "D:/Medical-Test/My project/Screenshots/Actions/"; string audio = "D:/Medical-Test/My project/Backend/medsim-proxy/data/student-audio/";
var results = new List<string>();
async Task W(float s) { float t = Time.realtimeSinceStartup + s; while (Time.realtimeSinceStartup < t) await Task.Yield(); }
float[] Wav(string n) { var b = File.ReadAllBytes(audio + n + ".wav"); int k = (b.Length - 44) / 2; var f = new float[k]; for (int i = 0; i < k; i++) f[i] = (short)(b[44 + 2 * i] | (b[45 + 2 * i] << 8)) / 32768f; return f; }
async Task Screen(string name)
{
  await Awaitable.EndOfFrameAsync();
  try { var tex = ScreenCapture.CaptureScreenshotAsTexture(2); File.WriteAllBytes(dir + name + ".png", tex.EncodeToPNG()); UnityEngine.Object.Destroy(tex); return; } catch { }
  var cam = Camera.main; var rt = new RenderTexture(1600, 900, 24); var prev = cam.targetTexture; cam.targetTexture = rt; cam.Render(); cam.targetTexture = prev; RenderTexture.active = rt;
  var t2 = new Texture2D(1600, 900, TextureFormat.RGB24, false); t2.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); t2.Apply(); RenderTexture.active = null; File.WriteAllBytes(dir + name + ".png", t2.EncodeToPNG()); rt.Release();
}
{ float tw = Time.realtimeSinceStartup + 30; while ((SessionManager.Instance == null || ClinicalScenarioManager.Instance?.Session == null) && Time.realtimeSinceStartup < tw) await Task.Yield(); await W(2f); }
var S = SessionManager.Instance; var V = VoiceInteractionManager.Instance; var AI = AIProviderManager.Instance;
AI.SetLanguageMode(LanguageMode.Auto); V.SetMicMode(MicMode.PushToTalk);
if (S.State == AppState.MainMenu) S.StartClinicalSession(true);
float t1 = Time.realtimeSinceStartup + 60; while (!V.Busy && Time.realtimeSinceStartup < t1) await Task.Yield(); while (V.Busy && Time.realtimeSinceStartup < t1 + 30) await Task.Yield();
var rig = UnityEngine.Object.FindAnyObjectByType<MedSimAI.XR.XRPlayerController>(); var door = UnityEngine.Object.FindAnyObjectByType<HospitalDoor>(); if (door) door.OpenBy(85);
await W(1f); var cc = rig.GetComponent<CharacterController>(); if (cc) cc.enabled = false;
rig.transform.position = new Vector3(1.15f, 0, 0.55f); rig.transform.rotation = Quaternion.Euler(0, -90, 0); if (cc) cc.enabled = true;
UnityEngine.Object.FindAnyObjectByType<MedSimAI.XR.DesktopPlayerController>()?.LookAt(new Vector3(0.3f, 1.05f, 1.3f));
await W(2f); while (V.Busy) await Task.Yield();
var P = CharacterActionRouter.Patient; var D = CharacterActionRouter.Doctor;
var events = new List<string>(); P.Completed += (t, s) => events.Add($"patient {t} {s} completed"); D.Completed += (t, s) => events.Add($"doctor {t} completed");
string heard = null, said = null; V.TranscriptReady += h => heard = h; CharacterSpeechController.SubtitleStarted += (c, t) => said = c.displayName + ": " + t;
var pat = P.GetComponent<Animator>();
async Task Turn(string label, Persona who, string clip, Func<string> measure)
{
  heard = null; said = null; int ev = events.Count; float t0 = Time.realtimeSinceStartup;
  V.SubmitVoice(who, Wav(clip));
  while (said == null && Time.realtimeSinceStartup - t0 < 25) await Task.Yield();
  float tSpeak = Time.realtimeSinceStartup - t0; await W(0.9f); await Screen(label + "_during");
  while ((V.Busy || events.Count == ev) && Time.realtimeSinceStartup - t0 < 30) await Task.Yield();
  await W(0.5f); await Screen(label + "_after");
  results.Add($"{label}: heard=«{heard}» → {said} | reply started {tSpeak:0.0}s after the clip | action: {(events.Count > ev ? events.Last() : "none completed")} | {measure()}");
}
await Turn("voice_01_sit_up", Persona.Patient, "student_ar_act_sit", () => $"sit={P.Sit:0.00} head y={pat.GetBoneTransform(HumanBodyBones.Head).position.y:0.00}");
await Turn("voice_02_raise_left_arm", Persona.Patient, "student_ar_act_raise", () => $"left arm weight={P.LeftArm:0.00} left hand y={pat.GetBoneTransform(HumanBodyBones.LeftHand).position.y:0.00}");
var d0 = D.transform.position;
await Turn("voice_03_doctor_step_back", Persona.Instructor, "student_ar_dr_stepback", () => $"doctor moved {Vector3.Distance(d0, D.transform.position):0.00} m, now {Vector3.Distance(new Vector3(D.transform.position.x, 0, D.transform.position.z), new Vector3(rig.transform.position.x, 0, rig.transform.position.z)):0.00} m from the student");
File.WriteAllLines(dir + "voice_actions_log.txt", results);
return string.Join("\n", results);
