using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using MedSimAI.Surgery.Shared;
using MedSimAI.Surgery.Shared.Unity;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MedSimAI.Surgery.Environment
{
    [RequireComponent(typeof(Camera))]
    [DisallowMultipleComponent]
    public sealed class OperatingRoomTour : MonoBehaviour
    {
        [SerializeField] private Transform[] views;
        [SerializeField] private float[] fieldsOfView;
        private Camera viewCamera;
        private int currentView;

#if UNITY_EDITOR
        public void Configure(Transform[] viewpoints, float[] fovs)
        {
            views = viewpoints;
            fieldsOfView = fovs;
        }
#endif

        private void Start()
        {
            viewCamera = GetComponent<Camera>();
            if (!SurgeryEventBusHost.Instance)
            {
                var host = new GameObject("Demo Surgery Event Bus").AddComponent<SurgeryEventBusHost>();
                host.Bus.BeginSession(Guid.NewGuid().ToString("N"));
                host.Bus.Publish(new SurgeryEvent(SurgeryEventIds.SessionStarted, SurgeryEventSources.Integration));
            }

            string[] args = System.Environment.GetCommandLineArgs();
            int benchmarkArg = Array.IndexOf(args, "-operating-room-benchmark");
            if (benchmarkArg >= 0 && benchmarkArg + 1 < args.Length)
                StartCoroutine(Benchmark(args[benchmarkArg + 1]));
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || views.Length == 0)
                return;

            if (keyboard.rightArrowKey.wasPressedThisFrame)
                MoveTo((currentView + 1) % views.Length);
            if (keyboard.leftArrowKey.wasPressedThisFrame)
                MoveTo((currentView + views.Length - 1) % views.Length);
        }

        private void MoveTo(int index)
        {
            currentView = index;
            Transform view = views[index];
            transform.SetPositionAndRotation(view.position, view.rotation);
            viewCamera.fieldOfView = fieldsOfView[index];
        }

        private IEnumerator Benchmark(string output)
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            Application.runInBackground = true;

            var data = new StringBuilder();
            data.AppendLine("view,width,height,wall_ms,gpu_ms");
            var timing = new FrameTiming[1];
            ulong previousTimestamp = 0;
            for (int index = 0; index < views.Length; index++)
            {
                MoveTo(index);
                yield return new WaitForSecondsRealtime(1.75f);
                for (int frame = 0; frame < 240; frame++)
                {
                    FrameTimingManager.CaptureFrameTimings();
                    yield return null;
                    double wallMs = Time.unscaledDeltaTime * 1000.0;
                    uint samples = FrameTimingManager.GetLatestTimings(1, timing);
                    bool fresh = samples > 0 && timing[0].frameStartTimestamp != previousTimestamp;
                    if (fresh)
                        previousTimestamp = timing[0].frameStartTimestamp;
                    string gpuMs = fresh && timing[0].gpuFrameTime > 0
                        ? timing[0].gpuFrameTime.ToString("F3", CultureInfo.InvariantCulture)
                        : "";
                    data.AppendLine(FormattableString.Invariant(
                        $"{views[index].name},{Screen.width},{Screen.height},{wallMs:F3},{gpuMs}"));
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
            File.WriteAllText(output, data.ToString());
            Debug.Log($"[Operating Room] Benchmark saved to {output} on {SystemInfo.graphicsDeviceName}, " +
                $"engine {Application.unityVersion}; frame timing enabled: {FrameTimingManager.IsFeatureEnabled()}.");
            if (!Application.isEditor)
                Application.Quit();
        }
    }
}
