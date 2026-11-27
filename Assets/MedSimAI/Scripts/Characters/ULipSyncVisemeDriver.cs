using System.Collections.Generic;
using UnityEngine;

namespace MedSimAI.Characters
{
    /// <summary>
    /// Audio-driven lip sync: reads uLipSync's phoneme analysis of this character's AudioSource (runtime speech clips,
    /// e.g. "live_*") and drives the Rocketbox viseme blend shapes through FaceRig. Lives on the same GameObject as the
    /// AudioSource, uLipSync and CharacterSpeechController. While a line has audio, the text-timed viseme path in
    /// CharacterSpeechController is switched off; text-only lines (no clip) keep using it.
    /// The mouth returns to rest by itself: nothing is added when the line ends or is interrupted, and FaceRig eases to zero.
    /// </summary>
    [DefaultExecutionOrder(900)] // after CharacterSpeechController, before FaceRig (1000)
    [RequireComponent(typeof(AudioSource))]
    public class ULipSyncVisemeDriver : MonoBehaviour
    {
        public uLipSync.uLipSync analyser;
        public CharacterSpeechController speech;
        public FaceRig face;
        [Range(0, 1.5f)] public float strength = 0.6f;
        [Tooltip("uLipSync volume below this is treated as silence (lips close).")] [Range(0, 0.5f)] public float silence = 0.06f;
        [Range(0, 1)] public float jawOpen = 0.12f;
        [Tooltip("Calibrated loudness range of this character's runtime voice (log10 of uLipSync raw volume): quietest voiced → loudest.")]
        public float minLogVolume = -2.5f, maxLogVolume = -1.5f;

        /// <summary>uLipSync profile phoneme → Rocketbox/Oculus viseme blend shape.</summary>
        public static readonly Dictionary<string, string> PhonemeToViseme = new Dictionary<string, string>
        {
            { "A", "AA_VI_10_aa" }, { "I", "AA_VI_12_I" }, { "U", "AA_VI_14_U" }, { "E", "AA_VI_11_E" }, { "O", "AA_VI_13_O" },
            { "S", "AA_VI_07_SS" }, { "N", "AA_VI_08_nn" },
        };
        /// <summary>How far each phoneme opens the jaw (0–1).</summary>
        static readonly Dictionary<string, float> JawFor = new Dictionary<string, float> { { "A", 1f }, { "O", 0.7f }, { "E", 0.5f }, { "I", 0.25f }, { "U", 0.3f }, { "S", 0.05f }, { "N", 0.1f } };

        public float LastVolume { get; private set; }
        public string LastPhoneme { get; private set; } = "";
        public bool Driving { get; private set; }

        void Awake()
        {
            if (!analyser) analyser = GetComponent<uLipSync.uLipSync>();
            if (!speech) speech = GetComponent<CharacterSpeechController>();
            if (!face) face = GetComponentInParent<FaceRig>();
            // only claim the mouth if the analyser can actually run
            if (speech) speech.audioDrivenLipSync = analyser && analyser.enabled && analyser.profile && face;
        }

        void OnDisable() { if (speech) speech.audioDrivenLipSync = false; Driving = false; }
        void OnEnable() { if (speech && analyser && analyser.profile && face) speech.audioDrivenLipSync = true; }

        void LateUpdate()
        {
            Driving = speech && speech.AudioLipSyncActive && analyser && face;
            if (!Driving) { LastVolume = 0; return; }
            var r = analyser.result; LastPhoneme = r.phoneme ?? "";
            float nv = r.rawVolume > 0f ? Mathf.Clamp01((Mathf.Log10(r.rawVolume) - minLogVolume) / Mathf.Max(maxLogVolume - minLogVolume, 1e-3f)) : 0f; // per-voice calibration
            LastVolume = nv;
            if (r.phonemeRatios == null || nv < silence) return; // silence inside a line: lips close
            float vol = Mathf.Clamp01((nv - silence) / (1f - silence));
            float gain = strength * Mathf.Clamp01(0.35f + vol);
            float jaw = 0;
            foreach (var kv in r.phonemeRatios)
            {
                if (kv.Value <= 0.02f || !PhonemeToViseme.TryGetValue(kv.Key, out var shape)) continue;
                face.Add(shape, kv.Value * gain);
                if (JawFor.TryGetValue(kv.Key, out var j)) jaw += j * kv.Value;
            }
            face.Add("jawOpen", jaw * vol * jawOpen);
        }
    }
}
