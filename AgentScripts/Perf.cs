using UnityEngine; using UnityEngine.XR; using System.Threading.Tasks; using System.Linq; using UnityEngine.Rendering.Universal;
public static class Perf {
  public static async Task<string> Run(float seconds) {
    float t0 = Time.realtimeSinceStartup; int f0 = Time.frameCount; var ms = new System.Collections.Generic.List<float>();
    while (Time.realtimeSinceStartup - t0 < seconds) { ms.Add(Time.unscaledDeltaTime * 1000f); await Task.Yield(); }
    ms.Sort(); float avg = ms.Average(), p95 = ms[(int)(ms.Count * 0.95f)];
    var urp = (UniversalRenderPipelineAsset)UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
    var disp = new System.Collections.Generic.List<XRDisplaySubsystem>(); SubsystemManager.GetSubsystems(disp);
    string refresh = disp.Count > 0 && disp[0].TryGetDisplayRefreshRate(out float hz) ? hz.ToString("0") : "?";
    return $"frames={ms.Count} avgMs={avg:0.0} (≈{1000/avg:0} fps) p95Ms={p95:0.0} | XR active={XRSettings.isDeviceActive} device={XRSettings.loadedDeviceName} eyeTex={XRSettings.eyeTextureWidth}x{XRSettings.eyeTextureHeight} eyeScale={XRSettings.eyeTextureResolutionScale} viewport={XRSettings.renderViewportScale} refresh={refresh}Hz stereo={XRSettings.stereoRenderingMode} | URP={urp.name} renderScale={urp.renderScale} msaa={urp.msaaSampleCount} hdr={urp.supportsHDR} shadowRes={urp.mainLightShadowmapResolution} | quality={QualitySettings.names[QualitySettings.GetQualityLevel()]} aniso={QualitySettings.anisotropicFiltering} texLimit={QualitySettings.globalTextureMipmapLimit} vsync={QualitySettings.vSyncCount}";
  }
}
