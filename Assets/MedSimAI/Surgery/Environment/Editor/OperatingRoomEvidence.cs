using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace MedSimAI.Surgery.Environment.Editor
{
    internal static class OperatingRoomEvidence
    {
        [MenuItem("MedSim AI/Surgery/Capture Operating Room Viewpoints")]
        public static void Capture()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            EditorSceneManager.OpenScene(OperatingRoomBuilder.ScenePath);
            var camera = Object.FindFirstObjectByType<Camera>();
            if (!camera || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Capture needs the demo camera and a working graphics device.");

            const int width = 1600, height = 900;
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            string output = Argument("-or-screenshots") ?? "Docs/Surgery/Environment/Screenshots";
            Directory.CreateDirectory(output);
            try
            {
                camera.targetTexture = target;
                target.Create();
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
                for (int i = 0; i < OperatingRoomBuilder.Viewpoints.Length; i++)
                {
                    OperatingRoomBuilder.SetView(camera, i);
                    RenderPipeline.SubmitRenderRequest(camera, request);
                    RenderTexture.active = target;
                    image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    image.Apply();
                    string path = Path.Combine(output, OperatingRoomBuilder.Viewpoints[i].Name + ".png");
                    File.WriteAllBytes(path, image.EncodeToPNG());
                    Debug.Log($"[Operating Room] Rendered {path}");
                }
            }
            finally
            {
                OperatingRoomBuilder.SetView(camera, 0);
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(image);
            }
        }

        public static void BuildDesktopBenchmark()
        {
            string output = Argument("-or-player");
            if (string.IsNullOrEmpty(output))
                throw new ArgumentException("Supply -or-player with an absolute output path outside Assets.");

            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            if (target is not (BuildTarget.StandaloneOSX or BuildTarget.StandaloneWindows64))
                throw new InvalidOperationException($"Select the macOS or Windows desktop build target. Current: {target}");

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { OperatingRoomBuilder.ScenePath },
                locationPathName = output,
                target = target,
                options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException($"The demo player build ended with {report.summary.result}.");
        }

        private static string Argument(string option)
        {
            string[] args = System.Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, option);
            return index >= 0 ? args.Skip(index + 1).FirstOrDefault() : null;
        }
    }
}
