using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using MedSimAI.AI;
using MedSimAI.Surgery.Shared;
using MedSimAI.Voice;
using UnityEngine;
using UnityEngine.Networking;

namespace MedSimAI.Surgery.AI
{
    [Serializable] public class SurgeryIntentDto { public string characterId = "", action = "none", instrumentId = "", languageCommand = ""; public bool forCurrentObjective, help, explain; }

    [Serializable] public class SurgeryTurnResponse
    {
        public string characterId, heard, text, lang, source, rejected, error;
        public bool deferred, noSpeech;
        public SurgeryIntentDto intent;
    }

    [Serializable] public class SurgerySayResponse { public string characterId, kind, text, lang, source, rejected, error, reason; }

    [Serializable] public class SurgeryContextDto { public string objectiveId = "", holding = ""; public string[] completed = new string[0], recent = new string[0]; }

    /// <summary>
    /// Unity client for the proxy's surgical routes (/v1/surgery/*). Same proxy, same client token and config file as the
    /// clinical module (medsim_ai_config.json); the OpenAI key never leaves the proxy machine. Separate session from the
    /// clinical scenario. Voice uses the existing streaming pipeline (VoiceStreamHandler → StreamingVoice).
    /// </summary>
    public class SurgeryLiveClient
    {
        readonly string endpoint, token; readonly int timeoutSeconds;
        public string SessionId { get; private set; }
        public bool Online { get; private set; }
        public string LastError { get; private set; } = "";
        public string Language = "en";
        public string LangLock = "";
        public event Action<bool, string> StatusChanged;

        public bool Configured => !string.IsNullOrEmpty(endpoint);

        public SurgeryLiveClient(string endpoint, string token, int timeoutSeconds = 25)
        { this.endpoint = (endpoint ?? "").TrimEnd('/'); this.token = token ?? ""; this.timeoutSeconds = timeoutSeconds; }

        /// <summary>Reads the same non-secret client config the clinical module uses. Returns an unconfigured client if absent.</summary>
        public static SurgeryLiveClient FromUserConfig()
        {
            if (AIProviderManager.ForceOffline) return new SurgeryLiveClient("", "");
            var cfg = AIProviderManager.ConfigOverride;
            try { if (cfg == null && File.Exists(AIProviderManager.ConfigPath)) cfg = JsonUtility.FromJson<AIRuntimeConfig>(File.ReadAllText(AIProviderManager.ConfigPath)); }
            catch (Exception e) { Debug.LogWarning("[Surgery] Could not read AI config: " + e.Message); }
            if (cfg == null || !cfg.preferOnline) return new SurgeryLiveClient("", "");
            return new SurgeryLiveClient(cfg.proxyEndpoint, cfg.clientSessionToken, Mathf.CeilToInt(cfg.timeoutMs / 1000f));
        }

        void SetOnline(bool on, string err = "")
        {
            bool changed = on != Online || err != LastError;
            Online = on; LastError = err ?? "";
            if (changed) StatusChanged?.Invoke(on, LastError);
        }

        static string Friendly(long code)
        {
            if (code == 0) return "AI service unreachable (is the MedSim proxy running?)";
            if (code == 401) return "AI proxy refused the client token";
            if (code == 402) return "AI budget reached";
            if (code == 429) return "AI rate limit reached";
            return "AI service error (" + code + ")";
        }

