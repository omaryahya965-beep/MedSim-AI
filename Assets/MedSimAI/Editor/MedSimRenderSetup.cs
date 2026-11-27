using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MedSimAI.EditorTools
{
    /// <summary>
    /// Creates dedicated VR render assets (copies of the template PC/Mobile assets, tuned for stereo VR):
    /// no SSAO (baked AO used instead), no depth/opaque texture copies (unused, costly with MSAA), Forward path,
    /// medium soft-shadow quality, GPU Resident Drawer off. Assigns them to the PC and Mobile quality levels.
    /// </summary>
    public static class MedSimRenderSetup
    {
        const string Dir = "Assets/MedSimAI/Settings";

        [MenuItem("MedSim AI/Create VR Render Assets")]
        public static string Create()
        {
            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/MedSimAI", "Settings");
            var pc = MakePair("Assets/Settings/PC_RPAsset.asset", "Assets/Settings/PC_Renderer.asset", "MedSim_PCVR", hdr: true);
            var mob = MakePair("Assets/Settings/Mobile_RPAsset.asset", "Assets/Settings/Mobile_Renderer.asset", "MedSim_Standalone", hdr: false);
            // Point the PerformanceManager in the scene + quality levels at the VR assets
            var names = QualitySettings.names; int cur = QualitySettings.GetQualityLevel();
            for (int i = 0; i < names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = names[i].ToLower().Contains("mobile") ? mob : pc;
            }
            QualitySettings.SetQualityLevel(cur, true);
            GraphicsSettings.defaultRenderPipeline = pc;
            foreach (var pm in Object.FindObjectsByType<MedSimAI.Core.PerformanceManager>(FindObjectsSortMode.None)) { pm.pcvrAsset = pc; pm.standaloneAsset = mob; EditorUtility.SetDirty(pm); }
            AssetDatabase.SaveAssets();
            return $"Created {AssetDatabase.GetAssetPath(pc)} and {AssetDatabase.GetAssetPath(mob)}";
        }

        static UniversalRenderPipelineAsset MakePair(string srcAsset, string srcRenderer, string name, bool hdr)
        {
            string ap = $"{Dir}/{name}_RP.asset", rp = $"{Dir}/{name}_Renderer.asset";
            if (!AssetDatabase.LoadAssetAtPath<Object>(rp)) AssetDatabase.CopyAsset(srcRenderer, rp);
            if (!AssetDatabase.LoadAssetAtPath<Object>(ap)) AssetDatabase.CopyAsset(srcAsset, ap);
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rp);
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(ap);

            // Renderer: Forward, no SSAO feature
            var so = new SerializedObject(renderer);
            so.FindProperty("m_RenderingMode").intValue = 0;          // Forward
            so.FindProperty("m_DepthPrimingMode").intValue = 0;       // Disabled
            so.ApplyModifiedPropertiesWithoutUndo();
            foreach (var f in renderer.rendererFeatures.ToList())
                if (f && f.GetType().Name.Contains("ScreenSpaceAmbientOcclusion")) { f.SetActive(false); EditorUtility.SetDirty(f); }
            EditorUtility.SetDirty(renderer);

            // Asset: point at our renderer, drop unused copies, tune shadows
            var sa = new SerializedObject(asset);
            var list = sa.FindProperty("m_RendererDataList");
            list.arraySize = 1; list.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
            sa.FindProperty("m_DefaultRendererIndex").intValue = 0;
            sa.FindProperty("m_RequireDepthTexture").boolValue = false;
            sa.FindProperty("m_RequireOpaqueTexture").boolValue = false;
            sa.FindProperty("m_SupportsHDR").boolValue = false; // HDR+MSAA4 stereo measured at 166 ms GPU on the RTX 3050 laptop (VRAM pressure); LDR keeps MSAA affordable
            sa.FindProperty("m_MSAA").intValue = 4;
            sa.FindProperty("m_RenderScale").floatValue = 1f;
            var soft = sa.FindProperty("m_SoftShadowQuality"); if (soft != null) soft.intValue = 2; // Medium
            var grd = sa.FindProperty("m_GPUResidentDrawerMode"); if (grd != null) grd.intValue = 0;
            var ap2 = sa.FindProperty("m_UseAdaptivePerformance"); if (ap2 != null) ap2.boolValue = false;
            sa.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            return asset;
        }
    }
}
