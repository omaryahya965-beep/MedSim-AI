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

            // R: Reset simulation
            if (keyboard.rKey.wasPressedThisFrame && (resetController != null || ResetRequested != null))
            {
                DropHeldItem();
                if (ResetRequested != null) ResetRequested(); else resetController.ExecuteReset();
                return;
            }

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
            if (Physics.Raycast(ray, out RaycastHit hit, pickupRange))
            {
                var anatomyTag = hit.collider.GetComponent<SurgeryAnatomyTag>();
                if (anatomyTag != null && !string.IsNullOrEmpty(heldToolId) && anatomyController != null)
                {
                    anatomyController.HandleContact(heldToolId, anatomyTag.targetId);
                }
                else if (anatomyTag != null && !string.IsNullOrEmpty(heldImplantId) && implantController != null)
                {
                    implantController.CheckPlacementAttempt(heldImplantId);
                }
            }
        }

        private void UpdateHeldObjectPosition()
        {
            if (heldObject == null || playerCamera == null) return;

            Vector3 targetPos = playerCamera.transform.position +
                                playerCamera.transform.forward * currentHoldDistance +
                                playerCamera.transform.right * 0.15f -
                                playerCamera.transform.up * 0.12f;

            Quaternion targetRot = playerCamera.transform.rotation * targetHoldRotation;

            heldObject.position = Vector3.Lerp(heldObject.position, targetPos, Time.deltaTime * followSpeed);
            heldObject.rotation = Quaternion.Slerp(heldObject.rotation, targetRot, Time.deltaTime * followSpeed);
        }
    }
}
