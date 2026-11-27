using MedSimAI.AI;
using MedSimAI.Instruments;
using MedSimAI.Voice;
using UnityEngine;

namespace MedSimAI.Characters
{
    /// <summary>Shared character wiring: speech + life layer + optional Animator parameters (Speaking, Listening, Pointing, Mood).</summary>
    public abstract class SimCharacterBase : MonoBehaviour
    {
        /// <summary>Head-level point for others to look or point at (humanoid head bone if available).</summary>
        public Transform Focus { get { var a = GetComponent<Animator>(); var h = a && a.isHuman ? a.GetBoneTransform(HumanBodyBones.Head) : null; return h ? h : transform; } }
        public CharacterSpeechController speech;
        public HumanoidLifeController life;
        public Animator animator;
        public bool IsSpeaking => speech && speech.IsSpeaking;
        static readonly int SpeakingHash = Animator.StringToHash("Speaking"), ListeningHash = Animator.StringToHash("Listening"), MoodHash = Animator.StringToHash("Mood"), PointHash = Animator.StringToHash("Point");

        protected virtual void Awake()
        {
            if (!speech) speech = GetComponentInChildren<CharacterSpeechController>();
            if (!life) life = GetComponentInChildren<HumanoidLifeController>();
            if (!animator) animator = GetComponentInChildren<Animator>();
        }

        protected abstract Persona Persona { get; }

        protected virtual void Update()
        {
            if (!animator || !animator.runtimeAnimatorController) return;
            var v = VoiceInteractionManager.Instance;
            animator.SetBool(SpeakingHash, IsSpeaking);
            animator.SetBool(ListeningHash, v && v.Target == Persona && (v.State == VoiceState.Listening || v.State == VoiceState.Thinking));
            animator.SetInteger(MoodHash, life ? (int)life.mood : 0);
        }

        protected void TriggerPoint() { if (animator && animator.runtimeAnimatorController) animator.SetTrigger(PointHash); }
    }
}
