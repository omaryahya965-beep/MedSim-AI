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
            ResolveDependencies();
            RegisterSceneAnatomyViews();
        }

        /// <summary>Lazy wiring: also works for controllers created from code or in EditMode tests (no Awake there).</summary>
        void ResolveDependencies()
        {
            if (!scenarioEngine) { scenarioEngine = GetComponent<SurgeryScenarioEngine>(); if (!scenarioEngine) scenarioEngine = FindFirstObjectByType<SurgeryScenarioEngine>(); }
            if (!instrumentController) { instrumentController = GetComponent<SurgicalInstrumentController>(); if (!instrumentController) instrumentController = FindFirstObjectByType<SurgicalInstrumentController>(); }
        }

        public void RegisterSceneAnatomyViews()
        {
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
            if (!targetViews.ContainsKey(targetId)) RegisterSceneAnatomyViews();
            targetViews.TryGetValue(targetId, out var view);
            return view;
        }

        /// <summary>Targets on the non-operative (left) knee are only there to detect wrong-side attempts.</summary>
        public static bool IsOperativeSide(string targetId) => string.IsNullOrEmpty(targetId) || !targetId.StartsWith("knee_l.");

        /// <summary>
        /// Handles physical contact between a surgical tool and an anatomical target. Every outcome is published:
        /// valid → anatomy.interacted (+ anatomy.state_changed, objective.completed when the objective is done);
        /// invalid → interaction.rejected and no state change (contract §7.3).
        /// </summary>
        public bool HandleContact(string toolId, string targetId) => HandleContact(toolId, targetId, IsOperativeSide(targetId));

        public bool HandleContact(string toolId, string targetId, bool isRightSide)
        {
            ResolveDependencies();
            if (scenarioEngine == null || scenarioEngine.IsFinished) return false;

            var activeObj = scenarioEngine.CurrentObjective;
            if (activeObj == null)
            {
                SurgeryEventBusHost.Publish(SurgeryEvent.Rejected(RejectReasons.PrerequisiteMissing, toolId, targetId));
                return false;
            }

            bool isToolActive = instrumentController != null && instrumentController.IsToolActive(toolId);
            var validation = SurgeryInteractionValidator.ValidateToolInteraction(activeObj, toolId, targetId, isToolActive, isRightSide, scenarioEngine.Progress);

            if (!validation.Success)
            {
                SurgeryEventBusHost.Publish(SurgeryEvent.Rejected(validation.Reason, toolId, targetId, activeObj.id));
                OnRejectedInteraction?.Invoke(validation.Reason, toolId, targetId, activeObj.id);
                return false;
            }

            SurgeryEventBusHost.Publish(SurgeryEvent.Anatomy(targetId, toolId, activeObj.id));
            if (validation.NextAnatomyState == AnatomyStates.GuidePlaced) scenarioEngine.Progress.GuidedTargets.Add(targetId);

            if (!string.IsNullOrEmpty(validation.NextAnatomyState))
            {
                var view = GetTargetView(targetId);
                if (view != null) view.SetState(validation.NextAnatomyState);
                SurgeryEventBusHost.Publish(SurgeryEvent.StateChanged(targetId, validation.NextAnatomyState, activeObj.id));
            }

            OnValidInteraction?.Invoke(toolId, targetId, activeObj.id);
            if (validation.CompletesObjective) scenarioEngine.CompleteCurrentObjective();
            return true;
        }

        /// <summary>
        /// Resets all registered anatomy targets to intact state.
        /// </summary>
        public void ResetAllAnatomyViews()
        {
            RegisterSceneAnatomyViews();
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
