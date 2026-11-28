using System;
using System.Collections.Generic;
using MedSimAI.Surgery.Shared.Unity;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace MedSimAI.Surgery.Mechanics
{
    /// <summary>
    /// Attach to an anatomy target collider or instrument tip to detect trigger contact.
    /// Forwards contacts to AnatomyInteractionController.
    ///
    /// Intentional application only (VR contact path). A contact counts as ONE attempt when all of these hold:
    ///   • the instrument is held by the student (not lying on the tray, not carried by the assistant),
    ///   • its working tip/blade (SurgeryInstrumentTag.tip) is at the target, within <see cref="tipTolerance"/>,
    ///   • the tip stays there for <see cref="minDwellSeconds"/> (a deliberate application, not a pass-by).
    /// Anything else — another part of the tool brushing the target, a quick pass, a tool that is not held — is ignored:
    /// no anatomy change, no rejected attempt, no penalty. While the tool stays in contact the attempt is not repeated;
    /// it must leave the target before it can be applied again. These are simulation parameters, not clinical values.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class AnatomyContactTrigger : MonoBehaviour
    {
        [Tooltip("How close the working tip must be to the target surface (m). Simulation parameter.")] public float tipTolerance = 0.03f;
        [Tooltip("How long the tip must stay at the target to count as a deliberate application (s). Simulation parameter.")] public float minDwellSeconds = 0.3f;

        /// <summary>Test/diagnostic counters: deliberate applications forwarded, and incidental contacts ignored.</summary>
        public static int Applied, IgnoredIncidental;
        /// <summary>Test hook: overrides "is this instrument held by the student?" (null = XR grab state).</summary>
        public static Func<SurgeryInstrumentTag, bool> HeldOverride;

        private AnatomyInteractionController interactionController;
        private SurgeryAnatomyTag anatomyTag;
        private SurgeryInstrumentTag instrumentTag;
        private Collider ownCollider;

        class Episode { public float dwell; public bool fired; }
        readonly Dictionary<int, Episode> episodes = new Dictionary<int, Episode>();

        private void Awake()
        {
            interactionController = FindFirstObjectByType<AnatomyInteractionController>();
            anatomyTag = GetComponent<SurgeryAnatomyTag>();
            instrumentTag = GetComponentInParent<SurgeryInstrumentTag>();

            ownCollider = GetComponent<Collider>();
            if (ownCollider != null) ownCollider.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other) { var p = Pair(other); if (p.inst != null) episodes[p.inst.GetInstanceID()] = new Episode(); }
        private void OnTriggerStay(Collider other) => Step(other, Time.fixedDeltaTime);
        private void OnTriggerExit(Collider other)
        {
            var p = Pair(other); if (p.inst == null) return;
            if (episodes.TryGetValue(p.inst.GetInstanceID(), out var e) && !e.fired) IgnoredIncidental++;
            episodes.Remove(p.inst.GetInstanceID());
        }

        /// <summary>One physics step of contact with <paramref name="other"/>. Returns true when a deliberate application was forwarded.</summary>
        public bool Step(Collider other, float dt)
        {
            var (inst, targetId, targetCol) = Pair(other);
            if (inst == null || string.IsNullOrEmpty(targetId)) return false;
            int key = inst.GetInstanceID();
            if (!episodes.TryGetValue(key, out var e)) episodes[key] = e = new Episode();
            if (e.fired) return false;                                                       // already counted for this contact
            if (!IsHeld(inst) || !TipAt(inst, targetCol)) { e.dwell = 0f; return false; }     // incidental: ignored, no penalty
            e.dwell += dt;
            if (e.dwell < minDwellSeconds) return false;
            if (interactionController == null) interactionController = FindFirstObjectByType<AnatomyInteractionController>();
            if (interactionController == null) return false;
            // Nested targets: when the tip also touches a more specific target that the current step needs (e.g. the marking
            // site inside the wound box), that target's own trigger counts the application; this enclosing box stays quiet.
            if (inst.tip != null)
            {
                var near = AnatomyInteractionController.TargetsNear(inst.tip.position, tipTolerance);
                if (near.Count > 1) { var best = interactionController.ResolveTarget(near); if (!string.IsNullOrEmpty(best) && best != targetId) return false; }
            }
            e.fired = true;
            Applied++;
            interactionController.HandleContact(inst.instrumentId, targetId);
            return true;
        }

        /// <summary>Ends the current contact episode for an instrument (what OnTriggerExit does); used by tests.</summary>
        public void EndContact(Collider other) => OnTriggerExit(other);

        (SurgeryInstrumentTag inst, string targetId, Collider targetCol) Pair(Collider other)
        {
            if (anatomyTag != null) return (other.GetComponentInParent<SurgeryInstrumentTag>(), anatomyTag.targetId, ownCollider);   // this = target
            if (instrumentTag != null) { var a = other.GetComponent<SurgeryAnatomyTag>(); return (a ? instrumentTag : null, a ? a.targetId : null, other); }   // this = tool tip
            return (null, null, null);
        }

        static bool IsHeld(SurgeryInstrumentTag inst)
        {
            if (HeldOverride != null) return HeldOverride(inst);
            var grab = inst.GetComponentInParent<XRGrabInteractable>();
            return grab != null && grab.isSelected;
        }

        bool TipAt(SurgeryInstrumentTag inst, Collider target)
        {
            if (inst.tip == null || target == null) return true;   // no authored tip: any part of the tool (previous behaviour)
            var p = inst.tip.position;
            return Vector3.Distance(target.ClosestPoint(p), p) <= tipTolerance;
        }
    }
}
