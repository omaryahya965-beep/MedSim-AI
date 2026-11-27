using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MedSimAI.EditorTools
{
    /// <summary>Applies PCVR + VIVE Focus 3 standalone player, quality and import settings. Re-runnable. Does not build.</summary>
    public static class MedSimProjectSetup
    {
        [MenuItem("MedSim AI/Configure Platforms (PCVR + Android)")]
        public static string Configure()
        {
            var log = new System.Text.StringBuilder();
            PlayerSettings.colorSpace = ColorSpace.Linear;

            // ---- Android (VIVE Focus 3 standalone) ----
            var android = NamedBuildTarget.Android;
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3, GraphicsDeviceType.Vulkan });
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
            PlayerSettings.SetManagedStrippingLevel(android, ManagedStrippingLevel.Low);
            PlayerSettings.Android.forceInternetPermission = true; // AI proxy (HTTPS) in Live AI mode
            PlayerSettings.Android.forceSDCardPermission = false;
            log.AppendLine("Android: IL2CPP, ARM64, API 29+, GLES3+Vulkan, Linear, ASTC");

            // ---- Windows PCVR ----
            var win = NamedBuildTarget.Standalone;
            PlayerSettings.SetScriptingBackend(win, ScriptingImplementation.Mono2x);
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });
            PlayerSettings.runInBackground = true;
            log.AppendLine("Windows: Mono, D3D11 (OpenXR/SteamVR/VIVE Business Streaming)");

            // Microphone usage text (shown by platforms that ask for it)
            PlayerSettings.iOS.microphoneUsageDescription = "Push-to-talk voice questions to the simulated patient and supervisor. Audio is processed in memory only.";

            // ---- URP quality profiles ----
            var pc = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            var mobile = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/Mobile_RPAsset.asset");
            if (pc) { pc.msaaSampleCount = 4; pc.renderScale = 1f; pc.supportsHDR = true; pc.shadowDistance = 12f; EditorUtility.SetDirty(pc); log.AppendLine("PCVR URP: MSAA 4x, HDR, shadows 12 m"); }
            if (mobile) { mobile.msaaSampleCount = 4; mobile.renderScale = 1f; mobile.supportsHDR = false; mobile.shadowDistance = 6f; mobile.shadowCascadeCount = 1; EditorUtility.SetDirty(mobile); log.AppendLine("Standalone URP: MSAA 4x, no HDR, shadows 6 m, 1 cascade"); }
            // Quality levels: per-platform default (PC → PC asset, Android → Mobile asset)
            var names = QualitySettings.names;
            for (int i = 0; i < names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                var lower = names[i].ToLowerInvariant();
                if (lower.Contains("mobile") && mobile) QualitySettings.renderPipeline = mobile;
                if (lower.Contains("pc") && pc) QualitySettings.renderPipeline = pc;
            }
            int pcIdx = System.Array.FindIndex(names, n => n.ToLowerInvariant().Contains("pc"));
            int mobIdx = System.Array.FindIndex(names, n => n.ToLowerInvariant().Contains("mobile"));
            if (pcIdx >= 0) QualitySettings.SetQualityLevel(pcIdx, true);
            log.AppendLine("Quality levels: " + string.Join(", ", names) + $" (PC idx {pcIdx}, Mobile idx {mobIdx})");

            // ---- Audio imports: voices mono Vorbis, streamed-in-background ----
            int voices = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/MedSimAI/Resources/Audio" }))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                var ai = (AudioImporter)AssetImporter.GetAtPath(p);
                ai.forceToMono = true; ai.loadInBackground = true;
                var s = ai.defaultSampleSettings; s.compressionFormat = AudioCompressionFormat.Vorbis; s.quality = 0.55f; s.loadType = AudioClipLoadType.CompressedInMemory; s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                ai.defaultSampleSettings = s; ai.SaveAndReimport(); voices++;
            }
            log.AppendLine($"Voice clips configured: {voices}");

            // ---- Arabic font (Noto Sans Arabic, OFL) as a TMP fallback for shaped RTL text ----
            const string ttf = "Assets/MedSimAI/Art/Fonts/NotoSansArabic-Regular.ttf", fa = "Assets/MedSimAI/Art/Fonts/NotoSansArabic SDF.asset";
            var font = AssetDatabase.LoadAssetAtPath<Font>(ttf);
            if (font)
            {
                var asset = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(fa);
                if (!asset)
                {
                    asset = TMPro.TMP_FontAsset.CreateFontAsset(font, 64, 6, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024, TMPro.AtlasPopulationMode.Dynamic, true);
                    asset.name = "NotoSansArabic SDF";
                    AssetDatabase.CreateAsset(asset, fa);
                    AssetDatabase.AddObjectToAsset(asset.material, asset);
                    foreach (var tex in asset.atlasTextures) AssetDatabase.AddObjectToAsset(tex, asset);
                }
                var settings = TMPro.TMP_Settings.instance;
                var so = new SerializedObject(settings);
                var list = so.FindProperty("m_fallbackFontAssets");
                bool present = false; for (int i = 0; i < list.arraySize; i++) if (list.GetArrayElementAtIndex(i).objectReferenceValue == asset) present = true;
                if (!present) { list.InsertArrayElementAtIndex(list.arraySize); list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = asset; so.ApplyModifiedProperties(); }
                log.AppendLine("Arabic TMP fallback font: " + (present ? "already registered" : "registered"));
            }
            AssetDatabase.SaveAssets();
            return log.ToString();
        }
    }
}
