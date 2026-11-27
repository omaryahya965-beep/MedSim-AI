using System;
using System.IO;
using MedSimAI.Core;
using UnityEngine;

namespace MedSimAI.AI
{
    public enum ConversationMode { OfflineScripted, OnlineAI }

    /// <summary>Non-secret runtime config. Read from persistentDataPath/medsim_ai_config.json (never contains provider API keys).</summary>
    [Serializable] public class AIRuntimeConfig
    {
        public string proxyEndpoint = "";      // e.g. https://your-proxy.example.org
        public string clientSessionToken = ""; // short-lived token issued by your proxy, NOT a provider key
        public bool preferOnline = false;
        public int timeoutMs = 12000;
        public string language = "en";
    }

    [DefaultExecutionOrder(-90)]
    public class AIProviderManager : MonoBehaviour
    {
        public static AIProviderManager Instance { get; private set; }
        public ConversationOrchestrator Orchestrator { get; private set; }
        public AIRuntimeConfig Config { get; private set; }
        public ITextToSpeechProvider OnlineTts { get; private set; }
        public ITextToSpeechProvider OfflineTts { get; } = new PrerenderedVoiceLibrary();
        public ISpeechToTextProvider OnlineStt { get; private set; }
        public MedSimAI.Clinical.LanguagePack Language { get; private set; }
        public ConversationMode Mode => Orchestrator.UseOnline ? ConversationMode.OnlineAI : ConversationMode.OfflineScripted;
        public event Action<ConversationMode> ModeChanged;

        public string ModeLabel => Mode == ConversationMode.OnlineAI ? "LIVE AI (validated, scenario-grounded)" : "OFFLINE SCRIPTED DEMO";

        void Awake()
        {
            Instance = this;
            Config = LoadConfig();
            var session = ClinicalScenarioManager.Instance.Session;
            IAIProvider online = string.IsNullOrEmpty(Config.proxyEndpoint) ? null : new GroundedProxyProvider(Config.proxyEndpoint, Config.clientSessionToken, () => ClinicalScenarioManager.Instance.Session, Mathf.CeilToInt(Config.timeoutMs / 1000f));
            Orchestrator = new ConversationOrchestrator(session, new OfflineScriptedProvider(session), online)
            { UseOnline = Config.preferOnline && online != null, TimeoutMs = Config.timeoutMs, Language = Config.language };
            if (online != null) { OnlineTts = new ProxyTextToSpeech(Config.proxyEndpoint, Config.clientSessionToken); OnlineStt = new ProxySpeechToText(Config.proxyEndpoint, Config.clientSessionToken); }
            Language = MedSimAI.Clinical.LanguagePack.Load(session.Scenario, Config.language);
            ClinicalScenarioManager.Instance.SessionReset += Orchestrator.Reset;
        }

        public bool OnlineAvailable => Orchestrator.Online != null;

        public void SetMode(ConversationMode m)
        {
            Orchestrator.UseOnline = m == ConversationMode.OnlineAI && OnlineAvailable;
            ModeChanged?.Invoke(Mode);
        }

        public void SetLanguage(string lang) { Config.language = lang; Orchestrator.Language = lang; Language = MedSimAI.Clinical.LanguagePack.Load(ClinicalScenarioManager.Instance.Scenario, lang); ModeChanged?.Invoke(Mode); }

        static AIRuntimeConfig LoadConfig()
        {
            var path = Path.Combine(Application.persistentDataPath, "medsim_ai_config.json");
            try { if (File.Exists(path)) return JsonUtility.FromJson<AIRuntimeConfig>(File.ReadAllText(path)) ?? new AIRuntimeConfig(); }
            catch (Exception e) { Debug.LogWarning("[MedSim] AI config unreadable, using offline mode: " + e.Message); }
            return new AIRuntimeConfig();
        }
    }
}
