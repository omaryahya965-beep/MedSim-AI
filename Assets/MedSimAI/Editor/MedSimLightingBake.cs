using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MedSimAI.EditorTools
{
    /// <summary>Configures baked lighting (GPU progressive lightmapper, Baked Indirect) for the ER scene and bakes it.</summary>
    public static class MedSimLightingBake
    {
        [MenuItem("MedSim AI/Bake Lighting")]
        public static void BakeMenu() => _ = Bake();

        public static async Task<string> Bake(int maxSeconds = 1500)
        {
            // Lightmap UVs on imported models
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/MedSimAI/Environment" }))
            {
                var mi = (ModelImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                if (!mi.generateSecondaryUV) { mi.generateSecondaryUV = true; mi.SaveAndReimport(); }
            }
            var settings = Lightmapping.lightingSettingsDefaults;
            var path = "Assets/MedSimAI/Generated/MedSim_LightingSettings.lighting";
            var ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(path);
            if (!ls) { ls = new LightingSettings(); AssetDatabase.CreateAsset(ls, path); }
            ls.bakedGI = true; ls.realtimeGI = false;
            ls.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
            ls.mixedBakeMode = MixedLightingMode.IndirectOnly;
            ls.lightmapResolution = 22f; ls.lightmapPadding = 4; ls.lightmapMaxSize = 2048;
            ls.directSampleCount = 48; ls.indirectSampleCount = 256; ls.environmentSampleCount = 128; ls.maxBounces = 3;
            ls.ao = true; ls.aoMaxDistance = 0.6f; ls.aoExponentIndirect = 1f; ls.aoExponentDirect = 0.2f;
            ls.filteringMode = LightingSettings.FilterMode.Auto; ls.lightmapCompression = LightmapCompression.NormalQuality;
            ls.directionalityMode = LightmapsMode.CombinedDirectional; // keeps normal-map detail under baked direct light
            Lightmapping.lightingSettings = ls;

            // Ceiling panels and fixtures: lights Mixed so characters/instruments still get realtime direct light + shadows.
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) { bool exam = l.name == "Exam Spot"; l.lightmapBakeType = exam ? LightmapBakeType.Mixed : LightmapBakeType.Baked; if (!exam) l.shadows = LightShadows.Soft; } // room lights fully baked (direct+shadows); exam spot realtime for characters
            // Dynamic objects must not contribute GI
            foreach (var r in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None)) GameObjectUtility.SetStaticEditorFlags(r.gameObject, 0);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (!Lightmapping.BakeAsync()) return "BakeAsync refused to start";
            while (Lightmapping.isRunning && sw.Elapsed.TotalSeconds < maxSeconds) await Task.Delay(1000);
            if (Lightmapping.isRunning) { Lightmapping.Cancel(); return $"Bake cancelled after {maxSeconds}s"; }
            UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
            return $"Baked in {sw.Elapsed.TotalSeconds:0}s; lightmaps={LightmapSettings.lightmaps.Length}; probes={(LightmapSettings.lightProbes ? LightmapSettings.lightProbes.count : 0)}";
        }
    }
}
