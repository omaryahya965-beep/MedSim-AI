using System;
using MedSimAI.Surgery.Shared.Unity;
using UnityEngine;

namespace MedSimAI.Surgery.Mechanics
{
    /// <summary>
    /// Attach to an anatomy target collider or instrument tip to detect trigger contact.
    /// Forwards contacts to AnatomyInteractionController.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class AnatomyContactTrigger : MonoBehaviour
    {
        private AnatomyInteractionController interactionController;
        private SurgeryAnatomyTag anatomyTag;
        private SurgeryInstrumentTag instrumentTag;

        private void Awake()
        {
            interactionController = FindFirstObjectByType<AnatomyInteractionController>();
            anatomyTag = GetComponent<SurgeryAnatomyTag>();
            instrumentTag = GetComponentInParent<SurgeryInstrumentTag>();

            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (interactionController == null)
            {
                interactionController = FindFirstObjectByType<AnatomyInteractionController>();
                if (interactionController == null) return;
            }

            // Case A: This object is Anatomy Target, other is Instrument Tip
            if (anatomyTag != null)
            {
                var otherInstTag = other.GetComponentInParent<SurgeryInstrumentTag>();
                if (otherInstTag != null)
                {
                    interactionController.HandleContact(otherInstTag.instrumentId, anatomyTag.targetId);
                }
            }
            // Case B: This object is Instrument Tip, other is Anatomy Target
            else if (instrumentTag != null)
            {
                var otherAnatomyTag = other.GetComponent<SurgeryAnatomyTag>();
                if (otherAnatomyTag != null)
                {
                    interactionController.HandleContact(instrumentTag.instrumentId, otherAnatomyTag.targetId);
                }
            }
        }
    }
}
