using System.IO;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;

namespace MedSimAI.Hospital.EditorTools
{
    /// <summary>
    /// Integrated builds (the hospital first, the original clinical scene kept for -clinical-only). They go to new folders,
    /// so the v0.6 builds are never overwritten. Desktop removes the OpenXR loader for its build only (always starts in
    /// Desktop Mode, even with SteamVR running) and restores it afterwards; PCVR keeps OpenXR (VIVE Focus 3 via streaming).
    /// No Android build here.
    /// </summary>
    public static class HospitalBuild
    {
        public const string DesktopPath = "D:/VRBuilds/MedSimAI_Integrated_Desktop/MedSimAI.exe";
        public const string PcvrPath = "D:/Medical-Test/Builds/MedSimAI_Integrated_PCVR/MedSimAI.exe";
        static readonly string[] Scenes = { HospitalBuilder.HospitalScene, HospitalBuilder.ErScene };
        const string OpenXrLoader = "UnityEngine.XR.OpenXR.OpenXRLoader";

        [MenuItem("MedSim AI/Hospital/Build Desktop (integrated)")] public static void DesktopMenu() => UnityEngine.Debug.Log(BuildDesktop());
        [MenuItem("MedSim AI/Hospital/Build PCVR (integrated)")] public static void PcvrMenu() => UnityEngine.Debug.Log(BuildPcvr());

        public static string BuildDesktop(string path = DesktopPath)
        {
            var g = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
            bool removed = XRPackageMetadataStore.RemoveLoader(g.Manager, OpenXrLoader, BuildTargetGroup.Standalone);
            try { return "Desktop: " + Build(path); }
            finally
            {
                if (removed) XRPackageMetadataStore.AssignLoader(g.Manager, OpenXrLoader, BuildTargetGroup.Standalone);
                EditorUtility.SetDirty(g.Manager); AssetDatabase.SaveAssets();
            }
        }

        public static string BuildPcvr(string path = PcvrPath) => "PCVR: " + Build(path);

        static string Build(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var r = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = Scenes, locationPathName = path, target = BuildTarget.StandaloneWindows64, options = BuildOptions.None });
            var s = r.summary;
            return $"{s.result} → {path} · errors={s.totalErrors} warnings={s.totalWarnings} size={s.totalSize / (1024 * 1024)} MB time={s.totalTime:mm\\:ss}";
        }
    }
}
