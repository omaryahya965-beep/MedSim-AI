using MedSimAI.Clinical;
using MedSimAI.Core;
using TMPro;
using UnityEngine;

namespace MedSimAI.Instruments
{
    /// <summary>
    /// Bedside monitor. Heart rate/rhythm come from PatientState (3-lead ECG is connected on arrival).
    /// SpO2 and NIBP show only after the student measures them. Waveforms are illustrative and labelled SIMULATED.
    /// The generative AI has no access to this component.
    /// </summary>
    public class PatientMonitorController : MonoBehaviour
    {
        public Renderer screen;
        public TMP_Text hrText, bpText, spo2Text, rrText, nibpStatus, footer;
        public int texWidth = 512, texHeight = 256;
        public AudioSource beepSource;
        [Range(0, 1)] public float beepVolume = 0.25f;
        public Color ecgColor = new Color(0.2f, 1f, 0.45f), plethColor = new Color(0.3f, 0.85f, 1f), respColor = new Color(1f, 0.9f, 0.3f), bg = new Color(0.01f, 0.02f, 0.04f);

        Texture2D tex; Color32[] pixels; int cursor; float phase, plethPhase, respPhase, accum;
        bool spo2Shown; int lastEcgY = -1, lastPlY = -1, lastRsY = -1; AudioClip beep; float nextBeepPhase;

        Vitals V => ClinicalScenarioManager.Instance.Session.Patient.Vitals;

