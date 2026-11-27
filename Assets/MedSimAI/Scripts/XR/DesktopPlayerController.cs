using System.Linq;
using MedSimAI.AI;
using MedSimAI.Characters;
using MedSimAI.Core;
using MedSimAI.Instruments;
using MedSimAI.Voice;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace MedSimAI.XR
{
    /// <summary>
    /// Desktop Mode (keyboard + mouse) for the SAME scene, clinical engine, AI, characters and instruments as VR.
    ///  WASD move (Shift = faster) · mouse look · E interact (doors, ECG, sanitiser, pick up) · Left click grab / apply ·
    ///  Right click put an instrument back · hold T to talk (existing push-to-talk) · Esc pause · F1 controls.
    /// The XR Origin root is moved with its CharacterController (collisions with walls, furniture, characters, door leaf);
    /// the XR Main Camera becomes the first-person camera at the same eye height (simulated 1.25 m + the +0.40 m lift).
    /// Instruments use MedicalInstrumentBase.DesktopGrab/DesktopRelease, so placement zones, snapping, dwell, findings,
    /// wrong-placement detection and supervisor events behave exactly as in VR.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class DesktopPlayerController : MonoBehaviour
    {
        public static bool Active { get; private set; }
        public float walkSpeed = 1.5f, runSpeed = 2.8f, lookSensitivity = 0.09f, reach = 2.6f, gravity = -9.81f;

        XROrigin origin; XRPlayerController player; Transform cam; Camera camera; CharacterController cc;
        float yaw, pitch, vy, ecgGaze, rejectCooldown, helpUntil;
        MedicalInstrumentBase held;
        TextMeshProUGUI prompt, status, help, toast; Image crosshair; float toastUntil;
        bool initialised;

        void Start()
        {
            origin = GetComponent<XROrigin>(); player = GetComponent<XRPlayerController>();
            if (!origin) { enabled = false; return; }
            cam = origin.Camera.transform; camera = origin.Camera;
            Active = true;
            // Disable XR-only rig parts (controllers, gaze interactor, teleport/turn locomotion); keep camera + rig root.
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name.Contains("Controller") && t != transform || t.name.StartsWith("Gaze") || t.name == "Locomotion") t.gameObject.SetActive(false);
            foreach (var b in cam.GetComponents<Behaviour>()) if (b.GetType().Name == "TrackedPoseDriver") b.enabled = false;
            cam.localPosition = new Vector3(0, player ? player.desktopTrackedHeight : 1.25f, 0);
            cam.localRotation = Quaternion.identity;
            yaw = transform.eulerAngles.y; pitch = 0;
            cc = GetComponent<CharacterController>() ?? gameObject.AddComponent<CharacterController>();
            cc.enabled = true; cc.height = 1.7f; cc.radius = 0.22f; cc.center = new Vector3(0, 0.85f, 0); cc.stepOffset = 0.3f; cc.slopeLimit = 45f; cc.skinWidth = 0.02f;
            // Desktop rendering: native resolution, HDR, MSAA + SMAA, higher shadow quality. No XR scaling or foveation.
            PerformanceManager.Instance?.ApplyDesktopQuality();
            camera.stereoTargetEye = StereoTargetEyeMask.None; camera.fieldOfView = 70f; camera.nearClipPlane = 0.03f; camera.allowHDR = true; camera.allowMSAA = true;
            var data = camera.GetUniversalAdditionalCameraData(); data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing; data.antialiasingQuality = AntialiasingQuality.High; data.renderPostProcessing = true;
#if !UNITY_EDITOR
            var r = Screen.currentResolution; Screen.SetResolution(r.width, r.height, FullScreenMode.FullScreenWindow);
#endif
            FindAnyObjectByType<MedSimAI.UI.MedSimUI>()?.SetDesktopMode();
            BuildHud();
            helpUntil = Time.unscaledTime + 14f;
            initialised = true;
        }

        /// <summary>Automation / desktop helper: turn the view toward a world point (yaw on the body, pitch on the camera).</summary>
        public void LookAt(Vector3 p)
        {
            var d = p - cam.position; if (d.sqrMagnitude < 1e-6f) return;
            var e = Quaternion.LookRotation(d).eulerAngles; yaw = e.y; pitch = Mathf.Clamp(e.x > 180 ? e.x - 360 : e.x, -80f, 80f);
            transform.rotation = Quaternion.Euler(0, yaw, 0); cam.localRotation = Quaternion.Euler(pitch, 0, 0);
        }

        void OnDestroy() { Active = false; Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }

        bool UiMode
        {
            get
            {
                var s = SessionManager.Instance; if (!s) return false;
                return s.State == AppState.Paused || s.State == AppState.Debrief || s.State == AppState.MainMenu || s.State == AppState.Loading;
            }
        }

        void Update()
        {
            if (!initialised) return;
            var kb = Keyboard.current; var mouse = Mouse.current;
            bool ui = UiMode;
            Cursor.lockState = ui ? CursorLockMode.None : CursorLockMode.Locked; Cursor.visible = ui;
            if (kb != null && kb.mKey.wasPressedThisFrame && VoiceInteractionManager.Instance) { var m = VoiceInteractionManager.Instance.CycleMicMode(); Toast("Microphone: " + (m == MicMode.HandsFree ? "hands-free — just speak" : m == MicMode.Muted ? "muted" : "push-to-talk — hold T")); }
            if (kb != null && (kb.f1Key.wasPressedThisFrame || kb.hKey.wasPressedThisFrame)) helpUntil = helpUntil > Time.unscaledTime ? 0 : Time.unscaledTime + 30f;
            UpdateStatus();
            if (ui || Time.timeScale == 0f) { SetPrompt(""); crosshair.enabled = false; return; }
            crosshair.enabled = true;

            // ---- look ----
            if (mouse != null) { var d = mouse.delta.ReadValue() * lookSensitivity; yaw += d.x; pitch = Mathf.Clamp(pitch - d.y, -80f, 80f); }
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            cam.localRotation = Quaternion.Euler(pitch, 0, 0);
            cam.localPosition = new Vector3(0, player ? player.desktopTrackedHeight : 1.25f, 0);

            // ---- move (collides with walls, furniture, characters and the closed door) ----
            Vector2 mv = Vector2.zero;
            if (kb != null) { if (kb.wKey.isPressed) mv.y += 1; if (kb.sKey.isPressed) mv.y -= 1; if (kb.dKey.isPressed) mv.x += 1; if (kb.aKey.isPressed) mv.x -= 1; }
            float speed = kb != null && kb.leftShiftKey.isPressed ? runSpeed : walkSpeed;
            var dir = (transform.forward * mv.y + transform.right * mv.x); if (dir.sqrMagnitude > 1) dir.Normalize();
            vy = cc.isGrounded ? -0.5f : vy + gravity * Time.deltaTime;
            cc.Move((dir * speed + Vector3.up * vy) * Time.deltaTime);

            // ---- interaction ----
            var hit = Probe(out var hasHit);
            Interact(hasHit, hit, kb, mouse);
        }

        RaycastHit Probe(out bool ok)
        {
            ok = false;
            var hits = Physics.RaycastAll(cam.position, cam.forward, reach, ~0, QueryTriggerInteraction.Collide).OrderBy(h => h.distance);
            foreach (var h in hits)
            {
                if (h.collider.transform.IsChildOf(transform)) continue;                       // own rig
                if (held && h.collider.transform.IsChildOf(held.transform)) continue;         // held instrument
                if (h.collider.isTrigger && !h.collider.GetComponent<PlacementZone>()) continue; // e.g. doorway trigger
                if (!held && h.collider.GetComponent<PlacementZone>()) continue;              // zones only matter while holding
                ok = true; return h;
            }
            return default;
        }

        static string Nice(MedicalInstrumentBase i) => i.InstrumentId == "bp_cuff" ? "blood pressure cuff" : i.InstrumentId == "pulse_oximeter" ? "pulse oximeter" : i.InstrumentId;

        void Interact(bool hasHit, RaycastHit hit, Keyboard kb, Mouse mouse)
        {
            bool e = kb != null && kb.eKey.wasPressedThisFrame;
            bool lmb = mouse != null && mouse.leftButton.wasPressedThisFrame;
            bool rmb = mouse != null && mouse.rightButton.wasPressedThisFrame;
            rejectCooldown -= Time.deltaTime;

            // ---------- holding an instrument ----------
            if (held)
            {
                var zone = hasHit ? hit.collider.GetComponent<PlacementZone>() : null;
                if (zone && hit.distance < 1.8f) PlaceProbeAt(held, hit.point, zone.transform.forward);
                else HoldInFront(held);
                bool snaps = held is BloodPressureController || held is PulseOximeterController;
                if (zone && zone.Accepts(held.InstrumentId))
                {
                    SetPrompt(snaps ? $"Left click — apply the {Nice(held)} ({zone.displayName})" : $"Hold steady on the {zone.displayName} to listen  ·  Right click — put it back");
                    if (lmb && snaps && held.HoverZone) { held.DesktopRelease(); held = null; return; }
                }
                else if (zone)
                {
                    SetPrompt($"Left click — apply the {Nice(held)} here  ·  Right click — put it back");
                    if (lmb && rejectCooldown <= 0) { held.DesktopRejectedPlacement(); rejectCooldown = 1.2f; Toast($"The {Nice(held)} doesn't work there."); }
                }
                else SetPrompt($"Aim at the patient to use the {Nice(held)}  ·  Right click — put it back");
                if (rmb) { var h = held; held = null; h.DesktopRelease(); if (!h.Attached) h.ResetInstrument(); }
                crosshair.color = zone && zone.Accepts(held ? held.InstrumentId : "") ? new Color(0.2f, 0.9f, 0.8f) : Color.white;
                return;
            }

            crosshair.color = Color.white;
            if (!hasHit) { SetPrompt(""); ecgGaze = 0; return; }
            var tr = hit.collider.transform;

            // instruments
            var inst = tr.GetComponentInParent<MedicalInstrumentBase>();
            if (inst)
            {
                SetPrompt($"Left click / E — pick up the {Nice(inst)}{(inst.Attached ? " (remove it)" : "")}"); crosshair.color = new Color(0.2f, 0.9f, 0.8f);
                if (lmb || e) { held = inst; inst.DesktopGrab(); }
                return;
            }
            // door
            var door = tr.GetComponentInParent<HospitalDoor>() ?? FindObjectsByType<HospitalDoor>(FindObjectsSortMode.None).FirstOrDefault(d => (d.leaf && tr.IsChildOf(d.leaf)) || (d.hinge && tr.IsChildOf(d.hinge)));
            if (door)
            {
                SetPrompt(door.IsOpen ? "E — close the door" : "E — open the door"); crosshair.color = new Color(0.2f, 0.9f, 0.8f);
                if (e || lmb) door.OpenBy(door.Angle > 20f ? 0f : 88f);
                return;
            }
            // ECG cart: record, then review (looking at the tracing for a moment also counts as reviewing)
            var ecg = tr.GetComponentInParent<ECGMachineController>();
            if (ecg)
            {
                bool recorded = ClinicalScenarioManager.Instance.Session.Tracker.Has("INV:INV_ECG");
                bool reviewed = ClinicalScenarioManager.Instance.Session.Tracker.Has(MedSimAI.Clinical.ClinicalEvents.EcgReviewed);
                SetPrompt(!recorded ? "E — record a 12-lead ECG" : reviewed ? "12-lead ECG (reviewed)" : "Reviewing the ECG…  (E — review now)"); crosshair.color = new Color(0.2f, 0.9f, 0.8f);
                if (!recorded && (e || lmb)) ecg.Record();
                else if (recorded && !reviewed) { ecgGaze += Time.deltaTime; if (e || lmb || ecgGaze > 1.5f) ecg.MarkReviewed(); }
                return;
            }
            ecgGaze = 0;
            // hand sanitiser
            var san = tr.GetComponentInParent<SanitizerDispenser>();
            if (san) { SetPrompt("E — sanitise your hands"); crosshair.color = new Color(0.2f, 0.9f, 0.8f); if (e || lmb) san.Use(); return; }
            // characters
            var ch = tr.GetComponentInParent<SimCharacterBase>();
            if (ch && ch.speech) { SetPrompt($"Hold T — talk to {ch.speech.displayName}"); return; }
            SetPrompt("");
        }

        void HoldInFront(MedicalInstrumentBase i)
        {
            var target = cam.position + cam.forward * 0.42f + cam.right * 0.16f - cam.up * 0.14f;
            i.transform.position = Vector3.Lerp(i.transform.position, target, 1 - Mathf.Exp(-18f * Time.deltaTime));
            i.transform.rotation = Quaternion.Slerp(i.transform.rotation, cam.rotation, 1 - Mathf.Exp(-12f * Time.deltaTime));
        }

        /// <summary>Puts the instrument's business end (probePoint) on the aimed body site, probe axis into the body.</summary>
        static void PlaceProbeAt(MedicalInstrumentBase i, Vector3 point, Vector3 outward)
        {
            var probe = i.probePoint ? i.probePoint : i.transform;
            var axis = probe.TransformDirection(i.probeAxis);
            i.transform.rotation = Quaternion.FromToRotation(axis, -outward) * i.transform.rotation;
            i.transform.position += (point + outward * 0.004f) - probe.position;
        }

        // ---------------- HUD (screen-space; nothing floats in the 3D world) ----------------
        void BuildHud()
        {
            var go = new GameObject("Desktop HUD", typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(transform, false);
            var c = go.GetComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = 5;
            var sc = go.GetComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
            var ch = new GameObject("Crosshair", typeof(Image)); ch.transform.SetParent(go.transform, false);
            crosshair = ch.GetComponent<Image>(); var rt = crosshair.rectTransform; rt.sizeDelta = new Vector2(7, 7); rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            crosshair.raycastTarget = false;
            prompt = MakeText(go.transform, new Vector2(0.5f, 0.5f), new Vector2(0, -46), 26, TextAlignmentOptions.Center, new Vector2(1200, 60));
            status = MakeText(go.transform, new Vector2(0f, 1f), new Vector2(28, -24), 20, TextAlignmentOptions.TopLeft, new Vector2(900, 80), new Vector2(0, 1));
            toast = MakeText(go.transform, new Vector2(0.5f, 0.5f), new Vector2(0, -92), 22, TextAlignmentOptions.Center, new Vector2(1200, 50));
            help = MakeText(go.transform, new Vector2(1f, 1f), new Vector2(-28, -24), 19, TextAlignmentOptions.TopRight, new Vector2(620, 260), new Vector2(1, 1));
            help.text = "<b>Desktop controls</b>\nWASD move · Shift faster\nMouse look\nE interact (doors, ECG, sanitiser)\nLeft click grab / apply instrument\nRight click put instrument back\nHold T talk to the person you look at\nM microphone: push-to-talk / hands-free / mute\nEsc pause · F1 show/hide controls";
        }

        static TextMeshProUGUI MakeText(Transform parent, Vector2 anchor, Vector2 pos, float size, TextAlignmentOptions align, Vector2 box, Vector2? pivot = null)
        {
            var go = new GameObject("Text", typeof(TextMeshProUGUI)); go.transform.SetParent(parent, false);
            var t = go.GetComponent<TextMeshProUGUI>(); t.fontSize = size; t.alignment = align; t.color = Color.white; t.raycastTarget = false;
            t.outlineWidth = 0.18f; t.outlineColor = new Color32(0, 0, 0, 200);
            var rt = t.rectTransform; rt.anchorMin = rt.anchorMax = anchor; rt.pivot = pivot ?? new Vector2(0.5f, 0.5f); rt.sizeDelta = box; rt.anchoredPosition = pos;
            return t;
        }

        void SetPrompt(string s) { if (prompt && prompt.text != s) prompt.text = s; }
        void Toast(string s) { toast.text = s; toastUntil = Time.unscaledTime + 2.5f; }

        void UpdateStatus()
        {
            if (toast && Time.unscaledTime > toastUntil) toast.text = "";
            if (help) help.enabled = Time.unscaledTime < helpUntil && !UiMode;
            var v = VoiceInteractionManager.Instance; var ai = AIProviderManager.Instance;
            string voice = v == null ? "" : v.State == VoiceState.Listening ? "<color=#FF6B6B>● Listening…</color>" : v.State == VoiceState.Transcribing || v.State == VoiceState.Thinking ? "<color=#64B5F6>● Thinking…</color>" : "";
            string target = v != null && v.State == VoiceState.Idle ? $"<color=#B0BEC5>Talking to: {(v.Target == Persona.Patient ? "Mr. Reyes" : "Dr. Hassan")}</color>" : "";
            if (v != null && v.LiveOn) target += $"<color=#B0BEC5>   ·   Mic: {(v.Mic == MicMode.HandsFree ? "hands-free" : v.Mic == MicMode.Muted ? "<color=#FF8A80>muted</color>" : "push-to-talk (T)")}</color>";
            string mode = ai ? (ai.Mode == ConversationMode.OnlineAI && ai.Live.Status != LiveStatus.Offline ? "<color=#64FFDA>LIVE AI</color>" : $"<color=#FFB74D>{ai.ModeLabel}</color>") : "";
            if (status) status.text = $"{mode}   {voice}\n<size=80%>{target}</size>";
        }
    }
}
