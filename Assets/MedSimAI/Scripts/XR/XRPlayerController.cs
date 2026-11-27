using System;
using System.Collections.Generic;
using MedSimAI.Core;
using MedSimAI.UI;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace MedSimAI.XR
{
    /// <summary>
    /// XR player rig controller.
    /// Player height uses the approach proven in VR Target Arena (PlayerHeightOffset): raise the XR Origin's
    /// Camera Offset object — which carries the headset camera AND both controllers — every LateUpdate, after
    /// XR Origin has applied its own tracking-origin offset. The rig root stays on the floor (Floor tracking,
    /// teleportation unchanged) and the tracked camera pose is never overridden. The world is never rescaled.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class XRPlayerController : MonoBehaviour
    {
        // v0.4: VR default raised to +0.55 m (new key, so the old +0.40 m default does not stick); adjustable in the pause menu.
        public const string PrefExtraHeight = "medsim.vrExtraHeight", PrefSeated = "medsim.seated";
        public const float DefaultExtraHeight = 0.55f, MinExtra = 0f, MaxExtra = 1f;
        /// <summary>Desktop Mode eye lift — unchanged (+0.40 m); the VR height setting does not affect Desktop Mode.</summary>
        public const float DesktopLift = 0.40f;

        public XROrigin origin;
        public Transform spawnPoint;
        public GameObject smoothMoveProvider; // ContinuousMoveProvider GameObject from the starter rig
        [Tooltip("Extra virtual height added above the real tracked head height (metres, 0..1).")]
        [Range(0, 1)] public float extraHeight = DefaultExtraHeight;
        [Tooltip("Additional lift in seated mode so a seated student sees the scene from a standing clinician's eye level.")]
        public float seatedLift = 0.45f;
        public bool Seated { get; private set; }
        public event Action<float, bool> HeightChanged;
        InputAction pauseAction, recenterAction;

        /// <summary>Total Camera Offset height currently applied on top of XR Origin's own base height.</summary>
        public float AppliedLift => DesktopPlayerController.Active ? DesktopLift : Mathf.Clamp(extraHeight, MinExtra, MaxExtra) + (Seated ? seatedLift : 0f);

        void Awake()
        {
            if (!origin) origin = FindAnyObjectByType<XROrigin>();
            UIFactory.RaycasterFactory = c => c.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
            extraHeight = Mathf.Clamp(PlayerPrefs.GetFloat(PrefExtraHeight, DefaultExtraHeight), MinExtra, MaxExtra);
            if (!GetComponent<VRLocomotion>()) gameObject.AddComponent<VRLocomotion>();
            Seated = PlayerPrefs.GetInt(PrefSeated, 0) == 1;
        }

        void Start()
        {
            var subsystems = new List<XRInputSubsystem>();
            SubsystemManager.GetSubsystems(subsystems);
            foreach (var s in subsystems) s.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
            if (origin) origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
            // locomotion (left-stick smooth move, right-stick turn + teleport) is configured by VRLocomotion
            Recenter();

            pauseAction = new InputAction("Pause", InputActionType.Button);
            pauseAction.AddBinding("<XRController>{LeftHand}/menu"); pauseAction.AddBinding("<XRController>{LeftHand}/menuButton"); pauseAction.AddBinding("<Keyboard>/escape");
            pauseAction.performed += _ => TogglePause(); pauseAction.Enable();
            recenterAction = new InputAction("Recenter", InputActionType.Button);
            recenterAction.AddBinding("<Keyboard>/r"); recenterAction.performed += _ => Recenter(); recenterAction.Enable();
        }

        void OnDestroy() { pauseAction?.Dispose(); recenterAction?.Dispose(); }

        /// <summary>Called by DisplayModeManager after XR was started manually (headset found).</summary>
        public void OnXRStarted()
        {
            var subsystems = new List<XRInputSubsystem>();
            SubsystemManager.GetSubsystems(subsystems);
            foreach (var s in subsystems) s.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
            if (origin) origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
            desktop = false;
            GetComponent<VRLocomotion>()?.Apply();
            Recenter();
        }

        // ---------------- Height (runs every frame after XR Origin; survives teleport, recentre and origin resets) ----------------
        void LateUpdate()
        {
            ApplyHeight();
            DesktopFallback();
        }

        /// <summary>Base height XR Origin itself wants: 0 for Floor tracking, Camera Y Offset for Device tracking.</summary>
        float BaseHeight()
        {
            bool deviceMode = origin.CurrentTrackingOriginMode == TrackingOriginModeFlags.Device
                              || (origin.CurrentTrackingOriginMode == TrackingOriginModeFlags.Unknown && origin.RequestedTrackingOriginMode == XROrigin.TrackingOriginMode.Device);
            return deviceMode ? origin.CameraYOffset : 0f;
        }

        public void ApplyHeight()
        {
            if (!origin || !origin.CameraFloorOffsetObject) return;
            var t = origin.CameraFloorOffsetObject.transform;
            var p = t.localPosition;
            float y = BaseHeight() + AppliedLift;
            if (!Mathf.Approximately(p.y, y)) t.localPosition = new Vector3(p.x, y, p.z);
        }

        public void SetExtraHeight(float metres)
        {
            extraHeight = Mathf.Clamp(metres, MinExtra, MaxExtra);
            PlayerPrefs.SetFloat(PrefExtraHeight, extraHeight); PlayerPrefs.Save();
            ApplyHeight(); HeightChanged?.Invoke(extraHeight, Seated);
        }
        public void AdjustHeight(float delta) => SetExtraHeight(extraHeight + delta);

        public void SetSeated(bool seated)
        {
            Seated = seated; PlayerPrefs.SetInt(PrefSeated, seated ? 1 : 0); PlayerPrefs.Save();
            ApplyHeight(); HeightChanged?.Invoke(extraHeight, Seated);
        }

        public void ResetHeight() { SetSeated(false); SetExtraHeight(DefaultExtraHeight); }

        // ---------------- Recentre ----------------
        /// <summary>Places the user's head (not the rig origin) at the spawn point facing its forward. Height lift is preserved.</summary>
        public void Recenter()
        {
            if (!origin || !spawnPoint) return;
            origin.MoveCameraToWorldLocation(new Vector3(spawnPoint.position.x, origin.Camera.transform.position.y, spawnPoint.position.z));
            var camFwd = Vector3.ProjectOnPlane(origin.Camera.transform.forward, Vector3.up);
            if (camFwd.sqrMagnitude > 0.001f) origin.MatchOriginUpCameraForward(Vector3.up, spawnPoint.forward);
            // keep the rig root on the floor
            var rp = origin.transform.position; rp.y = spawnPoint.position.y; origin.transform.position = rp;
            ApplyHeight();
        }

        // ---------------- Desktop fallback (no headset): editor testing only ----------------
        [Tooltip("Tracked head height simulated when no XR device is active (desktop testing). The extra lift is added on top.")]
        public float desktopTrackedHeight = 1.25f;
        float yaw, pitch; bool desktop;

        void DesktopFallback()
        {
            if (!origin || DesktopPlayerController.Active) return; // the full desktop controller owns the camera
            if (XRSettings.isDeviceActive) { desktop = false; return; }
            var cam = origin.Camera.transform;
            if (!desktop) { desktop = true; yaw = cam.eulerAngles.y; pitch = 12f; }
            var lp = cam.localPosition; lp.y = desktopTrackedHeight; cam.localPosition = lp; // simulated tracking only when no device
            var mouse = Mouse.current;
            if (mouse != null && mouse.rightButton.isPressed) { var d = mouse.delta.ReadValue() * 0.15f; yaw += d.x; pitch = Mathf.Clamp(pitch - d.y, -70, 70); }
            cam.rotation = Quaternion.Euler(pitch, yaw, 0);
        }

        public void DesktopLookAt(Vector3 p)
        {
            var cam = origin.Camera.transform; var dir = p - cam.position;
            var e = Quaternion.LookRotation(dir).eulerAngles; yaw = e.y; pitch = e.x > 180 ? e.x - 360 : e.x; desktop = true;
        }

        void TogglePause()
        {
            var s = SessionManager.Instance; if (!s) return;
            if (s.State == AppState.Running || s.State == AppState.Tutorial || s.State == AppState.Discussion) s.Pause(true); else if (s.State == AppState.Paused) s.Pause(false);
        }

        public void SetSmoothMovement(bool on) { if (smoothMoveProvider) smoothMoveProvider.SetActive(on); }
    }
}