        void Start()
        {
            tex = new Texture2D(texWidth, texHeight, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            pixels = new Color32[texWidth * texHeight];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = bg;
            tex.SetPixels32(pixels); tex.Apply();
            if (screen) { screen.material.SetTexture("_BaseMap", tex); screen.material.SetTexture("_EmissionMap", tex); screen.material.EnableKeyword("_EMISSION"); screen.material.SetColor("_EmissionColor", Color.white * 1.3f); }
            beep = Audio.ProceduralClinicalAudio.Beep(880, 0.08f);
            ClinicalScenarioManager.Instance.SessionReset += ResetDisplay;
            ResetDisplay();
        }

        public void ResetDisplay()
        {
            spo2Shown = false;
            if (bpText) bpText.text = "<size=55%>NIBP mmHg</size>\n---/---";
            if (spo2Text) spo2Text.text = "<size=55%>SpO2 %</size>\n--";
            if (nibpStatus) nibpStatus.text = "";
            if (footer) footer.text = "SIMULATED WAVEFORMS · EDUCATIONAL USE ONLY";
        }

        public void RevealBloodPressure(int sys, int dia)
        {
            // Scenario-defined abnormal values are flagged by colour (amber) rather than extra text, to keep the layout readable.
            if (bpText) bpText.text = sys >= 140 ? $"<size=55%>NIBP mmHg</size>\n<color=#FFB74D>{sys}/{dia}</color>" : $"<size=55%>NIBP mmHg</size>\n{sys}/{dia}";
            if (nibpStatus) nibpStatus.text = "Last NIBP " + System.DateTime.Now.ToString("HH:mm");
        }
        public void SetNibpStatus(string s) { if (nibpStatus) nibpStatus.text = s; }
        public void RevealSpO2(int spo2) { spo2Shown = true; if (spo2Text) spo2Text.text = $"<size=55%>SpO2 %</size>\n{spo2}"; }
        public void HideSpO2() { spo2Shown = false; if (spo2Text) spo2Text.text = "<size=55%>SpO2 %</size>\n--"; }

        void Update()
        {
            var v = V;
            if (hrText) hrText.text = $"<size=55%>HR bpm</size>\n{v.heartRate}";
            if (rrText) rrText.text = $"<size=60%>RR </size>{v.respiratoryRate}<size=50%> /min</size>";
            // Sweep at 25 mm/s equivalent: ~128 px per second.
            accum += Time.deltaTime * 128f;
            int steps = (int)accum; accum -= steps;
            float beatHz = v.heartRate / 60f, respHz = v.respiratoryRate / 60f;
            for (int s = 0; s < steps; s++)
            {
                float dt = 1f / 128f;
                phase += dt * beatHz; plethPhase += dt * beatHz; respPhase += dt * respHz;
                if (phase >= 1) phase -= 1; if (plethPhase >= 1) plethPhase -= 1; if (respPhase >= 1) respPhase -= 1;
                if (phase >= 0.08f && nextBeepPhase < 0.08f && spo2Shown && beepSource) beepSource.PlayOneShot(beep, beepVolume);
                nextBeepPhase = phase;
                DrawColumn(v);
            }
            if (steps > 0) { tex.SetPixels32(pixels); tex.Apply(false); }
        }

        void DrawColumn(Vitals v)
        {
            int x = cursor;
            // Clear a gap ahead of the cursor (classic monitor erase bar).
            for (int g = 0; g < 10; g++) { int cx = (x + g) % texWidth; for (int y = 0; y < texHeight; y++) pixels[y * texWidth + cx] = bg; }
            int band = texHeight / 3;
            int ecgY = 2 * band + (int)(ECGTracingRenderer.Lead2(phase, v.rhythm, stElevationMm: 0) * band * 0.38f) + band / 3;
            Line(x, lastEcgY, ecgY, ecgColor); lastEcgY = ecgY;
            if (spo2Shown)
            {
                int ply = band + band / 4 + (int)(Pleth(plethPhase) * band * 0.55f);
                Line(x, lastPlY, ply, plethColor); lastPlY = ply;
            }
            else lastPlY = -1;
            int ry = band / 4 + (int)((0.5f - 0.5f * Mathf.Cos(respPhase * Mathf.PI * 2)) * band * 0.5f);
            Line(x, lastRsY, ry, respColor); lastRsY = ry;
            cursor = (cursor + 1) % texWidth;
            if (cursor == 0) { lastEcgY = lastPlY = lastRsY = -1; }
        }

        void Line(int x, int y0, int y1, Color c)
        {
            if (y0 < 0) y0 = y1;
            int a = Mathf.Clamp(Mathf.Min(y0, y1) - 1, 0, texHeight - 1), b = Mathf.Clamp(Mathf.Max(y0, y1) + 1, 0, texHeight - 1);
            for (int y = a; y <= b; y++) pixels[y * texWidth + x] = c;
        }

        static float Pleth(float p)
        {
            float up = Mathf.Exp(-Mathf.Pow((p - 0.18f) / 0.07f, 2));
            float notch = 0.35f * Mathf.Exp(-Mathf.Pow((p - 0.42f) / 0.08f, 2));
            return Mathf.Clamp01(up + notch * (p > 0.3f ? 1 : 0));
        }
    }

