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
        /// Guided interactions (e.g. the site-marking guide) see a contact first and may take it over: return true to consume
        /// it (no validation, no rejection). The guide then reports the finished action through <see cref="HandleGuidedContact"/>.
        /// </summary>
        public static Func<string, string, bool> ContactInterceptor;
        bool bypassInterceptor;
        /// <summary>A guided interaction finished: validate it exactly like a direct contact (the interceptor is skipped).</summary>
        public bool HandleGuidedContact(string toolId, string targetId)
        {
            bypassInterceptor = true;
            try { return HandleContact(toolId, targetId, IsOperativeSide(targetId)); } finally { bypassInterceptor = false; }
        }

        /// <summary>
        /// The knee's interaction targets are nested trigger boxes (the wound box encloses the marking and incision boxes, the
        /// patella sits inside the capsule…), so one touch or one aim ray can hit several. The target the current objective
        /// needs wins; next a left-knee target (a wrong-side attempt must still be caught); otherwise the smallest box (the
        /// most specific structure). Fixes "pen on the knee → wrong_target (wound)" reported in manual testing.
        /// </summary>
        public string ResolveTarget(IEnumerable<(string id, Collider col)> candidates)
        {
            var list = candidates.Where(c => !string.IsNullOrEmpty(c.id)).ToList();
            if (list.Count == 0) return null;
            ResolveDependencies();
            var needed = scenarioEngine != null ? scenarioEngine.CurrentObjective?.targets : null;
            if (needed != null) { var hit = list.FirstOrDefault(c => needed.Contains(c.id)); if (hit.id != null) return hit.id; }
            var left = list.FirstOrDefault(c => !IsOperativeSide(c.id)); if (left.id != null) return left.id;
            return list.OrderBy(c => c.col ? Volume(c.col.bounds) : float.MaxValue).First().id;
        }
        static float Volume(Bounds b) => b.size.x * b.size.y * b.size.z;

        /// <summary>All anatomy targets whose collider is within <paramref name="tolerance"/> of a point (e.g. an instrument tip).</summary>
        public static List<(string id, Collider col)> TargetsNear(Vector3 p, float tolerance)
        {
            var r = new List<(string, Collider)>();
            foreach (var t in FindObjectsByType<SurgeryAnatomyTag>(FindObjectsSortMode.None))
            {
                var c = t.GetComponent<Collider>(); if (!c || !c.enabled || !t.gameObject.activeInHierarchy) continue;
                if (Vector3.Distance(c.ClosestPoint(p), p) <= tolerance) r.Add((t.targetId, c));
            }
            return r;
        }

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
            if (!bypassInterceptor && ContactInterceptor != null && ContactInterceptor(toolId, targetId)) return false;   // a guided interaction owns it

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
