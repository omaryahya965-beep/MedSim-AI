using System;
using System.Collections;
using System.Collections.Generic;
using MedSimAI.Core;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;

namespace MedSimAI.XR
{
    public enum DisplayMode { Detecting, VR, Desktop }
    public enum DisplayModePreference { Auto = 0, Desktop = 1, VR = 2 }

    /// <summary>
    /// Chooses VR or Desktop Mode at startup. "Initialize XR on Startup" is OFF for Standalone, so XR is started here:
    ///  • Auto (default): try OpenXR for a few seconds; if a headset display is running → VR, otherwise → Desktop.
    ///  • Forced by command line (-desktop / -vr) or the pause-menu preference (saved, applies on next launch).
    /// This also fixes the PC player stalling at OpenXR start-up when no headset is connected.
    /// VR Mode is unchanged once XR is running (same rig, XRI interactors, +0.40 m height, VIVE Focus 3 profile).
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class DisplayModeManager : MonoBehaviour
    {
        public const string PrefKey = "medsim.displayMode";
        public static DisplayModeManager Instance { get; private set; }
        public static DisplayMode Mode { get; private set; } = DisplayMode.Detecting;
        public static event Action<DisplayMode> ModeChanged;
        /// <summary>Tests: skip automatic desktop activation (keep the legacy rig untouched).</summary>
        public static bool DisableAutoDesktop;
        /// <summary>Tests / tools: force a mode before the scene loads.</summary>
        public static DisplayModePreference? ForcedPreference;
        public float autoDetectSeconds = 4f, forcedVrSeconds = 12f;
        public string Detail { get; private set; } = "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            if (Instance) return;
            var go = new GameObject("[MedSim Display Mode]"); DontDestroyOnLoad(go); go.AddComponent<DisplayModeManager>();
        }

        public static DisplayModePreference SavedPreference
        {
            get => (DisplayModePreference)PlayerPrefs.GetInt(PrefKey, 0);
            set { PlayerPrefs.SetInt(PrefKey, (int)value); PlayerPrefs.Save(); }
        }

        static DisplayModePreference Requested()
        {
            if (ForcedPreference.HasValue) return ForcedPreference.Value;
            foreach (var a in Environment.GetCommandLineArgs())
            {
                var s = a.ToLowerInvariant();
                if (s == "-desktop" || s == "--desktop") return DisplayModePreference.Desktop;
                if (s == "-vr" || s == "--vr") return DisplayModePreference.VR;
            }
            return SavedPreference;
        }

        void Awake()
        {
            Instance = this; Mode = DisplayMode.Detecting;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (s, m) => { if (Mode == DisplayMode.Desktop && !DisableAutoDesktop && this) StartCoroutine(EnableDesktopWhenSceneReady()); };
        }

        /// <summary>Tests/tools: switch the current scene to Desktop Mode now (no XR attempt).</summary>
        bool forcedNow;
        public void ForceDesktopNow() { forcedNow = true; Mode = DisplayMode.Desktop; Detail = "forced"; StartCoroutine(EnableDesktopWhenSceneReady()); ModeChanged?.Invoke(Mode); }

        IEnumerator Start()
        {
            var req = Requested();
            var mgr = XRGeneralSettings.Instance ? XRGeneralSettings.Instance.Manager : null;
            // If XR was already started (e.g. "Initialize on Startup" re-enabled), respect it.
            if (mgr != null && mgr.isInitializationComplete && XRSettings.isDeviceActive) { Set(DisplayMode.VR, "XR already running"); yield break; }
            if (req == DisplayModePreference.Desktop || mgr == null) { Set(DisplayMode.Desktop, mgr == null ? "no XR settings" : "desktop requested"); yield break; }

            if (mgr.activeLoader == null) yield return mgr.InitializeLoader();
            if (mgr.activeLoader == null) { Set(DisplayMode.Desktop, "no OpenXR runtime/headset (loader failed)"); yield break; }
            mgr.StartSubsystems();
            float until = Time.realtimeSinceStartup + (req == DisplayModePreference.VR ? forcedVrSeconds : autoDetectSeconds);
            while (Time.realtimeSinceStartup < until && !DisplayRunning()) yield return null;
            if (DisplayRunning()) { Set(DisplayMode.VR, "headset display running"); yield break; }
            mgr.StopSubsystems(); mgr.DeinitializeLoader();
            Set(DisplayMode.Desktop, "XR started but no headset display");
        }

        static bool DisplayRunning()
        {
            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            foreach (var d in displays) if (d.running) return true;
            return false;
        }

        void Set(DisplayMode m, string why)
        {
            Mode = m; Detail = why;
            Debug.Log($"[MedSim] Display mode: {m} ({why})");
            if (m == DisplayMode.VR) { var p = FindAnyObjectByType<XRPlayerController>(); if (p) p.OnXRStarted(); }
            if (m == DisplayMode.Desktop && !DisableAutoDesktop) StartCoroutine(EnableDesktopWhenSceneReady());
            ModeChanged?.Invoke(m);
        }

        IEnumerator EnableDesktopWhenSceneReady()
        {
            XRPlayerController rig = null;
            float t = 0; while ((rig = FindAnyObjectByType<XRPlayerController>()) == null && t < 10) { t += Time.unscaledDeltaTime; yield return null; }
            if (!rig || (DisableAutoDesktop && Mode != DisplayMode.Desktop) || (DisableAutoDesktop && !forcedNow)) yield break;
            forcedNow = false;
            if (!rig.GetComponent<DesktopPlayerController>()) rig.gameObject.AddComponent<DesktopPlayerController>();
        }

        /// <summary>Pause-menu selector: cycles Auto → Desktop → VR (applies on next launch).</summary>
        public static DisplayModePreference CyclePreference()
        {
            var next = (DisplayModePreference)(((int)SavedPreference + 1) % 3);
            SavedPreference = next; return next;
        }
    }
}
