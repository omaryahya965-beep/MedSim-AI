using System;
using MedSimAI.Clinical;
using MedSimAI.Core;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace MedSimAI.Instruments
{
    /// <summary>Global bus for clinical feedback the UI shows (findings, measurement status).</summary>
    public static class ClinicalFeedbackBus
    {
        public static event Action<string, string> Finding; // title, text
        public static void Publish(string title, string text) => Finding?.Invoke(title, text);
    }
}
