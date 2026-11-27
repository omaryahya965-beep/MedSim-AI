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
    public enum LiveStatus { NotConfigured, Connecting, Online, Offline }

    [Serializable] public class LiveContext { public string stage, stateId, discussionId; public string[] events; public string[] missing; }

    [Serializable] public class LiveTurnResponse
    {
        public string persona, heard, text, lang, languageCommand, studentAct, emotion, intent, investigationId, source, rejected, audioId, error, model;
        public bool noSpeech;
        public string[] factIds, concepts, deterministicConcepts;
        public bool escalationRequested, diagnosisNamed, isQuestion;
        public int llmMs;
    }

    [Serializable] public class LiveSayResponse { public string persona, kind, text, lang, source, rejected, audioId, error; public int llmMs; }

    [Serializable] public class DebriefPayload
    {
        public string[] met, missed, missedCritical, interventions, aiInterpreted;
        public bool escalated; public float ecgSeconds = -1; public int hints;
    }

    /// <summary>
    /// Unity client for the MedSim Live AI proxy. Holds only the non-secret client session token; the OpenAI key never
    /// leaves the proxy machine. Every character line is generated and voiced at runtime by the proxy.
    /// </summary>
    public class LiveAIClient
    {
        readonly string endpoint, token; readonly int timeoutSeconds;
        public string SessionId { get; private set; }
        public LiveStatus Status { get; private set; } = LiveStatus.NotConfigured;
        public string LastError { get; private set; }
        public event Action<LiveStatus, string> StatusChanged;
        public string Language = "en";
        /// <summary>"ar"/"en" when the student fixed the language explicitly; null = follow the student's language.</summary>
        public string LangLock;

        public LiveAIClient(string endpoint, string token, int timeoutSeconds = 25)
        {
            this.endpoint = (endpoint ?? "").TrimEnd('/'); this.token = token; this.timeoutSeconds = timeoutSeconds;
            if (!string.IsNullOrEmpty(this.endpoint)) Status = LiveStatus.Connecting;
        }
        public bool Configured => !string.IsNullOrEmpty(endpoint);

        void SetStatus(LiveStatus s, string err = null)
        {
            LastError = err;
            if (s == Status && err == null) return;
            Status = s; StatusChanged?.Invoke(s, err);
        }

        static string FriendlyError(long code, string body)
        {
            if (code == 0) return "AI service unreachable (is the MedSim proxy running?)";
            if (code == 401) return "AI proxy refused the client token";
            if (code == 402) return "AI test budget reached";
            if (body != null && body.Contains("provider_auth_failed")) return "AI provider rejected the API key (check the proxy secrets)";
            if (code == 429) return "AI rate limit reached — please wait a moment";
            if (code == 504) return "AI service timed out";
            return "AI service error (" + code + ")";
        }

        async Task<string> Send(string method, string route, byte[] body, string contentType, CancellationToken ct, int? timeout = null)
        {
            using var www = new UnityWebRequest(endpoint + route, method);
            if (body != null) { www.uploadHandler = new UploadHandlerRaw(body); www.SetRequestHeader("Content-Type", contentType); }
            www.downloadHandler = new DownloadHandlerBuffer();
            if (!string.IsNullOrEmpty(token)) www.SetRequestHeader("X-MedSim-Client", token);
            www.timeout = timeout ?? timeoutSeconds;
            var op = www.SendWebRequest();
            while (!op.isDone) { if (ct.IsCancellationRequested) { www.Abort(); ct.ThrowIfCancellationRequested(); } await Task.Yield(); }
            if (www.result != UnityWebRequest.Result.Success)
            {
                var msg = FriendlyError(www.responseCode, www.downloadHandler?.text);
                SetStatus(LiveStatus.Offline, msg);
                throw new Exception(msg);
            }
            SetStatus(LiveStatus.Online);
            return www.downloadHandler.text;
        }
        Task<string> PostJson(string route, object payload, CancellationToken ct) => Send("POST", route, Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload)), "application/json", ct);

        [Serializable] class StartReq { public string scenarioId, scenarioVersion, language; }
        [Serializable] class StartResp { public string sessionId, scenarioVersion, error; }
        [Serializable] class TurnReq { public string sessionId, persona, utterance, language, langLock; public LiveContext context; public bool speak = true; }
        [Serializable] class SayReq { public string sessionId, persona, kind, baseText, severity, language, langLock; public LiveContext context; public DebriefPayload result; public bool speak = true; }
        [Serializable] class EndReq { public string sessionId; }

        public async Task<bool> StartSession(ScenarioData s, CancellationToken ct = default)
        {
            if (!Configured) { SetStatus(LiveStatus.NotConfigured, "Live AI is not configured"); return false; }
            try
            {
                var r = JsonUtility.FromJson<StartResp>(await PostJson("/v1/session/start", new StartReq { scenarioId = s.scenarioId, scenarioVersion = s.version, language = Language }, ct));
                SessionId = r?.sessionId;
                return !string.IsNullOrEmpty(SessionId);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) { Debug.LogWarning("[MedSim] Live AI session start failed: " + e.Message); return false; }
        }

        public async Task EndSession()
        {
            if (string.IsNullOrEmpty(SessionId)) return;
            try { await PostJson("/v1/session/end", new EndReq { sessionId = SessionId }, CancellationToken.None); } catch { }
            SessionId = null;
        }

        public static LiveContext ContextOf(ClinicalSession s, string discussionId = null) => new LiveContext
        {
            stage = s.Stages.Current.ToString(), stateId = s.Patient.ConditionId ?? "", discussionId = discussionId ?? "",
            events = s.Tracker.Log.Select(e => e.eventId).Distinct().ToArray(), missing = new string[0]
        };

        public async Task<LiveTurnResponse> Turn(Persona p, string utterance, LiveContext ctx, CancellationToken ct)
        {
            var json = await PostJson("/v1/turn", new TurnReq { sessionId = SessionId, persona = p.ToString(), utterance = utterance, language = Language, langLock = LangLock ?? "", context = ctx }, ct);
            return JsonUtility.FromJson<LiveTurnResponse>(json);
        }

        public async Task<LiveSayResponse> Say(Persona p, string kind, string baseText, LiveContext ctx, CancellationToken ct, string severity = null, DebriefPayload result = null)
        {
            var json = await PostJson("/v1/say", new SayReq { sessionId = SessionId, persona = p.ToString(), kind = kind, baseText = baseText ?? "", severity = severity ?? "", language = Language, langLock = LangLock ?? "", context = ctx, result = result }, ct);
            return JsonUtility.FromJson<LiveSayResponse>(json);
        }

        /// <summary>Downloads runtime-generated speech (WAV, 24 kHz mono). Waits while the proxy synthesises it.</summary>
        public async Task<AudioClip> FetchAudio(string audioId, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(audioId)) return null;
            var url = $"{endpoint}/v1/audio/{audioId}";
            using var www = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.WAV);
            if (!string.IsNullOrEmpty(token)) www.SetRequestHeader("X-MedSim-Client", token);
            www.timeout = timeoutSeconds + 15;
            var op = www.SendWebRequest();
            while (!op.isDone) { if (ct.IsCancellationRequested) { www.Abort(); ct.ThrowIfCancellationRequested(); } await Task.Yield(); }
            if (www.result != UnityWebRequest.Result.Success) { SetStatus(LiveStatus.Offline, "AI voice unavailable (" + www.responseCode + ")"); return null; }
            var clip = DownloadHandlerAudioClip.GetContent(www);
            if (clip) clip.name = "live_" + audioId.Substring(0, 8);
            return clip;
        }

        /// <summary>
        /// Native voice turn: the student's audio (PCM16 24 kHz mono) goes straight to the proxy's persistent Realtime
        /// session (no separate speech-to-text request). The validated reply arrives as a JSON header (with what the
        /// session heard, for transcripts and deterministic assessment) followed by streamed speech for 'voice'.
        /// </summary>
        public UnityWebRequest VoiceTurn(Persona p, byte[] pcm24k, LiveContext ctx, MedSimAI.Voice.StreamingVoice voice, Action<LiveTurnResponse> onMeta)
        {
            var ctxB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonUtility.ToJson(ctx))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var url = $"{endpoint}/v1/voice?sessionId={UnityWebRequest.EscapeURL(SessionId)}&persona={p}&lang={Language}&langLock={LangLock ?? ""}&ctx={ctxB64}";
            var www = new UnityWebRequest(url, "POST") { uploadHandler = new UploadHandlerRaw(pcm24k), downloadHandler = new MedSimAI.Voice.VoiceStreamHandler(voice, json => onMeta?.Invoke(JsonUtility.FromJson<LiveTurnResponse>(json))), timeout = timeoutSeconds + 30 };
            www.SetRequestHeader("Content-Type", "application/octet-stream");
            if (!string.IsNullOrEmpty(token)) www.SetRequestHeader("X-MedSim-Client", token);
            return www;
        }

        /// <summary>Character-initiated line (welcome, intervention, discussion, debrief) with streamed speech.</summary>
        public UnityWebRequest SayStream(Persona p, string kind, string baseText, LiveContext ctx, MedSimAI.Voice.StreamingVoice voice, Action<LiveSayResponse> onMeta, string severity = null, DebriefPayload result = null)
        {
            var body = JsonUtility.ToJson(new SayReq { sessionId = SessionId, persona = p.ToString(), kind = kind, baseText = baseText ?? "", severity = severity ?? "", language = Language, langLock = LangLock ?? "", context = ctx, result = result });
            var www = new UnityWebRequest(endpoint + "/v1/say-stream", "POST") { uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)), downloadHandler = new MedSimAI.Voice.VoiceStreamHandler(voice, json => onMeta?.Invoke(JsonUtility.FromJson<LiveSayResponse>(json))), timeout = timeoutSeconds + 30 };
            www.SetRequestHeader("Content-Type", "application/json");
            if (!string.IsNullOrEmpty(token)) www.SetRequestHeader("X-MedSim-Client", token);
            return www;
        }

        public void ReportError(long code, string body) => SetStatus(LiveStatus.Offline, FriendlyError(code, body));
        public void ReportOnline() => SetStatus(LiveStatus.Online);

        [Serializable] class SttResp { public string text; }
        public async Task<string> Transcribe(AudioClip clip, CancellationToken ct)
        {
            var wav = WavUtility.FromAudioClip(clip);
            var json = await Send("POST", "/v1/stt?lang=" + Language, wav, "audio/wav", ct, 20);
            return JsonUtility.FromJson<SttResp>(json)?.text ?? "";
        }
    }
}
