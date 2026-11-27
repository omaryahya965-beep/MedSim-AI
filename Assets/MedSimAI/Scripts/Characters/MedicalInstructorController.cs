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

        /// <summary>Non-verbal cue: mood + optional point/look toward the relevant object (equipment, patient, door).</summary>
        public void Gesture(CharacterMood mood, Transform target)
        {
            if (life) { life.mood = mood; if (target) life.PointAt(target, 2.4f); }
            if (target) TriggerPoint();
        }

        public void PointAtDoor(Transform handle) { if (life && handle) { life.PointAt(handle, 2.6f); } TriggerPoint(); }

        void OnEnable() { ClinicalFeedbackBus.Finding += OnFinding; }
        void OnDisable() { ClinicalFeedbackBus.Finding -= OnFinding; }
        void OnFinding(string title, string text) { if (life) life.mood = CharacterMood.Attentive; }
    }
}
