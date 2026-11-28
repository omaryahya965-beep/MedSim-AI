using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MedSimAI.AI;
using MedSimAI.Characters;
using MedSimAI.Voice;
using MedSimAI.XR;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace MedSimAI.Hospital
{
    /// <summary>
    /// SMALL, PAID live check of the real voice path in the standalone build (real proxy, real Realtime provider):
    ///   MedSimAI.exe -hospitalLiveSmoke "D:\out" "D:\clips"
    /// Student speech = synthetic WAV clips played through the PC speakers while T is held, so the REAL microphone hears
    /// them (an acoustic loop: synthetic voice, real capture, real recognition). The app's own audio OUTPUT is recorded to
    /// app_output.wav as evidence of audible character speech (the microphone is never recorded to disk).
    /// Steps: reception welcome → English question to the doctor → walk into Room 01 (E opens the door) → Arabic request to
    /// the patient ("raise your left arm"). Roughly 4 live calls; the proxy's ledger enforces the budget.
    /// </summary>
    public class HospitalLiveVoiceSmoke : MonoBehaviour
    {
        const string Arg = "-hospitalLiveSmoke";
        string outDir, clipDir; readonly List<string> log = new List<string>(); float t0;
        readonly HashSet<Key> held = new HashSet<Key>(); bool release;
        WavTap tap;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var a = Environment.GetCommandLineArgs(); int i = Array.FindIndex(a, x => string.Equals(x, Arg, StringComparison.OrdinalIgnoreCase));
            if (i < 0 || i + 2 >= a.Length || !HospitalCoordinator.Instance) return;
            var go = new GameObject("[Hospital Live Voice Smoke]"); DontDestroyOnLoad(go);
            var r = go.AddComponent<HospitalLiveVoiceSmoke>(); r.outDir = a[i + 1]; r.clipDir = a[i + 2];
        }

        void L(string k, string m) { var s = $"{Time.realtimeSinceStartup - t0,7:0.0}s [{k}] {m}"; log.Add(s); Debug.Log("[LiveSmoke] " + s); File.WriteAllLines(Path.Combine(outDir, "live_voice_smoke.log"), log); }
        void LateUpdate() { if (Keyboard.current != null && (held.Count > 0 || release)) { InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(held.ToArray())); release = false; } }
        VoiceInteractionManager V => VoiceInteractionManager.Instance;
        DesktopPlayerController Desk => FindAnyObjectByType<DesktopPlayerController>();
        Transform Player => FindAnyObjectByType<MedSimAI.XR.XRPlayerController>().transform;
        IEnumerator Until(Func<bool> c, float timeout) { float t = 0; while (!c() && t < timeout) { t += Time.deltaTime; yield return null; } }

        IEnumerator WalkTo(Vector3 p, float timeout)
        {
            float t = 0; while (t < timeout) { var d = p - Player.position; d.y = 0; if (d.magnitude < 0.3f) break; Desk.LookAt(new Vector3(p.x, Camera.main.transform.position.y, p.z)); held.Add(Key.W); t += Time.deltaTime; yield return null; }
            held.Remove(Key.W); release = true; yield return null;
            L("walk", $"to ({p.x:0.0}, {p.z:0.0}): {(Vector3.Distance(new Vector3(p.x, 0, p.z), new Vector3(Player.position.x, 0, Player.position.z)) < 0.35f ? "arrived" : "stopped at " + Player.position.ToString("F2"))}");
        }

        /// <summary>Holds T and plays the clip through the speakers (external player, so the microphone hears it like a person).</summary>
        IEnumerator Say(string clip)
        {
            var path = Path.Combine(clipDir, clip);
            if (!File.Exists(path)) { L("error", "missing clip " + path); yield break; }
            float captured = -1; Action<float[]> cap = s => captured = s.Length / 24000f; V.UtteranceCaptured += cap;
            string heard = null; Action<string> tr = s => heard = s; V.TranscriptReady += tr;
            held.Add(Key.T); yield return new WaitForSecondsRealtime(0.35f);
            var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powershell", $"-NoProfile -Command \"(New-Object Media.SoundPlayer '{path}').PlaySync()\"") { CreateNoWindow = true, UseShellExecute = false });
            yield return Until(() => p.HasExited, 20f); yield return new WaitForSecondsRealtime(0.4f);
            held.Remove(Key.T); release = true;
            yield return Until(() => heard != null, 15f);
            V.UtteranceCaptured -= cap; V.TranscriptReady -= tr;
            L("talk", $"played {clip} into the microphone · captured {captured:0.00} s · Realtime heard: '{heard ?? "(nothing)"}' · target {V.Target}");
        }

        IEnumerator Hear(CharacterSpeechController who, string label, float timeout = 25f)
        {
            yield return Until(() => who.IsSpeaking, timeout);
            if (!who.IsSpeaking) { L("audio", label + ": NOT speaking"); yield break; }
            float peak = 0, dur = 0; var buf = new float[1024];
            while (who.IsSpeaking && dur < 30f) { AudioListener.GetOutputData(buf, 0); peak = Mathf.Max(peak, Mathf.Sqrt(buf.Sum(x => x * x) / buf.Length)); dur += Time.deltaTime; yield return null; }
            L("audio", $"{label}: {who.displayName} spoke {dur:0.0} s · output RMS peak {peak:0.000} ({(peak > 0.01f ? "AUDIBLE" : "SILENT")}) · voice source={AIProviderManager.Instance.Live.Status}");
        }

        IEnumerator Start()
        {
            t0 = Time.realtimeSinceStartup; Directory.CreateDirectory(outDir);
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            yield return Until(() => DesktopPlayerController.Active && V, 40f);
            var ai = AIProviderManager.Instance;
            L("info", $"LIVE smoke · AI={ai.ModeLabel} · configured={ai.Live.Configured} · LiveOn={V.LiveOn} · mic devices=[{string.Join(", ", Microphone.devices)}]");
            tap = Camera.main.gameObject.AddComponent<WavTap>(); tap.Begin(Path.Combine(outDir, "app_output.wav"));
            CharacterSpeechController.SubtitleStarted += (c, s) => L("subtitle", c.displayName + ": " + s);
            V.Error += e => L("voice-error", e);
            CharacterActionRouter.StateChanged += r => { if (r.state != ActionState.Requested && r.state != ActionState.Validated) L("action", r.ToString()); };

            yield return Hear(V.instructorSpeech, "reception welcome (live)", 40f);
            Desk.LookAt(V.instructorSpeech.transform.position); yield return new WaitForSeconds(0.5f);
            yield return Say("student_en_dr_hello.wav");
            yield return Hear(V.instructorSpeech, "doctor's live reply (English)");

            yield return WalkTo(new Vector3(9.4f, 0, -3.9f), 15f); yield return WalkTo(new Vector3(1.85f, 0, -3.45f), 25f);
            Desk.LookAt(new Vector3(1.6f, 1.1f, -2.56f)); yield return new WaitForSeconds(0.3f);
            held.Add(Key.E); yield return null; yield return null; held.Remove(Key.E); release = true; yield return new WaitForSeconds(1.2f);
            yield return WalkTo(new Vector3(1.85f, 0, -1.9f), 8f); yield return WalkTo(new Vector3(1.3f, 0, -0.6f), 8f);
            yield return Hear(V.instructorSpeech, "Room 01 welcome (live)", 20f);
            yield return Until(() => !V.instructorSpeech.IsSpeaking, 30f);
            Desk.LookAt(V.patientSpeech.transform.position); yield return new WaitForSeconds(1.0f);
            yield return Say("student_ar_act_raise.wav");
            yield return Hear(V.patientSpeech, "patient's live reply (Arabic)");
            yield return new WaitForSeconds(3f);
            ScreenCapture.CaptureScreenshot(Path.Combine(outDir, "live_room01_patient.png")); yield return null; yield return null;
            tap.End();
            L("done", "live smoke finished · app_output.wav " + (new FileInfo(Path.Combine(outDir, "app_output.wav")).Length / 1024) + " KB");
            yield return new WaitForSecondsRealtime(1f);
            Application.Quit();
        }
    }

    /// <summary>Writes what the AudioListener plays (the app's own output) to a 16-bit WAV. Never the microphone.</summary>
    public class WavTap : MonoBehaviour
    {
        FileStream fs; int channels, rate, samples; readonly object gate = new object();
        public void Begin(string path) { rate = AudioSettings.outputSampleRate; lock (gate) { fs = new FileStream(path, FileMode.Create); fs.Write(new byte[44], 0, 44); samples = 0; } }
        void OnAudioFilterRead(float[] data, int ch)
        {
            lock (gate)
            {
                if (fs == null) return; channels = ch;
                var b = new byte[data.Length * 2];
                for (int i = 0; i < data.Length; i++) { short s = (short)(Mathf.Clamp(data[i], -1f, 1f) * 32767); b[2 * i] = (byte)s; b[2 * i + 1] = (byte)(s >> 8); }
                fs.Write(b, 0, b.Length); samples += data.Length;
            }
        }
        public void End()
        {
            lock (gate)
            {
                if (fs == null) return;
                int bytes = samples * 2; fs.Seek(0, SeekOrigin.Begin);
                var w = new BinaryWriter(fs);
                w.Write("RIFF".ToCharArray()); w.Write(36 + bytes); w.Write("WAVE".ToCharArray()); w.Write("fmt ".ToCharArray()); w.Write(16); w.Write((short)1); w.Write((short)channels);
                w.Write(rate); w.Write(rate * channels * 2); w.Write((short)(channels * 2)); w.Write((short)16); w.Write("data".ToCharArray()); w.Write(bytes);
                w.Flush(); fs.Dispose(); fs = null;
            }
        }
        void OnDestroy() => End();
    }
}
