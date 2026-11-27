using UnityEngine; using UnityEngine.Rendering; using UnityEngine.Rendering.Universal; using System.Threading.Tasks; using System.Linq; using MedSimAI.Core;
public static class Bench3 {
  static async Task<float> Measure(Camera cam, int eye) {
    var rt = new RenderTexture(eye * 2, eye, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
    cam.targetTexture = rt; for (int i = 0; i < 20; i++) await Task.Yield();
    var gpu = new System.Collections.Generic.List<float>(); var t = new FrameTiming[1];
    for (int i = 0; i < 90; i++) { await Task.Yield(); FrameTimingManager.CaptureFrameTimings(); if (FrameTimingManager.GetLatestTimings(1, t) > 0 && t[0].gpuFrameTime > 0) gpu.Add((float)t[0].gpuFrameTime); }
    cam.targetTexture = null; rt.Release(); return gpu.Count > 0 ? gpu.Average() : -1;
  }
  public static async Task<string> Run() {
    while (SessionManager.Instance == null) await Task.Yield();
    var mgr = UnityEngine.XR.Management.XRGeneralSettings.Instance?.Manager; if (mgr != null && mgr.isInitializationComplete) mgr.StopSubsystems();
    var main = Camera.main; if (main) main.enabled = false;
    var go = new GameObject("BenchCam"); var cam = go.AddComponent<Camera>(); cam.stereoTargetEye = StereoTargetEyeMask.None; cam.fieldOfView = 100;
    go.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
    cam.transform.position = new Vector3(1.2f, 1.62f, 0.65f); cam.transform.LookAt(new Vector3(0, 1.0f, 0.9f));
    PerformanceManager.Instance.Apply(GraphicsProfile.Balanced, false);
    var lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
    var sun = lights.First(l => l.type == LightType.Directional); var exam = lights.First(l => l.name == "Exam Spot"); var ceil = lights.Where(l => l != exam && l.type == LightType.Spot).ToArray();
    string s = "";
    void Set(LightShadows sunS, LightShadows examS, LightShadows ceilS) { sun.shadows = sunS; exam.shadows = examS; foreach (var c in ceil) c.shadows = c.name == "Light" && c.transform.parent.name.EndsWith("2") ? ceilS : LightShadows.None; }
    Set(LightShadows.Soft, LightShadows.Soft, LightShadows.Soft); s += $"all soft: {await Measure(cam, 1788):0.0} ms\n";
    Set(LightShadows.Hard, LightShadows.Hard, LightShadows.Hard); s += $"all hard: {await Measure(cam, 1788):0.0} ms\n";
    Set(LightShadows.None, LightShadows.Soft, LightShadows.None); s += $"exam spot soft only: {await Measure(cam, 1788):0.0} ms\n";
    Set(LightShadows.None, LightShadows.Hard, LightShadows.None); s += $"exam spot hard only: {await Measure(cam, 1788):0.0} ms\n";
    Set(LightShadows.Soft, LightShadows.None, LightShadows.None); s += $"sun soft only: {await Measure(cam, 1788):0.0} ms\n";
    Set(LightShadows.None, LightShadows.None, LightShadows.None); s += $"no realtime shadows: {await Measure(cam, 1788):0.0} ms\n";
    Set(LightShadows.None, LightShadows.Soft, LightShadows.None); s += $"exam soft @1.2 scale: {await Measure(cam, 2146):0.0} ms\n";
    Object.Destroy(go); if (main) main.enabled = true;
    return s;
  }
}
