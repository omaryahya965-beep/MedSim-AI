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
        public Transform initialParent;
        public bool initialKinematic;
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

        private readonly Dictionary<string, TrackedImplant> implants = new Dictionary<string, TrackedImplant>();

        public event Action<string, bool, float, float> OnImplantPlacementAttempted; // (implantId, success, posErr, angErr)
        public event Action<string> OnImplantPlacementCompleted;

        private void Awake()
        {
            ResolveDependencies();
            if (autoRegisterSceneImplants) RegisterSceneImplants();
        }

        bool subscribed;
        void OnEnable() => ResolveDependencies();
        void OnDisable() { if (subscribed && scenarioEngine) scenarioEngine.OnObjectiveCompleted -= HandleObjectiveCompleted; subscribed = false; }

        void ResolveDependencies()
        {
            if (!scenarioEngine) { scenarioEngine = GetComponent<SurgeryScenarioEngine>(); if (!scenarioEngine) scenarioEngine = FindFirstObjectByType<SurgeryScenarioEngine>(); }
            if (!anatomyController) { anatomyController = GetComponent<AnatomyInteractionController>(); if (!anatomyController) anatomyController = FindFirstObjectByType<AnatomyInteractionController>(); }
            if (!subscribed && scenarioEngine) { scenarioEngine.OnObjectiveCompleted += HandleObjectiveCompleted; subscribed = true; }
        }

        /// <summary>Trials are taken out once the trial reduction is done, so the final components can be seated.</summary>
        void HandleObjectiveCompleted(SurgicalObjective o)
        {
            if (o == null || !SurgeryInteractionValidator.IsTrial(o.implantId)) return;
            foreach (var id in SurgeryInteractionValidator.ComponentsFor(o)) ResetImplant(id);
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
                initialParent = tag.transform.parent,
                initialKinematic = tag.GetComponent<Rigidbody>() != null && tag.GetComponent<Rigidbody>().isKinematic,
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
            ResolveDependencies();
            if (!implants.TryGetValue(implantId, out var item) || item.tag == null) return false;
            if (scenarioEngine == null || scenarioEngine.IsFinished) return false;
            var activeObj = scenarioEngine.CurrentObjective;
            if (activeObj == null) return false;

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

            string targetAnatomyId = SurgeryInteractionValidator.TargetOfImplant(implantId);
            if (string.IsNullOrEmpty(targetAnatomyId)) targetAnatomyId = ResolveTargetAnatomy(implantId, activeObj);
            var validation = SurgeryInteractionValidator.ValidateImplantPlacement(activeObj, implantId,
                item.tag.transform.position, item.tag.transform.rotation, targetPose.position, targetPose.rotation, scenarioEngine.Progress);

            SurgeryEventBusHost.Publish(SurgeryEvent.Implant(SurgeryEventIds.ImplantPlacementAttempted, implantId, targetAnatomyId,
                validation.PositionErrorMm, validation.AngleErrorDeg, validation.Success, activeObj.id));
            OnImplantPlacementAttempted?.Invoke(implantId, validation.Success, validation.PositionErrorMm, validation.AngleErrorDeg);

            if (!validation.Success)
            {
                SurgeryEventBusHost.Publish(SurgeryEvent.Rejected(validation.Reason, implantId, targetAnatomyId, activeObj.id));
                return false;
            }

            // Seat: snap to the authored pose and keep it there (kinematic, parented to the target so it follows the knee).
            item.isPlaced = true;
            scenarioEngine.Progress.PlacedImplants.Add(implantId);
            if (item.rb != null)
            {
                if (!item.rb.isKinematic) { item.rb.linearVelocity = Vector3.zero; item.rb.angularVelocity = Vector3.zero; }
                item.rb.isKinematic = true;
            }
            item.tag.transform.SetParent(targetPose, true);
            item.tag.transform.SetPositionAndRotation(targetPose.position, targetPose.rotation);

            SurgeryEventBusHost.Publish(SurgeryEvent.Implant(SurgeryEventIds.ImplantPlacementCompleted, implantId, targetAnatomyId,
                validation.PositionErrorMm, validation.AngleErrorDeg, true, activeObj.id));
            OnImplantPlacementCompleted?.Invoke(implantId);

            if (!string.IsNullOrEmpty(validation.NextAnatomyState))
            {
                var view = anatomyController != null ? anatomyController.GetTargetView(targetAnatomyId) : null;
                if (view != null) view.SetState(validation.NextAnatomyState);
                SurgeryEventBusHost.Publish(SurgeryEvent.StateChanged(targetAnatomyId, validation.NextAnatomyState, activeObj.id));
            }
            if (validation.CompletesObjective) scenarioEngine.CompleteCurrentObjective();
            return true;
        }

        private string ResolveTargetAnatomy(string implantId, SurgicalObjective activeObj)
        {
            if (implantId.Contains("femoral")) return AnatomyTargetIds.DistalFemur;
            if (implantId.Contains("tibial") || implantId.Contains("poly")) return AnatomyTargetIds.ProximalTibia;
            return activeObj.targets != null && activeObj.targets.Length > 0 ? activeObj.targets[0] : "";
        }

        public void ResetAllImplants()
        {
            foreach (var id in implants.Keys.ToList()) ResetImplant(id);
        }

        /// <summary>Returns one component to where it started (its own spot on the implant table) and makes it grabbable again.</summary>
        public void ResetImplant(string implantId)
        {
            if (!implants.TryGetValue(implantId, out var item) || item.tag == null) return;
            item.tag.transform.SetParent(item.initialParent, true);
            item.tag.transform.SetPositionAndRotation(item.initialPosition, item.initialRotation);
            if (item.rb != null)
            {
                item.rb.isKinematic = item.initialKinematic;
                if (!item.rb.isKinematic) { item.rb.linearVelocity = Vector3.zero; item.rb.angularVelocity = Vector3.zero; }
            }
            item.isPlaced = false;
        }
    }
}
