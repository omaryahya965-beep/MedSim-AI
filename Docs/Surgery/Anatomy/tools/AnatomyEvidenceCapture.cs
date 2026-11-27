// Visual evidence for the Person 2 surgical assets (run in PLAY MODE in Assets/MedSimAI/Surgery/Scenes/Anatomy_Test.unity):
//   unity command script eval -- --file Docs/Surgery/Anatomy/tools/AnatomyEvidenceCapture.cs --timeout 900
// Renders 1600x900 PNGs (URP, post-processing, SMAA) to Screenshots/Surgery/Anatomy/.
using UnityEngine; using System; using System.IO; using System.Linq; using System.Threading.Tasks; using System.Collections.Generic;
using MedSimAI.Surgery.Anatomy; using MedSimAI.Surgery.AssetsInspector; using MedSimAI.Surgery.Instruments; using MedSimAI.Surgery.Shared;
string outDir = "D:/Medical-Test/My project/Screenshots/Surgery/Anatomy/"; Directory.CreateDirectory(outDir);
var log = new List<string>();
async Task W(float s) { float t = Time.realtimeSinceStartup + s; while (Time.realtimeSinceStartup < t) await Task.Yield(); }
await W(1.0f);
var insp = UnityEngine.Object.FindAnyObjectByType<AnatomyAssetInspector>(); insp.showPanel = false;
var knee = insp.knee; var patient = insp.patient; var disp = insp.explodedKnee; var dispCtl = disp.GetComponent<KneeAnatomyStateController>();
var camGo = new GameObject("EvidenceCam"); var cam = camGo.AddComponent<Camera>(); cam.nearClipPlane = 0.01f; cam.enabled = false;
var data = camGo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>(); data.renderPostProcessing = true; data.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
insp.cam.enabled = false;
void Shot(string name, Vector3 pos, Vector3 look, float fov = 40)
{
  cam.transform.position = pos; cam.transform.LookAt(look); cam.fieldOfView = fov;
  var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.DefaultHDR) { antiAliasing = 8 }; cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
  var ldr = RenderTexture.GetTemporary(1600, 900, 0, RenderTextureFormat.ARGB32); Graphics.Blit(rt, ldr);
  RenderTexture.active = ldr; var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); tex.Apply(); RenderTexture.active = null;
  File.WriteAllBytes(outDir + name + ".png", tex.EncodeToPNG()); UnityEngine.Object.Destroy(tex); rt.Release(); RenderTexture.ReleaseTemporary(ldr); log.Add(name);
}
void Cat(Transform r, AnatomyCategory c, bool on) { foreach (var s in r.GetComponentsInChildren<AnatomyStructure>(true)) if (s.category == c) foreach (var rr in s.GetComponentsInChildren<Renderer>(true)) rr.enabled = on; }
void OnlyCats(Transform r, params AnatomyCategory[] cs) { foreach (AnatomyCategory c in Enum.GetValues(typeof(AnatomyCategory))) Cat(r, c, cs.Contains(c)); }
void AllCats(Transform r) { foreach (AnatomyCategory c in Enum.GetValues(typeof(AnatomyCategory))) Cat(r, c, true); }
var site = patient.surgicalSite.position; var dk = disp.position;
var standRenderers = disp.parent.GetComponentsInChildren<Renderer>(true).Where(r => !r.transform.IsChildOf(disp)).ToArray();
// ---------------------------------------------------------------- patient
patient.SetDrapeVisible(false);
Shot("01_surgical_patient_complete", new Vector3(-2.05f, 1.75f, -0.35f), new Vector3(0, 1.05f, -0.05f), 38);
Shot("02_patient_on_temporary_table", new Vector3(-1.9f, 1.35f, -1.9f), new Vector3(0, 0.95f, -0.05f), 40);
patient.SetDrapeVisible(true);
Shot("03_surgical_drape", new Vector3(-1.75f, 1.95f, -1.45f), new Vector3(0, 1.0f, -0.1f), 40);
Shot("03b_draped_knee_surgeon_view", site + new Vector3(-0.62f, 0.42f, -0.32f), site, 40);
// ---------------------------------------------------------------- knee anatomy (display knee: no skin)
foreach (var r in standRenderers) r.enabled = false;
// cameras from the display knee's own anatomical frame (anchors), so the flexed knee is framed correctly
Transform A(string n) => disp.GetComponentsInChildren<Transform>(true).First(t => t.name == n);
var fr = A("FemurRoot"); var tr = A("TibiaRoot");
Vector3 ant = (fr.forward + tr.forward).normalized, lat = fr.right, prox = fr.up, dst = -tr.up;
Vector3 Cm(float a, float l, float p, float d = 0) => dk + ant * a + lat * l + prox * p + dst * d;
var dkc = dk + ant * 0.01f;
Shot("04_knee_anatomy_complete", Cm(0.24f, 0.20f, 0.06f), dkc, 38);
Shot("05_knee_front", Cm(0.34f, 0.04f, 0.03f, 0.03f), dkc, 34);
Shot("06_knee_lateral", Cm(0.06f, 0.36f, 0.0f), dkc, 36);
Shot("07_knee_medial_posterior", Cm(-0.20f, -0.28f, -0.02f), dkc, 38);
OnlyCats(disp, AnatomyCategory.Bone); Shot("08_individual_bones", Cm(0.28f, 0.28f, 0.10f), dkc, 42);
OnlyCats(disp, AnatomyCategory.Cartilage, AnatomyCategory.Meniscus); Shot("09_cartilage_and_menisci", Cm(0.17f, 0.10f, 0.05f, 0.05f), dkc, 34);
OnlyCats(disp, AnatomyCategory.Bone, AnatomyCategory.Ligament); Shot("10_ligaments_with_bones", Cm(0.10f, -0.26f, 0.0f, 0.02f), dkc, 36);
OnlyCats(disp, AnatomyCategory.Ligament, AnatomyCategory.Meniscus, AnatomyCategory.Tendon); Shot("10b_ligaments_tendons_menisci_only", Cm(0.20f, 0.16f, 0.02f), dkc, 36);
AllCats(disp); Cat(disp, AnatomyCategory.Skin, false); Cat(disp, AnatomyCategory.SkinOverlay, false);
insp.explode = 1f; await W(0.3f); Shot("11_exploded_anatomy", Cm(0.36f, 0.30f, 0.06f), dkc, 46); insp.explode = 0f; await W(0.3f);
foreach (var r in standRenderers) r.enabled = true;
// ---------------------------------------------------------------- implants & instruments
var set = GameObject.Find("KneeImplantSet").transform;
Shot("12_implant_components", set.position + new Vector3(0.02f, 0.26f, -0.30f), set.position + new Vector3(0, 0.025f, 0), 40);
var tray = GameObject.Find("InstrumentTray_Populated").transform;
Shot("13_instrument_collection", tray.position + new Vector3(0.0f, 0.70f, -0.35f), tray.position + new Vector3(0, 0.02f, 0), 46);
Shot("14_instruments_closeup", tray.position + new Vector3(-0.30f, 0.30f, -0.30f), tray.position + new Vector3(-0.12f, 0.05f, 0.0f), 40);
foreach (var id in new[] { "inst.oscillating_saw", "inst.needle_driver", "inst.cutting_guide_femoral", "inst.scalpel", "inst.retractor", "inst.skin_stapler" })
{
  var it = tray.GetComponentsInChildren<InstrumentAssetInfo>(true).First(x => x.name.StartsWith(id)); var rs = it.GetComponentsInChildren<Renderer>(); var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
  float d = Mathf.Max(0.12f, b.extents.magnitude * 2.4f);
  Shot("14_closeup_" + id.Substring(5), b.center + new Vector3(-0.35f, 0.75f, -0.55f).normalized * d, b.center, 34);
}
insp.SetAnchors(true);
Shot("15_instrument_grip_and_tip_anchors", tray.position + new Vector3(0.18f, 0.40f, -0.25f), tray.position + new Vector3(0.13f, 0.02f, 0.0f), 36);
// implant anchors on the knee: resected state + placement targets
dispCtl.SetTargetState(AnatomyTargetIds.JointCapsule, AnatomyStates.Exposed); dispCtl.SetTargetState(AnatomyTargetIds.DistalFemur, AnatomyStates.Resected); dispCtl.SetTargetState(AnatomyTargetIds.ProximalTibia, AnatomyStates.Resected); await W(0.8f);
foreach (var r in standRenderers) r.enabled = false;
Shot("16_implant_placement_anchors", Cm(0.22f, 0.17f, 0.02f), dkc, 34);
insp.SetAnchors(false);
dispCtl.showComponentVisuals = true; dispCtl.SetTargetState(AnatomyTargetIds.DistalFemur, AnatomyStates.Implanted); dispCtl.SetTargetState(AnatomyTargetIds.ProximalTibia, AnatomyStates.Implanted); await W(0.2f);
Shot("16b_components_seated_at_targets", Cm(0.22f, 0.17f, 0.02f), dkc, 34);
dispCtl.ResetAll(); dispCtl.showComponentVisuals = false; dispCtl.Refresh(true);
foreach (var r in standRenderers) r.enabled = true;
// ---------------------------------------------------------------- anatomical state compatibility (on the patient, surgeon view)
knee.showComponentVisuals = true;
int k = 0;
foreach (var p in AnatomyAssetInspector.Presets)
{
  insp.ApplyPreset(p.name); await W(0.8f);
  Shot($"17_{k++:00}_state_{p.name.ToLower().Replace(' ', '_')}", site + new Vector3(-0.20f, 0.40f, -0.16f), site + new Vector3(0, 0.01f, 0.0f), 32);
}
insp.ApplyPreset("Intact"); knee.showComponentVisuals = false; knee.Refresh(true);
// ---------------------------------------------------------------- final scene
Shot("18_asset_demonstration_scene", new Vector3(-2.4f, 2.3f, -2.6f), new Vector3(0.0f, 0.95f, 0.0f), 42);
UnityEngine.Object.Destroy(camGo); insp.cam.enabled = true;
File.WriteAllLines(outDir + "evidence_log.txt", log);
return string.Join("\n", log);
