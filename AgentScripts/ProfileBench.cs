using UnityEngine; using UnityEngine.Rendering; using UnityEngine.Rendering.Universal; using System.Threading.Tasks; using System.Linq; using MedSimAI.Core;
public static class ProfileBench {
  public static async Task<string> Run() {
    while (MedSimAI.Core.SessionManager.Instance == null) await Task.Yield();
    var mgr = UnityEngine.XR.Management.XRGeneralSettings.Instance?.Manager; if (mgr != null && mgr.isInitializationComplete) mgr.StopSubsystems();
    await Task.Delay(500);
    var main = Camera.main; if (main) main.enabled = false;
    var go = new GameObject("BenchCam"); var cam = go.AddComponent<Camera>(); cam.stereoTargetEye = StereoTargetEyeMask.None; cam.fieldOfView = 100; cam.nearClipPlane = 0.02f;
    go.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
    cam.transform.position = new Vector3(1.2f, 1.62f + 0.0f, 0.65f); cam.transform.LookAt(new Vector3(0, 1.0f, 0.9f));
    var sb = new System.Text.StringBuilder();
    var timings = new FrameTiming[1];
    foreach (GraphicsProfile p in new[] { GraphicsProfile.Performance, GraphicsProfile.Balanced, GraphicsProfile.HighQuality }) {
      PerformanceManager.Instance.Apply(p, persist: false);
      var s = PerformanceManager.Instance.ActiveSettings;
      // two eyes side-by-side at the runtime's recommended 1788x1788 per eye, scaled by the profile render scale
      int eye = Mathf.RoundToInt(1788 * s.renderScale);
      var rt = new RenderTexture(eye * 2, eye, 24, s.hdr ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.ARGB32) { antiAliasing = s.msaa };
      cam.targetTexture = rt; cam.allowMSAA = true; cam.allowHDR = s.hdr;
      for (int i = 0; i < 30; i++) await Task.Yield(); // warm-up
      var cpu = new System.Collections.Generic.List<float>(); var gpu = new System.Collections.Generic.List<float>();
      for (int i = 0; i < 180; i++) {
        await Task.Yield();
        cpu.Add(Time.unscaledDeltaTime * 1000f);
        FrameTimingManager.CaptureFrameTimings();
        if (FrameTimingManager.GetLatestTimings(1, timings) > 0 && timings[0].gpuFrameTime > 0) gpu.Add((float)timings[0].gpuFrameTime);
      }
      cpu.Sort(); gpu.Sort();
      sb.AppendLine($"{s.label}: eye {eye}x{eye} msaa{s.msaa} hdr={s.hdr} shadows={s.mainShadowRes}/{s.shadowCascades}c | frame avg {cpu.Average():0.0} ms p95 {cpu[(int)(cpu.Count*0.95f)]:0.0} | GPU avg {(gpu.Count>0?gpu.Average():-1):0.0} ms p95 {(gpu.Count>0?gpu[(int)(gpu.Count*0.95f)]:-1):0.0} (n={gpu.Count})");
      cam.targetTexture = null; rt.Release();
    }
    Object.Destroy(go); if (main) main.enabled = true;
    PerformanceManager.Instance.Apply(GraphicsProfile.Balanced, persist: false);
    return sb.ToString();
  }
}
