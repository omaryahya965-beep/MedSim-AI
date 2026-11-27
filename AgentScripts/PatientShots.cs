// Close-up shots of the patient / bed / room in Play mode (pose applied). Desktop Mode.
//   unity command script eval -- --file AgentScripts/PatientShots.cs --timeout 120     (MEDSIM_SHOT_TAG env var → file prefix)
using UnityEngine; using System.IO; using System.Linq; using System.Threading.Tasks;
string tagFile = "D:/Medical-Test/My project/Screenshots/Polish/tag.txt"; string tag = File.Exists(tagFile) ? File.ReadAllText(tagFile).Trim() : "shot";
string dir = "D:/Medical-Test/My project/Screenshots/Polish/"; Directory.CreateDirectory(dir);
var log = new System.Text.StringBuilder();
async Task W(float s) { float t = Time.realtimeSinceStartup + s; while (Time.realtimeSinceStartup < t) await Task.Yield(); }
void Take(string name, Vector3 pos, Vector3 look, float fov) {
  var go = new GameObject("TempShotCam"); var cam = go.AddComponent<Camera>(); cam.stereoTargetEye = StereoTargetEyeMask.None;
  cam.transform.position = pos; cam.transform.LookAt(look); cam.fieldOfView = fov; cam.nearClipPlane = 0.02f;
  var data = go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>(); data.renderPostProcessing = true; data.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.SubpixelMorphologicalAntiAliasing; data.renderShadows = true;
  int Wd = 1600, Ht = 900; var rt = new RenderTexture(Wd, Ht, 24, RenderTextureFormat.DefaultHDR) { antiAliasing = 4 }; cam.targetTexture = rt; cam.Render();
  var ldr = RenderTexture.GetTemporary(Wd, Ht, 0, RenderTextureFormat.ARGB32); Graphics.Blit(rt, ldr);
  RenderTexture.active = ldr; var tex = new Texture2D(Wd, Ht, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, Wd, Ht), 0, 0); tex.Apply(); RenderTexture.active = null;
  File.WriteAllBytes(dir + tag + "_" + name + ".png", tex.EncodeToPNG()); cam.targetTexture = null; Object.Destroy(go); rt.Release(); RenderTexture.ReleaseTemporary(ldr); log.AppendLine("shot " + name);
}
await W(2f);
var pat = GameObject.Find("Patient - Mr Adam Reyes"); var an = pat.GetComponent<Animator>();
Vector3 B(HumanBodyBones b) => an.GetBoneTransform(b).position;
var head = B(HumanBodyBones.Head); var hips = B(HumanBodyBones.Hips); var lf = B(HumanBodyBones.LeftFoot); var rf = B(HumanBodyBones.RightFoot); var le = B(HumanBodyBones.LeftLowerArm); var re = B(HumanBodyBones.RightLowerArm);
log.AppendLine($"head {head:F2} hips {hips:F2} lfoot {lf:F2} rfoot {rf:F2} lelbow {le:F2} relbow {re:F2}");
Take("P1_bed_side", new Vector3(1.55f, 1.45f, 0.9f), new Vector3(0.3f, 0.85f, 1.0f), 55);
Take("P2_feet_end", new Vector3(0.3f, 1.35f, -0.55f), new Vector3(0.3f, 0.8f, 0.6f), 55);
Take("P3_face", head + new Vector3(0.05f, 0.02f, -0.55f), head, 30);
Take("P4_left_arm_elbow", le + new Vector3(0.55f, 0.25f, -0.2f), le, 40);
Take("P5_right_arm_elbow", re + new Vector3(-0.55f, 0.25f, -0.2f), re, 40);
Take("P6_student_view", new Vector3(1.2f, 1.65f, 0.65f), new Vector3(0.3f, 0.95f, 1.1f), 70);
Take("P7_feet_low", new Vector3(0.95f, 0.85f, 0.1f), (lf + rf) * 0.5f, 45);
Take("P9_blanket_top_edge", new Vector3(0.95f, 1.25f, 1.0f), new Vector3(0.3f, 0.85f, 1.05f), 40);
Take("P8_room_corner_dark", new Vector3(1.6f, 1.6f, 0.0f), new Vector3(-1.2f, 0.8f, 2.4f), 75);
return log.ToString();
