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
    /// Live-AI provider for the scenario-grounded proxy (Backend/medsim-proxy). Sends only scenario id/version, persona,
    /// utterance, stage and revealed-finding ids. The proxy loads the authoritative case itself and returns a constrained
    /// classification (fact ids / intent) plus optional validated conversational wording.
    /// </summary>
    public class GroundedProxyProvider : IAIProvider
    {
        readonly string endpoint, token; readonly int timeoutSeconds; readonly Func<ClinicalSession> session;
        public GroundedProxyProvider(string endpoint, string token, Func<ClinicalSession> session, int timeoutSeconds = 15)
        { this.endpoint = endpoint; this.token = token; this.session = session; this.timeoutSeconds = timeoutSeconds; }
        public string Name => "Grounded proxy";
        public bool IsOnline => true;

        [Serializable] class Req { public string scenarioId, scenarioVersion, persona, utterance, stage, language; public string[] revealed; public ConversationMessage[] history; }
        [Serializable] class Resp { public string persona, scenarioVersion, leadIn, speech, intent, investigationId, reply, error; public string[] factIds; public bool offTopic, mentionsEscalation; }

        public async Task<AIResponse> GenerateAsync(AIRequest req, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(endpoint)) throw new InvalidOperationException("Proxy endpoint not configured");
            var s = session();
            var revealed = s.Tracker.Log.Select(e => e.eventId).Where(id => id.StartsWith("EXAM:") || id.StartsWith("INV:")).Distinct().ToArray();
            var body = new Req
            {
                scenarioId = s.Scenario.scenarioId, scenarioVersion = s.Scenario.version, persona = req.context.persona.ToString(),
                utterance = req.studentUtterance, stage = s.Stages.Current.ToString(), language = req.language, revealed = revealed,
                history = req.history.Skip(Math.Max(0, req.history.Count - 8)).ToArray()
            };
            using var www = new UnityWebRequest(endpoint.TrimEnd('/') + "/v1/converse", "POST");
            www.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(body)));
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");
            if (!string.IsNullOrEmpty(token)) www.SetRequestHeader("X-MedSim-Client", token);
            www.timeout = timeoutSeconds;
            var op = www.SendWebRequest();
            while (!op.isDone) { if (ct.IsCancellationRequested) { www.Abort(); ct.ThrowIfCancellationRequested(); } await Task.Yield(); }
            if (www.result != UnityWebRequest.Result.Success) throw new Exception("Proxy error: " + www.responseCode);
            var r = JsonUtility.FromJson<Resp>(www.downloadHandler.text);
            if (r == null || !string.IsNullOrEmpty(r.error)) throw new Exception("Proxy returned error");
            if (r.scenarioVersion != s.Scenario.version) throw new Exception("Scenario version mismatch");
            return new AIResponse
            {
                grounded = true, providerName = Name, usedFactIds = r.factIds ?? new string[0], leadIn = r.leadIn, outOfScope = r.offTopic,
                intent = r.intent, investigationId = string.IsNullOrEmpty(r.investigationId) ? null : r.investigationId, mentionsEscalation = r.mentionsEscalation, reply = r.reply,
                speech = r.persona == "Patient" ? r.speech : r.reply
            };
        }
    }
}
