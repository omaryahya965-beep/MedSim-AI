using System;
using System.Collections.Generic;
using System.Linq;
using MedSimAI.Surgery.Shared;
using MedSimAI.Surgery.Shared.Unity;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace MedSimAI.Surgery.Mechanics
{
    [Serializable]
    public class TrackedInstrument
    {
        public string instrumentId;
        public SurgeryInstrumentTag tag;
        public XRGrabInteractable grabInteractable;
        public Rigidbody rb;
        public Vector3 initialPosition;
        public Quaternion initialRotation;
        public Transform initialParent;
        public InstrumentHolder holder = InstrumentHolder.OnTable;
        public bool isActivated;
        public bool initialKinematic;
    }

    /// <summary>
    /// Manages physical instrument interactions, XR grab bindings, desktop interaction hooks,
    /// power tool activation states, and registry tracking. Implements IInstrumentRegistry.
    /// </summary>
    public class SurgicalInstrumentController : MonoBehaviour, IInstrumentRegistry
    {
        [Header("Runtime References")]
        [SerializeField] private SurgeryScenarioEngine scenarioEngine;
        [SerializeField] private bool autoRegisterSceneInstruments = true;

        private readonly Dictionary<string, TrackedInstrument> instruments = new Dictionary<string, TrackedInstrument>();

        public event Action<string> OnToolGrabbed;
        public event Action<string> OnToolReleased;
        public event Action<string, bool> OnToolActivationChanged;

        // IInstrumentRegistry implementation
        public IReadOnlyList<string> AvailableInstrumentIds => instruments.Keys.ToList();

        public bool IsAvailable(string instrumentId)
        {
            return instruments.TryGetValue(instrumentId, out var item) && item.holder == InstrumentHolder.OnTable;
        }

        public InstrumentHolder HolderOf(string instrumentId)
        {
            return instruments.TryGetValue(instrumentId, out var item) ? item.holder : InstrumentHolder.Missing;
        }

        private void Awake()
        {
            if (!scenarioEngine)
                scenarioEngine = FindFirstObjectByType<SurgeryScenarioEngine>();

            if (autoRegisterSceneInstruments)
            {
                RegisterSceneInstruments();
            }
        }

        public void RegisterSceneInstruments()
        {
            var tags = FindObjectsByType<SurgeryInstrumentTag>(FindObjectsSortMode.None);
            foreach (var tag in tags)
            {
                RegisterInstrument(tag);
            }
        }

        public void RegisterInstrument(SurgeryInstrumentTag tag)
        {
            if (tag == null || string.IsNullOrEmpty(tag.instrumentId)) return;

            string id = tag.instrumentId;
            var tracked = new TrackedInstrument
            {
                instrumentId = id,
                tag = tag,
                grabInteractable = tag.GetComponent<XRGrabInteractable>(),
                rb = tag.GetComponent<Rigidbody>(),
                initialPosition = tag.transform.position,
                initialRotation = tag.transform.rotation,
                initialParent = tag.transform.parent,
                initialKinematic = tag.GetComponent<Rigidbody>() != null && tag.GetComponent<Rigidbody>().isKinematic,
                holder = InstrumentHolder.OnTable,
                isActivated = false
            };

            // Wire up XRI events if present
            if (tracked.grabInteractable != null)
            {
                tracked.grabInteractable.selectEntered.AddListener(args => HandleGrab(id, InstrumentHolder.Player));
                tracked.grabInteractable.selectExited.AddListener(args => HandleRelease(id));
                tracked.grabInteractable.activated.AddListener(args => SetToolActive(id, true));
                tracked.grabInteractable.deactivated.AddListener(args => SetToolActive(id, false));
            }

            instruments[id] = tracked;
        }

        public TrackedInstrument GetTrackedInstrument(string instrumentId)
        {
            instruments.TryGetValue(instrumentId, out var tracked);
            return tracked;
        }

        public Transform GetToolTip(string instrumentId)
        {
            if (instruments.TryGetValue(instrumentId, out var tracked) && tracked.tag != null)
            {
                return tracked.tag.tip != null ? tracked.tag.tip : tracked.tag.transform;
            }
            return null;
        }

        public bool IsToolActive(string toolId)
        {
            return instruments.TryGetValue(toolId, out var item) && item.isActivated;
        }

        public void HandleGrab(string toolId, InstrumentHolder newHolder = InstrumentHolder.Player)
        {
            if (!instruments.TryGetValue(toolId, out var item)) return;

            item.holder = newHolder;
            if (!scenarioEngine) { scenarioEngine = GetComponent<SurgeryScenarioEngine>(); if (!scenarioEngine) scenarioEngine = FindFirstObjectByType<SurgeryScenarioEngine>(); }
            string activeObjId = scenarioEngine != null ? scenarioEngine.CurrentObjectiveId : "";

            SurgeryEventBusHost.Publish(SurgeryEvent.Tool(SurgeryEventIds.ToolGrabbed, toolId, activeObjId));
            OnToolGrabbed?.Invoke(toolId);
        }

        public void HandleRelease(string toolId)
        {
            if (!instruments.TryGetValue(toolId, out var item)) return;

            item.holder = InstrumentHolder.OnTable;
            item.isActivated = false;

            SurgeryEventBusHost.Publish(SurgeryEvent.Tool(SurgeryEventIds.ToolReleased, toolId));
            OnToolReleased?.Invoke(toolId);
        }

        public void SetToolActive(string toolId, bool active)
        {
            if (!instruments.TryGetValue(toolId, out var item)) return;
            if (item.isActivated == active) return;

            item.isActivated = active;
            if (active)
            {
                SurgeryEventBusHost.Publish(SurgeryEvent.Tool(SurgeryEventIds.ToolActivated, toolId));
            }

            OnToolActivationChanged?.Invoke(toolId, active);
        }

        public void SetHolder(string toolId, InstrumentHolder holder)
        {
            if (instruments.TryGetValue(toolId, out var item))
            {
                item.holder = holder;
            }
        }

        /// <summary>
        /// Resets all instruments to their initial table slots or anchor points.
        /// </summary>
        public void ResetAllInstruments()
        {
            foreach (var kv in instruments)
            {
                var item = kv.Value;
                if (item.tag != null)
                {
                    // Back to where it started (its tray slot): same parent, pose and physics state.
                    item.tag.transform.SetParent(item.initialParent, true);
                    var slotAnchor = item.initialParent ? null : SurgeryAnchor.Find(AnchorIds.InstrumentSlot(item.instrumentId));
                    if (slotAnchor != null) item.tag.transform.SetPositionAndRotation(slotAnchor.position, slotAnchor.rotation);
                    else item.tag.transform.SetPositionAndRotation(item.initialPosition, item.initialRotation);
                    if (item.rb != null)
                    {
                        item.rb.isKinematic = item.initialKinematic;
                        if (!item.rb.isKinematic) { item.rb.linearVelocity = Vector3.zero; item.rb.angularVelocity = Vector3.zero; }
                    }
                }

                item.holder = InstrumentHolder.OnTable;
                item.isActivated = false;
            }
        }
    }
}
