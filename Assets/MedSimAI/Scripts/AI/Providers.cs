using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MedSimAI.Clinical;
using UnityEngine;
using UnityEngine.Networking;

namespace MedSimAI.AI
{
    /// <summary>
    /// Offline demonstration mode: approved scripted responses only, selected deterministically from scenario data.
    /// No network. Always available. Labelled "OFFLINE SCRIPTED" in the UI.
    /// </summary>
    public class OfflineScriptedProvider : IAIProvider
    {
        readonly ClinicalSession session;
        public OfflineScriptedProvider(ClinicalSession s) { session = s; }
        public string Name => "Offline scripted";
        public bool IsOnline => false;

        public Task<AIResponse> GenerateAsync(AIRequest req, CancellationToken ct)
        {
            var s = session.Scenario;
            if (req.context.persona == Persona.Patient)
            {
                var matches = HistoryMatcher.Match(s, req.studentUtterance);
                if (matches.Count == 0)
                {
                    int idx = Math.Abs(StableHash(req.studentUtterance)) % s.unknownAnswers.Length; // deterministic choice
                    return Task.FromResult(new AIResponse { speech = s.unknownAnswers[idx], providerName = Name });
                }
                var top = matches.Take(1).ToList(); // one approved answer per turn (keeps speech natural and matches pre-rendered audio)
                return Task.FromResult(new AIResponse { speech = string.Join(" ", top.Select(f => f.answer)), usedFactIds = top.Select(f => f.id).ToArray(), providerName = Name });
            }
            // Instructor: scripted coaching driven by deterministic case state.
            var u = HistoryMatcher.Normalize(req.studentUtterance);
            string speech;
            if (u.Contains("hint") || u.Contains("help me") || u.Contains("stuck") || u.Contains("what should i do")) speech = session.RequestHint();
            else if (session.Stages.Current == ClinicalStage.ClinicalReasoning || session.Stages.Current == ClinicalStage.SupervisorFeedback)
                speech = session.Tracker.Has(ClinicalEvents.EscalationRequested) ? s.instructorLines.escalationAck : ScriptedLines.InstructorReasoningFollowUp;
            else speech = ScriptedLines.InstructorGeneric;
            return Task.FromResult(new AIResponse { speech = speech, providerName = Name });
        }

        public static int StableHash(string s) { unchecked { int h = 23; foreach (var c in s ?? "") h = h * 31 + c; return h; } }
    }

    [Serializable] class ProxyRequestBody
    {
        public string persona; public string language; public string studentUtterance; public string roleDescription; public string stage;
        public AllowedFact[] allowedFacts; public string[] forbiddenTopics; public ConversationMessage[] history; public string unknownAnswer;
    }

    /// <summary>
    /// Real-AI mode: posts the persona-filtered context to a backend proxy you control (see AI_SETUP.md).
    /// No API keys live in the client. The proxy holds provider credentials and must return {speech, usedFactIds, outOfScope}.
    /// </summary>
    public class BackendProxyProvider : IAIProvider
    {
        readonly string endpoint; readonly string clientToken; readonly int timeoutSeconds;
        public BackendProxyProvider(string endpoint, string clientToken, int timeoutSeconds = 12)
        { this.endpoint = endpoint; this.clientToken = clientToken; this.timeoutSeconds = timeoutSeconds; }
        public string Name => "Backend proxy";
        public bool IsOnline => true;

