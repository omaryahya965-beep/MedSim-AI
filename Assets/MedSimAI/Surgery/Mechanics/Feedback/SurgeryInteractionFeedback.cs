using System;
using MedSimAI.Surgery.Shared;
using MedSimAI.Surgery.Shared.Unity;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace MedSimAI.Surgery.Mechanics
{
    /// <summary>
    /// Provides audio, visual highlight, and XR controller haptic feedback
    /// for valid and invalid surgical interactions and state transitions.
    /// </summary>
    public class SurgeryInteractionFeedback : MonoBehaviour
    {
        [Header("Audio Source")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip successClip;
        [SerializeField] private AudioClip rejectClip;
        [SerializeField] private AudioClip cutClip;
        [SerializeField] private AudioClip sawLoopClip;
        [SerializeField] private AudioClip impactClip;

        [Header("XR Interactors for Haptics")]
        [SerializeField] private XRBaseInputInteractor leftController;
        [SerializeField] private XRBaseInputInteractor rightController;

        private void Awake()
        {
            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
                if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
            }
            audioSource.playOnAwake = false;
        }

        private void Start()
        {
            if (SurgeryEventBusHost.Instance != null && SurgeryEventBusHost.Instance.Bus != null)
            {
                SurgeryEventBusHost.Instance.Bus.EventPublished += HandleEvent;
            }
        }

        private void OnDestroy()
        {
            if (SurgeryEventBusHost.Instance != null && SurgeryEventBusHost.Instance.Bus != null)
            {
                SurgeryEventBusHost.Instance.Bus.EventPublished -= HandleEvent;
            }
        }

        private void HandleEvent(SurgeryEvent e)
        {
            if (e == null) return;

            switch (e.id)
            {
                case SurgeryEventIds.AnatomyInteracted:
                    PlaySound(cutClip != null ? cutClip : successClip);
                    SendHapticPulse(0.5f, 0.1f);
                    break;

                case SurgeryEventIds.InteractionRejected:
                    PlaySound(rejectClip);
                    SendHapticPulse(0.9f, 0.2f);
                    break;

                case SurgeryEventIds.ImplantPlacementCompleted:
                case SurgeryEventIds.ObjectiveCompleted:
                    PlaySound(impactClip != null ? impactClip : successClip);
                    SendHapticPulse(0.7f, 0.15f);
                    break;

                case SurgeryEventIds.ToolGrabbed:
                    SendHapticPulse(0.25f, 0.05f);
                    break;
            }
        }

        public void PlaySound(AudioClip clip)
        {
            if (audioSource != null && clip != null)
            {
                audioSource.PlayOneShot(clip);
            }
        }

        public void SendHapticPulse(float amplitude, float duration)
        {
            TriggerHapticOn(rightController, amplitude, duration);
            TriggerHapticOn(leftController, amplitude, duration);
        }

        private void TriggerHapticOn(XRBaseInputInteractor interactor, float amplitude, float duration)
        {
            if (interactor != null)
            {
                interactor.SendHapticImpulse(amplitude, duration);
            }
        }
    }
}