        async Task<string> Post(string route, object payload)
        {
            using var www = new UnityWebRequest(endpoint + route, "POST")
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload))),
                downloadHandler = new DownloadHandlerBuffer(), timeout = timeoutSeconds
            };
            www.SetRequestHeader("Content-Type", "application/json");
            if (!string.IsNullOrEmpty(token)) www.SetRequestHeader("X-MedSim-Client", token);
            var op = www.SendWebRequest();
            while (!op.isDone) await Task.Yield();
            if (www.result != UnityWebRequest.Result.Success && www.responseCode != 422) { SetOnline(false, Friendly(www.responseCode)); throw new Exception(LastError); }
            SetOnline(true);
            return www.downloadHandler.text;
        }

        [Serializable] class StartReq { public string scenarioId, scenarioVersion, language; }
        [Serializable] class StartResp { public string sessionId, error; }
        [Serializable] class IdReq { public string sessionId; }
        [Serializable] class EventReq { public string sessionId; public SurgeryContextDto context; }
        [Serializable] class TurnReq { public string sessionId, characterId, utterance, language, langLock; public SurgeryContextDto context; }
        [Serializable] class SayReq { public string sessionId, characterId, kind, baseText, language, severity; public bool approvedOnly; public DebriefFacts facts; }
        [Serializable] class ActionReq { public string sessionId, characterId, action, instrumentId, status; }

        public async Task<bool> StartSession(SurgicalScenarioData s)
        {
            if (!Configured) { SetOnline(false, "Live AI is not configured — text-only mode"); return false; }
            try
            {
                var r = JsonUtility.FromJson<StartResp>(await Post("/v1/surgery/session/start", new StartReq { scenarioId = s.scenarioId, scenarioVersion = s.version, language = Language }));
                SessionId = r?.sessionId; return !string.IsNullOrEmpty(SessionId);
            }
            catch (Exception e) { Debug.LogWarning("[Surgery] Live AI session start failed: " + e.Message); return false; }
        }

        public async Task EndSession()
        {
            if (string.IsNullOrEmpty(SessionId)) return;
            var id = SessionId; SessionId = null;
            try { await Post("/v1/surgery/session/end", new IdReq { sessionId = id }); } catch { }
        }

        public async void PostContext(SurgeryContextDto ctx)
        {
            if (string.IsNullOrEmpty(SessionId)) return;
            try { await Post("/v1/surgery/event", new EventReq { sessionId = SessionId, context = ctx }); } catch { }
        }

        public async void PostActionResult(CharacterActionResult r, string status)
        {
            if (string.IsNullOrEmpty(SessionId) || r == null) return;
            try { await Post("/v1/surgery/action-result", new ActionReq { sessionId = SessionId, characterId = r.characterId, action = r.action, instrumentId = r.instrumentId, status = status }); } catch { }
        }

        public async Task<SurgeryTurnResponse> Turn(string characterId, string utterance, SurgeryContextDto ctx)
        {
            var json = await Post("/v1/surgery/turn", new TurnReq { sessionId = SessionId, characterId = characterId, utterance = utterance, language = Language, langLock = LangLock, context = ctx });
            return JsonUtility.FromJson<SurgeryTurnResponse>(json);
        }

        /// <summary>Native voice turn (PCM16 24 kHz mono). Header JSON arrives first (heard + intent); speech streams after it unless deferred.</summary>
        public UnityWebRequest VoiceTurn(string characterId, byte[] pcm24k, SurgeryContextDto ctx, StreamingVoice voice, Action<SurgeryTurnResponse> onMeta)
        {
            var ctxB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonUtility.ToJson(ctx))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var url = $"{endpoint}/v1/surgery/voice?sessionId={UnityWebRequest.EscapeURL(SessionId ?? "")}&character={characterId}&lang={Language}&langLock={LangLock}&ctx={ctxB64}";
            var www = new UnityWebRequest(url, "POST") { uploadHandler = new UploadHandlerRaw(pcm24k), downloadHandler = new VoiceStreamHandler(voice, json => onMeta?.Invoke(JsonUtility.FromJson<SurgeryTurnResponse>(json))), timeout = timeoutSeconds + 30 };
            www.SetRequestHeader("Content-Type", "application/octet-stream");
            if (!string.IsNullOrEmpty(token)) www.SetRequestHeader("X-MedSim-Client", token);
            return www;
        }

        /// <summary>Character-initiated line: approved base text, optionally rephrased under grounding, streamed as speech.</summary>
        public UnityWebRequest SayStream(string characterId, string kind, string baseText, bool approvedOnly, StreamingVoice voice, Action<SurgerySayResponse> onMeta, string severity = "", DebriefFacts facts = null)
        {
            var body = JsonUtility.ToJson(new SayReq { sessionId = SessionId, characterId = characterId, kind = kind, baseText = baseText, language = Language, severity = severity, approvedOnly = approvedOnly, facts = facts });
            var www = new UnityWebRequest(endpoint + "/v1/surgery/say-stream", "POST") { uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)), downloadHandler = new VoiceStreamHandler(voice, json => onMeta?.Invoke(JsonUtility.FromJson<SurgerySayResponse>(json))), timeout = timeoutSeconds + 30 };
            www.SetRequestHeader("Content-Type", "application/json");
            if (!string.IsNullOrEmpty(token)) www.SetRequestHeader("X-MedSim-Client", token);
            return www;
        }

        public void ReportStreamResult(UnityWebRequest www)
        {
            if (www.result == UnityWebRequest.Result.Success) SetOnline(true);
            else SetOnline(false, Friendly(www.responseCode));
        }
    }
}
