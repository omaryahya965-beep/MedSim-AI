using System.Linq;
using MedSimAI.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.OpenXR;

namespace MedSimAI.Tests
{
    public class Phase3Tests
    {
        // ---------- Graphics profiles ----------
        [Test] public void Profiles_AreOrdered_AndUseMeasuredSafeSettings()
        {
            var perf = PerformanceManager.SettingsFor(GraphicsProfile.Performance, false);
            var bal = PerformanceManager.SettingsFor(GraphicsProfile.Balanced, false);
            var high = PerformanceManager.SettingsFor(GraphicsProfile.HighQuality, false);
            Assert.Less(perf.renderScale, bal.renderScale); Assert.Less(bal.renderScale, high.renderScale);
            Assert.LessOrEqual(high.renderScale, 1.1f, "1.2 measured past the VRAM cliff on the RTX 3050 laptop");
            foreach (var s in new[] { perf, bal, high })
            {
                Assert.AreEqual(4, s.msaa, "4x MSAA in every VR profile");
                Assert.IsFalse(s.hdr, "HDR+MSAA4 stereo measured at 166 ms GPU — LDR in VR profiles");
            }
        }

        [Test] public void Android_NeverReceivesPcSettings()
        {
            foreach (GraphicsProfile p in System.Enum.GetValues(typeof(GraphicsProfile)))
            {
                var s = PerformanceManager.SettingsFor(p, android: true);
                Assert.AreEqual(1.0f, s.renderScale); Assert.AreEqual(1, s.shadowCascades); Assert.IsFalse(s.softShadows); Assert.IsFalse(s.bloom);
            }
        }

        [Test] public void VrRenderAssets_HaveNoSsao_NoDepthOpaqueCopies()
        {
            var pc = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/MedSimAI/Settings/MedSim_PCVR_RP.asset");
            Assert.NotNull(pc);
            Assert.IsFalse(pc.supportsCameraDepthTexture); Assert.IsFalse(pc.supportsCameraOpaqueTexture);
            Assert.AreEqual(4, pc.msaaSampleCount);
            var rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/MedSimAI/Settings/MedSim_PCVR_Renderer.asset");
            Assert.IsFalse(rd.rendererFeatures.Any(f => f && f.isActive && f.GetType().Name.Contains("ScreenSpaceAmbientOcclusion")));
        }

        // ---------- XR render settings ----------
        [TestCase(BuildTargetGroup.Standalone)]
        [TestCase(BuildTargetGroup.Android)]
        public void OpenXR_LoaderEnabled_WithFocus3Profile(BuildTargetGroup g)
        {
            var gs = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(g);
            Assert.NotNull(gs, g.ToString());
            Assert.IsTrue(gs.Manager.activeLoaders.Any(l => l.GetType().Name == "OpenXRLoader"));
            var s = OpenXRSettings.GetSettingsForBuildTargetGroup(g);
            Assert.IsTrue(s.GetFeatures().Any(f => f.enabled && f.GetType().Name == "VIVEFocus3Profile"));
            if (g == BuildTargetGroup.Standalone) Assert.AreEqual(OpenXRSettings.RenderMode.SinglePassInstanced, s.renderMode);
        }

        [Test] public void TextureImport_AnisotropicAndTrilinear_OnEnvironmentAndCharacters()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/MedSimAI/Environment/PolyHaven/terrazzo_tiles", "Assets/MedSimAI/Characters/AdamReyes/Textures" }))
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                Assert.GreaterOrEqual(ti.anisoLevel, 4, ti.assetPath); Assert.IsTrue(ti.mipmapEnabled);
            }
        }

        [Test] public void HairLashBrows_UseAlphaToCoverage()
        {
            foreach (var n in new[] { "AdamReyes_Hair", "AdamReyes_Eyelashes", "DrSaraHassan_Hair", "DrSaraHassan_Eyebrows" })
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>($"Assets/MedSimAI/Characters/{n.Split('_')[0]}/Materials/{n}.mat");
                Assert.NotNull(m, n); Assert.AreEqual(1f, m.GetFloat("_AlphaToMask"), n);
            }
        }
    }
}
