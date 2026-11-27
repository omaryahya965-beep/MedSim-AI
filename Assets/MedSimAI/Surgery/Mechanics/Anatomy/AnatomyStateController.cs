using System;
using System.Collections.Generic;
using System.Linq;
using MedSimAI.Surgery.Shared;
using MedSimAI.Surgery.Shared.Unity;
using UnityEngine;

namespace MedSimAI.Surgery.Mechanics
{
    [Serializable]
    public class StateVisualBinding
    {
        public string stateName = AnatomyStates.Intact;
        public GameObject visualObject;
        public Material stateMaterial;
        public Renderer targetRenderer;
    }

    /// <summary>
    /// Implements IAnatomyStateView on an anatomical target.
    /// Controls deterministic state-based visual transitions (swapping mesh parts or materials).
    /// </summary>
    [RequireComponent(typeof(SurgeryAnatomyTag))]
    public class AnatomyStateController : MonoBehaviour, IAnatomyStateView
    {
        [Header("State Bindings")]
        [SerializeField] private string defaultState = AnatomyStates.Intact;
        [SerializeField] private List<StateVisualBinding> stateBindings = new List<StateVisualBinding>();

        private SurgeryAnatomyTag anatomyTag;
        private string currentState;

        public string TargetId => anatomyTag != null ? anatomyTag.targetId : "";
        public string CurrentState => currentState;
        public IReadOnlyList<string> SupportedStates => stateBindings.Select(b => b.stateName).ToList();

        public event Action<string, string> OnStateTransition; // (targetId, newState)

        private void Awake()
        {
            anatomyTag = GetComponent<SurgeryAnatomyTag>();
            if (string.IsNullOrEmpty(currentState))
            {
                currentState = string.IsNullOrEmpty(defaultState) ? AnatomyStates.Intact : defaultState;
            }
            ApplyVisualState(currentState);
        }

        public bool SetState(string newState)
        {
            if (string.IsNullOrEmpty(newState)) return false;

            // If we have explicit bindings, check support
            if (stateBindings.Count > 0 && !stateBindings.Any(b => string.Equals(b.stateName, newState, StringComparison.OrdinalIgnoreCase)))
            {
                // Not supported
                return false;
            }

            string previousState = currentState;
            currentState = newState;
            ApplyVisualState(newState);

            OnStateTransition?.Invoke(TargetId, newState);
            return true;
        }

        public void ResetToDefault()
        {
            SetState(string.IsNullOrEmpty(defaultState) ? AnatomyStates.Intact : defaultState);
        }

        private void ApplyVisualState(string state)
        {
            if (stateBindings == null || stateBindings.Count == 0) return;

            foreach (var binding in stateBindings)
            {
                bool matches = string.Equals(binding.stateName, state, StringComparison.OrdinalIgnoreCase);

                if (binding.visualObject != null)
                {
                    binding.visualObject.SetActive(matches);
                }

                if (matches && binding.targetRenderer != null && binding.stateMaterial != null)
                {
                    binding.targetRenderer.material = binding.stateMaterial;
                }
            }
        }
    }
}
