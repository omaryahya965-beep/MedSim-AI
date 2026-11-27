// Corridor views in Play mode (lighting as at runtime). Tag from Screenshots/Polish/tag.txt.
using UnityEngine; using System.IO; using System.Threading.Tasks;
string tagFile = "D:/Medical-Test/My project/Screenshots/Polish/tag.txt"; string tag = File.Exists(tagFile) ? File.ReadAllText(tagFile).Trim() : "shot";
string dir = "D:/Medical-Test/My project/Screenshots/Polish/";
var log = new System.Text.StringBuilder();
void Take(string name, Vector3 pos, Vector3 look, float fov) {
  var go = new GameObject("TempShotCam"); var cam = go.AddComponent<Camera>(); cam.stereoTargetEye = StereoTargetEyeMask.None;
  cam.transform.position = pos; cam.transform.LookAt(look); cam.fieldOfView = fov; cam.nearClipPlane = 0.02f;
  var data = go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>(); data.renderPostProcessing = true; data.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.SubpixelMorphologicalAntiAliasing; data.renderShadows = true;
  int Wd = 1600, Ht = 900; var rt = new RenderTexture(Wd, Ht, 24, RenderTextureFormat.DefaultHDR) { antiAliasing = 4 }; cam.targetTexture = rt; cam.Render();
  var ldr = RenderTexture.GetTemporary(Wd, Ht, 0, RenderTextureFormat.ARGB32); Graphics.Blit(rt, ldr);
  RenderTexture.active = ldr; var tex = new Texture2D(Wd, Ht, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, Wd, Ht), 0, 0); tex.Apply(); RenderTexture.active = null;
  File.WriteAllBytes(dir + tag + "_" + name + ".png", tex.EncodeToPNG()); cam.targetTexture = null; Object.Destroy(go); rt.Release(); RenderTexture.ReleaseTemporary(ldr); log.AppendLine("shot " + name);
}
Take("C00_student_spawn_view", new Vector3(0.45f,1.65f,-4.0f), new Vector3(1.25f,1.35f,-2.62f), 85);
Take("C01_sign_from_2m", new Vector3(0.5f,1.65f,-4.6f), new Vector3(0.5f,1.6f,-2.62f), 60);
Take("C02_corridor_east", new Vector3(-1.4f,1.65f,-3.6f), new Vector3(3.4f,1.2f,-3.9f), 80);
Take("C03_corridor_west", new Vector3(3.2f,1.65f,-3.4f), new Vector3(-1.6f,1.1f,-4.1f), 80);
var main = Camera.main; if (main) { log.AppendLine("player camera " + main.transform.position.ToString("F2")); Take("C10_player_camera_now", main.transform.position, main.transform.position + main.transform.forward, main.fieldOfView); }
return log.ToString();
