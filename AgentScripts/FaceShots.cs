// Close-ups of both characters for the realism pass (Play mode). Tag from Screenshots/Realism/tag.txt.
using UnityEngine; using System.IO; using System.Threading.Tasks;
string dir = "D:/Medical-Test/My project/Screenshots/Realism/"; Directory.CreateDirectory(dir);
string tag = File.Exists(dir + "tag.txt") ? File.ReadAllText(dir + "tag.txt").Trim() : "shot";
void Take(string name, Vector3 pos, Vector3 look, float fov) {
  var go = new GameObject("FaceCam"); var cam = go.AddComponent<Camera>(); cam.stereoTargetEye = StereoTargetEyeMask.None; cam.nearClipPlane = 0.02f;
  cam.transform.position = pos; cam.transform.LookAt(look); cam.fieldOfView = fov;
  var d = go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>(); d.renderPostProcessing = true; d.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
  var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.DefaultHDR) { antiAliasing = 8 }; cam.targetTexture = rt; cam.Render();
  var ldr = RenderTexture.GetTemporary(1280, 720, 0, RenderTextureFormat.ARGB32); Graphics.Blit(rt, ldr); RenderTexture.active = ldr;
  var t = new Texture2D(1280, 720, TextureFormat.RGB24, false); t.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); t.Apply(); RenderTexture.active = null;
  File.WriteAllBytes(dir + tag + "_" + name + ".png", t.EncodeToPNG()); Object.Destroy(go); rt.Release(); RenderTexture.ReleaseTemporary(ldr);
}
float tw = Time.realtimeSinceStartup + 3; while (Time.realtimeSinceStartup < tw) await Task.Yield();
var pa = GameObject.Find("Patient - Mr Adam Reyes").GetComponent<Animator>(); var da = GameObject.Find("Instructor - Dr Sara Hassan").GetComponent<Animator>();
var ph = pa.GetBoneTransform(HumanBodyBones.Head).position; var dh = da.GetBoneTransform(HumanBodyBones.Head).position;
Take("patient_face", ph + new Vector3(0.25f, 0.12f, -0.38f), ph + new Vector3(0, 0.02f, 0), 28);
Take("patient_gown_chest", ph + new Vector3(0.35f, 0.05f, -0.55f), ph + new Vector3(0, -0.3f, -0.15f), 30);
var dfwd = GameObject.Find("Instructor - Dr Sara Hassan").transform.forward;
Take("doctor_face", dh + dfwd * 0.55f + new Vector3(0.1f, 0.03f, 0), dh + new Vector3(0, 0.02f, 0), 28);
return "ok";
