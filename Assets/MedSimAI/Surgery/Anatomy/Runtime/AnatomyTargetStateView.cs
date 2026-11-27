using System;
using System.Collections.Generic;
using MedSimAI.Surgery.Shared;
using MedSimAI.Surgery.Shared.Unity;
using UnityEngine;

namespace MedSimAI.Surgery.Anatomy
{
    /// <summary>
    /// Contract §6.2 / §8: one per anatomy interaction target (with a trigger collider). Implements IAnatomyStateView:
    /// visual state only — mechanics (Person 3) decide WHEN to change it; unsupported states are refused and change nothing.
    /// It also carries the target id and adds the shared <see cref="SurgeryAnatomyTag"/> at runtime (execution order −1000),
    /// because SurgerySceneTags.cs currently cannot serialize that tag in prefabs (see InstrumentTagBridge for details).
    /// The owning <see cref="KneeAnatomyStateController"/> composes the visuals of all targets (skin, bones, patella...).
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public class AnatomyTargetStateView : MonoBehaviour, IAnatomyStateView
    {
        [SerializeField] string targetId = "";
        [Tooltip("First entry is the initial/reset state. Values from AnatomyStates (scenario JSON targets[].states).")]
        [SerializeField] string[] supportedStates = { AnatomyStates.Intact };
        [SerializeField] KneeAnatomyStateController controller;
        string current;

        public event Action<AnatomyTargetStateView, string> StateChanged;

        public string TargetId => targetId;
        public string CurrentState => current ?? (supportedStates.Length > 0 ? supportedStates[0] : AnatomyStates.Intact);
        public IReadOnlyList<string> SupportedStates => supportedStates;
        public KneeAnatomyStateController Controller => controller;

        void Awake() => EnsureTag();

        /// <summary>Adds (or refreshes) the shared SurgeryAnatomyTag carrying this target id.</summary>
        public SurgeryAnatomyTag EnsureTag()
        {
            var t = GetComponent<SurgeryAnatomyTag>(); if (!t) t = gameObject.AddComponent<SurgeryAnatomyTag>();
            t.targetId = targetId; return t;
        }

        public bool SetState(string state)
        {
            if (string.IsNullOrEmpty(state) || Array.IndexOf(supportedStates, state) < 0) return false;
            if (state == CurrentState && current != null) return true;
            current = state;
            StateChanged?.Invoke(this, state);
            if (controller) controller.OnTargetStateChanged(this);
            return true;
        }

        /// <summary>Back to the first supported state (normally "intact").</summary>
        public void ResetState() { if (supportedStates.Length > 0) SetState(supportedStates[0]); }

#if UNITY_EDITOR
        /// <summary>Editor builder only.</summary>
        public void EditorConfigure(string id, string[] states, KneeAnatomyStateController owner) { targetId = id; supportedStates = states; controller = owner; }
#endif
    }
}
