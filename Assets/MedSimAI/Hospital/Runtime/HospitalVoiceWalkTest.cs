using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MedSimAI.AI;
using MedSimAI.Characters;
using MedSimAI.Core;
using MedSimAI.Surgery.Shared;
using MedSimAI.Surgery.Shared.Unity;
using MedSimAI.Voice;
using MedSimAI.XR;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Networking;

namespace MedSimAI.Hospital
{
    /// <summary>
    /// Standalone-executable voice and walkthrough test (Desktop build):
    ///   MedSimAI.exe -hospitalVoiceTest "D:\out"
    /// Needs the MedSim proxy on 127.0.0.1:8787. By default it REFUSES to run unless the fake-provider proxy
    /// (Backend/medsim-proxy/tools/fake-provider-server.mjs, control API on :8788) is running, so it can never spend money by
    /// accident; add -allowLive to run against the real proxy.
    ///
    /// The student WALKS (simulated W key through the Input System → the desktop player's own movement and collisions),
    /// opens the Room 01 door with E, and TALKS with T (simulated key → the real push-to-talk binding → the real microphone,
    /// recorded in memory). With the fake provider the transcript comes from a queue (speech recognition is the one part a
    /// fake cannot do); everything after it is the production path: proxy routes, grounding, streamed speech, AudioSource,
    /// lip sync, hospital routing, surgical voice, hand-over. AUTOMATED INPUT, NOT A HUMAN VOICE.
    /// </summary>
    public class HospitalVoiceWalkTest : MonoBehaviour
    {
        const string Arg = "-hospitalVoiceTest", Control = "http://127.0.0.1:8788";
        string outDir; readonly List<string> log = new List<string>(); float t0; bool fake;
        readonly HashSet<Key> held = new HashSet<Key>();
        bool recording; int frameNo;

        static int ArgIndex() => Array.FindIndex(Environment.GetCommandLineArgs(), a => string.Equals(a, Arg, StringComparison.OrdinalIgnoreCase));

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            int i = ArgIndex(); if (i < 0 || !HospitalCoordinator.Instance) return;
            var args = Environment.GetCommandLineArgs();
            var go = new GameObject("[Hospital Voice Walk Test]"); DontDestroyOnLoad(go);
            var r = go.AddComponent<HospitalVoiceWalkTest>();
            r.outDir = i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[i + 1] : Path.Combine(Application.persistentDataPath, "hospital_voice_test");
        }

        void L(string k, string m) { var s = $"{Time.realtimeSinceStartup - t0,7:0.0}s [{k}] {m}"; log.Add(s); Debug.Log("[HospitalVoiceTest] " + s); File.WriteAllLines(Path.Combine(outDir, "hospital_voice_test.log"), log); }

        HospitalCoordinator HC => HospitalCoordinator.Instance;
        DesktopPlayerController Desk => FindAnyObjectByType<DesktopPlayerController>();
        Transform Player => FindAnyObjectByType<XRPlayerController>().transform;
        Transform Head => Camera.main.transform;
        VoiceInteractionManager Voice => VoiceInteractionManager.Instance;
        CharacterSpeechController DoctorSpeech => Voice.instructorSpeech;
        CharacterSpeechController PatientSpeech => Voice.patientSpeech;

