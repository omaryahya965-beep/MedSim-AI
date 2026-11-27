using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MedSimAI.Core
{
    public enum GraphicsProfile { Performance = 0, Balanced = 1, HighQuality = 2 }

    [Serializable] public struct GraphicsProfileSettings
    {
        public string label;
        public float renderScale;      // URP render scale (applies to XR eye textures)
        public int msaa;               // 1,2,4,8
        public bool hdr;
        public float shadowDistance;
        public int shadowCascades;
        public int mainShadowRes;      // 1024 / 2048 / 4096
        public bool additionalLightShadows;
        public bool softShadows;
        public bool bloom;
        public float lodBias;
        public AnisotropicFiltering aniso;
        public int textureMipLimit;    // 0 = full resolution
    }

    /// <summary>
    /// Three VR graphics profiles. URP assets are CLONED at runtime so the project assets are never modified.
    /// PCVR: Balanced (default) / High Quality / Performance. Android standalone: Performance only (never PC settings).
    /// Frame timing is measured, never assumed.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class PerformanceManager : MonoBehaviour
    {
        public const string PrefProfile = "medsim.gfxProfile";
        public static PerformanceManager Instance { get; private set; }
        public UniversalRenderPipelineAsset pcvrAsset, standaloneAsset;
        public Volume postVolume;
        public GraphicsProfile Active { get; private set; }
        public GraphicsProfileSettings ActiveSettings { get; private set; }
        public float AverageFrameMs { get; private set; }
        public float WorstFrameMs { get; private set; }
        public float GpuFrameMs { get; private set; }
        public event Action<GraphicsProfile> ProfileChanged;
        UniversalRenderPipelineAsset runtimeAsset;
        readonly System.Collections.Generic.Dictionary<Light, LightShadows> originalShadows = new System.Collections.Generic.Dictionary<Light, LightShadows>();
        float acc; int frames; float worst; float windowStart;
        readonly FrameTiming[] timings = new FrameTiming[1];

        public static bool IsStandaloneAndroid =>
#if UNITY_ANDROID && !UNITY_EDITOR
            true;
#else
            false;
#endif

        public static GraphicsProfileSettings SettingsFor(GraphicsProfile p, bool android)
        {
            if (android || p == GraphicsProfile.Performance)
                return new GraphicsProfileSettings { label = android ? "Performance (standalone)" : "Performance", renderScale = android ? 1.0f : 0.9f, msaa = 4, hdr = false, shadowDistance = 6f, shadowCascades = 1, mainShadowRes = 1024, additionalLightShadows = false, softShadows = false, bloom = false, lodBias = 1f, aniso = AnisotropicFiltering.Enable, textureMipLimit = 0 };
            if (p == GraphicsProfile.HighQuality)
                return new GraphicsProfileSettings { label = "High Quality", renderScale = 1.1f, msaa = 4, hdr = false, shadowDistance = 12f, shadowCascades = 2, mainShadowRes = 2048, additionalLightShadows = true, softShadows = true, bloom = true, lodBias = 2f, aniso = AnisotropicFiltering.ForceEnable, textureMipLimit = 0 };
            return new GraphicsProfileSettings { label = "Balanced", renderScale = 1.0f, msaa = 4, hdr = false, shadowDistance = 10f, shadowCascades = 2, mainShadowRes = 2048, additionalLightShadows = true, softShadows = true, bloom = true, lodBias = 1.5f, aniso = AnisotropicFiltering.ForceEnable, textureMipLimit = 0 };
        }

        void Awake()
        {
            Instance = this;
            FrameTimingManager.CaptureFrameTimings();
            var def = IsStandaloneAndroid ? GraphicsProfile.Performance : GraphicsProfile.Balanced;
            Apply((GraphicsProfile)PlayerPrefs.GetInt(PrefProfile, (int)def), persist: false);
        }

        public void Apply(GraphicsProfile p, bool persist = true)
        {
            if (IsStandaloneAndroid) p = GraphicsProfile.Performance;
            var s = SettingsFor(p, IsStandaloneAndroid);
            var src = IsStandaloneAndroid || p == GraphicsProfile.Performance ? (standaloneAsset ? standaloneAsset : pcvrAsset) : pcvrAsset;
            if (src)
            {
                if (runtimeAsset) Destroy(runtimeAsset);
                runtimeAsset = Instantiate(src); runtimeAsset.name = src.name + " (runtime " + s.label + ")";
                runtimeAsset.renderScale = s.renderScale;
                runtimeAsset.msaaSampleCount = s.msaa;
                runtimeAsset.supportsHDR = s.hdr;
                runtimeAsset.shadowDistance = s.shadowDistance;
                runtimeAsset.shadowCascadeCount = s.shadowCascades;
                runtimeAsset.mainLightShadowmapResolution = s.mainShadowRes;
                runtimeAsset.additionalLightsShadowmapResolution = 1024; // only the exam spot casts realtime shadows (measured: more shadow maps hit a VRAM cliff on 4 GB laptop GPUs)
                GraphicsSettings.defaultRenderPipeline = runtimeAsset;
                QualitySettings.renderPipeline = runtimeAsset;
            }
            QualitySettings.vSyncCount = 0; // XR runtime paces frames
            QualitySettings.lodBias = s.lodBias;
            QualitySettings.anisotropicFiltering = s.aniso;
            QualitySettings.globalTextureMipmapLimit = s.textureMipLimit;
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                {
                    if (!originalShadows.ContainsKey(l)) originalShadows[l] = l.shadows;
                    var orig = originalShadows[l];
                    if (orig == LightShadows.None || l.bakingOutput.lightmapBakeType == LightmapBakeType.Baked) continue; // baked lights: shadows live in the lightmap
                    l.shadows = s.softShadows ? LightShadows.Soft : LightShadows.Hard; // soft vs hard is per light in URP 17
                }
            if (!postVolume) postVolume = FindAnyObjectByType<Volume>();
            if (postVolume && postVolume.sharedProfile && postVolume.sharedProfile.TryGet<Bloom>(out var bloom)) bloom.active = s.bloom;
            Active = p; ActiveSettings = s;
            if (persist) { PlayerPrefs.SetInt(PrefProfile, (int)p); PlayerPrefs.Save(); }
            ProfileChanged?.Invoke(p);
        }

        void Update()
        {
            float ms = Time.unscaledDeltaTime * 1000f;
            acc += ms; frames++; worst = Mathf.Max(worst, ms);
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, timings) > 0 && timings[0].gpuFrameTime > 0) GpuFrameMs = (float)timings[0].gpuFrameTime;
            if (Time.unscaledTime - windowStart > 2f)
            {
                AverageFrameMs = acc / Mathf.Max(1, frames); WorstFrameMs = worst;
                acc = 0; frames = 0; worst = 0; windowStart = Time.unscaledTime;
            }
        }

        void OnDestroy() { if (runtimeAsset) Destroy(runtimeAsset); }
    }
}
