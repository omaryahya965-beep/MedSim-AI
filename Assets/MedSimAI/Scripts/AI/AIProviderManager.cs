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
        public string proxyEndpoint = "";      // e.g. http://127.0.0.1:8787 (the MedSim proxy on this PC)
        public string clientSessionToken = ""; // shared secret for the proxy (MEDSIM_CLIENT_TOKEN), NOT a provider key
        public bool preferOnline = true;
        public int timeoutMs = 25000;
        public string language = "en";
    }

    /// <summary>
    /// Owns the conversation pipeline. Live AI (OpenAI behind the MedSim proxy) is the primary path: both characters
    /// generate replies and runtime speech. If the proxy is unreachable the app says so and continues TEXT-ONLY with
    /// deterministic scenario text — it never plays pre-recorded dialogue or pretends to be Live AI.
    /// </summary>
    [DefaultExecutionOrder(-90)]
    public class AIProviderManager : MonoBehaviour
    {
        /// <summary>Set by automated tests so they never make billable provider calls.</summary>
        public static bool ForceOffline;
        /// <summary>Test hook: use this config instead of the user config file (e.g. an unreachable endpoint).</summary>
        public static AIRuntimeConfig ConfigOverride;
        public static AIProviderManager Instance { get; private set; }
        public ConversationOrchestrator Orchestrator { get; private set; }
        public AIRuntimeConfig Config { get; private set; }
        public LiveAIClient Live { get; private set; }
        public MedSimAI.Clinical.LanguagePack Language { get; private set; }
        public ConversationMode Mode => Orchestrator.UseOnline ? ConversationMode.OnlineAI : ConversationMode.OfflineScripted;
        public event Action<ConversationMode> ModeChanged;
        public event Action<LiveStatus, string> LiveStatusChanged;
        /// <summary>Raised when the conversation language changes (explicitly or because the student switched language).</summary>
        public event Action<string> LanguageChanged;
        public const string PrefLangMode = "medsim.langMode", PrefConvLang = "medsim.convLang";
        /// <summary>Auto (follow the student) or an explicit Arabic/English choice shared by both characters.</summary>
        public LanguageMode LangMode { get; private set; } = LanguageMode.Auto;
        /// <summary>"ar"/"en" when the student chose a language explicitly, otherwise null.</summary>
        public string LangLock => LangMode == LanguageMode.Arabic ? "ar" : LangMode == LanguageMode.English ? "en" : null;
        public string ConversationLanguage => Config.language == "ar" ? "ar" : "en";

        public string ModeLabel => Mode == ConversationMode.OnlineAI
            ? (Live.Status == LiveStatus.Offline ? "AI CONNECTION PROBLEM — " + Live.LastError : "LIVE AI")
            : (ForceOffline ? "TEST MODE — text only" : "AI OFFLINE — text only (no voice)");

        void Awake()
        {
            Instance = this;
            Config = ConfigOverride ?? (ForceOffline ? new AIRuntimeConfig { preferOnline = false } : LoadConfig());
            var session = ClinicalScenarioManager.Instance.Session;
            if (!ForceOffline && ConfigOverride == null)
            {
                LangMode = (LanguageMode)PlayerPrefs.GetInt(PrefLangMode, (int)LanguageMode.Auto);
                Config.language = LangLock ?? PlayerPrefs.GetString(PrefConvLang, Config.language == "ar" ? "ar" : "en");
            }
            Live = new LiveAIClient(ForceOffline && ConfigOverride == null ? "" : Config.proxyEndpoint, Config.clientSessionToken, Mathf.CeilToInt(Config.timeoutMs / 1000f)) { Language = Config.language, LangLock = LangLock };
            Live.StatusChanged += (s, e) => { LiveStatusChanged?.Invoke(s, e); ModeChanged?.Invoke(Mode); };
            IAIProvider online = Live.Configured ? new LiveProvider(Live, () => ClinicalScenarioManager.Instance.Session) : null;
            Orchestrator = new ConversationOrchestrator(session, new OfflineScriptedProvider(session), online)
            { UseOnline = Config.preferOnline && online != null, TimeoutMs = Config.timeoutMs, Language = Config.language };
            Language = MedSimAI.Clinical.LanguagePack.Load(session.Scenario, Config.language);
            ClinicalScenarioManager.Instance.SessionReset += Orchestrator.Reset;
            if (!Live.Configured && !ForceOffline) Debug.LogWarning("[MedSim] Live AI not configured (see AI_SETUP.md). Characters will be text-only.");
        }

        public bool OnlineAvailable => Orchestrator.Online != null;

        public void SetMode(ConversationMode m)
        {
            Orchestrator.UseOnline = m == ConversationMode.OnlineAI && OnlineAvailable;
            ModeChanged?.Invoke(Mode);
        }

        public void SetLanguage(string lang)
        {
            lang = lang == "ar" ? "ar" : "en";
            bool changed = Config.language != lang;
            Config.language = lang; Orchestrator.Language = lang; Live.Language = lang; Live.LangLock = LangLock;
            Language = MedSimAI.Clinical.LanguagePack.Load(ClinicalScenarioManager.Instance.Scenario, lang);
            if (!ForceOffline) { PlayerPrefs.SetString(PrefConvLang, lang); PlayerPrefs.Save(); }
            ModeChanged?.Invoke(Mode);
            if (changed) { Debug.Log("[MedSim] Conversation language → " + lang + " (" + LangMode + ")"); LanguageChanged?.Invoke(lang); }
        }

        /// <summary>Explicit choice (voice command or menu): persistent for both characters until changed again.</summary>
        public void SetLanguageMode(LanguageMode m)
        {
            LangMode = m; if (!ForceOffline) { PlayerPrefs.SetInt(PrefLangMode, (int)m); PlayerPrefs.Save(); }
            Live.LangLock = LangLock;
            SetLanguage(LangLock ?? Config.language);
            ModeChanged?.Invoke(Mode);
        }

        /// <summary>Auto mode: the conversation follows the language the student just spoke or typed.</summary>
        public void NoteStudentLanguage(string lang)
        {
            if (LangMode != LanguageMode.Auto || (lang != "ar" && lang != "en")) return;
            if (lang != Config.language) SetLanguage(lang);
        }

        public string LanguageModeLabel => LangMode == LanguageMode.Arabic ? "العربية (fixed)" : LangMode == LanguageMode.English ? "English (fixed)" : "Automatic — follows your speech (now " + (Config.language == "ar" ? "العربية" : "English") + ")";

        public static string ConfigPath => Path.Combine(Application.persistentDataPath, "medsim_ai_config.json");

        static AIRuntimeConfig LoadConfig()
        {
            try { if (File.Exists(ConfigPath)) return JsonUtility.FromJson<AIRuntimeConfig>(File.ReadAllText(ConfigPath)) ?? new AIRuntimeConfig(); }
            catch (Exception e) { Debug.LogWarning("[MedSim] AI config unreadable, using text-only mode: " + e.Message); }
            return new AIRuntimeConfig();
        }
    }
}
