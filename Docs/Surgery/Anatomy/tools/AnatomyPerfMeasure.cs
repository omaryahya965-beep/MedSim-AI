// Rendering cost of the Person 2 assets in Anatomy_Test.unity (run in PLAY MODE):
//   unity command script eval -- --file Docs/Surgery/Anatomy/tools/AnatomyPerfMeasure.cs --timeout 300
// Reports per-asset triangle counts and Editor game-view frame timings (NOT headset performance).
using UnityEngine; using System.Linq; using System.Threading.Tasks; using System.Collections.Generic;
using MedSimAI.Surgery.Anatomy; using MedSimAI.Surgery.AssetsInspector; using MedSimAI.Surgery.Shared;
var sb = new System.Text.StringBuilder();
long Tris(GameObject g) => g.GetComponentsInChildren<MeshFilter>().Where(m => m.GetComponent<Renderer>() && m.GetComponent<Renderer>().enabled && m.gameObject.activeInHierarchy).Sum(m => (long)m.sharedMesh.triangles.Length / 3);
var insp = Object.FindAnyObjectByType<AnatomyAssetInspector>(); insp.showPanel = false; var cam = insp.cam;
var patient = insp.patient.gameObject; var knee = insp.knee.gameObject;
sb.AppendLine($"GPU: {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsMemorySize} MB), {SystemInfo.graphicsDeviceType}; screen {Screen.width}x{Screen.height}; QualityLevel {QualitySettings.names[QualitySettings.GetQualityLevel()]}");
sb.AppendLine($"tris (active renderers): patient incl. knee+drape={Tris(patient):N0}; knee (intact state)={Tris(knee):N0}; drape={Tris(insp.patient.drape.gameObject):N0}; tray+instruments={Tris(GameObject.Find("InstrumentTray_Populated")):N0}; implant set={Tris(GameObject.Find("KneeImplantSet")):N0}");
var mats = patient.GetComponentsInChildren<Renderer>().SelectMany(r => r.sharedMaterials).Distinct().Count(); sb.AppendLine($"distinct materials on the patient prefab: {mats}");
async Task<string> Measure(string label, Vector3 pos, Vector3 look, int frames = 360)
{
  cam.transform.position = pos; cam.transform.LookAt(look); for (int i = 0; i < 30; i++) await Task.Yield();
  var ft = new List<float>(); for (int i = 0; i < frames; i++) { await Task.Yield(); ft.Add(Time.unscaledDeltaTime * 1000f); }
  ft.Sort(); float avg = ft.Average();
#if UNITY_EDITOR
  var st = $" | batches {UnityEditor.UnityStats.batches}, setpass {UnityEditor.UnityStats.setPassCalls}, tris rendered {UnityEditor.UnityStats.triangles:N0}, verts {UnityEditor.UnityStats.vertices:N0}";
#else
  var st = "";
#endif
  return $"{label}: avg {avg:0.00} ms ({1000f / avg:0} fps), p50 {ft[ft.Count / 2]:0.00}, p95 {ft[(int)(ft.Count * 0.95f)]:0.00}, p99 {ft[(int)(ft.Count * 0.99f)]:0.00} ms{st}";
}
var site = insp.patient.surgicalSite.position;
insp.ApplyPreset("Exposed");
sb.AppendLine(await Measure("surgeon view, knee exposed", site + new Vector3(-0.45f, 0.38f, -0.25f), site));
insp.ApplyPreset("Implanted");
sb.AppendLine(await Measure("surgeon view, implanted", site + new Vector3(-0.45f, 0.38f, -0.25f), site));
insp.ApplyPreset("Intact");
sb.AppendLine(await Measure("room overview (all assets in view)", new Vector3(-2.4f, 2.3f, -2.6f), new Vector3(0, 0.95f, 0)));
System.IO.File.WriteAllText("D:/Medical-Test/My project/Screenshots/Surgery/Anatomy/perf_editor.txt", sb.ToString());
return sb.ToString();
