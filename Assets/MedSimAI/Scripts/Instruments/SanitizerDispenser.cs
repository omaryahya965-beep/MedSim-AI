using System.Collections;
using System.Collections.Generic;
using MedSimAI.Audio;
using MedSimAI.Clinical;
using MedSimAI.Core;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace MedSimAI.Instruments
{
    /// <summary>Hand sanitiser dispenser: hovering a hand underneath records hand hygiene (not scored in v0.1 rubric).</summary>
    public class SanitizerDispenser : MonoBehaviour
    {
        public XRSimpleInteractable trigger; public AudioSource sfx; public AudioClip dispense;
        void Awake() { if (trigger) trigger.selectEntered.AddListener(_ => Use()); }
        public void Use()
        {
            if (sfx && dispense) sfx.PlayOneShot(dispense);
            var s = ClinicalScenarioManager.Instance.Session;
            if (!s.Tracker.Has(ClinicalEvents.HandsSanitized)) { s.Record(ClinicalEvents.HandsSanitized); ClinicalFeedbackBus.Publish("Hand hygiene", "Hands sanitised."); }
        }
    }
}
