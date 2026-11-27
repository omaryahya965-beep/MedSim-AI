using UnityEngine;

namespace MedSimAI.Characters
{
    /// <summary>Blink for simple rigs: eyelid pivots scale on Y from "open" (small) to 1 (closed). Distressed characters blink more.</summary>
    public class SimpleEyelidBlink : MonoBehaviour
    {
        public Transform[] lids;
        public float openScale = 0.12f;
        public float minInterval = 2.2f, maxInterval = 5.5f, blinkDuration = 0.15f;
        HumanoidLifeController life; float next, phase = -1;

        void Start() { life = GetComponent<HumanoidLifeController>(); next = Time.time + Random.Range(1f, 3f); }

        void Update()
        {
            bool distress = life && life.mood == CharacterMood.Discomfort;
            if (phase < 0 && Time.time >= next) { phase = 0; next = Time.time + Random.Range(distress ? 1.4f : minInterval, maxInterval) + blinkDuration; }
            float close = 0;
            if (phase >= 0) { phase += Time.deltaTime / blinkDuration; close = Mathf.Sin(Mathf.Clamp01(phase) * Mathf.PI); if (phase >= 1) phase = -1; }
            float open = distress ? openScale * 2.2f : openScale; // heavier lids when in pain
            foreach (var l in lids) if (l) { var s = l.localScale; s.y = Mathf.Lerp(open, 1f, close); l.localScale = s; }
        }
    }
}
