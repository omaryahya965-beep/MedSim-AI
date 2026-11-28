using System;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;

namespace MedSimAI.Hospital.EditorTools
{
    /// <summary>
    /// CI entry point (GitHub Actions, batch mode): the SAME integrated builds as the menu items, written inside the
    /// project folder so the workflow can upload them. Uses the committed scenes; regenerates nothing.
    /// </summary>
    public static class HospitalBuildCI
    {
        public static void BuildBoth()
        {
            int code = 0;
            try
            {
                var d = HospitalBuild.BuildDesktop("build/MedSimAI_Integrated_Desktop/MedSimAI.exe"); UnityEngine.Debug.Log("[HospitalBuildCI] " + d);
                if (!d.Contains("Succeeded")) code = 1;
                var p = HospitalBuild.BuildPcvr("build/MedSimAI_Integrated_PCVR/MedSimAI.exe"); UnityEngine.Debug.Log("[HospitalBuildCI] " + p);
                if (!p.Contains("Succeeded")) code = 1;
            }
            catch (Exception e) { UnityEngine.Debug.LogException(e); code = 1; }
            EditorApplication.Exit(code);
        }

        /// <summary>macOS desktop build (Apple Silicon/Intel): same scenes as HospitalBuild, desktop mode only (OpenXR loader
        /// removed for the build and restored afterwards, exactly like HospitalBuild.BuildDesktop).</summary>
        public static void BuildMac()
        {
            int code = 1;
            var g = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
            bool removed = XRPackageMetadataStore.RemoveLoader(g.Manager, "UnityEngine.XR.OpenXR.OpenXRLoader", BuildTargetGroup.Standalone);
            try
            {
                System.IO.Directory.CreateDirectory("build/MedSimAI_Mac");
                // Intel (x64) slice: built from a Windows runner it cannot be code-signed, and macOS runs unsigned x64
                // apps (via Rosetta on Apple Silicon) but refuses unsigned arm64 ones.
                EditorUserBuildSettings.SetPlatformSettings("Standalone", "OSXUniversal", "Architecture", "x64");
                var r = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { HospitalBuilder.HospitalScene, HospitalBuilder.ErScene },
                    locationPathName = "build/MedSimAI_Mac/MedSimAI.app",
                    target = BuildTarget.StandaloneOSX, options = BuildOptions.None
                });
                UnityEngine.Debug.Log($"[HospitalBuildCI] Mac: {r.summary.result} errors={r.summary.totalErrors} size={r.summary.totalSize / (1024 * 1024)} MB");
                code = r.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1;
            }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
            finally { if (removed) XRPackageMetadataStore.AssignLoader(g.Manager, "UnityEngine.XR.OpenXR.OpenXRLoader", BuildTargetGroup.Standalone); }
            EditorApplication.Exit(code);
        }
    }
}
