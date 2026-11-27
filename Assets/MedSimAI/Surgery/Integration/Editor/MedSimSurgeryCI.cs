using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MedSimAI.Surgery.EditorTools
{
    /// <summary>Batch-mode entry point for CI (GameCI unity-builder buildMethod): builds the surgery scenes, then the desktop player.</summary>
    public static class MedSimSurgeryCI
    {
        public static void BuildDesktop()
        {
            try
            {
                Debug.Log(MedSimSurgeryBuilder.BuildMenuScene());
                Debug.Log(MedSimSurgeryBuilder.BuildTestScene());
                Debug.Log(MedSimSurgeryBuilder.BuildOperatingRoom());
                Debug.Log(MedSimSurgeryBuilder.AddScenesToBuild());
                var scenes = EditorBuildSettings.scenes.Where(s => s.enabled && File.Exists(s.path)).Select(s => s.path).ToArray();
                var outDir = Environment.GetEnvironmentVariable("BUILD_PATH");
                if (string.IsNullOrEmpty(outDir)) outDir = "build/StandaloneWindows64";
                var file = Environment.GetEnvironmentVariable("BUILD_FILE");
                if (string.IsNullOrEmpty(file)) file = "MedSimAI.exe";
                Directory.CreateDirectory(outDir);
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = scenes, locationPathName = Path.Combine(outDir, file), target = BuildTarget.StandaloneWindows64, options = BuildOptions.None });
                Debug.Log($"[Surgery CI] Build {report.summary.result}: {report.summary.totalErrors} errors, {report.summary.totalSize / 1048576} MB, scenes: {string.Join(", ", scenes)}");
                EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
    }
}
