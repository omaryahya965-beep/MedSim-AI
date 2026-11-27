using System.Collections.Generic;
using UnityEngine;

namespace MedSimAI.Audio
{
    /// <summary>
    /// Synthesised, illustrative clinical sounds (no third-party recordings). Labelled simulated in the UI.
    /// Deterministic: fixed seeds, so the same scenario state always produces the same sound.
    /// </summary>
    public static class ProceduralClinicalAudio
    {
        const int Rate = 22050;
        static readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();

        public static AudioClip HeartSounds(int heartRate)
        {
            string key = "heart" + heartRate;
            if (cache.TryGetValue(key, out var c)) return c;
            float beat = 60f / Mathf.Max(30, heartRate);
            int n = Mathf.RoundToInt(beat * 4 * Rate); var d = new float[n];
            var rng = new System.Random(7);
            for (int i = 0; i < n; i++)
            {
                float t = (i / (float)Rate) % beat;
                float s1 = Thump(t, 0.0f, 0.10f, 52f, 1.0f);
                float s2 = Thump(t, beat * 0.36f, 0.08f, 68f, 0.7f);
                d[i] = (s1 + s2) * 0.9f + (float)(rng.NextDouble() * 2 - 1) * 0.004f;
            }
            return cache[key] = Make(key, d);
        }

        static float Thump(float t, float start, float len, float freq, float amp)
        {
            float x = t - start; if (x < 0 || x > len) return 0;
            float env = Mathf.Sin(Mathf.PI * x / len); env *= env;
            return amp * env * (Mathf.Sin(2 * Mathf.PI * freq * x) * 0.7f + Mathf.Sin(2 * Mathf.PI * freq * 1.9f * x) * 0.3f);
        }

        public static AudioClip BreathSounds(int respRate)
        {
            string key = "breath" + respRate;
            if (cache.TryGetValue(key, out var c)) return c;
            float cycle = 60f / Mathf.Max(6, respRate);
            int n = Mathf.RoundToInt(cycle * 2 * Rate); var d = new float[n];
            var rng = new System.Random(11); float lp = 0, lp2 = 0;
            for (int i = 0; i < n; i++)
            {
                float t = (i / (float)Rate) % cycle; float ph = t / cycle;
                float env = ph < 0.4f ? Mathf.Sin(ph / 0.4f * Mathf.PI) : (ph < 0.85f ? 0.45f * Mathf.Sin((ph - 0.4f) / 0.45f * Mathf.PI) : 0);
                float noise = (float)(rng.NextDouble() * 2 - 1);
                lp += (noise - lp) * 0.08f; lp2 += (lp - lp2) * 0.15f; // soft, low "vesicular" character
                d[i] = lp2 * env * 2.2f;
            }
            return cache[key] = Make(key, d);
        }

        public static AudioClip Beep(float freq, float len)
        {
            string key = "beep" + freq + len;
            if (cache.TryGetValue(key, out var c)) return c;
            int n = Mathf.RoundToInt(len * Rate); var d = new float[n];
            for (int i = 0; i < n; i++) { float t = i / (float)Rate; float env = Mathf.Min(1, t / 0.005f) * Mathf.Min(1, (len - t) / 0.02f); d[i] = Mathf.Sin(2 * Mathf.PI * freq * t) * env * 0.5f; }
            return cache[key] = Make(key, d);
        }

        public static AudioClip Ventilation(float seconds = 8f)
        {
            if (cache.TryGetValue("hvac", out var c)) return c;
            int n = Mathf.RoundToInt(seconds * Rate); var d = new float[n]; var rng = new System.Random(3); float b = 0;
            for (int i = 0; i < n; i++) { b = (b + (float)(rng.NextDouble() * 2 - 1) * 0.02f) * 0.995f; d[i] = b * 1.8f + Mathf.Sin(i * 2 * Mathf.PI * 60f / Rate) * 0.004f; }
            // crossfade loop seam
            int f = Rate / 2; for (int i = 0; i < f; i++) { float a = i / (float)f; d[i] = d[i] * a + d[n - f + i] * (1 - a); }
            System.Array.Resize(ref d, n - f);
            return cache["hvac"] = Make("hvac", d);
        }

        public static AudioClip Click() => Beep(2200, 0.02f);

        public static AudioClip PumpLoop()
        {
            if (cache.TryGetValue("pump", out var c)) return c;
            int n = Rate; var d = new float[n]; var rng = new System.Random(5);
            for (int i = 0; i < n; i++) { float t = i / (float)Rate; d[i] = (Mathf.Sin(2 * Mathf.PI * 120 * t) * 0.15f + (float)(rng.NextDouble() * 2 - 1) * 0.05f) * (0.6f + 0.4f * Mathf.Sin(2 * Mathf.PI * 8 * t)); }
            return cache["pump"] = Make("pump", d);
        }

        static AudioClip Make(string name, float[] d) { var c = AudioClip.Create(name, d.Length, 1, Rate, false); c.SetData(d, 0); return c; }
    }
}
