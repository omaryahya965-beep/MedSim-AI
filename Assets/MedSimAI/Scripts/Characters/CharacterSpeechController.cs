using System;
using System.Collections;
using MedSimAI.AI;
using UnityEngine;

namespace MedSimAI.Characters
{
    /// <summary>
    /// Plays a spoken line with spatial audio and drives lip sync:
    /// visemes (FaceRig blend shapes) timed to the text across the clip length, scaled by live audio amplitude.
    /// Falls back to a jaw bone / mouth scale on simple rigs. Raises subtitle events.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class CharacterSpeechController : MonoBehaviour
    {
        public Persona persona;
        public string displayName = "Speaker";
        public FaceRig face;
        [Tooltip("Fallback: jaw bone rotated around local X.")] public Transform jaw;
        public float jawOpenDegrees = 14f;
        [Tooltip("Fallback: mouth mesh scaled vertically.")] public Transform mouthScaleTarget;
        [Range(0, 1)] public float visemeStrength = 0.85f;

        public bool IsSpeaking { get; private set; }
        public float MouthOpen { get; private set; }
        public string CurrentViseme { get; private set; } = "sil_00";
        public static event Action<CharacterSpeechController, string> SubtitleStarted;
        public static event Action<CharacterSpeechController> SubtitleEnded;
        public event Action SpeechFinished;

        AudioSource source; Coroutine routine; Quaternion jawRest; Vector3 mouthRest; readonly float[] buf = new float[256];
        string text = ""; float progress;

        void Awake()
        {
            source = GetComponent<AudioSource>();
            source.spatialBlend = 1f; source.rolloffMode = AudioRolloffMode.Logarithmic; source.minDistance = 0.8f; source.maxDistance = 12f; source.playOnAwake = false;
            source.spatialize = false; source.dopplerLevel = 0;
            if (jaw) jawRest = jaw.localRotation;
            if (mouthScaleTarget) mouthRest = mouthScaleTarget.localScale;
            if (!face) face = GetComponentInParent<FaceRig>();
        }

        public void Speak(string line, AudioClip clip = null) { Stop(); routine = StartCoroutine(SpeakRoutine(line, clip)); }

        public void Stop()
        {
            if (routine != null) StopCoroutine(routine);
            routine = null;
            if (source) source.Stop();
            if (IsSpeaking) { IsSpeaking = false; SubtitleEnded?.Invoke(this); SpeechFinished?.Invoke(); } // interrupted lines also finish (stops mouth + waiters)
            MouthOpen = 0; CurrentViseme = "sil_00";
        }

        IEnumerator SpeakRoutine(string line, AudioClip clip)
        {
            IsSpeaking = true; text = line ?? ""; progress = 0;
            float duration;
            if (clip != null) { source.clip = clip; source.Play(); duration = clip.length; }
            else { source.clip = null; duration = Mathf.Clamp(text.Length * 0.065f, 1.2f, 20f); }
            SubtitleStarted?.Invoke(this, text); // after the clip is assigned, so listeners can tell runtime audio from text-only
            float t = 0;
            while (t < duration)
            {
                t += Time.deltaTime; progress = t / duration;
                float amp;
                if (clip != null && source.isPlaying)
                {
                    source.GetOutputData(buf, 0);
                    float sum = 0; for (int i = 0; i < buf.Length; i++) sum += buf[i] * buf[i];
                    amp = Mathf.Clamp01(Mathf.Sqrt(sum / buf.Length) * 9f);
                }
                else amp = 0.6f + 0.25f * Mathf.Sin(t * 21f);
                // Text position approximates the phoneme currently being voiced (speech runs ~uniformly over the clip).
                int idx = Mathf.Clamp((int)(progress * text.Length), 0, Mathf.Max(0, text.Length - 1));
                char c = text.Length > 0 ? text[idx] : ' ', n = idx + 1 < text.Length ? text[idx + 1] : ' ';
                CurrentViseme = FaceRig.VisemeFor(c, n);
                if (amp < 0.06f) CurrentViseme = "sil_00";
                float open = CurrentViseme == "p_b_m_21" || CurrentViseme == "sil_00" ? 0.05f : amp;
                MouthOpen = Mathf.Lerp(MouthOpen, open, Time.deltaTime * 18f);
                yield return null;
            }
            MouthOpen = 0; CurrentViseme = "sil_00";
            IsSpeaking = false; routine = null;
            SubtitleEnded?.Invoke(this);
            SpeechFinished?.Invoke();
        }

        void LateUpdate()
        {
            if (face)
            {
                if (IsSpeaking)
                {
                    face.Add(CurrentViseme, visemeStrength * Mathf.Clamp01(0.35f + MouthOpen));
                    face.Add("jawOpen", MouthOpen * 0.28f);
                }
                return;
            }
            if (jaw) jaw.localRotation = jawRest * Quaternion.Euler(MouthOpen * jawOpenDegrees, 0, 0);
            if (mouthScaleTarget) mouthScaleTarget.localScale = new Vector3(mouthRest.x, mouthRest.y * (0.25f + MouthOpen * 1.6f), mouthRest.z);
        }
    }
}
