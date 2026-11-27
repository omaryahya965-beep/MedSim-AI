using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace MedSimAI.Surgery.AI
{
    /// <summary>
    /// Microphone capture for the surgical module (in memory only, never written to disk). Push-to-talk: hold T on
    /// desktop or A/X on a VR controller. Hands-free (toggle with M): simple energy-based voice activity detection.
    /// Emits PCM16 24 kHz mono, the format the proxy's Realtime understanding expects. Speaking while a character
    /// talks raises <see cref="StartedSpeaking"/> so the controller can interrupt (intentional barge-in).
    /// </summary>
    public class SurgeryVoiceInput : MonoBehaviour
    {
        public const int Rate = 24000;
        public enum Mode { PushToTalk, HandsFree, Muted }
        public Mode mode = Mode.PushToTalk;
        [Tooltip("Hands-free: RMS level that counts as speech.")] public float vadThreshold = 0.02f;
        [Tooltip("Hands-free: silence that ends an utterance (s).")] public float vadSilence = 0.8f;
        public float minUtteranceSeconds = 0.35f, maxUtteranceSeconds = 20f;

        public event Action<byte[]> UtteranceReady;
        public event Action StartedSpeaking;
        public event Action<Mode> ModeChanged;
        public bool Capturing { get; private set; }
        public float Level { get; private set; }

        string device; AudioClip clip; int readPos; float[] buf = new float[0];
        readonly System.Collections.Generic.List<float> utter = new System.Collections.Generic.List<float>();
        float silence; bool xrWasDown;

        void OnEnable() { Open(); }
        void OnDisable() { Close(); }

        void Open()
        {
            if (clip || mode == Mode.Muted || Microphone.devices.Length == 0) return;
            device = null;  // system default (same as the clinical module's default)
            clip = Microphone.Start(device, true, 20, Rate); readPos = 0;
        }
        void Close() { if (clip) { Microphone.End(device); clip = null; } Capturing = false; utter.Clear(); }

        public void SetMode(Mode m) { mode = m; if (m == Mode.Muted) Close(); else Open(); ModeChanged?.Invoke(m); }

        bool PttHeld()
        {
            bool key = Keyboard.current != null && Keyboard.current.tKey.isPressed;
            bool xr = false;
            var l = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand); var r = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if (l.isValid && l.TryGetFeatureValue(CommonUsages.primaryButton, out var lb) && lb) xr = true;
            if (r.isValid && r.TryGetFeatureValue(CommonUsages.primaryButton, out var rb) && rb) xr = true;
            return key || xr;
        }

        void Update()
        {
            if (Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame) SetMode(mode == Mode.HandsFree ? Mode.PushToTalk : Mode.HandsFree);
            if (!clip) { if (mode != Mode.Muted) Open(); return; }
            int pos = Microphone.GetPosition(device); if (pos < 0) return;
            int n = pos - readPos; if (n < 0) n += clip.samples;
            if (n <= 0) return;
            if (buf.Length < n) buf = new float[n];
            if (readPos + n <= clip.samples) clip.GetData(buf, readPos);
            else { var a = new float[clip.samples - readPos]; clip.GetData(a, readPos); var b = new float[n - a.Length]; clip.GetData(b, 0); Array.Copy(a, buf, a.Length); Array.Copy(b, 0, buf, a.Length, b.Length); }
            readPos = pos;
            float sum = 0; for (int i = 0; i < n; i++) sum += buf[i] * buf[i];
            Level = Mathf.Sqrt(sum / n);

            bool speakNow;
            if (mode == Mode.PushToTalk)
            {
                bool held = PttHeld();
                if (held && !Capturing) { Capturing = true; utter.Clear(); StartedSpeaking?.Invoke(); }
                if (Capturing) for (int i = 0; i < n; i++) utter.Add(buf[i]);
                if (!held && Capturing) Finish();
                xrWasDown = held;
                return;
            }
            if (mode != Mode.HandsFree) return;
            speakNow = Level > vadThreshold;
            if (speakNow && !Capturing) { Capturing = true; utter.Clear(); silence = 0; StartedSpeaking?.Invoke(); }
            if (!Capturing) return;
            for (int i = 0; i < n; i++) utter.Add(buf[i]);
            silence = speakNow ? 0 : silence + n / (float)Rate;
            if (silence >= vadSilence || utter.Count >= maxUtteranceSeconds * Rate) Finish();
        }

        void Finish()
        {
            Capturing = false;
            if (utter.Count < minUtteranceSeconds * Rate) { utter.Clear(); return; }
            int count = Mathf.Min(utter.Count, (int)(maxUtteranceSeconds * Rate));
            var pcm = new byte[count * 2];
            for (int i = 0; i < count; i++) { short s = (short)Mathf.Clamp(utter[i] * 32767f, -32768f, 32767f); pcm[2 * i] = (byte)(s & 0xff); pcm[2 * i + 1] = (byte)((s >> 8) & 0xff); }
            utter.Clear();
            UtteranceReady?.Invoke(pcm);
        }
    }
}
