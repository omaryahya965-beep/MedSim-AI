using UnityEngine; using UnityEngine.Rendering; using UnityEngine.Rendering.Universal; using System.Threading.Tasks; using System.Linq; using MedSimAI.Core;
public static class Bench2 {
  static async Task<string> Measure(Camera cam, string label, int eye, int msaa, bool hdr) {
    var rt = new RenderTexture(eye * 2, eye, 24, hdr ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.ARGB32) { antiAliasing = msaa };
    cam.targetTexture = rt; cam.allowHDR = hdr; cam.allowMSAA = msaa > 1;
    for (int i = 0; i < 20; i++) await Task.Yield();
    var gpu = new System.Collections.Generic.List<float>(); var t = new FrameTiming[1];
    for (int i = 0; i < 90; i++) { await Task.Yield(); FrameTimingManager.CaptureFrameTimings(); if (FrameTimingManager.GetLatestTimings(1, t) > 0 && t[0].gpuFrameTime > 0) gpu.Add((float)t[0].gpuFrameTime); }
    cam.targetTexture = null; rt.Release();
    return $"{label}: GPU avg {(gpu.Count > 0 ? gpu.Average() : -1):0.0} ms\n";
  }
  public static async Task<string> Run() {
    while (SessionManager.Instance == null) await Task.Yield();
    var mgr = UnityEngine.XR.Management.XRGeneralSettings.Instance?.Manager; if (mgr != null && mgr.isInitializationComplete) mgr.StopSubsystems();
    var main = Camera.main; if (main) main.enabled = false;
    var go = new GameObject("BenchCam"); var cam = go.AddComponent<Camera>(); cam.stereoTargetEye = StereoTargetEyeMask.None; cam.fieldOfView = 100;
    go.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
    cam.transform.position = new Vector3(1.2f, 1.62f, 0.65f); cam.transform.LookAt(new Vector3(0, 1.0f, 0.9f));
    PerformanceManager.Instance.Apply(GraphicsProfile.Balanced, false);
    var s = "";
    s += await Measure(cam, "Balanced asset, HDR on,  MSAA4", 1788, 4, true);
    s += await Measure(cam, "Balanced asset, HDR off, MSAA4", 1788, 4, false);
    s += await Measure(cam, "Balanced asset, HDR on,  MSAA1", 1788, 1, true);
    s += await Measure(cam, "Balanced asset, HDR off, MSAA1", 1788, 1, false);
    go.GetComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
    s += await Measure(cam, "Balanced asset, HDR off, MSAA4, no post", 1788, 4, false);
    foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) l.shadows = LightShadows.None;
    s += await Measure(cam, "Balanced asset, HDR off, MSAA4, no post, NO SHADOWS", 1788, 4, false);
    Object.Destroy(go); if (main) main.enabled = true;
    return s;
  }
}
