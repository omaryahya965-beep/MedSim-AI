using UnityEngine; using UnityEngine.Rendering; using UnityEngine.Rendering.Universal; using System.Threading.Tasks; using System.Linq; using MedSimAI.Core;
public static class Bench4 {
  static async Task<float> Measure(Camera cam, int eye) {
    var rt = new RenderTexture(eye * 2, eye, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
    cam.targetTexture = rt; for (int i = 0; i < 20; i++) await Task.Yield();
    var gpu = new System.Collections.Generic.List<float>(); var t = new FrameTiming[1];
    for (int i = 0; i < 120; i++) { await Task.Yield(); FrameTimingManager.CaptureFrameTimings(); if (FrameTimingManager.GetLatestTimings(1, t) > 0 && t[0].gpuFrameTime > 0) gpu.Add((float)t[0].gpuFrameTime); }
    cam.targetTexture = null; rt.Release(); gpu.Sort(); return gpu.Count > 0 ? gpu[gpu.Count / 2] : -1;
  }
  public static async Task<string> Run() {
    while (SessionManager.Instance == null) await Task.Yield();
    var mgr = UnityEngine.XR.Management.XRGeneralSettings.Instance?.Manager; if (mgr != null && mgr.isInitializationComplete) mgr.StopSubsystems();
    var main = Camera.main; if (main) main.enabled = false;
    var go = new GameObject("BenchCam"); var cam = go.AddComponent<Camera>(); cam.stereoTargetEye = StereoTargetEyeMask.None; cam.fieldOfView = 100;
    go.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
    cam.transform.position = new Vector3(1.2f, 1.62f, 0.65f); cam.transform.LookAt(new Vector3(0, 1.0f, 0.9f));
    PerformanceManager.Instance.Apply(GraphicsProfile.Balanced, false);
    var asset = (UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline; asset.additionalLightsShadowmapResolution = 1024;
    foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) l.shadows = l.name == "Exam Spot" ? LightShadows.Soft : LightShadows.None;
    string s = "";
    foreach (var sc in new[] { 1.0f, 1.1f, 1.2f, 1.0f, 1.1f, 1.2f }) s += $"scale {sc:0.0} (eye {Mathf.RoundToInt(1788 * sc)}): median GPU {await Measure(cam, Mathf.RoundToInt(1788 * sc)):0.0} ms\n";
    Object.Destroy(go); if (main) main.enabled = true;
    return s;
  }
}
