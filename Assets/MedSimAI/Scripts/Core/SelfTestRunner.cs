using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MedSimAI.AI;
using MedSimAI.Characters;
using MedSimAI.Clinical;
using MedSimAI.Instruments;
using MedSimAI.Voice;
using MedSimAI.XR;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MedSimAI.Core
{
    /// <summary>
    /// Built-player self-test, ONLY active when launched with  -medsimSelfTest "&lt;folder with student WAV clips&gt;".
    /// Verifies the standalone exe (not the Editor): OS keyboard/mouse input reaching the game, frame timing, native-resolution
    /// screenshots, a native voice turn, and an ACOUSTIC LOOPBACK of the real microphone path.
    /// AUTOMATED AUDIO, NOT A HUMAN: the loopback plays a synthetic student clip through the PC speakers; the real microphone,
    /// hands-free VAD and Realtime path must pick it up. Results: %USERPROFILE%/AppData/LocalLow/…/selftest/selftest.log.
    /// </summary>
    public class SelfTestRunner : MonoBehaviour
    {
        string wavDir, outDir; readonly List<string> log = new List<string>(); float t0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.FindIndex(args, a => string.Equals(a, "-medsimSelfTest", StringComparison.OrdinalIgnoreCase));
            if (i < 0) return;
            var go = new GameObject("[MedSim Self-Test]"); DontDestroyOnLoad(go);
            var r = go.AddComponent<SelfTestRunner>(); r.wavDir = i + 1 < args.Length ? args[i + 1] : "";
        }

        void L(string k, string m) { var s = $"{Time.realtimeSinceStartup - t0,7:0.0}s [{k}] {m}"; log.Add(s); Debug.Log("[SelfTest] " + s); File.WriteAllLines(Path.Combine(outDir, "selftest.log"), log); }

        IEnumerator Start()
        {
            t0 = Time.realtimeSinceStartup;
            outDir = Path.Combine(Application.persistentDataPath, "selftest"); Directory.CreateDirectory(outDir);
            L("info", $"exe self-test · {Application.version} · {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsMemorySize} MB) · {Screen.width}x{Screen.height} @ {Screen.currentResolution.refreshRateRatio.value:0} Hz · fullscreen={Screen.fullScreenMode}");
            float t = 0; while (!DesktopPlayerController.Active && t < 30) { t += Time.unscaledDeltaTime; yield return null; }
            var V = VoiceInteractionManager.Instance; var AI = AIProviderManager.Instance; var S = SessionManager.Instance;
            L("mode", $"display={DisplayModeManager.Mode} ({DisplayModeManager.Instance?.Detail}) desktop={DesktopPlayerController.Active} AI={AI.ModeLabel} live={V.LiveOn} xrLoaderInBuild={(UnityEngine.XR.Management.XRGeneralSettings.Instance?.Manager?.activeLoaders?.Count ?? 0)} mic devices=[{string.Join(", ", Microphone.devices)}]");
            var subs = new List<string>(); CharacterSpeechController.SubtitleStarted += (c, s) => { subs.Add(c.displayName + ": " + s); L("subtitle", c.displayName + ": " + s); };
            string heard = null; V.TranscriptReady += h => { heard = h; L("transcript", "You: " + h); };
            int captures = 0; V.UtteranceCaptured += s => { captures++; L("mic", $"VAD captured {s.Length / 24000f:0.00}s from the real microphone"); };
            V.Error += e => L("error", e);

            // ---- 1. OS-level input probe (an external script sends real key/mouse events to this window)
            var rig = FindAnyObjectByType<XRPlayerController>(); var p0 = rig.transform.position; float yaw0 = Camera.main.transform.eulerAngles.y;
            var seen = new HashSet<string>(); float until = Time.realtimeSinceStartup + 30f; bool tHeld = false; bool paused = false;
            while (Time.realtimeSinceStartup < until)
            {
                var kb = Keyboard.current; var ms = Mouse.current;
                if (kb != null) foreach (var k in new[] { kb.wKey, kb.aKey, kb.sKey, kb.dKey, kb.eKey, kb.tKey, kb.mKey, kb.escapeKey, kb.leftShiftKey }) if (k.wasPressedThisFrame && seen.Add(k.name)) L("input", "key " + k.name + " received");
                if (ms != null && ms.delta.ReadValue().sqrMagnitude > 4 && seen.Add("mouseMove")) L("input", "mouse movement received");
                if (ms != null && ms.leftButton.wasPressedThisFrame && seen.Add("lmb")) L("input", "left click received");
                if (kb != null && kb.tKey.isPressed && V.State == VoiceState.Listening && !tHeld) { tHeld = true; L("input", "T held → voice state Listening, microphone " + (V.MicDevice ?? "system default") + " open"); }
                if (S.State == AppState.Paused && !paused) { paused = true; L("input", "Esc → paused (pause menu shown)"); }
                yield return null;
            }
            if (S.State == AppState.Paused) S.Pause(false);
            L("input", $"moved {(Vector3.Distance(rig.transform.position, p0)):0.00} m, turned {Mathf.DeltaAngle(yaw0, Camera.main.transform.eulerAngles.y):0} deg; keys seen: {string.Join(",", seen)}");

            // ---- 2. corridor frame timing + screenshot (native resolution)
            if (S.State == AppState.MainMenu) S.StartClinicalSession(true);
            yield return Perf("corridor", 10f);
            ScreenCapture.CaptureScreenshot(Path.Combine(outDir, "exe_corridor.png")); yield return null; yield return null;
            // ---- 3. into the room
            var door = FindAnyObjectByType<HospitalDoor>(); if (door) door.OpenBy(85);
            yield return new WaitForSecondsRealtime(1.5f);
            var cc = rig.GetComponent<CharacterController>(); if (cc) cc.enabled = false;
            rig.transform.position = new Vector3(1.15f, 0, 0.55f); rig.transform.rotation = Quaternion.Euler(0, -90, 0); if (cc) cc.enabled = true;
            FindAnyObjectByType<DesktopPlayerController>()?.LookAt(new Vector3(0.3f, 1.1f, 1.3f));
            t = 0; while (V.Busy && t < 40) { t += Time.unscaledDeltaTime; yield return null; }
            yield return Perf("room", 10f);
            ScreenCapture.CaptureScreenshot(Path.Combine(outDir, "exe_room.png")); yield return null; yield return null;

            // ---- 4. native voice turn (synthetic clip injected after the microphone stage)
            var allergy = Path.Combine(wavDir, "student_en_allergy.wav");
            if (V.LiveOn && File.Exists(allergy))
            {
                heard = null; int before = subs.Count; L("voice", "AUTOMATED: synthetic clip student_en_allergy.wav → patient (injected)");
                V.SubmitVoice(Persona.Patient, Wav(allergy));
                t = 0; while (subs.Count == before && t < 30) { t += Time.unscaledDeltaTime; yield return null; }
                t = 0; while (V.LastFirstAudioMs < 0 && t < 10) { t += Time.unscaledDeltaTime; yield return null; }
                L("voice", $"heard=\"{heard}\" understood={V.LastUnderstoodMs:0}ms firstAudio={V.LastFirstAudioMs:0}ms");
                t = 0; while (V.Busy && t < 40) { t += Time.unscaledDeltaTime; yield return null; }
            }
            // ---- 5. acoustic loopback: speakers → REAL microphone → hands-free VAD → Realtime → reply
            var hello = Path.Combine(wavDir, "student_en_dr_hello.wav");
            if (V.LiveOn && File.Exists(hello) && Microphone.devices.Length > 0)
            {
                var prev = V.Mic; V.SetMicMode(MicMode.HandsFree); yield return new WaitForSecondsRealtime(2.5f);
                V.SetTarget(Persona.Instructor); V.autoTargetByGaze = false;
                heard = null; captures = 0; int before = subs.Count;
                var go = new GameObject("loopback speaker"); var src = go.AddComponent<AudioSource>(); src.spatialBlend = 0; src.volume = 1f;
                var w = Wav(hello); var clip = AudioClip.Create("loopback", w.Length, 1, 24000, false); clip.SetData(w, 0); src.clip = clip;
                L("loopback", $"AUTOMATED AUDIO (not a human): playing synthetic clip \"Hello Doctor Hassan, nice to meet you.\" through the PC speakers; mic={(V.MicDevice ?? "system default")} noise level={V.MicLevel:0.0000}");
                src.Play(); float tPlayEnd = Time.realtimeSinceStartup + clip.length;
                t = 0; while (subs.Count == before && t < 25) { t += Time.unscaledDeltaTime; yield return null; }
                L("loopback", subs.Count > before
                    ? $"captured={captures} heard=\"{heard}\" reply started {Time.realtimeSinceStartup - tPlayEnd:0.00}s after the clip ended; understood={V.LastUnderstoodMs:0}ms firstAudio={V.LastFirstAudioMs:0}ms (after VAD end-of-speech)"
                    : $"NO reply: captures={captures} heard=\"{heard}\" — speakers/mic volume may be too low for an acoustic loopback (use the manual microphone checklist)");
                t = 0; while (V.Busy && t < 40) { t += Time.unscaledDeltaTime; yield return null; }
                // Arabic through the real microphone: transcript must be Arabic (RTL), reply Arabic, conversation switches to Arabic
                var arClip = Path.Combine(wavDir, "student_ar_where.wav");
                if (File.Exists(arClip))
                {
                    V.SetTarget(Persona.Patient); heard = null; captures = 0; before = subs.Count;
                    var wa = Wav(arClip); var ca = AudioClip.Create("loopback_ar", wa.Length, 1, 24000, false); ca.SetData(wa, 0); src.clip = ca;
                    L("loopback-ar", "AUTOMATED AUDIO (not a human): synthetic Palestinian Arabic clip «طيب وين بالزبط الوجع؟ بيمتد لمحل تاني؟» through the PC speakers → patient");
                    src.Play(); float endAr = Time.realtimeSinceStartup + ca.length;
                    t = 0; while (subs.Count == before && t < 25) { t += Time.unscaledDeltaTime; yield return null; }
                    yield return new WaitForSecondsRealtime(1.5f);
                    ScreenCapture.CaptureScreenshot(Path.Combine(outDir, "exe_arabic_transcript.png")); yield return null; yield return null;
                    var reply = subs.Count > before ? subs[subs.Count - 1] : "(none)";
                    L("loopback-ar", $"captured={captures} heard=\"{heard}\" heardArabic={MedSimAI.UI.ArabicShaper.ContainsArabic(heard)} reply={reply} replyArabic={MedSimAI.UI.ArabicShaper.ContainsArabic(reply)} conversation={AI.ConversationLanguage} reply started {Time.realtimeSinceStartup - endAr:0.00}s after the clip ended");
                    t = 0; while (V.Busy && t < 40) { t += Time.unscaledDeltaTime; yield return null; }
                    AI.SetLanguageMode(LanguageMode.Auto); AI.SetLanguage("en");
                }
                V.SetMicMode(prev); V.autoTargetByGaze = true; Destroy(go);
            }
            // ---- 6. embodied actions in the built player (validated actions; the AI part is covered by the voice step below)
            if (S.State == AppState.Paused) S.Pause(false);
            V.Interrupt(); yield return new WaitForSecondsRealtime(1f);
            var P = CharacterActionRouter.Patient; var D = CharacterActionRouter.Doctor;
            var zones = FindObjectsByType<PlacementZone>(FindObjectsSortMode.None); var cuff = FindAnyObjectByType<BloodPressureController>();
            void Do(Persona w, string type, string side = "none") => CharacterActionRouter.Execute(w, new LiveAction { type = type, side = side, status = "accepted" });
            var ft = new List<float>(); float animMs = 0; int animN = 0;
            IEnumerator Run(string label, Persona w, string type, string side, float secs)
            {
                Do(w, type, side); float end = Time.realtimeSinceStartup + secs;
                while (Time.realtimeSinceStartup < end) { ft.Add(Time.unscaledDeltaTime * 1000f); animMs += PatientActionController.AvgMs; animN++; yield return null; }
                var rec = CharacterActionRouter.History.LastOrDefault(); L("action", $"{label}: {rec}");
                ScreenCapture.CaptureScreenshot(Path.Combine(outDir, "exe_action_" + label + ".png")); yield return null; yield return null;
            }
            if (cuff) cuff.PlaceInZone(zones.First(z => z.zoneId == "ARM_UPPER_L"));
            yield return Run("sit_up", Persona.Patient, "sit_up", "none", 3f);
            yield return Run("raise_left_arm", Persona.Patient, "raise_arm", "left", 2.5f);
            var zl = zones.First(z => z.zoneId == "ARM_UPPER_L"); if (cuff) L("action", $"cuff on the raised arm: {Vector3.Distance(cuff.transform.position, (zl.snapPoint ? zl.snapPoint : zl.transform).position):0.000} m from its snap point");
            yield return Run("extend_right_arm", Persona.Patient, "extend_arm", "right", 2.5f);
            yield return Run("deep_breath", Persona.Patient, "deep_breath", "none", 5f);
            yield return Run("doctor_come_to_student", Persona.Instructor, "come_to_student", "none", 6f);
            yield return Run("doctor_step_back", Persona.Instructor, "step_back", "none", 5f);
            Do(Persona.Patient, "rest"); if (cuff) cuff.ResetInstrument();
            ft.Sort(); L("perf", $"during character actions: {ft.Count} frames, avg {ft.Average():0.00} ms ({1000f / ft.Average():0} fps), p95 {ft[(int)(ft.Count * 0.95f)]:0.00} ms, p99 {ft[(int)(ft.Count * 0.99f)]:0.00} ms; patient action layer {(animN > 0 ? animMs / animN : 0):0.000} ms/frame");
            // ---- 7. one REAL spoken physical instruction (AUTOMATED synthetic clip through the Realtime path)
            var act = Path.Combine(wavDir, "student_ar_act_raise.wav");
            if (V.LiveOn && File.Exists(act))
            {
                int before = CharacterActionRouter.History.Count; heard = null; yield return new WaitForSecondsRealtime(2f);
                L("voice-action", "AUTOMATED: synthetic clip «ارفع إيدك الشمال لو سمحت» → patient");
                V.SubmitVoice(Persona.Patient, Wav(act)); float t1 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t1 < 20 && !CharacterActionRouter.History.Skip(before).Any(h => h.state == ActionState.Completed)) yield return null;
                yield return new WaitForSecondsRealtime(0.8f);
                ScreenCapture.CaptureScreenshot(Path.Combine(outDir, "exe_voice_action.png")); yield return null; yield return null;
                L("voice-action", $"heard=«{heard}» actions: {string.Join(", ", CharacterActionRouter.History.Skip(before).Select(h => h.ToString()))} after {Time.realtimeSinceStartup - t1:0.0}s");
                t = 0; while (V.Busy && t < 30) { t += Time.unscaledDeltaTime; yield return null; }
            }
            L("done", "self-test finished");
            yield return new WaitForSecondsRealtime(1f);
            Application.Quit();
        }

        IEnumerator Perf(string where, float seconds)
        {
            var ft = new List<float>(); float end = Time.realtimeSinceStartup + seconds; var pm = PerformanceManager.Instance;
            var gpu = new List<float>();
            while (Time.realtimeSinceStartup < end) { ft.Add(Time.unscaledDeltaTime * 1000f); if (pm && pm.GpuFrameMs > 0) gpu.Add(pm.GpuFrameMs); yield return null; }
            ft.Sort(); float P(List<float> l, float q) => l.Count == 0 ? 0 : l[Mathf.Clamp((int)(q * l.Count), 0, l.Count - 1)];
            gpu.Sort();
            L("perf", $"{where}: {ft.Count} frames, avg {ft.Average():0.00} ms ({1000f / ft.Average():0} fps), p50 {P(ft, 0.5f):0.00} ms, p95 {P(ft, 0.95f):0.00} ms, p99 {P(ft, 0.99f):0.00} ms, max {ft.Max():0.0} ms; GPU p50 {P(gpu, 0.5f):0.00} ms; vSync={QualitySettings.vSyncCount} target={Application.targetFrameRate}");
        }

        static float[] Wav(string path) { var b = File.ReadAllBytes(path); int n = (b.Length - 44) / 2; var f = new float[n]; for (int i = 0; i < n; i++) f[i] = (short)(b[44 + 2 * i] | (b[45 + 2 * i] << 8)) / 32768f; return f; }
    }
}