    /// <summary>Deterministic, parameterised ECG drawing from authored scenario keys. Never LLM-generated.</summary>
    public static class ECGTracingRenderer
    {
        // Gaussian-sum PQRST template (normalised millivolts). stElevationMm shifts the ST segment.
        public static float Lead2(float p, string rhythm, float stElevationMm, float amp = 1f, bool invert = false)
        {
            float G(float c, float w, float a) => a * Mathf.Exp(-Mathf.Pow((p - c) / w, 2));
            float v = G(0.12f, 0.025f, 0.12f) + G(0.215f, 0.008f, -0.1f) + G(0.235f, 0.01f, 1.0f) + G(0.255f, 0.01f, -0.22f) + G(0.42f, 0.045f, 0.28f);
            float st = stElevationMm * 0.1f;
            if (p > 0.265f && p < 0.46f) v += st * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.265f, 0.29f, p)) * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.40f, 0.46f, p)));
            v *= amp;
            return invert ? -v : v;
        }

        static readonly string[] Leads = { "I", "aVR", "V1", "V4", "II", "aVL", "V2", "V5", "III", "aVF", "V3", "V6" };

        /// <summary>Renders a 12-lead (4x3) educational tracing for the given key.</summary>
        public static Texture2D Render(string key, int heartRate, int w = 1024, int h = 512)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true);
            var px = new Color32[w * h];
            var paper = new Color32(255, 246, 240, 255); var minor = new Color32(250, 205, 200, 255); var major = new Color32(232, 150, 145, 255); var ink = new Color32(25, 25, 35, 255);
            int mm = 4; // px per mm
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                px[y * w + x] = (x % (mm * 5) == 0 || y % (mm * 5) == 0) ? major : (x % mm == 0 || y % mm == 0) ? minor : paper;
            int cols = 4, rows = 3; int cw = w / cols, rh = h / rows;
            float beatSec = 60f / Mathf.Max(30, heartRate); float pxPerSec = 25 * mm; // 25 mm/s
            for (int i = 0; i < Leads.Length; i++)
            {
                int c = i % cols, r = rows - 1 - i / cols;
                int x0 = c * cw, yBase = r * rh + rh / 2;
                float stMm = 0; float amp = 1f; bool inv = false;
                if (key == "ecg_inferior_ste")
                {
                    switch (Leads[i])
                    {
                        case "II": stMm = 2.5f; break;
                        case "III": stMm = 3f; amp = 0.9f; break;
                        case "aVF": stMm = 2.5f; amp = 0.95f; break;
                        case "I": stMm = -1.2f; amp = 0.7f; break;
                        case "aVL": stMm = -1.8f; amp = 0.55f; break;
                        case "aVR": inv = true; amp = 0.8f; break;
                        case "V1": amp = 0.6f; inv = true; break;
                        case "V2": amp = 0.8f; break;
                    }
                }
                int last = -1;
                for (int x = 4; x < cw - 4; x++)
                {
                    float sec = x / pxPerSec; float p = (sec % beatSec) / beatSec;
                    float mv = Lead2(p, "sinus", stMm, amp, inv);
                    int y = Mathf.Clamp(yBase + (int)(mv * 10 * mm), 1, h - 2);
                    int a = last < 0 ? y : Mathf.Min(last, y), b = last < 0 ? y : Mathf.Max(last, y);
                    for (int yy = a - 1; yy <= b + 1; yy++) if (yy > 0 && yy < h) px[yy * w + x0 + x] = ink;
                    last = y;
                }
                DrawLabel(px, w, h, x0 + 8, r * rh + rh - 22, Leads[i], ink);
            }
            t.SetPixels32(px); t.Apply(true);
            return t;
        }

        // Tiny 3x5 bitmap font for lead labels (A, V, R, L, F, I, digits).
        static readonly System.Collections.Generic.Dictionary<char, string> Font = new System.Collections.Generic.Dictionary<char, string>
        {
            {'I',"111010010010111"},{'V',"101101101101010"},{'A',"010101111101101"},{'R',"110101110101101"},{'L',"100100100100111"},{'F',"111100110100100"},
            {'a',"010101111101101"},{'1',"010110010010111"},{'2',"110001010100111"},{'3',"110001010001110"},{'4',"101101111001001"},{'5',"111100110001110"},{'6',"011100111101111"}
        };
        static void DrawLabel(Color32[] px, int w, int h, int x, int y, string s, Color32 c)
        {
            int scale = 3;
            foreach (var ch in s)
            {
                if (Font.TryGetValue(ch, out var g))
                    for (int gy = 0; gy < 5; gy++) for (int gx = 0; gx < 3; gx++)
                        if (g[gy * 3 + gx] == '1')
                            for (int sy = 0; sy < scale; sy++) for (int sx = 0; sx < scale; sx++)
                            { int X = x + gx * scale + sx, Y = y + (4 - gy) * scale + sy; if (X >= 0 && X < w && Y >= 0 && Y < h) px[Y * w + X] = c; }
                x += 4 * scale;
            }
        }
    }
}