        // ------------------------------------------------------------------ simulated keyboard (same path as a real key)
        void LateUpdate() { if (Keyboard.current != null && (held.Count > 0 || releaseFrame)) { InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(held.ToArray())); releaseFrame = false; } }
        bool releaseFrame;
        void Hold(Key k) { held.Add(k); }
        void Release(Key k) { held.Remove(k); releaseFrame = true; }
        IEnumerator Tap(Key k) { Hold(k); yield return null; yield return null; Release(k); yield return null; }

        // ------------------------------------------------------------------ walking (W + looking where to go; collisions are real)
        IEnumerator WalkTo(Vector3 p, float timeout = 25f, string what = null)
        {
            float t = 0, lastCheck = 0; Vector3 lastPos = Player.position; bool blocked = false;
            while (t < timeout)
            {
                var d = p - Player.position; d.y = 0;
                if (d.magnitude < 0.3f) break;
                Desk.LookAt(new Vector3(p.x, Head.position.y, p.z));
                Hold(Key.W);
                t += Time.deltaTime;
                if (t - lastCheck > 1.5f) { if ((Player.position - lastPos).magnitude < 0.08f) { blocked = true; break; } lastPos = Player.position; lastCheck = t; }
                yield return null;
            }
            Release(Key.W); yield return null;
            var left = p - Player.position; left.y = 0;
            L("walk", $"{what ?? "to"} ({p.x:0.00}, {p.z:0.00}): {(left.magnitude < 0.3f ? "arrived" : blocked ? "BLOCKED at " + Player.position.ToString("F2") : "timeout at " + Player.position.ToString("F2"))} in {t:0.0} s");
        }

        // ------------------------------------------------------------------ walkthrough recording (JPG frames, 960x540)
        IEnumerator Recorder()
        {
            var dir = Path.Combine(outDir, "walkthrough"); Directory.CreateDirectory(dir);
            var rt = new RenderTexture(960, 540, 0); var tex = new Texture2D(960, 540, TextureFormat.RGB24, false);
            while (true)
            {
                yield return new WaitForSecondsRealtime(0.25f);
                if (!recording) continue;
                yield return new WaitForEndOfFrame();
                var shot = ScreenCapture.CaptureScreenshotAsTexture();
                Graphics.Blit(shot, rt); Destroy(shot);
                var prev = RenderTexture.active; RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); tex.Apply(); RenderTexture.active = prev;
                File.WriteAllBytes(Path.Combine(dir, $"f{frameNo++:0000}.jpg"), tex.EncodeToJPG(78));
            }
        }

        IEnumerator Shot(string name) { yield return null; ScreenCapture.CaptureScreenshot(Path.Combine(outDir, name + ".png")); yield return null; yield return null; L("shot", name); }

        // ------------------------------------------------------------------ fake provider control
        IEnumerator Queue(string heard, string lang, string fieldsJson = null)
        {
            if (!fake) yield break;
            var body = "{\"heard\":\"" + heard.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\",\"lang\":\"" + lang + "\"" + (fieldsJson != null ? ",\"fields\":" + fieldsJson : "") + "}";
            using var www = new UnityWebRequest(Control + "/heard", "POST") { uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)), downloadHandler = new DownloadHandlerBuffer() };
            yield return www.SendWebRequest();
        }

        // ------------------------------------------------------------------ audibility and lip sync while a character speaks
        readonly float[] outL = new float[1024], outR = new float[1024];
        IEnumerator Probe(CharacterSpeechController who, string label, float startTimeout = 20f)
        {
            float t = 0; while (who && !who.IsSpeaking && t < startTimeout) { t += Time.deltaTime; yield return null; }
            if (!who || !who.IsSpeaking) { L("audio", $"{label}: {(who ? who.displayName : "?")} did NOT start speaking within {startTimeout:0} s"); yield break; }
            var root = who.GetComponentInParent<Animator>() ? who.GetComponentInParent<Animator>().transform : who.transform.root;
            var faces = root.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r => r.sharedMesh && r.sharedMesh.blendShapeCount > 0).ToArray();
            var src = who.GetComponentsInChildren<AudioSource>().Concat(who.GetComponents<AudioSource>()).FirstOrDefault();
            float maxRms = 0, sumRms = 0, maxShape = 0, dur = 0; int n = 0; bool clip = false;
            while (who.IsSpeaking && dur < 20f)
            {
                AudioListener.GetOutputData(outL, 0); AudioListener.GetOutputData(outR, 1);
                float s = 0; for (int i = 0; i < outL.Length; i++) s += outL[i] * outL[i] + outR[i] * outR[i];
                float rms = Mathf.Sqrt(s / (2 * outL.Length)); maxRms = Mathf.Max(maxRms, rms); sumRms += rms; n++;
                foreach (var f in faces) for (int b = 0; b < f.sharedMesh.blendShapeCount; b++) maxShape = Mathf.Max(maxShape, f.GetBlendShapeWeight(b));
                if (src && src.isPlaying && src.clip) clip = true;
                dur += Time.deltaTime; yield return null;
            }
            L("audio", $"{label}: {who.displayName} spoke {dur:0.0} s · AudioSource playing a clip={clip} · listener RMS max {maxRms:0.000} avg {(n > 0 ? sumRms / n : 0):0.000} ({(maxRms > 0.01f ? "AUDIBLE" : "SILENT")}) · max blend-shape weight {maxShape:0} ({(maxShape > 5 ? "lip sync moving" : "no mouth movement")}) · AudioListener.volume {AudioListener.volume:0.00} pause={AudioListener.pause}");
        }

        // ------------------------------------------------------------------ talking with T (real push-to-talk binding, real microphone)
        float[] lastCapture; int surgicalBytes;
        IEnumerator Talk(string heard, string lang, float holdSeconds, string fieldsJson = null)
        {
            yield return Queue(heard, lang, fieldsJson);
            lastCapture = null; surgicalBytes = 0;
            Hold(Key.T); yield return new WaitForSecondsRealtime(holdSeconds); Release(Key.T);
            yield return new WaitForSecondsRealtime(0.4f);
            string cap = lastCapture != null ? $"{lastCapture.Length / 24000f:0.00} s, RMS {Rms(lastCapture):0.0000}" : surgicalBytes > 0 ? $"{surgicalBytes / 48000f:0.00} s (surgical voice input)" : "NOTHING CAPTURED";
            L("talk", $"held T {holdSeconds:0.0} s · '{heard}' ({lang}) · target {Voice.Target} · player room {HC.PlayerRoom} · microphone captured {cap}");
        }
        static float Rms(float[] a) { double s = 0; foreach (var v in a) s += v * v; return a.Length == 0 ? 0 : (float)Math.Sqrt(s / a.Length); }

        IEnumerator Until(Func<bool> c, float timeout) { float t = 0; while (!c() && t < timeout) { t += Time.deltaTime; yield return null; } }

        // ------------------------------------------------------------------ the journey
        IEnumerator Start()
        {
            t0 = Time.realtimeSinceStartup; Directory.CreateDirectory(outDir);
            // Never spend by accident: require the fake-provider control API unless -allowLive is given.
            using (var probe = UnityWebRequest.Get(Control + "/log")) { probe.timeout = 3; yield return probe.SendWebRequest(); fake = probe.result == UnityWebRequest.Result.Success; }
            bool allowLive = Environment.GetCommandLineArgs().Contains("-allowLive");
            if (fake) using (var reset = new UnityWebRequest(Control + "/reset", "POST") { downloadHandler = new DownloadHandlerBuffer() }) yield return reset.SendWebRequest();   // no transcripts left over from an earlier run
            if (!fake && !allowLive) { L("abort", "fake-provider proxy not found on :8788 and -allowLive not given: refusing to run against a real (paid) proxy"); yield return new WaitForSecondsRealtime(1f); Application.Quit(); yield break; }
            yield return Until(() => HC && DesktopPlayerController.Active && Voice, 40f);
            var ai = AIProviderManager.Instance;
            L("info", $"voice walkthrough · provider={(fake ? "FAKE (no key, no spending)" : "REAL (paid)")} · {Application.version} · display={DisplayModeManager.Mode} · AI={ai.ModeLabel} · live configured={ai.Live.Configured} · LiveOn={Voice.LiveOn} · ForceOffline={AIProviderManager.ForceOffline}");
            L("mic", $"devices: [{string.Join(", ", Microphone.devices)}] · picked: {VoiceInteractionManager.PickMicDevice() ?? "system default"} · mode {Voice.Mic}");
            Voice.UtteranceCaptured += s => lastCapture = s;
            Voice.TranscriptReady += s => L("heard", "clinical voice: " + s);
            Voice.Error += e => L("voice-error", e);
            CharacterSpeechController.SubtitleStarted += (c, s) => L("subtitle", c.displayName + ": " + s);
            HC.GuideSaid += id => L("guide", id);
            HC.RoomChanged += (a, b) => L("room", a + " → " + b);
            CharacterActionRouter.StateChanged += r => { if (r.state != ActionState.Requested) L("action", r.ToString()); };
            // Simulated keys must keep working if the window loses focus (otherwise the Input System drops keyboard events).
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            Application.focusChanged += f => L("focus", "window focused=" + f);
            StartCoroutine(Recorder());
            recording = true;

            // ---- 1. Reception: the welcome must be heard (voice + lip sync)
            yield return Probe(DoctorSpeech, "reception welcome", 25f);
            yield return Shot("v01_reception_welcome");

            // ---- 2. Reception: a question to the doctor through the microphone (full clinical voice turn)
            Desk.LookAt(DoctorSpeech.transform.position);
            yield return Talk("Hello doctor, nice to meet you.", "english", 1.6f);
            yield return Probe(DoctorSpeech, "reception reply (English)");

            // ---- 3. Corridor: both entrances visible; targeting stays on the doctor even when looking towards Room 01
            yield return WalkTo(new Vector3(9.4f, 0, -3.9f), 20f, "reception → corridor");
            Desk.LookAt(new Vector3(2f, 1.55f, -3.6f)); yield return new WaitForSeconds(0.5f);
            yield return Shot("v02_corridor_both_entrances");
            L("target", $"in the corridor looking towards Room 01: voice target = {Voice.Target} (patient addressable: {VoiceInteractionManager.TargetAvailable?.Invoke(Persona.Patient)})");

            // ---- 4. Ask the doctor (English, by voice) to go to Room 01; walk with her; open the door with E; enter
            Desk.LookAt(DoctorSpeech.transform.position);
            yield return Talk("Take me to room one, please.", "english", 1.6f);
            yield return Probe(DoctorSpeech, "guide line 'follow me'");
            L("hospital", "guiding to " + HC.GuidingTo);
            yield return WalkTo(new Vector3(3.4f, 0, -3.9f), 25f, "corridor → Room 01 door");
            yield return WalkTo(new Vector3(1.85f, 0, -3.45f), 10f, "in front of the Room 01 door");
            var door = FindAnyObjectByType<MedSimAI.Instruments.HospitalDoor>();
            Desk.LookAt(new Vector3(1.6f, 1.1f, -2.56f)); yield return new WaitForSeconds(0.4f);
            yield return Shot("v03_room01_door");
            yield return Tap(Key.E);
            yield return Until(() => door.IsOpen, 4f);
            L("door", $"E pressed while looking at the Room 01 door → open={door.IsOpen} angle={door.Angle:0}°");
            yield return WalkTo(new Vector3(1.85f, 0, -1.9f), 10f, "through the Room 01 doorway");
            yield return WalkTo(new Vector3(1.3f, 0, -0.6f), 10f, "into Room 01");
            yield return Until(() => HC.SessionRoom == HospitalRoomId.Room01, 6f);
            L("clinical", $"Room 01 session: {HC.SessionRoom} · state {SessionManager.Instance.State}");
            Desk.LookAt(PatientSpeech.transform.position); yield return new WaitForSeconds(1.2f);
            yield return Shot("v04_room01_patient");
            yield return Until(() => !DoctorSpeech.IsSpeaking, 25f);

            // ---- 5. Room 01: Arabic question to the patient (target follows gaze inside Room 01)
            L("target", $"in Room 01 looking at the patient: voice target = {Voice.Target}");
            yield return Talk("مرحبا سيد رييس، وين بيوجعك بالزبط؟", "arabic", 2.0f);
            yield return Probe(PatientSpeech, "patient reply (Arabic)");
            L("language", "conversation language now " + ai.ConversationLanguage);

            // ---- 6. Room 01 → Room 02 by voice (Arabic), walking out through the door and down the corridor
            Desk.LookAt(DoctorSpeech.transform.position);
            yield return Talk("خديني على غرفة العمليات", "arabic", 1.6f);
            yield return Probe(DoctorSpeech, "guide line to Room 02 (Arabic)");
            yield return WalkTo(new Vector3(1.85f, 0, -1.9f), 10f, "back to the Room 01 doorway");
            yield return WalkTo(new Vector3(1.85f, 0, -3.6f), 10f, "out to the corridor");
            yield return Until(() => HC.SessionRoom == HospitalRoomId.None, 6f);
            L("clinical", $"left Room 01 → session room {HC.SessionRoom}; records: {string.Join(" | ", HC.Records.Select(x => $"{x.room} {x.outcome}"))}");
            yield return WalkTo(new Vector3(5.33f, 0, -3.7f), 20f, "corridor → Room 02 entrance");
            yield return WalkTo(new Vector3(5.33f, 0, -1.6f), 10f, "into the Room 02 vestibule");
            yield return WalkTo(new Vector3(5.33f, 0, -0.2f), 10f, "through the sliding doors");
            var stud = SurgeryAnchor.Find(AnchorIds.StudentOperatingPosition).position;
            yield return WalkTo(stud, 20f, "to the student's operating position");
            yield return Until(() => HC.SessionRoom == HospitalRoomId.Room02 && HC.surgery.controller && HC.surgery.controller.Core != null && HC.surgery.controller.Core.Active, 15f);
            if (HC.SessionRoom != HospitalRoomId.Room02) { L("abort", "not in Room 02 (student at " + Player.position.ToString("F2") + ")"); File.WriteAllLines(Path.Combine(outDir, "hospital_voice_test.log"), log); Application.Quit(); yield break; }
            var ctl = HC.surgery.controller;
            if (ctl.voice) ctl.voice.UtteranceReady += pcm => surgicalBytes = pcm.Length;
            L("surgery", $"Room 02 session active={ctl.Core?.Active} · live online={ctl.Live?.Online} · last error='{ctl.Live?.LastError}' · clinical voice manager enabled={Voice.isActiveAndEnabled} · surgical voice input={(ctl.voice ? ctl.voice.mode.ToString() : "none")}");
            Desk.LookAt(SurgeryAnchor.Find(AnchorIds.SurgicalSiteKneeR).position);
            yield return Probe(DoctorSpeech, "surgical welcome (doctor as supervising surgeon)", 15f);
            yield return Shot("v05_operating_room");

            // ---- 7. Room 02: Arabic question to the surgeon, then an English hand-over request to Sami
            yield return Until(() => !DoctorSpeech.IsSpeaking, 20f);
            yield return Talk("شو الخطوة الجاي؟", "arabic", 1.6f, "{\"intent\":{\"addressedTo\":\"surgeon\",\"action\":\"explain_objective\",\"instrumentId\":\"none\",\"forCurrentObjective\":true,\"help\":false,\"explain\":true,\"languageCommand\":\"none\"},\"reply\":\"\"}");
            yield return Probe(DoctorSpeech, "surgeon reply (Arabic)");
            var sami = HC.surgery.assistant ? HC.surgery.assistant.GetComponentInChildren<CharacterSpeechController>() : null;
            yield return Talk("Sami, pass me the skin marker please.", "english", 1.8f, "{\"intent\":{\"addressedTo\":\"assistant\",\"action\":\"hand_instrument\",\"instrumentId\":\"inst.skin_marker\",\"forCurrentObjective\":true,\"help\":false,\"explain\":false,\"languageCommand\":\"none\"},\"reply\":\"\"}");
            yield return Probe(sami, "assistant reply (hand-over)");
            yield return Until(() => ctl.Core != null && !string.IsNullOrEmpty(ctl.Core.Handoff.PendingTool), 40f);
            L("handoff", "assistant holding out: " + ctl.Core.Handoff.PendingTool);
            yield return Shot("v06_assistant_handover");

            // ---- 8. Room 02 → corridor by voice (hospital routing from inside the surgical voice path), walk out
            yield return Talk("Take me back to the corridor.", "english", 1.6f);
            yield return Probe(DoctorSpeech, "guide line back to the corridor");
            yield return WalkTo(new Vector3(5.33f, 0, -0.2f), 15f, "back to the sliding doors");
            yield return WalkTo(new Vector3(5.33f, 0, -1.6f), 10f, "vestibule");
            yield return WalkTo(new Vector3(5.6f, 0, -3.9f), 10f, "corridor");
            yield return Until(() => HC.SessionRoom == HospitalRoomId.None, 6f);
            L("surgery", $"left Room 02 → session room {HC.SessionRoom}; clinical voice manager enabled again={Voice.isActiveAndEnabled}");

            // ---- 9. Corridor: language continuity (the student spoke Arabic last in Room 01) and hands-free mode
            L("language", "conversation language in the corridor: " + ai.ConversationLanguage + " (" + ai.LangMode + ")");
            Voice.SetMicMode(MicMode.HandsFree); yield return new WaitForSeconds(2f);
            L("mic", $"hands-free: microphone open={Microphone.IsRecording(Voice.MicDevice)} device={Voice.MicDevice ?? "system default"} level={Voice.MicLevel:0.0000}");
            Voice.SetMicMode(MicMode.PushToTalk);
            yield return Shot("v07_back_in_corridor");
            recording = false;

            if (fake)
            {
                using var w = UnityWebRequest.Get(Control + "/log"); yield return w.SendWebRequest();
                File.WriteAllText(Path.Combine(outDir, "fake_provider_log.json"), w.downloadHandler.text);
            }
            L("hospital", "log tail: " + string.Join(" ; ", HC.Log.Skip(Math.Max(0, HC.Log.Count - 20))));
            L("done", $"voice walkthrough finished · {frameNo} walkthrough frames");
            yield return new WaitForSecondsRealtime(1f);
            Application.Quit();
        }
    }
}