        public async Task<AIResponse> GenerateAsync(AIRequest req, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(endpoint)) throw new InvalidOperationException("Proxy endpoint not configured");
            var body = new ProxyRequestBody
            {
                persona = req.context.persona.ToString(), language = req.language, studentUtterance = req.studentUtterance,
                roleDescription = req.context.roleDescription, stage = req.context.stage, allowedFacts = req.context.allowedFacts.ToArray(),
                forbiddenTopics = req.context.forbiddenTopics.ToArray(), history = req.history.ToArray(), unknownAnswer = req.context.unknownAnswer
            };
            using var www = new UnityWebRequest(endpoint.TrimEnd('/') + "/v1/converse", "POST");
            www.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(body)));
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");
            if (!string.IsNullOrEmpty(clientToken)) www.SetRequestHeader("X-MedSim-Client", clientToken); // session token, not a provider key
            www.timeout = timeoutSeconds;
            var op = www.SendWebRequest();
            while (!op.isDone) { if (ct.IsCancellationRequested) { www.Abort(); ct.ThrowIfCancellationRequested(); } await Task.Yield(); }
            if (www.result != UnityWebRequest.Result.Success) throw new Exception("Proxy error: " + www.error);
            var resp = JsonUtility.FromJson<AIResponse>(www.downloadHandler.text);
            if (resp == null) throw new Exception("Proxy returned malformed JSON");
            resp.providerName = Name;
            return resp;
        }
    }

    /// <summary>Proxy-backed speech-to-text (Android and PC). Uploads in-memory WAV; nothing written to disk.</summary>
    public class ProxySpeechToText : ISpeechToTextProvider
    {
        readonly string endpoint; readonly string token;
        public ProxySpeechToText(string endpoint, string token) { this.endpoint = endpoint; this.token = token; }
        public string Name => "Proxy STT";
        public bool IsAvailable => !string.IsNullOrEmpty(endpoint);

        public async Task<string> TranscribeAsync(AudioClip clip, string language, CancellationToken ct)
        {
            var wav = WavUtility.FromAudioClip(clip);
            using var www = new UnityWebRequest(endpoint.TrimEnd('/') + "/v1/stt?lang=" + language, "POST");
            www.uploadHandler = new UploadHandlerRaw(wav); www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "audio/wav");
            if (!string.IsNullOrEmpty(token)) www.SetRequestHeader("X-MedSim-Client", token);
            www.timeout = 15;
            var op = www.SendWebRequest();
            while (!op.isDone) { if (ct.IsCancellationRequested) { www.Abort(); ct.ThrowIfCancellationRequested(); } await Task.Yield(); }
            if (www.result != UnityWebRequest.Result.Success) throw new Exception("STT error: " + www.error);
            return JsonUtility.FromJson<SttResponse>(www.downloadHandler.text)?.text ?? "";
        }
        [Serializable] class SttResponse { public string text; }
    }

    /// <summary>Proxy-backed TTS returning WAV. Distinct voices per persona are chosen server-side.</summary>
    public class ProxyTextToSpeech : ITextToSpeechProvider
    {
        readonly string endpoint; readonly string token;
        public ProxyTextToSpeech(string endpoint, string token) { this.endpoint = endpoint; this.token = token; }
        public string Name => "Proxy TTS";
        public bool IsAvailable => !string.IsNullOrEmpty(endpoint);

        public async Task<AudioClip> SynthesizeAsync(string text, Persona persona, string language, CancellationToken ct)
        {
            var url = $"{endpoint.TrimEnd('/')}/v1/tts?voice={persona.ToString().ToLowerInvariant()}&lang={language}";
            using var www = new UnityWebRequest(url, "POST");
            www.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(text));
            www.downloadHandler = new DownloadHandlerAudioClip(url, AudioType.WAV);
            www.SetRequestHeader("Content-Type", "text/plain; charset=utf-8");
            if (!string.IsNullOrEmpty(token)) www.SetRequestHeader("X-MedSim-Client", token);
            www.timeout = 15;
            var op = www.SendWebRequest();
            while (!op.isDone) { if (ct.IsCancellationRequested) { www.Abort(); ct.ThrowIfCancellationRequested(); } await Task.Yield(); }
            if (www.result != UnityWebRequest.Result.Success) throw new Exception("TTS error: " + www.error);
            return DownloadHandlerAudioClip.GetContent(www);
        }
    }

    /// <summary>
    /// Live AI provider (v0.2): the proxy generates a natural, scenario-grounded reply for the addressed persona, validates
    /// it deterministically against the authoritative scenario and starts runtime speech synthesis. No pre-recorded dialogue.
    /// </summary>
    public class LiveProvider : IAIProvider
    {
        readonly LiveAIClient client; readonly Func<ClinicalSession> session;
        public static string DiscussionId; // set by SessionManager during the clinical discussion
        public LiveProvider(LiveAIClient client, Func<ClinicalSession> session) { this.client = client; this.session = session; }
        public string Name => "Live AI (OpenAI via MedSim proxy)";
        public bool IsOnline => true;
        public async Task<AIResponse> GenerateAsync(AIRequest req, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(client.SessionId) && !await client.StartSession(session().Scenario, ct)) throw new Exception(client.LastError ?? "Live AI unavailable");
            var r = await client.Turn(req.context.persona, req.studentUtterance, LiveAIClient.ContextOf(session(), DiscussionId), ct);
            if (r == null || string.IsNullOrEmpty(r.text)) throw new Exception("Live AI returned no text");
            return new AIResponse
            {
                live = true, providerName = Name, speech = r.text, audioId = r.audioId, usedFactIds = r.factIds ?? new string[0], intent = r.intent,
                investigationId = string.IsNullOrEmpty(r.investigationId) ? null : r.investigationId, escalationRequested = r.escalationRequested,
                diagnosisNamed = r.diagnosisNamed, isQuestion = r.isQuestion, concepts = r.concepts ?? new string[0], liveSource = r.source,
                action = r.action, supervisorIntervention = r.supervisorIntervention
            };
        }
    }

    public static class WavUtility
    {
        public static byte[] FromAudioClip(AudioClip clip)
        {
            var samples = new float[clip.samples * clip.channels];
            clip.GetData(samples, 0);
            var bytes = new byte[44 + samples.Length * 2];
            void W(int o, string s) { for (int i = 0; i < 4; i++) bytes[o + i] = (byte)s[i]; }
            void I32(int o, int v) => BitConverter.GetBytes(v).CopyTo(bytes, o);
            void I16(int o, short v) => BitConverter.GetBytes(v).CopyTo(bytes, o);
            W(0, "RIFF"); I32(4, bytes.Length - 8); W(8, "WAVE"); W(12, "fmt "); I32(16, 16); I16(20, 1); I16(22, (short)clip.channels);
            I32(24, clip.frequency); I32(28, clip.frequency * clip.channels * 2); I16(32, (short)(clip.channels * 2)); I16(34, 16); W(36, "data"); I32(40, samples.Length * 2);
            for (int i = 0; i < samples.Length; i++) I16(44 + i * 2, (short)(Mathf.Clamp(samples[i], -1f, 1f) * short.MaxValue));
            return bytes;
        }
    }
}
