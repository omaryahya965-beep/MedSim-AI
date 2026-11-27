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
        [Tooltip("Set by ULipSyncVisemeDriver: when a line has audio, visemes come from the audio (uLipSync) and the text-timed path is skipped. Text-only lines still use the text-timed path.")]
        public bool audioDrivenLipSync;
        /// <summary>True while a line with real audio is playing and an audio-driven lip-sync component owns the mouth.</summary>
        public bool AudioLipSyncActive => audioDrivenLipSync && IsSpeaking && hasClip;
        bool hasClip;

        public bool IsSpeaking { get; private set; }
        /// <summary>0..1 position in the current line (drives subtitle paging).</summary>
        public float Progress => progress;
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

        /// <summary>Speaks runtime speech that is still streaming in (native voice). Ends when the stream has been played out.</summary>
        public void SpeakStreaming(string line, MedSimAI.Voice.StreamingVoice stream) { Stop(); routine = StartCoroutine(StreamRoutine(line, stream)); }
        /// <summary>Lowers the voice while the student may be talking over it (hands-free), without stopping it.</summary>
        public void Duck(bool on) { if (source) source.volume = on ? 0.25f : 1f; }
        public MedSimAI.Voice.StreamingVoice CurrentStream { get; private set; }

        IEnumerator StreamRoutine(string line, MedSimAI.Voice.StreamingVoice stream)
        {
            CurrentStream = stream;
            IsSpeaking = true; text = line ?? ""; progress = 0; hasClip = true;
            source.clip = stream.Clip; source.loop = false; source.volume = 1f; source.Play();
            source.timeSamples = stream.StartClipPosition; // skip the silence Unity pre-read at clip creation (0.8 s)
            SubtitleStarted?.Invoke(this, text);
            float t = 0, est = Mathf.Max(1f, text.Length * 0.06f), stuck = 0; int lastPos = -1;
            // Finished only when the PLAYHEAD has passed the last real sample (Unity reads streamed clips ahead of playback,
            // so "all data handed to the callback" is not "all audio heard" — stopping then clipped the last words).
            while (!stream.PlayedOut(source.timeSamples) && (source.isPlaying || !stream.Completed))
            {
                int pos = source.timeSamples; stuck = pos == lastPos && stream.Completed ? stuck + Time.unscaledDeltaTime : 0; lastPos = pos;
                if (stuck > 1.5f) break; // playback stalled after the stream ended (device change etc.)
                t += Time.deltaTime; progress = Mathf.Clamp01(t / Mathf.Max(est, stream.ReceivedSeconds));
                source.GetOutputData(buf, 0); float sum = 0; for (int i = 0; i < buf.Length; i++) sum += buf[i] * buf[i];
                float amp = Mathf.Clamp01(Mathf.Sqrt(sum / buf.Length) * 9f);
                int idx = Mathf.Clamp((int)(progress * text.Length), 0, Mathf.Max(0, text.Length - 1));
                CurrentViseme = amp < 0.06f ? "sil_00" : FaceRig.VisemeFor(text.Length > 0 ? text[idx] : ' ', idx + 1 < text.Length ? text[idx + 1] : ' ');
                MouthOpen = Mathf.Lerp(MouthOpen, CurrentViseme == "sil_00" ? 0.05f : amp, Time.deltaTime * 18f);
                if (t > 300f) break; // safety
                yield return null;
            }
            yield return new WaitForSecondsRealtime(0.04f); // output-device latency margin
            source.Stop(); CurrentStream = null;
            MouthOpen = 0; CurrentViseme = "sil_00"; IsSpeaking = false; hasClip = false; routine = null;
            SubtitleEnded?.Invoke(this);
            SpeechFinished?.Invoke();
        }

        public void Stop()
        {
            if (routine != null) StopCoroutine(routine);
            routine = null;
            if (CurrentStream != null) { CurrentStream.Cancel(); CurrentStream = null; }
            if (source) { source.Stop(); source.volume = 1f; }
            if (IsSpeaking) { IsSpeaking = false; SubtitleEnded?.Invoke(this); SpeechFinished?.Invoke(); } // interrupted lines also finish (stops mouth + waiters)
            MouthOpen = 0; CurrentViseme = "sil_00"; hasClip = false;
        }

        IEnumerator SpeakRoutine(string line, AudioClip clip)
        {
            IsSpeaking = true; text = line ?? ""; progress = 0; hasClip = clip != null;
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
            IsSpeaking = false; routine = null; hasClip = false;
            SubtitleEnded?.Invoke(this);
            SpeechFinished?.Invoke();
        }

        void LateUpdate()
        {
            if (face)
            {
                if (IsSpeaking && !AudioLipSyncActive)
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
