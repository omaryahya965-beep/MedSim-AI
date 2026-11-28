// Room-fit check for the Person 2 assets in Person 1's OperatingRoom.prefab (run in PLAY MODE in Assets/MedSimAI/Surgery/Scenes/Anatomy_Test.unity):
//   unity command script eval --file Docs/Surgery/Anatomy/tools/AnatomyRoomFitCheck.cs --timeout 600
// Hides the test scene, instantiates the room, puts SurgicalPatient at PatientRoot and the populated tray at InstrumentTable,
// reports anchor resolution, body/deck clearance and tray footprint, and renders 19_integration_*.png to Screenshots/Surgery/Anatomy/.
using UnityEngine; using System; using System.IO; using System.Linq; using System.Threading.Tasks; using System.Collections.Generic;
using MedSimAI.Surgery.Shared; using MedSimAI.Surgery.Shared.Unity;
string outDir = "D:/Medical-Test/My project/Screenshots/Surgery/Anatomy/";
var sb = new System.Text.StringBuilder();
async Task W(float s) { float t = Time.realtimeSinceStartup + s; while (Time.realtimeSinceStartup < t) await Task.Yield(); }
await W(1.0f);
foreach (var r in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects()) if (!r.GetComponent<UnityEngine.Rendering.Volume>()) r.SetActive(false);
var room = UnityEngine.Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MedSimAI/Surgery/Environment/Prefabs/OperatingRoom.prefab"));
Transform F(GameObject root, string n) => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == n);
var proot = F(room, "PatientRoot"); var roomSite = F(room, "SurgicalSite"); var instT = F(room, "InstrumentTable"); var surgeon = F(room, "SurgeonPosition");
var patient = UnityEngine.Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MedSimAI/Surgery/Anatomy/Prefabs/SurgicalPatient.prefab"), proot.position, proot.rotation);
var tray = UnityEngine.Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MedSimAI/Surgery/Instruments/Prefabs/InstrumentTray_Populated.prefab"), instT.position, instT.rotation);
await W(0.5f);
var site = SurgeryAnchor.Find(AnchorIds.SurgicalSiteKneeR);
var mySite = F(patient, AnchorIds.SurgicalSiteKneeR);
sb.AppendLine("Find(SurgicalSite_Knee_R) -> " + (site == mySite ? "patient anchor" : site ? site.name + " (room)" : "null") + " at " + site.position.ToString("F3"));
sb.AppendLine("room SurgicalSite " + roomSite.position.ToString("F3") + " vs patient knee " + mySite.position.ToString("F3") + " delta " + (mySite.position - roomSite.position).ToString("F3") + " |d|=" + (mySite.position - roomSite.position).magnitude.ToString("F3"));
// body vs deck penetration
float deckTop = proot.position.y; int below = 0, total = 0; float minY = 9;
foreach (var mf in patient.GetComponentsInChildren<MeshFilter>()) {
  var p = UnityEditor.AnimationUtility.CalculateTransformPath(mf.transform, patient.transform);
  if (!p.StartsWith("Body")) continue;
  var m = mf.sharedMesh; var v = m.vertices; var M = mf.transform.localToWorldMatrix;
  foreach (var lv in v) { var w = M.MultiplyPoint3x4(lv); total++; if (Mathf.Abs(w.x) < 0.25f && Mathf.Abs(w.z) < 1.04f) { minY = Mathf.Min(minY, w.y); if (w.y < deckTop - 0.005f) below++; } }
}
sb.AppendLine("body verts " + total + ", below deck top by >5mm: " + below + ", lowest body point over deck " + ((minY - deckTop) * 1000).ToString("F1") + " mm");
var dr = patient.GetComponentsInChildren<Renderer>().Where(r => UnityEditor.AnimationUtility.CalculateTransformPath(r.transform, patient.transform).Contains("Drape"));
var db = dr.First().bounds; foreach (var r in dr) db.Encapsulate(r.bounds);
sb.AppendLine("drape bounds min " + db.min.ToString("F3") + " max " + db.max.ToString("F3"));
var tb = tray.GetComponentsInChildren<Renderer>().First().bounds; foreach (var r in tray.GetComponentsInChildren<Renderer>()) tb.Encapsulate(r.bounds);
var back = F(room, "Sterile Instrument Back Table"); var bb = back.GetComponentsInChildren<Renderer>().First().bounds; foreach (var r in back.GetComponentsInChildren<Renderer>()) bb.Encapsulate(r.bounds);
sb.AppendLine("tray bounds " + tb.min.ToString("F3") + "-" + tb.max.ToString("F3") + "; back table " + bb.min.ToString("F3") + "-" + bb.max.ToString("F3"));
// renders
var camGo = new GameObject("EvidenceCam"); var cam = camGo.AddComponent<Camera>(); cam.nearClipPlane = 0.02f; cam.enabled = false;
var data = camGo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>(); data.renderPostProcessing = true; data.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
void Shot(string name, Vector3 pos, Vector3 look, float fov) {
  cam.transform.position = pos; cam.transform.LookAt(look); cam.fieldOfView = fov;
  var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.DefaultHDR) { antiAliasing = 8 }; cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
  var ldr = RenderTexture.GetTemporary(1600, 900, 0, RenderTextureFormat.ARGB32); Graphics.Blit(rt, ldr);
  RenderTexture.active = ldr; var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); tex.Apply(); RenderTexture.active = null;
  File.WriteAllBytes(outDir + name + ".png", tex.EncodeToPNG()); UnityEngine.Object.Destroy(tex); rt.Release(); RenderTexture.ReleaseTemporary(ldr); sb.AppendLine("shot " + name);
}
await W(0.5f);
Shot("19_integration_room_overview", new Vector3(-2.3f, 2.1f, -2.6f), new Vector3(-0.3f, 0.95f, 0f), 50);
var eye = surgeon.position + Vector3.up * 1.65f;
Shot("19b_integration_surgeon_view", eye, mySite.position, 45);
Shot("19c_integration_instrument_table", instT.position + new Vector3(0.75f, 0.75f, -0.55f), instT.position, 45);
return sb.ToString();
