using System;
using MedSimAI.Surgery.Shared;
using MedSimAI.Surgery.Shared.Unity;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MedSimAI.Surgery.Mechanics
{
    /// <summary>
    /// Provides precise first-person Desktop Mode instrument control,
    /// enabling complete surgical interaction via keyboard and mouse without VR hardware.
    /// Preserves standard WASD locomotion and mouse look while offering tool manipulation.
    /// </summary>
    public class SurgicalDesktopInteractionAdapter : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private Camera playerCamera;
        [SerializeField] private SurgicalInstrumentController instrumentController;
        [SerializeField] private AnatomyInteractionController anatomyController;
        [SerializeField] private ImplantPlacementController implantController;
        [SerializeField] private SurgeryResetController resetController;

        [Header("Interaction Settings")]
        [SerializeField] private float pickupRange = 3.0f;
        [SerializeField] private float defaultHoldDistance = 0.65f;
        [SerializeField] private float minHoldDistance = 0.35f;
        [SerializeField] private float maxHoldDistance = 1.2f;
        [SerializeField] private float followSpeed = 20f;
        [SerializeField] private float rotationSpeed = 90f;

        private Transform heldObject;
        private string heldToolId;
        private string heldImplantId;
        private Rigidbody heldRb;
        private float currentHoldDistance;
        private Quaternion targetHoldRotation = Quaternion.identity;

        public bool IsHoldingItem => heldObject != null;
        /// <summary>
        /// Integration: when set, R asks the host to restart the whole surgical session (mechanics + supervisor + assessment
        /// together, through SurgerySessionController.Restart) instead of resetting only the mechanics.
        /// </summary>
        public static Action ResetRequested;
        /// <summary>Shows/clears the "press R again" confirmation (host HUD). Empty string = clear.</summary>
        public static Action<string> ResetPrompt;
        float resetArmedAt = -10f;
        /// <summary>The point on the patient the desktop aim ray is on (for guided drawing), or null.</summary>
        public Ray AimRay => playerCamera ? playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0)) : new Ray();
        public Transform HeldObject => heldObject;
        /// <summary>Guided drawing (site marking): world point the held tool's tip is held on; null = normal hold.</summary>
        public Vector3? TipLock { get; set; }
        public string HeldToolId => heldToolId;
        public string HeldImplantId => heldImplantId;

        private void Awake()
        {
            if (playerCamera == null) playerCamera = Camera.main;
            if (!instrumentController) instrumentController = FindFirstObjectByType<SurgicalInstrumentController>();
            if (!anatomyController) anatomyController = FindFirstObjectByType<AnatomyInteractionController>();
            if (!implantController) implantController = FindFirstObjectByType<ImplantPlacementController>();
            if (!resetController) resetController = FindFirstObjectByType<SurgeryResetController>();

            currentHoldDistance = defaultHoldDistance;
        }

        private void Update()
        {
            HandleInput();
            UpdateHeldObjectPosition();
        }

        private void HandleInput()
        {
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            if (mouse == null || keyboard == null) return;

            // R: Reset simulation — deliberate only: press R twice within 3 s (one accidental R restarted the whole operation
            // during manual testing). The first press only shows the confirmation prompt.
            if (keyboard.rKey.wasPressedThisFrame && (resetController != null || ResetRequested != null))
            {
                if (Time.unscaledTime - resetArmedAt > 3f) { resetArmedAt = Time.unscaledTime; ResetPrompt?.Invoke("Press R again to restart the operation from step 1"); return; }
                resetArmedAt = -10f; ResetPrompt?.Invoke("");
                DropHeldItem();
                if (ResetRequested != null) ResetRequested(); else resetController.ExecuteReset();
                return;
            }
            if (resetArmedAt > 0 && Time.unscaledTime - resetArmedAt > 3f) { resetArmedAt = -10f; ResetPrompt?.Invoke(""); }

            // Left-Click: Pick up or Interact
            if (mouse.leftButton.wasPressedThisFrame)
            {
                if (heldObject == null)
                {
                    TryPickup();
                }
                else
                {
                    TryApplyHeldTool();
                }
            }

            // E: Toggle tool activation (saw/drill)
            if (keyboard.eKey.wasPressedThisFrame && !string.IsNullOrEmpty(heldToolId) && instrumentController != null)
            {
                bool active = !instrumentController.IsToolActive(heldToolId);
                instrumentController.SetToolActive(heldToolId, active);
            }

            // Q: Drop held item
            if (keyboard.qKey.wasPressedThisFrame && heldObject != null)
            {
                DropHeldItem();
            }

            // Scroll wheel: Adjust depth
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f && heldObject != null)
            {
                currentHoldDistance = Mathf.Clamp(currentHoldDistance + (scroll > 0 ? 0.05f : -0.05f), minHoldDistance, maxHoldDistance);
            }

            // Right-mouse hold: Rotate tool
            if (mouse.rightButton.isPressed && heldObject != null)
            {
                Vector2 delta = mouse.delta.ReadValue();
                Quaternion yaw = Quaternion.AngleAxis(delta.x * rotationSpeed * Time.deltaTime, Vector3.up);
                Quaternion pitch = Quaternion.AngleAxis(-delta.y * rotationSpeed * Time.deltaTime, Vector3.right);
                targetHoldRotation = yaw * pitch * targetHoldRotation;
            }
        }

        private void TryPickup()
        {
            if (playerCamera == null) return;

            Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
            if (Physics.Raycast(ray, out RaycastHit hit, pickupRange))
            {
                // Check if instrument
                var instTag = hit.collider.GetComponentInParent<SurgeryInstrumentTag>();
                if (instTag != null)
                {
                    PickupInstrument(instTag);
                    return;
                }

                // Check if implant
                var impTag = hit.collider.GetComponentInParent<SurgeryImplantTag>();
                if (impTag != null)
                {
                    PickupImplant(impTag);
                    return;
                }
            }
        }

        public void PickupInstrument(SurgeryInstrumentTag tag)
        {
            heldObject = tag.transform;
            heldObject.SetParent(null, true);   // e.g. taken from the assistant's hand; reset puts it back in its tray slot
            heldToolId = tag.instrumentId;
            heldImplantId = null;
            heldRb = tag.GetComponent<Rigidbody>();
            if (heldRb != null) heldRb.isKinematic = true;

            currentHoldDistance = defaultHoldDistance;
            targetHoldRotation = Quaternion.identity;

            if (instrumentController != null)
            {
                instrumentController.HandleGrab(heldToolId, InstrumentHolder.Player);
            }
        }

        public void PickupImplant(SurgeryImplantTag tag)
        {
            heldObject = tag.transform;
            heldToolId = null;
            heldImplantId = tag.implantId;
            heldRb = tag.GetComponent<Rigidbody>();
            if (heldRb != null) heldRb.isKinematic = true;

            currentHoldDistance = defaultHoldDistance;
            targetHoldRotation = Quaternion.identity;
        }

        public void DropHeldItem()
        {
            if (heldObject == null) return;

            if (!string.IsNullOrEmpty(heldToolId) && instrumentController != null)
            {
                instrumentController.HandleRelease(heldToolId);
            }
            else if (!string.IsNullOrEmpty(heldImplantId) && implantController != null)
            {
                implantController.CheckPlacementAttempt(heldImplantId);
            }

            if (heldRb != null)
            {
                heldRb.isKinematic = false;
            }

            heldObject = null;
            heldToolId = null;
            heldImplantId = null;
            heldRb = null;
        }

        private void TryApplyHeldTool()
        {
            if (playerCamera == null) return;

            Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
            // Every anatomy target along the aim ray (they are nested boxes); the resolver picks the one the current step needs.
            var hits = Physics.RaycastAll(ray, pickupRange, ~0, QueryTriggerInteraction.Collide);
            var candidates = new System.Collections.Generic.List<(string, Collider)>();
            float firstSolid = float.MaxValue;
            foreach (var h in hits) if (!h.collider.isTrigger && !h.collider.GetComponentInParent<SurgeryInstrumentTag>() && !h.collider.GetComponentInParent<SurgeryImplantTag>()) firstSolid = Mathf.Min(firstSolid, h.distance);
            foreach (var h in hits)
            {
                var tag = h.collider.GetComponent<SurgeryAnatomyTag>();
                if (tag != null && h.distance <= firstSolid + 0.25f) candidates.Add((tag.targetId, h.collider));   // not through walls/furniture
            }
            if (candidates.Count == 0) return;
            string targetId = anatomyController != null ? anatomyController.ResolveTarget(candidates) : candidates[0].Item1;
            if (!string.IsNullOrEmpty(heldToolId) && anatomyController != null)
            {
                anatomyController.HandleContact(heldToolId, targetId);
            }
            else if (!string.IsNullOrEmpty(heldImplantId) && implantController != null)
            {
                implantController.CheckPlacementAttempt(heldImplantId);
            }
        }

        private void UpdateHeldObjectPosition()
        {
            if (heldObject == null || playerCamera == null) return;

            var tag = heldObject.GetComponent<SurgeryInstrumentTag>();
            if (TipLock.HasValue && tag && tag.tip)
            {   // guided drawing: the working tip rests on the skin, the tool tilted toward the student like a held pen
                var camFlat = Vector3.ProjectOnPlane(playerCamera.transform.forward, Vector3.up).normalized;
                var lockRot = Quaternion.LookRotation((Vector3.down * 0.8f + camFlat * 0.6f).normalized, camFlat);
                heldObject.rotation = Quaternion.Slerp(heldObject.rotation, lockRot, Time.deltaTime * followSpeed);
                heldObject.position += TipLock.Value - tag.tip.position;
                return;
            }

            Vector3 targetPos = playerCamera.transform.position +
                                playerCamera.transform.forward * currentHoldDistance +
                                playerCamera.transform.right * 0.15f -
                                playerCamera.transform.up * 0.12f;

            // Natural hold: the grip (pivot) sits low-right in view and the working tip points forward, slightly down and toward
            // the centre of view (where the student aims), instead of straight away from the camera.
            Quaternion grip = Quaternion.Euler(18f, -10f, 0f);
            Quaternion targetRot = playerCamera.transform.rotation * grip * targetHoldRotation;

            heldObject.position = Vector3.Lerp(heldObject.position, targetPos, Time.deltaTime * followSpeed);
            heldObject.rotation = Quaternion.Slerp(heldObject.rotation, targetRot, Time.deltaTime * followSpeed);
        }
    }
}
