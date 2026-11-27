// Embodied-action check in Play mode with MOCKED validated actions (no API cost): executes patient and doctor actions
// through CharacterActionRouter, captures sequential screenshots and measures body/zone/instrument alignment.
//   unity command script eval -- --file AgentScripts/ActionShots.cs --timeout 400
using UnityEngine; using System; using System.IO; using System.Linq; using System.Threading.Tasks; using System.Collections.Generic;
using MedSimAI.AI; using MedSimAI.Characters; using MedSimAI.Instruments; using MedSimAI.Core;
string dir = "D:/Medical-Test/My project/Screenshots/Actions/"; Directory.CreateDirectory(dir);
var log = new List<string>();
async Task W(float s) { float t = Time.realtimeSinceStartup + s; while (Time.realtimeSinceStartup < t) await Task.Yield(); }
var results = new List<string>();
void Shot(string name, Vector3 pos, Vector3 look, float fov = 50)
{
  var go = new GameObject("TempShotCam"); var cam = go.AddComponent<Camera>(); cam.stereoTargetEye = StereoTargetEyeMask.None;
  cam.transform.position = pos; cam.transform.LookAt(look); cam.fieldOfView = fov; cam.nearClipPlane = 0.02f;
  var data = go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>(); data.renderPostProcessing = true; data.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
  int Wd = 1280, Ht = 720; var rt = new RenderTexture(Wd, Ht, 24, RenderTextureFormat.DefaultHDR) { antiAliasing = 4 }; cam.targetTexture = rt; cam.Render();
  var ldr = RenderTexture.GetTemporary(Wd, Ht, 0, RenderTextureFormat.ARGB32); Graphics.Blit(rt, ldr);
  RenderTexture.active = ldr; var tex = new Texture2D(Wd, Ht, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, Wd, Ht), 0, 0); tex.Apply(); RenderTexture.active = null;
  File.WriteAllBytes(dir + name + ".png", tex.EncodeToPNG()); cam.targetTexture = null; UnityEngine.Object.Destroy(go); rt.Release(); RenderTexture.ReleaseTemporary(ldr);
}
{ float tw = Time.realtimeSinceStartup + 30; while ((SessionManager.Instance == null || ClinicalScenarioManager.Instance == null || ClinicalScenarioManager.Instance.Session == null) && Time.realtimeSinceStartup < tw) await Task.Yield(); await W(2f); }
var S = SessionManager.Instance; if (S.State == AppState.MainMenu) S.StartClinicalSession(true);
MedSimAI.Voice.VoiceInteractionManager.Instance.Interrupt();
var rig = UnityEngine.Object.FindAnyObjectByType<MedSimAI.XR.XRPlayerController>();
var door = UnityEngine.Object.FindAnyObjectByType<HospitalDoor>(); if (door) door.OpenBy(85);
await W(1f); var cc = rig.GetComponent<CharacterController>(); if (cc) cc.enabled = false;
rig.transform.position = new Vector3(1.15f, 0, 0.55f); rig.transform.rotation = Quaternion.Euler(0, -90, 0); if (cc) cc.enabled = true;
UnityEngine.Object.FindAnyObjectByType<MedSimAI.XR.DesktopPlayerController>()?.LookAt(new Vector3(0.3f, 1.1f, 1.3f));
await W(1.5f);
var P = CharacterActionRouter.Patient; var D = CharacterActionRouter.Doctor;
var done = new List<string>(); P.Completed += (t, s) => done.Add("P:" + t + ":" + s); P.WasInterrupted += (t, s) => done.Add("P:" + t + ":" + s + ":interrupted");
D.Completed += (t, s) => done.Add("D:" + t); D.Failed += (t, r) => done.Add("D:" + t + ":" + r);
var an = P.GetComponent<Animator>(); Transform B(HumanBodyBones b) => an.GetBoneTransform(b);
var zones = UnityEngine.Object.FindObjectsByType<PlacementZone>(FindObjectsSortMode.None);
var zArmL = zones.First(z => z.zoneId == "ARM_UPPER_L"); var zFingerR = zones.First(z => z.zoneId == "FINGER_R"); var zMitral = zones.First(z => z.zoneId == "CHEST_MITRAL");
var cuff = UnityEngine.Object.FindAnyObjectByType<BloodPressureController>(); cuff.PlaceInZone(zArmL); await W(1.5f);
Vector3 side = new Vector3(1.6f, 1.45f, 1.0f), sideLook = new Vector3(0.3f, 1.0f, 1.25f);
Shot("A00_rest_with_cuff_on_left_arm", side, sideLook);
async Task Act(string label, Persona who, string type, string side2 = "none", string instrument = "none", float wait = 3f, Vector3? camPos = null, Vector3? camLook = null)
{
  int before = done.Count;
  bool ok = CharacterActionRouter.Execute(who, new LiveAction { type = type, side = side2, instrument = instrument, status = "accepted" });
  float t0 = Time.realtimeSinceStartup; while (done.Count == before && Time.realtimeSinceStartup - t0 < wait + 8) await Task.Yield();
  await W(0.3f);
  Shot(label, camPos ?? side, camLook ?? sideLook);
  var cuffOff = cuff.Attached ? Vector3.Distance(cuff.transform.position, (zArmL.snapPoint ? zArmL.snapPoint : zArmL.transform).position) : -1;
  results.Add($"{label}: started={ok} result={(done.Count > before ? done.Last() : "pending")} after {Time.realtimeSinceStartup - t0:0.0}s | hand L y={B(HumanBodyBones.LeftHand).position.y:0.00} R y={B(HumanBodyBones.RightHand).position.y:0.00} head=({B(HumanBodyBones.Head).position.y:0.00},{B(HumanBodyBones.Head).position.z:0.00}) | zone ARM_UPPER_L→upper arm {Vector3.Distance(zArmL.transform.position, B(HumanBodyBones.LeftUpperArm).position):0.000} m, cuff→zone {cuffOff:0.000} m");
}
results.Add($"rest: hand L y={B(HumanBodyBones.LeftHand).position.y:0.00} R y={B(HumanBodyBones.RightHand).position.y:0.00} head=({B(HumanBodyBones.Head).position.y:0.00},{B(HumanBodyBones.Head).position.z:0.00}) zone→upper arm {Vector3.Distance(zArmL.transform.position, B(HumanBodyBones.LeftUpperArm).position):0.000} m");
await Act("A01_sit_up", Persona.Patient, "sit_up", camPos: new Vector3(1.9f, 1.3f, 1.25f), camLook: new Vector3(0.3f, 1.0f, 1.3f));
await Act("A02_raise_left_arm_with_cuff", Persona.Patient, "raise_arm", "left");
await Act("A03_lower_arms", Persona.Patient, "lower_arm", "both");
await Act("A04_extend_right_arm_to_student", Persona.Patient, "extend_arm", "right");
await Act("A05_point_to_pain", Persona.Patient, "point_to_pain", camPos: new Vector3(0.3f, 1.4f, 0.1f), camLook: new Vector3(0.3f, 1.05f, 1.3f));
await Act("A06_turn_to_left", Persona.Patient, "turn_to_side", "left", camPos: new Vector3(0.3f, 1.4f, 0.1f), camLook: new Vector3(0.3f, 1.05f, 1.3f));
await Act("A07_open_mouth", Persona.Patient, "open_mouth", camPos: B(HumanBodyBones.Head).position + new Vector3(0.05f, 0.05f, -0.5f), camLook: B(HumanBodyBones.Head).position);
var chestY0 = B(HumanBodyBones.UpperChest)?.position ?? B(HumanBodyBones.Chest).position;
await Act("A08_deep_breath", Persona.Patient, "deep_breath", wait: 16f);
await Act("A09_rest", Persona.Patient, "rest");
// doctor
var docT = D.transform; var studentFloor = new Vector3(rig.transform.position.x, 0, rig.transform.position.z);
async Task DocAct(string label, string type, string instrument = "none")
{
  int before = done.Count; bool ok = CharacterActionRouter.Execute(Persona.Instructor, new LiveAction { type = type, instrument = instrument, status = "accepted" });
  float t0 = Time.realtimeSinceStartup; while (done.Count == before && Time.realtimeSinceStartup - t0 < 26) await Task.Yield(); await W(0.5f);
  Shot(label, new Vector3(2.0f, 2.3f, -1.2f), new Vector3(0.6f, 0.6f, 0.9f), 70);
  results.Add($"{label}: started={ok} result={(done.Count > before ? done.Last() : "pending")} after {Time.realtimeSinceStartup - t0:0.0}s | doctor at ({docT.position.x:0.00},{docT.position.z:0.00}) distance to student {Vector3.Distance(new Vector3(docT.position.x, 0, docT.position.z), studentFloor):0.00} m, to bed centre {Vector3.Distance(new Vector3(docT.position.x, 0, docT.position.z), new Vector3(0.3f, 0, 0.9f)):0.00} m");
}
await DocAct("D01_come_to_student", "come_to_student");
await DocAct("D02_stand_beside_student", "stand_beside_student");
await DocAct("D03_approach_patient", "approach_patient");
await DocAct("D04_step_back", "step_back");
await DocAct("D05_demonstrate_bp_cuff", "demonstrate_instrument", "bp_cuff");
await DocAct("D06_return_to_observation", "return_to_observation");
File.WriteAllLines(dir + "actions_log.txt", results);
return string.Join("\n", results);
