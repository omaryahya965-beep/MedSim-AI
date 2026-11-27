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

        [Header("Body reactions (Animator triggers)")]
        [Tooltip("Minimum seconds between examination reactions.")] public float reactionCooldown = 7f;
        [Tooltip("Seconds between spontaneous pain shifts: x = min, y = max. Shorter while in distress.")] public Vector2 painShiftInterval = new Vector2(45f, 75f);
        float lastReaction = -99f, nextPainShift; int examCount; bool wasThinking; PlacementZone[] zones;
        static readonly int WinceHash = Animator.StringToHash("Wince"), HandToChestHash = Animator.StringToHash("HandToChest"), PainShiftHash = Animator.StringToHash("PainShift"), ThinkHash = Animator.StringToHash("Think"), ConditionHash = Animator.StringToHash("Condition");

        bool CanAnimate => animator && animator.runtimeAnimatorController && animator.isActiveAndEnabled;
        bool Trigger(int hash) { if (!CanAnimate) return false; animator.SetTrigger(hash); lastReaction = Time.time; return true; }
        /// <summary>The right hand must stay put while something is attached to that arm, and must not land on a stethoscope.</summary>
        bool RightHandFree()
        {
            zones ??= GetComponentsInChildren<PlacementZone>(true);
            foreach (var z in zones) if (z && z.Occupied && (z.zoneId.EndsWith("_R") || z.zoneId.StartsWith("CHEST"))) return false;
            return true;
        }

        void OnExam(string title, string text)
        {
            // Brief reaction to being examined (cold diaphragm, cuff squeeze): gaze drops, and a body reaction with a cooldown.
            wince = 1.2f;
            if (life) life.gazeWeight = 0.4f;
            if (Time.time - lastReaction < reactionCooldown) return;
            examCount++;
            // first contact is always the cold-stethoscope flinch; afterwards mostly winces, sometimes the hand goes to the chest
            if (examCount > 1 && examCount % 3 == 0 && RightHandFree()) Trigger(HandToChestHash); else Trigger(WinceHash);
        }

        void ScheduleNextPainShift()
        {
            int cond = CanAnimate ? animator.GetInteger(ConditionHash) : 0;
            float k = cond >= 2 ? 0.4f : cond == 1 ? 0.55f : 1f;
            nextPainShift = Time.time + Random.Range(painShiftInterval.x, painShiftInterval.y) * k;
        }

        void Start() { ScheduleNextPainShift(); nextPainShift = Time.time + Random.Range(18f, 30f); }

        protected override void Update()
        {
            base.Update();
            if (wince > 0) { wince -= Time.deltaTime; if (wince <= 0 && life) life.gazeWeight = 1f; }
            if (!CanAnimate) return;
            // spontaneous discomfort: shifts in the bed (more often in distress), occasionally a hand to the chest
            if (Time.time >= nextPainShift)
            {
                if (Time.time - lastReaction > 4f && !IsSpeaking)
                {
                    bool chest = animator.GetInteger(ConditionHash) >= 1 && Random.value < 0.35f && RightHandFree();
                    Trigger(chest ? HandToChestHash : PainShiftHash);
                }
                ScheduleNextPainShift();
            }
            // thinking behaviour while the AI prepares his answer (breath in, glance down)
            var v = VoiceInteractionManager.Instance;
            bool thinking = v && v.Target == Persona && v.State == VoiceState.Thinking;
            if (thinking && !wasThinking) animator.SetTrigger(ThinkHash);
            wasThinking = thinking;
        }
    }
}
