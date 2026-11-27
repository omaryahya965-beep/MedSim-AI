using System;
using System.Collections.Generic;
using System.Linq;
using MedSimAI.Surgery.Shared;
using MedSimAI.Surgery.Shared.Unity;
using UnityEngine;

namespace MedSimAI.Surgery.Mechanics
{
    /// <summary>
    /// Mediates interactions between instruments and anatomy targets.
    /// Evaluates actions against authored scenario objectives, triggers anatomy state changes,
    /// and publishes valid/rejected events to the SurgeryEventBus.
    /// </summary>
    public class AnatomyInteractionController : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private SurgeryScenarioEngine scenarioEngine;
        [SerializeField] private SurgicalInstrumentController instrumentController;

        private readonly Dictionary<string, IAnatomyStateView> targetViews = new Dictionary<string, IAnatomyStateView>();

        public event Action<string, string, string> OnValidInteraction; // (toolId, targetId, objId)
        public event Action<string, string, string, string> OnRejectedInteraction; // (reason, toolId, targetId, objId)

        private void Awake()
        {
            if (!scenarioEngine) scenarioEngine = FindFirstObjectByType<SurgeryScenarioEngine>();
            if (!instrumentController) instrumentController = FindFirstObjectByType<SurgicalInstrumentController>();

            RegisterSceneAnatomyViews();
        }

        public void RegisterSceneAnatomyViews()
        {
            targetViews.Clear();
            var views = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).OfType<IAnatomyStateView>();
            foreach (var v in views)
            {
                if (!string.IsNullOrEmpty(v.TargetId))
                {
                    targetViews[v.TargetId] = v;
                }
            }
        }

        public void RegisterTargetView(IAnatomyStateView view)
        {
            if (view != null && !string.IsNullOrEmpty(view.TargetId))
            {
                targetViews[view.TargetId] = view;
            }
        }

        public IAnatomyStateView GetTargetView(string targetId)
        {
            targetViews.TryGetValue(targetId, out var view);
            return view;
        }

        /// <summary>
        /// Handles physical contact between a surgical tool and an anatomical target.
        /// </summary>
        public bool HandleContact(string toolId, string targetId, bool isRightSide = true)
        {
            if (scenarioEngine == null || scenarioEngine.IsFinished) return false;

            var activeObj = scenarioEngine.CurrentObjective;
            if (activeObj == null)
            {
                SurgeryEventBusHost.Publish(SurgeryEvent.Rejected(RejectReasons.PrerequisiteMissing, toolId, targetId));
                return false;
            }

            bool isToolActive = instrumentController != null && instrumentController.IsToolActive(toolId);

            var validation = SurgeryInteractionValidator.ValidateToolInteraction(
                activeObj, toolId, targetId, isToolActive, isRightSide);

            if (validation.Success)
            {
                // 1. Publish anatomy.interacted
                SurgeryEventBusHost.Publish(SurgeryEvent.Anatomy(targetId, toolId, activeObj.id));

                // 2. Change anatomy state if a new state is specified
                if (!string.IsNullOrEmpty(validation.NextAnatomyState))
                {
                    if (targetViews.TryGetValue(targetId, out var view))
                    {
                        view.SetState(validation.NextAnatomyState);
                    }

                    SurgeryEventBusHost.Publish(SurgeryEvent.StateChanged(targetId, validation.NextAnatomyState, activeObj.id));
                }

                OnValidInteraction?.Invoke(toolId, targetId, activeObj.id);

                // 3. Complete objective if applicable
                if (validation.CompletesObjective)
                {
                    scenarioEngine.CompleteCurrentObjective();
                }

                return true;
            }
            else
            {
                // Ordering guarantee §7.3: A rejected attempt publishes interaction.rejected and does not change anatomy state
                SurgeryEventBusHost.Publish(SurgeryEvent.Rejected(validation.Reason, toolId, targetId, activeObj.id));
                OnRejectedInteraction?.Invoke(validation.Reason, toolId, targetId, activeObj.id);
                return false;
            }
        }

        /// <summary>
        /// Resets all registered anatomy targets to intact state.
        /// </summary>
        public void ResetAllAnatomyViews()
        {
            foreach (var kv in targetViews)
            {
                if (kv.Value is AnatomyStateController ctrl)
                {
                    ctrl.ResetToDefault();
                }
                else
                {
                    kv.Value.SetState(AnatomyStates.Intact);
                }
            }
        }
    }
}
