using UnityEngine; using System.IO; using System.Linq;
string dir = "D:/Medical-Test/My project/Screenshots/CompleteRedesign/after/"; Directory.CreateDirectory(dir);
var log = new System.Text.StringBuilder();
void Take(string name, Vector3 pos, Vector3 look, float fov) {
  var go = new GameObject("TempAuditCam"); go.hideFlags = HideFlags.HideAndDontSave;
  var cam = go.AddComponent<Camera>(); cam.stereoTargetEye = StereoTargetEyeMask.None;
  cam.transform.position = pos; cam.transform.LookAt(look); cam.fieldOfView = fov; cam.nearClipPlane = 0.02f;
  var data = go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>(); data.renderPostProcessing = true; data.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.SubpixelMorphologicalAntiAliasing; data.renderShadows = true;
  int W=1920,H=1080; var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 }; cam.targetTexture = rt; cam.Render();
  RenderTexture.active = rt; var tex = new Texture2D(W, H, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0,0,W,H),0,0); tex.Apply(); RenderTexture.active = null;
  File.WriteAllBytes(dir + name + ".png", tex.EncodeToPNG()); cam.targetTexture = null; Object.DestroyImmediate(go); rt.Release(); Object.DestroyImmediate(tex); log.AppendLine(name);
}
var hingeT = GameObject.Find("Redesign_Door").GetComponent<MedSimAI.Instruments.HospitalDoor>().hinge; var rest = hingeT.localRotation;
Take("C00_student_spawn_view", new Vector3(0.45f,1.65f,-4.0f), new Vector3(1.25f,1.35f,-2.62f), 85);
Take("C01_sign_closeup", new Vector3(0.5f,1.6f,-3.75f), new Vector3(0.5f,1.6f,-2.62f), 50);
Take("C02_corridor_overview_east", new Vector3(-1.4f,1.65f,-3.6f), new Vector3(3.4f,1.2f,-3.9f), 80);
Take("C03_corridor_overview_west", new Vector3(3.2f,1.65f,-3.4f), new Vector3(-1.6f,1.1f,-4.1f), 80);
Take("C04_corridor_window_bench", new Vector3(0.8f,1.65f,-2.9f), new Vector3(0.9f,1.0f,-5.0f), 85);
Take("C08_notice_board", new Vector3(-0.95f,1.5f,-3.6f), new Vector3(-0.95f,1.5f,-2.62f), 50);
hingeT.localRotation = rest * Quaternion.Euler(0, 50, 0);
Take("C05_door_half_open_corridor", new Vector3(1.2f,1.65f,-4.2f), new Vector3(1.85f,1.2f,-2.5f), 65);
Take("C06_door_half_open_room", new Vector3(0.6f,1.65f,-0.4f), new Vector3(1.85f,1.15f,-2.5f), 60);
hingeT.localRotation = rest * Quaternion.Euler(0, 92, 0);
Take("C07_doorway_from_inside_open", new Vector3(1.6f,1.65f,-0.9f), new Vector3(1.85f,1.3f,-3.5f), 65);
Take("C09_room_reveal_through_door", new Vector3(1.85f,1.65f,-3.1f), new Vector3(0.6f,1.0f,0.8f), 75);
hingeT.localRotation = rest;
return log.ToString();
