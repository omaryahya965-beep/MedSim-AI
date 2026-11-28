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

        public const string VivePath = "D:/Medical-Test/Builds/MedSimAI_VIVE_Standalone/MedSimAI_VIVE_Focus3.apk";
        [MenuItem("MedSim AI/Hospital/Build VIVE Focus 3 standalone APK")] public static void ViveMenu() => UnityEngine.Debug.Log(BuildViveStandalone());

        /// <summary>
        /// Native Android APK for the VIVE Focus 3 (ARM64, IL2CPP, OpenXR + VIVE Focus 3 feature/controller profile, GLES3,
        /// single-pass instanced, "Mobile" quality level / MedSim_Standalone_RP). Network: the AI proxy is reached over the LAN
        /// with plain HTTP, so cleartext is allowed; the endpoint and client token come from medsim_ai_config.json pushed to
        /// the headset at install time — nothing secret is in the APK.
        /// </summary>
        public static string BuildViveStandalone(string path = VivePath)
        {
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.Android.forceInternetPermission = true;
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3 });
            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
            var rp = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset>("Assets/MedSimAI/Settings/MedSim_Standalone_RP.asset");
            if (rp)
            {
                rp.msaaSampleCount = 2; rp.renderScale = 1f; rp.supportsHDR = false; rp.shadowDistance = 6f;
                var so = new SerializedObject(rp); var p = so.FindProperty("m_AdditionalLightsRenderingMode"); if (p != null) p.intValue = 2; // per-vertex
                so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(rp);
            }
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var r = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = Scenes, locationPathName = path, target = BuildTarget.Android, targetGroup = BuildTargetGroup.Android, options = BuildOptions.None });
            var s = r.summary;
            return $"VIVE APK: {s.result} → {path} · errors={s.totalErrors} warnings={s.totalWarnings} size={s.totalSize / (1024 * 1024)} MB time={s.totalTime}";
        }

        static string Build(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var r = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = Scenes, locationPathName = path, target = BuildTarget.StandaloneWindows64, options = BuildOptions.None });
            var s = r.summary;
            return $"{s.result} → {path} · errors={s.totalErrors} warnings={s.totalWarnings} size={s.totalSize / (1024 * 1024)} MB time={s.totalTime:mm\\:ss}";
        }
    }
}
