using MedSimAI.AI;
using MedSimAI.Instruments;
using MedSimAI.Voice;
using UnityEngine;

namespace MedSimAI.Characters
{
    public class MedicalInstructorController : SimCharacterBase
    {
        public Transform trolleyPoint;
        protected override Persona Persona => Persona.Instructor;

        public void Say(string text, CharacterMood mood = CharacterMood.Neutral, Transform pointAt = null)
        {
            if (life) { life.mood = mood; if (pointAt) life.PointAt(pointAt, 2.8f); }
            if (pointAt) TriggerPoint();
            _ = VoiceInteractionManager.Instance.SpeakAsync(Persona.Instructor, text);
        }

        void OnEnable() { ClinicalFeedbackBus.Finding += OnFinding; }
        void OnDisable() { ClinicalFeedbackBus.Finding -= OnFinding; }
        void OnFinding(string title, string text) { if (life) life.mood = CharacterMood.Attentive; }
    }
}
