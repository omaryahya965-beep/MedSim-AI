// Visual evidence of embodied actions (Play mode, mocked validated actions — no API cost): frame sequences (~10 fps) of the
// rendered characters for each action, assembled into GIFs afterwards (tools: AgentScripts/make_gifs.ps1).
//   unity command script eval -- --file AgentScripts/EvidenceCapture.cs --timeout 600
using UnityEngine; using System; using System.IO; using System.Linq; using System.Threading.Tasks; using System.Collections.Generic;
using MedSimAI.AI; using MedSimAI.Characters; using MedSimAI.Instruments; using MedSimAI.Core;
string root = "D:/Medical-Test/My project/Screenshots/Embodied/"; Directory.CreateDirectory(root);
var log = new List<string>();
async Task W(float s) { float t = Time.realtimeSinceStartup + s; while (Time.realtimeSinceStartup < t) await Task.Yield(); }
GameObject camGo = new GameObject("EvidenceCam"); var cam = camGo.AddComponent<Camera>(); cam.stereoTargetEye = StereoTargetEyeMask.None; cam.nearClipPlane = 0.02f; cam.enabled = false;
var data = camGo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>(); data.renderPostProcessing = true; data.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
void Frame(string path, int w, int h)
{
  var rt = new RenderTexture(w, h, 24, RenderTextureFormat.DefaultHDR) { antiAliasing = 4 }; cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
  var ldr = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32); Graphics.Blit(rt, ldr);
  RenderTexture.active = ldr; var tex = new Texture2D(w, h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply(); RenderTexture.active = null;
  File.WriteAllBytes(path, tex.EncodeToPNG()); UnityEngine.Object.Destroy(tex); rt.Release(); RenderTexture.ReleaseTemporary(ldr);
}
async Task Clip(string name, Vector3 pos, Vector3 look, float fov, float seconds, Action start)
{
  var d = root + name + "/"; Directory.CreateDirectory(d); foreach (var f in Directory.GetFiles(d, "*.png")) File.Delete(f);
  cam.transform.position = pos; cam.transform.LookAt(look); cam.fieldOfView = fov;
  start?.Invoke(); int i = 0; float t0 = Time.realtimeSinceStartup;
  while (Time.realtimeSinceStartup - t0 < seconds) { Frame(d + $"f{i++:000}.png", 640, 360); float next = t0 + i * 0.1f; while (Time.realtimeSinceStartup < next) await Task.Yield(); }
  Frame(root + name + ".png", 1280, 720); log.Add($"{name}: {i} frames");
}
{ float tw = Time.realtimeSinceStartup + 30; while ((SessionManager.Instance == null || ClinicalScenarioManager.Instance?.Session == null) && Time.realtimeSinceStartup < tw) await Task.Yield(); await W(2f); }
var S = SessionManager.Instance; if (S.State == AppState.MainMenu) S.StartClinicalSession(true);
MedSimAI.Voice.VoiceInteractionManager.Instance.Interrupt(); await W(1f);
var rig = UnityEngine.Object.FindAnyObjectByType<MedSimAI.XR.XRPlayerController>(); var door = UnityEngine.Object.FindAnyObjectByType<HospitalDoor>(); if (door) door.OpenBy(85);
await W(1f); var cc = rig.GetComponent<CharacterController>(); if (cc) cc.enabled = false;
rig.transform.position = new Vector3(1.15f, 0, 0.55f); rig.transform.rotation = Quaternion.Euler(0, -90, 0); if (cc) cc.enabled = true;
UnityEngine.Object.FindAnyObjectByType<MedSimAI.XR.DesktopPlayerController>()?.LookAt(new Vector3(0.3f, 1.05f, 1.3f));
await W(1.5f); MedSimAI.Voice.VoiceInteractionManager.Instance.Interrupt();
var P = CharacterActionRouter.Patient; P.autonomous = false;
var D = CharacterActionRouter.Doctor;
void Do(Persona who, string type, string side = "none", string instr = "none") => CharacterActionRouter.Execute(who, new LiveAction { type = type, side = side, instrument = instr, status = "accepted" });
var zones = UnityEngine.Object.FindObjectsByType<PlacementZone>(FindObjectsSortMode.None);
var cuff = UnityEngine.Object.FindAnyObjectByType<BloodPressureController>(); var ox = UnityEngine.Object.FindAnyObjectByType<PulseOximeterController>(); var steth = UnityEngine.Object.FindAnyObjectByType<StethoscopeController>();
Vector3 side = new Vector3(1.75f, 1.45f, 0.85f), sideLook = new Vector3(0.3f, 1.0f, 1.25f), front = new Vector3(0.3f, 1.55f, -0.35f), frontLook = new Vector3(0.3f, 1.0f, 1.25f);
await Clip("01_resting_reclined", side, sideLook, 50, 2f, null);
await Clip("02_sit_up", side, sideLook, 50, 3.5f, () => Do(Persona.Patient, "sit_up"));
await Clip("03_raise_left_arm_bp_cuff", front, frontLook, 52, 3f, () => { cuff.PlaceInZone(zones.First(z => z.zoneId == "ARM_UPPER_L")); Do(Persona.Patient, "raise_arm", "left"); });
var snapL = zones.First(z => z.zoneId == "ARM_UPPER_L"); log.Add($"cuff on raised arm: {Vector3.Distance(cuff.transform.position, (snapL.snapPoint ? snapL.snapPoint : snapL.transform).position):0.000} m from its snap point");
Do(Persona.Patient, "lower_arm", "left"); await W(1.6f);
await Clip("04_extend_right_arm_pulse_oximeter", front, frontLook, 52, 3f, () => { ox.PlaceInZone(zones.First(z => z.zoneId == "FINGER_R")); Do(Persona.Patient, "extend_arm", "right"); });
var snapF = zones.First(z => z.zoneId == "FINGER_R"); log.Add($"oximeter on extended finger: {Vector3.Distance(ox.transform.position, (snapF.snapPoint ? snapF.snapPoint : snapF.transform).position):0.000} m from its snap point");
Do(Persona.Patient, "lower_arm", "both"); await W(1.6f);
await Clip("05_deep_breath", side, new Vector3(0.3f, 1.1f, 1.3f), 32, 9f, () => Do(Persona.Patient, "deep_breath"));
Do(Persona.Patient, "breathe_normally");
{ // stethoscope held on the mitral area: the real listening logic runs each frame (as when the student holds it there)
  var zm = zones.First(z => z.zoneId == "CHEST_MITRAL"); var wh = typeof(MedicalInstrumentBase).GetMethod("WhileHeld", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
  var d6 = root + "06_auscultation_holds_still/"; Directory.CreateDirectory(d6); foreach (var f in Directory.GetFiles(d6, "*.png")) File.Delete(f);
  cam.transform.position = front; cam.transform.LookAt(frontLook); cam.fieldOfView = 48; int i6 = 0; float t6 = Time.realtimeSinceStartup; bool still = false;
  while (Time.realtimeSinceStartup - t6 < 3.5f) { steth.transform.SetPositionAndRotation(zm.transform.position, zm.transform.rotation); wh.Invoke(steth, new object[] { zm }); await Task.Yield();
    still |= P.GetComponent<HumanoidLifeController>().holdStill; if (Time.realtimeSinceStartup > t6 + i6 * 0.1f) Frame(d6 + $"f{i6++:000}.png", 640, 360); }
  Frame(root + "06_auscultation_holds_still.png", 1280, 720);
  log.Add($"06_auscultation_holds_still: {i6} frames; stethoscope on the chest → patient holding still: {still}");
}
steth.ResetInstrument(); ox.ResetInstrument(); cuff.ResetInstrument();
Do(Persona.Patient, "recline"); await W(2.5f);
var overhead = new Vector3(2.4f, 2.6f, -1.6f); var overLook = new Vector3(0.4f, 0.4f, 0.8f);
await Clip("07_doctor_walks_to_student", overhead, overLook, 72, 6f, () => Do(Persona.Instructor, "come_to_student"));
await Clip("08_doctor_moves_aside", overhead, overLook, 72, 5f, () => Do(Persona.Instructor, "step_back"));
await Clip("09_doctor_other_side_of_bed", overhead, overLook, 72, 7f, () => Do(Persona.Instructor, "go_other_side"));
// speech + movement together (text-only line, same subtitle/lip-sync path as live speech) — captured with the UI from the player's view
var patSpeech = UnityEngine.Object.FindObjectsByType<CharacterSpeechController>(FindObjectsSortMode.None).First(c => c.persona == Persona.Patient);
var player = Camera.main; cam.transform.SetPositionAndRotation(player.transform.position, player.transform.rotation); cam.fieldOfView = player.fieldOfView;
{
  var d = root + "10_speech_and_movement/"; Directory.CreateDirectory(d); foreach (var f in Directory.GetFiles(d, "*.png")) File.Delete(f);
  patSpeech.Speak("آه، تفضل دكتور. هيك منيح؟"); Do(Persona.Patient, "raise_arm", "left");
  int i = 0; float t0 = Time.realtimeSinceStartup;
  while (Time.realtimeSinceStartup - t0 < 3f) { await Awaitable.EndOfFrameAsync(); try { var tex = ScreenCapture.CaptureScreenshotAsTexture(); File.WriteAllBytes(d + $"f{i++:000}.png", tex.EncodeToPNG()); UnityEngine.Object.Destroy(tex); } catch { Frame(d + $"f{i++:000}.png", 640, 360); } float next = t0 + i * 0.1f; while (Time.realtimeSinceStartup < next) await Task.Yield(); }
  log.Add($"10_speech_and_movement: {i} frames (player view with the conversation panel)");
}
Do(Persona.Patient, "rest"); Do(Persona.Instructor, "return_to_observation");
log.Add($"patient action layer cost ≈ {PatientActionController.AvgMs:0.000} ms/frame");
log.Add("action history: " + string.Join(" | ", CharacterActionRouter.History.Select(h => h.ToString())));
UnityEngine.Object.Destroy(camGo); P.autonomous = true;
File.WriteAllLines(root + "evidence_log.txt", log);
return string.Join("\n", log);
