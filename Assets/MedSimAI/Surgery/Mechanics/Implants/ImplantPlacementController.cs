using System;
using System.Collections.Generic;
using System.Linq;
using MedSimAI.Surgery.Shared;
using MedSimAI.Surgery.Shared.Unity;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace MedSimAI.Surgery.Mechanics
{
    [Serializable]
    public class TrackedImplant
    {
        public string implantId;
        public SurgeryImplantTag tag;
        public XRGrabInteractable grabInteractable;
        public Rigidbody rb;
        public Vector3 initialPosition;
        public Quaternion initialRotation;
        public bool isPlaced;
    }

    /// <summary>
    /// Handles placement of generic implant and trial components.
    /// Calculates alignment tolerances, emits placement and rejection events,
    /// and manages smooth seating onto authored target poses.
    /// </summary>
    public class ImplantPlacementController : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private SurgeryScenarioEngine scenarioEngine;
        [SerializeField] private AnatomyInteractionController anatomyController;
        [SerializeField] private bool autoRegisterSceneImplants = true;
        [SerializeField] private float snapSpeed = 10f;

        private readonly Dictionary<string, TrackedImplant> implants = new Dictionary<string, TrackedImplant>();

        public event Action<string, bool, float, float> OnImplantPlacementAttempted; // (implantId, success, posErr, angErr)
        public event Action<string> OnImplantPlacementCompleted;

        private void Awake()
        {
            if (!scenarioEngine) scenarioEngine = FindFirstObjectByType<SurgeryScenarioEngine>();
            if (!anatomyController) anatomyController = FindFirstObjectByType<AnatomyInteractionController>();

            if (autoRegisterSceneImplants)
            {
                RegisterSceneImplants();
            }
        }

        public void RegisterSceneImplants()
        {
            var tags = FindObjectsByType<SurgeryImplantTag>(FindObjectsSortMode.None);
            foreach (var tag in tags)
            {
                RegisterImplant(tag);
            }
        }

        public void RegisterImplant(SurgeryImplantTag tag)
        {
            if (tag == null || string.IsNullOrEmpty(tag.implantId)) return;

            string id = tag.implantId;
            var tracked = new TrackedImplant
            {
                implantId = id,
                tag = tag,
                grabInteractable = tag.GetComponent<XRGrabInteractable>(),
                rb = tag.GetComponent<Rigidbody>(),
                initialPosition = tag.transform.position,
                initialRotation = tag.transform.rotation,
                isPlaced = false
            };

            // When released in VR, attempt placement validation
            if (tracked.grabInteractable != null)
            {
                tracked.grabInteractable.selectExited.AddListener(args => CheckPlacementAttempt(id));
            }

            implants[id] = tracked;
        }

        public TrackedImplant GetTrackedImplant(string implantId)
        {
            implants.TryGetValue(implantId, out var item);
            return item;
        }

        /// <summary>
        /// Validates placement of an implant against its authored target pose.
        /// </summary>
        public bool CheckPlacementAttempt(string implantId)
        {
            if (!implants.TryGetValue(implantId, out var item) || item.tag == null) return false;
            if (scenarioEngine == null || scenarioEngine.IsFinished) return false;

            var activeObj = scenarioEngine.CurrentObjective;
            if (activeObj == null) return false;

            // Find authored target pose: from tag.placementTarget or scene search "PlacementTarget_<implantId>"
            Transform targetPose = item.tag.placementTarget;
            if (targetPose == null)
            {
                var targetObj = GameObject.Find("PlacementTarget_" + implantId);
                if (targetObj != null) targetPose = targetObj.transform;
            }

            if (targetPose == null)
            {
                Debug.LogWarning($"[ImplantPlacementController] No placement target found for {implantId}");
                return false;
            }

            string targetAnatomyId = ResolveTargetAnatomy(implantId, activeObj);

            Vector3 actualPos = item.tag.transform.position;
            Quaternion actualRot = item.tag.transform.rotation;
            Vector3 expectedPos = targetPose.position;
            Quaternion expectedRot = targetPose.rotation;

            var validation = SurgeryInteractionValidator.ValidateImplantPlacement(
                activeObj,
                implantId,
                targetAnatomyId,
                actualPos,
                actualRot,
                expectedPos,
                expectedRot);

            // 1. Emit implant.placement_attempted
            SurgeryEventBusHost.Publish(SurgeryEvent.Implant(
                SurgeryEventIds.ImplantPlacementAttempted,
                implantId,
                targetAnatomyId,
                validation.PositionErrorMm,
                validation.AngleErrorDeg,
                validation.Success,
                activeObj.id));

            OnImplantPlacementAttempted?.Invoke(implantId, validation.Success, validation.PositionErrorMm, validation.AngleErrorDeg);

            if (validation.Success)
            {
                // Snap implant to authored pose
                item.isPlaced = true;
                if (item.rb != null)
                {
                    item.rb.isKinematic = true;
                    item.rb.linearVelocity = Vector3.zero;
                    item.rb.angularVelocity = Vector3.zero;
                }

                item.tag.transform.position = expectedPos;
                item.tag.transform.rotation = expectedRot;

                // 2. Emit implant.placement_completed
                SurgeryEventBusHost.Publish(SurgeryEvent.Implant(
                    SurgeryEventIds.ImplantPlacementCompleted,
                    implantId,
                    targetAnatomyId,
                    validation.PositionErrorMm,
                    validation.AngleErrorDeg,
                    true,
                    activeObj.id));

                OnImplantPlacementCompleted?.Invoke(implantId);

                // 3. Update anatomy state
                if (!string.IsNullOrEmpty(validation.NextAnatomyState))
                {
                    var view = anatomyController != null ? anatomyController.GetTargetView(targetAnatomyId) : null;
                    if (view != null) view.SetState(validation.NextAnatomyState);

                    SurgeryEventBusHost.Publish(SurgeryEvent.StateChanged(targetAnatomyId, validation.NextAnatomyState, activeObj.id));
                }

                // 4. Complete objective
                if (validation.CompletesObjective)
                {
                    scenarioEngine.CompleteCurrentObjective();
                }

                return true;
            }
            else
            {
                // Emit rejection
                SurgeryEventBusHost.Publish(SurgeryEvent.Rejected(validation.Reason, implantId, targetAnatomyId, activeObj.id));
                return false;
            }
        }

        private string ResolveTargetAnatomy(string implantId, SurgicalObjective activeObj)
        {
            if (implantId.Contains("femoral")) return AnatomyTargetIds.DistalFemur;
            if (implantId.Contains("tibial") || implantId.Contains("poly")) return AnatomyTargetIds.ProximalTibia;
            return activeObj.targets != null && activeObj.targets.Length > 0 ? activeObj.targets[0] : "";
        }

        public void ResetAllImplants()
        {
            foreach (var kv in implants)
            {
                var item = kv.Value;
                if (item.tag != null)
                {
                    var implantTableAnchor = SurgeryAnchor.Find(AnchorIds.ImplantTable);
                    if (implantTableAnchor != null)
                    {
                        item.tag.transform.position = implantTableAnchor.position;
                        item.tag.transform.rotation = implantTableAnchor.rotation;
                    }
                    else
                    {
                        item.tag.transform.position = item.initialPosition;
                        item.tag.transform.rotation = item.initialRotation;
                    }

                    if (item.rb != null)
                    {
                        item.rb.isKinematic = false;
                        item.rb.linearVelocity = Vector3.zero;
                        item.rb.angularVelocity = Vector3.zero;
                    }
                }
                item.isPlaced = false;
            }
        }
    }
}
