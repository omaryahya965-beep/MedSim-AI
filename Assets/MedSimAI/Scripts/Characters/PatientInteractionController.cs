using MedSimAI.AI;
using MedSimAI.Instruments;
using MedSimAI.Voice;
using UnityEngine;

namespace MedSimAI.Characters
{
    public class PatientInteractionController : SimCharacterBase
    {
        protected override Persona Persona => Persona.Patient;
        [Tooltip("Hand pressed to chest while in pain (optional).")] public Transform painHand;
        float wince;

        protected override void Awake() { base.Awake(); if (life) { life.mood = CharacterMood.Discomfort; life.lyingDown = true; } }

        void OnEnable() { ClinicalFeedbackBus.Finding += OnExam; }
        void OnDisable() { ClinicalFeedbackBus.Finding -= OnExam; }

        void OnExam(string title, string text)
        {
            // Brief reaction to being examined (cold diaphragm, cuff squeeze).
            wince = 1.2f;
            if (life) life.gazeWeight = 0.4f;
        }

        protected override void Update()
        {
            base.Update();
            if (wince > 0) { wince -= Time.deltaTime; if (wince <= 0 && life) life.gazeWeight = 1f; }
        }
    }
}
