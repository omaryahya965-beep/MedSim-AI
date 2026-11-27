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

        /// <summary>Applies the authored condition (expression, breathing) — the AI never changes it.</summary>
        public void ApplyCondition(MedSimAI.Clinical.PatientStateDef st)
        {
            if (st == null || !life) return;
            switch (st.expression)
            {
                case "severe_distress": life.mood = CharacterMood.Discomfort; life.breathsPerMinute = 24; life.painIntensity = 1.35f; break;
                case "distress": life.mood = CharacterMood.Discomfort; life.breathsPerMinute = 22; life.painIntensity = 1.15f; break;
                case "anxious_reassured": life.mood = CharacterMood.Discomfort; life.painIntensity = 0.85f; break;
                default: life.mood = CharacterMood.Discomfort; life.breathsPerMinute = 20; life.painIntensity = 1f; break;
            }
            var anim = animator;
            if (anim && anim.runtimeAnimatorController) anim.SetInteger("Condition", st.expression == "severe_distress" ? 2 : st.expression == "distress" ? 1 : 0);
        }

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
